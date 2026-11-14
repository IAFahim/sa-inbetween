using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace SA.Inbetween.Timeline.Editor
{
    /// <summary>A bridge being generated. Finishes on its own; poll <see cref="Done"/> or listen to <see cref="TimelineBridge.Finished"/>.</summary>
    public sealed class BridgeJob
    {
        public TimelineAsset Timeline;
        public AnimationTrack Track;
        public TimelineClip A;
        public TimelineClip B;
        public TimelineClip Bridge;
        public InbetweenRecipe Recipe;
        public PlayableDirector Director;
        public bool Done;
        public string Error = "";
        public BridgePlacement Placement;
    }

    /// <summary>
    /// Bridges two clips on an ordinary Animation Track. The bridge is the recipe's exported .anim on a normal
    /// AnimationPlayableAsset clip; the link back to its recipe is the file itself (<c>X.anim</c> or
    /// <c>X_Humanoid.anim</c> next to <c>X.asset</c>), so the timeline holds no custom types.
    /// </summary>
    public static class TimelineBridge
    {
        public const string FolderSuffix = "_Bridges";
        const double Probe = 1e-4;

        static readonly List<BridgeJob> Jobs = new List<BridgeJob>();
        static bool hooked;

        public static event Action<BridgeJob> Finished;

        public static IReadOnlyList<BridgeJob> Pending => Jobs;

        // ---------- neighbours ----------

        public static IEnumerable<TimelineClip> Ordered(TrackAsset track) => track.GetClips().OrderBy(c => c.start).ThenBy(c => c.end);

        /// <summary>The clip that follows <paramref name="clip"/> on its track (next start), skipping <paramref name="skip"/>.</summary>
        public static TimelineClip Next(TimelineClip clip, TimelineClip skip = null)
        {
            var track = clip?.GetParentTrack();
            if (track == null) return null;
            return Ordered(track).FirstOrDefault(c => c != clip && c != skip && (c.start > clip.start + BridgePlan.Epsilon || (Math.Abs(c.start - clip.start) <= BridgePlan.Epsilon && c.end > clip.end)));
        }

        /// <summary>The clip that ends last before <paramref name="clip"/> starts, skipping <paramref name="skip"/>.</summary>
        public static TimelineClip Previous(TimelineClip clip, TimelineClip skip = null)
        {
            var track = clip?.GetParentTrack();
            if (track == null) return null;
            return Ordered(track).Where(c => c != clip && c != skip && c.start < clip.start - BridgePlan.Epsilon).OrderBy(c => c.end).LastOrDefault();
        }

        public static string NeighbourProblem(TimelineClip a, TimelineClip b)
        {
            if (a == null || b == null) return "Select two clips on the same Animation Track.";
            if (a == b) return "Select two different clips.";
            var track = a.GetParentTrack() as AnimationTrack;
            if (track == null) return "Bridges go on Animation Tracks.";
            if (b.GetParentTrack() != track) return "Both clips must be on the same Animation Track.";
            if (track.inClipMode == false) return "The track is in infinite (recorded) mode; convert it to clip mode first.";
            if (ClipOf(a) == null || ClipOf(b) == null) return "Both clips need an animation clip.";
            if (b.start < a.start) return "The first clip must start before the second.";
            if (Next(a) != b) return "The two clips must be next to each other on the track.";
            return "";
        }

        public static AnimationClip ClipOf(TimelineClip clip) => (clip?.asset as AnimationPlayableAsset)?.clip;

        // ---------- character ----------

        /// <summary>The bound object of the track (an Animator or its GameObject), or null.</summary>
        public static Animator BoundAnimator(PlayableDirector director, TrackAsset track)
        {
            if (director == null || track == null) return null;
            var bound = director.GetGenericBinding(track);
            if (bound is Animator animator) return animator;
            if (bound is GameObject go) return go.GetComponentInChildren<Animator>(true);
            return null;
        }

        /// <summary>The model or prefab asset a scene character comes from: its prefab source, else the model file its avatar was made in.</summary>
        public static GameObject CharacterAsset(GameObject sceneCharacter)
        {
            if (sceneCharacter == null) return null;
            if (EditorUtility.IsPersistent(sceneCharacter)) return sceneCharacter.transform.root.gameObject;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(sceneCharacter);
            if (root != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (source != null) return source.transform.root.gameObject;
            }

            var animator = sceneCharacter.GetComponentInChildren<Animator>(true);
            var avatarPath = animator != null && animator.avatar != null ? AssetDatabase.GetAssetPath(animator.avatar) : "";
            return avatarPath == "" ? null : AssetDatabase.LoadMainAssetAtPath(avatarPath) as GameObject;
        }

        public static bool IsHumanoid(GameObject character)
        {
            var animator = character != null ? character.GetComponentInChildren<Animator>(true) : null;
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        // ---------- recipe ----------

        /// <summary>Source frame at the end of A: End when A plays to the last frame of its clip, else the exact time.</summary>
        public static ClipFrame EndOf(TimelineClip a, GameObject character)
        {
            var clip = ClipOf(a);
            var length = clip != null ? clip.length : 0;
            var loop = (a.asset as AnimationPlayableAsset)?.loop == AnimationPlayableAsset.LoopMode.On || (clip != null && clip.isLooping && (a.asset as AnimationPlayableAsset)?.loop != AnimationPlayableAsset.LoopMode.Off);
            var local = BridgePlan.LocalEnd(a.clipIn, a.duration, a.timeScale, length, loop);
            var rate = clip != null && clip.frameRate > 0 ? clip.frameRate : 30f;
            var anchor = BridgePlan.Anchor(local, length, rate);
            return UnityFrame(clip, character, anchor == BridgeAnchor.End ? FrameWhere.End : FrameWhere.Time, local);
        }

        /// <summary>Source frame at the start of B: Start when B starts at its clip's first frame, else the exact time.</summary>
        public static ClipFrame StartOf(TimelineClip b, GameObject character)
        {
            var clip = ClipOf(b);
            var length = clip != null ? clip.length : 0;
            var local = BridgePlan.LocalStart(b.clipIn, length, clip != null && clip.isLooping);
            var rate = clip != null && clip.frameRate > 0 ? clip.frameRate : 30f;
            var anchor = BridgePlan.Anchor(local, length, rate);
            return UnityFrame(clip, character, anchor == BridgeAnchor.Start ? FrameWhere.Start : FrameWhere.Time, local);
        }

        public static ClipFrame UnityFrame(AnimationClip clip, GameObject character, FrameWhere where, double seconds)
        {
            var rate = clip != null && clip.frameRate > 0 ? clip.frameRate : 30f;
            return new ClipFrame
            {
                Kind = SourceKind.UnityClip,
                Motion = clip,
                Character = character,
                Where = where,
                Seconds = (float)seconds,
                Frame = (int)Math.Round(seconds * rate),
            };
        }

        /// <summary>Points a recipe at A's end and B's start. Returns true when anything changed.</summary>
        public static bool Configure(InbetweenRecipe recipe, TimelineClip a, TimelineClip b, GameObject character, double duration)
        {
            var before = JsonUtility.ToJson(recipe);
            recipe.Character = character;
            recipe.Start = EndOf(a, character);
            recipe.Goals = new List<ClipFrame> { StartOf(b, character) };
            recipe.DurationSeconds = (float)duration;
            recipe.SaveAnimationClip = true;
            return JsonUtility.ToJson(recipe) != before;
        }

        public static string Folder(TimelineAsset timeline)
        {
            var path = AssetDatabase.GetAssetPath(timeline);
            if (path == "") return "";
            return (Path.GetDirectoryName(path) ?? "Assets").Replace('\\', '/') + "/" + timeline.name + FolderSuffix;
        }

        public static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (name ?? "").Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
            var s = new string(chars).Trim('_');
            return s == "" ? "Clip" : s;
        }

        public static string RecipeName(TimelineClip a, TimelineClip b) => "Bridge_" + SafeName(ClipOf(a)?.name ?? a.displayName) + "_to_" + SafeName(ClipOf(b)?.name ?? b.displayName);

        /// <summary>The .anim the Timeline clip plays: the Humanoid export for Humanoid characters, the Generic one otherwise.</summary>
        public static string BridgeClipPath(InbetweenRecipe recipe) =>
            IsHumanoid(SaClipExport.CharacterFor(recipe)) ? SaClipHumanoidExport.ExportPath(recipe) : SaClipExport.ExportPath(recipe);

        /// <summary>The recipe behind a bridge clip, found from the clip's file name (<c>X.anim</c>/<c>X_Humanoid.anim</c> next to <c>X.asset</c>).</summary>
        public static InbetweenRecipe RecipeOf(TimelineClip bridge)
        {
            var clip = ClipOf(bridge);
            if (clip == null) return null;
            var path = AssetDatabase.GetAssetPath(clip);
            if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) return null;
            var stem = path.Substring(0, path.Length - ".anim".Length);
            if (stem.EndsWith("_Humanoid", StringComparison.Ordinal)) stem = stem.Substring(0, stem.Length - "_Humanoid".Length);
            var recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(stem + ".asset");
            if (recipe != null) return recipe;
            foreach (var guid in AssetDatabase.FindAssets("t:InbetweenRecipe"))
            {
                var r = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null && r.SavedClip == clip) return r;
            }

            return null;
        }

        public static bool IsBridge(TimelineClip clip) => RecipeOf(clip) != null;

        // ---------- actions ----------

        /// <summary>Makes a recipe for A to B, generates it, then places the bridge on the track (asynchronously).</summary>
        public static BridgeJob Add(PlayableDirector director, TimelineClip a, TimelineClip b, out string error)
        {
            error = NeighbourProblem(a, b);
            if (error != "") return null;
            var track = (AnimationTrack)a.GetParentTrack();
            var timeline = track.timelineAsset;
            var folder = Folder(timeline);
            if (folder == "") { error = "Save the timeline as an asset first."; return null; }
            var character = CharacterAsset(BoundAnimator(director, track)?.gameObject);
            if (character == null) { error = "Bind the track to a character (an Animator from a model or prefab) in the scene first."; return null; }

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder)?.Replace('\\', '/'), Path.GetFileName(folder));
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            Configure(recipe, a, b, character, BridgePlan.DurationFor(BridgePlan.Gap(a.end, b.start)));
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + RecipeName(a, b) + ".asset");
            AssetDatabase.CreateAsset(recipe, path);
            AssetDatabase.SaveAssetIfDirty(recipe);
            return Begin(new BridgeJob { Timeline = timeline, Track = track, A = a, B = b, Recipe = recipe, Director = director }, out error);
        }

        /// <summary>Re-reads the bridge's neighbours (A = clip ending before it, B = clip after it), regenerates if anything changed, and re-places the same Timeline clip.</summary>
        public static BridgeJob Regenerate(PlayableDirector director, TimelineClip bridge, out string error)
        {
            error = "";
            var recipe = RecipeOf(bridge);
            if (recipe == null) { error = "This clip is not an Inbetween bridge (no recipe next to its .anim)."; return null; }
            var a = Previous(bridge);
            var b = Next(bridge);
            if (a == null || b == null) { error = "A bridge needs a clip before it and a clip after it on the same track."; return null; }
            var track = (AnimationTrack)bridge.GetParentTrack();
            var character = CharacterAsset(BoundAnimator(director, track)?.gameObject) ?? SaClipExport.CharacterFor(recipe);
            if (character == null) { error = "Bind the track to a character first."; return null; }

            // The designer sets the length by moving B: the room for the bridge is from A's end to B's start.
            var room = Math.Max(0, b.start - a.end);
            var duration = room > BridgePlan.Epsilon ? BridgePlan.DurationFor(room) : bridge.duration > BridgePlan.Epsilon ? BridgePlan.DurationFor(bridge.duration) : BridgePlan.DefaultSeconds;
            Undo.RecordObject(recipe, "Regenerate bridge");
            if (Configure(recipe, a, b, character, duration)) EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            return Begin(new BridgeJob { Timeline = track.timelineAsset, Track = track, A = a, B = b, Bridge = bridge, Recipe = recipe, Director = director }, out error);
        }

        static BridgeJob Begin(BridgeJob job, out string error)
        {
            error = "";
            var state = RecipeRunner.StateOf(job.Recipe);
            var clipReady = AssetDatabase.LoadAssetAtPath<AnimationClip>(BridgeClipPath(job.Recipe)) != null;
            if (state != RecipeState.UpToDate || !clipReady)
            {
                if (!RecipeRunner.Start(job.Recipe, out error)) return null;
            }

            Jobs.Add(job);
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            Tick();
            return job;
        }

        static void Tick()
        {
            for (var i = Jobs.Count - 1; i >= 0; i--)
            {
                var job = Jobs[i];
                var run = RecipeRunner.Get(job.Recipe);
                if (run != null && !run.Done) continue;
                Jobs.RemoveAt(i);
                if (run != null && run.Error != "") job.Error = "Generating the bridge failed: " + run.Error;
                else if (!Complete(job, out var error)) job.Error = error;
                job.Done = true;
                if (job.Error != "") Debug.LogWarning("SA Inbetween: " + job.Error, job.Recipe);
                Finished?.Invoke(job);
            }

            if (Jobs.Count == 0 && hooked)
            {
                EditorApplication.update -= Tick;
                hooked = false;
            }
        }

        /// <summary>Puts the generated clip on the track between A and B, ripples B and later clips, and matches root offsets. One Undo step.</summary>
        internal static bool Complete(BridgeJob job, out string error)
        {
            error = "";
            var recipe = job.Recipe;
            if (job.A == null || job.B == null || job.Track == null || !job.Track.GetClips().Contains(job.A) || !job.Track.GetClips().Contains(job.B))
            {
                error = "The clips changed while the bridge was generating. Use Regenerate bridge.";
                return false;
            }

            AnimationClip clip;
            if (IsHumanoid(SaClipExport.CharacterFor(recipe)))
            {
                if (!SaClipHumanoidExport.TryExport(recipe, out clip, out error)) return false;
            }
            else
            {
                if ((recipe.SavedClip == null || SaClipExport.SavedClipStale(recipe)) && !SaClipExport.TrySave(recipe, out error)) return false;
                clip = recipe.SavedClip;
            }

            if (clip == null || clip.length <= 0f) { error = "The bridge clip is empty."; return false; }

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(job.Bridge == null ? "Add Inbetween bridge" : "Regenerate Inbetween bridge");
            Undo.RegisterCompleteObjectUndo(job.Track, "Inbetween bridge");
            foreach (var c in job.Track.GetClips())
                if (c.asset != null) Undo.RecordObject(c.asset, "Inbetween bridge");

            var placement = BridgePlan.Place(job.A.end, job.B.start, clip.length);
            var bStart = job.B.start;
            foreach (var c in job.Track.GetClips().ToList())
                if (c != job.A && c != job.Bridge && BridgePlan.Ripples(c.start, bStart))
                    c.start += placement.Shift;

            var bridge = job.Bridge;
            if (bridge == null)
            {
                bridge = job.Track.CreateClip(clip);
                if (bridge.asset != null) Undo.RegisterCreatedObjectUndo(bridge.asset, "Inbetween bridge");
            }

            var asset = (AnimationPlayableAsset)bridge.asset;
            asset.clip = clip;
            asset.loop = AnimationPlayableAsset.LoopMode.Off;
            // The exported Humanoid clip has no IK goal curves; foot IK would pull the feet to empty goals and fold the legs.
            asset.applyFootIK = false;
            asset.ResetOffsets();
            bridge.start = placement.Start;
            bridge.clipIn = 0;
            bridge.timeScale = 1;
            bridge.duration = clip.length;
            bridge.easeInDuration = 0;
            bridge.easeOutDuration = 0;
            bridge.displayName = "Bridge " + (job.A.displayName ?? "") + " > " + (job.B.displayName ?? "");
            job.Bridge = bridge;
            job.Placement = placement;

            var matched = MatchOffsets(job, out var matchNote);
            if (!matched && matchNote != "") Debug.Log("SA Inbetween: bridge placed without matching offsets: " + matchNote, recipe);

            EditorUtility.SetDirty(job.Track);
            EditorUtility.SetDirty(job.Timeline);
            AssetDatabase.SaveAssetIfDirty(job.Timeline);
            Undo.CollapseUndoOperations(group);
            if (TimelineEditor.inspectedAsset == job.Timeline) TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            return true;
        }

        // ---------- root matching ----------

        /// <summary>
        /// Moves the bridge so its hips start where A's hips end, then moves B (and every later clip by the same amount) so B's hips
        /// start where the bridge's end. Position XYZ and yaw only; the same matrix math as Timeline's Match Offsets, but matched on the
        /// hips on a hidden copy of the bound character, so the scene is never evaluated or dirtied.
        /// </summary>
        public static bool MatchOffsets(BridgeJob job, out string note)
        {
            note = "";
            var bound = BoundAnimator(job.Director, job.Track);
            if (bound == null) { note = "the track has no bound character"; return false; }
            using var probe = new TrackProbe(job.Timeline, job.Track, bound);
            if (!probe.Valid) { note = probe.Problem; return false; }

            var bridgeAsset = (AnimationPlayableAsset)job.Bridge.asset;
            var aEnd = probe.Hips(job.A.end - Probe);
            var bridgeStart = probe.Hips(job.Bridge.start + Probe);
            Apply(probe, bridgeAsset, Delta(aEnd, bridgeStart));

            var bridgeEnd = probe.Hips(job.Bridge.end - Probe);
            var bStart = probe.Hips(job.B.start + Probe);
            var toB = Delta(bridgeEnd, bStart);
            foreach (var c in job.Track.GetClips())
                if (c != job.A && c != job.Bridge && c.start >= job.B.start - BridgePlan.Epsilon && c.asset is AnimationPlayableAsset later)
                    Apply(probe, later, toB);
            return true;
        }

        public const double ChainSeam = 1e-3;

        public static Matrix4x4 DeltaOnGround(Pose to, Pose from)
        {
            var full = Delta(to, from);
            var translation = (Vector3)full.GetColumn(3);
            translation.y = 0f;
            return Matrix4x4.TRS(translation, full.rotation, Vector3.one);
        }

        public static int MatchChain(TimelineAsset timeline, AnimationTrack track, PlayableDirector director, out string note)
        {
            note = "";
            var bound = director != null && director.playableAsset == timeline ? BoundAnimator(director, track) : null;
            if (bound == null) { note = "the Generated track has no bound character"; return 0; }
            using var probe = new TrackProbe(timeline, track, bound);
            if (!probe.Valid) { note = probe.Problem; return 0; }
            var ordered = track.GetClips().Where(c => c.asset is AnimationPlayableAsset).OrderBy(c => c.start).ToList();
            var matched = 0;
            for (var i = 1; i < ordered.Count; i++)
            {
                var before = ordered[i - 1];
                var after = ordered[i];
                if (Math.Abs(after.start - before.end) > ChainSeam) continue;
                var leaving = probe.Hips(before.end - Probe);
                var arriving = probe.Hips(after.start + Probe);
                Apply(probe, (AnimationPlayableAsset)after.asset, DeltaOnGround(leaving, arriving));
                matched++;
            }

            return matched;
        }

        /// <summary>World rigid delta (yaw + translation) that takes <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static Matrix4x4 Delta(Pose to, Pose from)
        {
            var twist = to.rotation * Quaternion.Inverse(from.rotation);
            var forward = twist * Vector3.forward;
            forward.y = 0;
            var yaw = forward.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            var translation = to.position - yaw * from.position;
            return Matrix4x4.TRS(translation, yaw, Vector3.one);
        }

        static void Apply(TrackProbe probe, AnimationPlayableAsset asset, Matrix4x4 delta)
        {
            var track = probe.TrackMatrix;
            var clipMatrix = Matrix4x4.TRS(asset.position, asset.rotation, Vector3.one);
            var next = track.inverse * delta * track * clipMatrix;
            asset.position = next.GetColumn(3);
            asset.rotation = next.rotation;
        }

        /// <summary>A hidden copy of the bound character driven by its own director in a preview scene.</summary>
        sealed class TrackProbe : IDisposable
        {
            readonly Scene scene;
            readonly GameObject root;
            readonly PlayableDirector director;
            readonly Transform hips;
            public readonly string Problem = "";
            public bool Valid => Problem == "";
            readonly AnimationTrack track;
            readonly bool sceneOffsets;

            /// <summary>What Timeline puts in front of a clip's offsets: the track offset, or nothing in scene-offset mode (the copy sits at the origin).</summary>
            public Matrix4x4 TrackMatrix => sceneOffsets ? Matrix4x4.identity : Matrix4x4.TRS(track.position, track.rotation, Vector3.one);

            public TrackProbe(TimelineAsset timeline, AnimationTrack track, Animator bound)
            {
                this.track = track;
                scene = EditorSceneManager.NewPreviewScene();
                root = new GameObject("SA Bridge Probe") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, scene);
                var copy = Object.Instantiate(bound.gameObject, root.transform);
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                foreach (var other in copy.GetComponentsInChildren<PlayableDirector>(true)) Object.DestroyImmediate(other);
                var animator = copy.GetComponent<Animator>();
                // Nothing renders the copy, and a culled Animator skips writing transforms for root-motion clips.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                sceneOffsets = track.trackOffset == TrackOffset.ApplySceneOffsets || (track.trackOffset == TrackOffset.Auto && animator.runtimeAnimatorController != null);
                hips = HipsOf(animator);
                if (hips == null) { Problem = "cannot find the hips of '" + bound.name + "'"; return; }
                director = root.AddComponent<PlayableDirector>();
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.extrapolationMode = DirectorWrapMode.Hold;
                director.playableAsset = timeline;
                director.SetGenericBinding(track, animator);
            }

            public Pose Hips(double time)
            {
                director.RebuildGraph();
                director.time = time;
                director.Evaluate();
                return new Pose(hips.position, hips.rotation);
            }

            public void Dispose()
            {
                if (director != null && director.playableGraph.IsValid()) director.playableGraph.Destroy();
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static Transform HipsOf(Animator animator)
        {
            if (animator == null) return null;
            if (animator.isHuman)
            {
                var h = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (h != null) return h;
            }

            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (n.EndsWith("hips") || n.EndsWith("pelvis")) return t;
            }

            return animator.transform;
        }
    }
}

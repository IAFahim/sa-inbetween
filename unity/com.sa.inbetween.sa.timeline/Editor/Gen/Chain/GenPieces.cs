using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SA.Animation;
using SA.Animation.Content;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class GenPiece
    {
        public GenSource Source;
        public ChainTrack Chain;
        public double Start;
        public double End;
        public CanonicalFrames Frames;
        public string Name = "";
        public SaClipAsset Saclip;
        public AnimationClip Clip;
        public TimelineClip Placed;

        public double Seconds => End - Start;
    }

    public static class GenPieces
    {
        public const string FolderName = "Pieces";
        public const float Rate = 30f;
        const double Seam = 1e-3;
        const double ShortestPiece = 1.0 / 30.0;

        public static string Folder(TimelineAsset timeline) => GenRecipes.Folder(timeline) + "/" + FolderName;

        public static string Work(TimelineAsset timeline) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "SaInbetween", "pieces", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(timeline)));

        public static bool TryBytes(GenSource source, GameObject character, string work, out byte[] bytes, out string error)
        {
            bytes = null;
            error = "";
            var payload = source.Payload;
            if (payload.Kind == SourceKind.SaClip)
            {
                bytes = payload.Clip != null ? payload.Clip.Bytes : null;
                if (bytes == null || bytes.Length == 0) error = "the .saclip is empty";
                return error == "";
            }

            if (payload.Kind == SourceKind.Pose)
            {
                error = "a pose block has no motion to play";
                return false;
            }

            var who = payload.Character != null ? TimelineBridge.CharacterAsset(payload.Character) : character;
            var frame = new ClipFrame { Kind = SourceKind.UnityClip, Motion = payload.Motion, Character = who, Where = FrameWhere.Start };
            if (!RecipeRunner.TryBakeSource(frame, work, out var file, out error)) return false;
            bytes = File.ReadAllBytes(file);
            return true;
        }

        public static bool TryFrames(GenSource source, double from, double to, GameObject character, string work, out CanonicalFrames frames, out string error)
        {
            frames = null;
            if (!TryBytes(source, character, work, out var bytes, out error)) return false;
            if (!CanonicalClip.TryLoad(bytes, out var clip, out error)) return false;
            var count = Math.Max(2, (int)Math.Round((to - from) * Rate) + 1);
            frames = new CanonicalFrames { Count = count, Duration = to - from, Loop = false, RootPolicy = RootPolicy.Planar, Poses = new Pose[count * SeamFix.Bones], Root = new RigidPose[count] };
            RigidMath.TryInverse(RootAt(clip, source, from), out var origin);
            for (var i = 0; i < count; i++)
            {
                var t = from + (to - from) * i / (count - 1);
                var local = Local(clip, source, t);
                Array.Copy(CanonicalClip.Sample(clip, local), 0, frames.Poses, i * SeamFix.Bones, SeamFix.Bones);
                RigidMath.TryCompose(origin, RootAt(clip, source, t), out frames.Root[i]);
            }

            return true;
        }

        static double Unwrapped(GenSource source, double t) => Math.Max(0.0, source.ClipIn) + Math.Max(0.0, t - source.Start) * (source.TimeScale > 0 ? source.TimeScale : 1.0);

        static double Local(SharedClip clip, GenSource source, double t)
        {
            var u = Unwrapped(source, t);
            var length = clip.Duration;
            if (length <= 0) return 0;
            if (!source.Loops) return Math.Min(u, length);
            var k = Math.Floor(u / length);
            return u - k * length;
        }

        static RigidPose RootAt(SharedClip clip, GenSource source, double t)
        {
            var u = Unwrapped(source, t);
            var length = clip.Duration;
            if (length <= 0 || !source.Loops) return SaClipExport.RootAt(clip, Math.Min(u, Math.Max(0.0, length)));
            var cycles = (int)Math.Floor(u / length);
            var frac = u - cycles * length;
            RigidMath.TryInverse(SaClipExport.RootAt(clip, 0.0), out var start);
            RigidMath.TryCompose(start, SaClipExport.RootAt(clip, length), out var lap);
            var root = SaClipExport.RootAt(clip, 0.0);
            for (var i = 0; i < cycles; i++) RigidMath.TryCompose(root, lap, out root);
            RigidMath.TryCompose(start, SaClipExport.RootAt(clip, frac), out var within);
            RigidMath.TryCompose(root, within, out var result);
            return result;
        }

        public static List<GenSource> SourcesToPlace(GenBakeRun run)
        {
            var sources = new List<GenSource>();
            foreach (var job in run.Jobs.Where(j => j.Error == "" && j.Job.Export.PlaceOnTimeline && j.Job.Export.PlaceSources))
                foreach (var source in new[] { job.Job.StartSource, job.Job.GoalSource })
                    if (source != null && source.Payload.Kind != SourceKind.Pose && !sources.Contains(source))
                        sources.Add(source);
            foreach (var source in run.Plan.Sources)
                if (source.Frame.Track is ChainTrack chain && chain.Chain.Export.PlaceOnTimeline && chain.Chain.Export.PlaceSources && source.Payload.Kind != SourceKind.Pose && source.Asset.HasSource && !sources.Contains(source))
                    sources.Add(source);
            return sources.OrderBy(s => s.Start).ToList();
        }

        public static GameObject CharacterFor(GenBakeRun run, GenSource source)
        {
            foreach (var job in run.Jobs)
                if ((job.Job.StartSource == source || job.Job.GoalSource == source) && job.Job.Character != null)
                    return job.Job.Character;
            if (source.Frame.Track is ChainTrack chain && chain.Chain.Character != null) return TimelineBridge.CharacterAsset(chain.Chain.Character);
            if (source.Payload.Character != null) return TimelineBridge.CharacterAsset(source.Payload.Character);
            return run.Plan.BoundCharacter(source.Frame.Track);
        }

        public static GenRig RigFor(GenBakeRun run, GenSource source)
        {
            foreach (var job in run.Jobs)
                if (job.Job.StartSource == source || job.Job.GoalSource == source)
                    return job.Job.Rig;
            return source.Frame.Track is ChainTrack chain ? chain.Chain.Rig : GenRig.Auto;
        }

        public static string Place(GenBakeRun run, List<GenPiece> pieces)
        {
            var timeline = run.Timeline;
            var sources = SourcesToPlace(run);
            var track = GenOutputs.Track(timeline, sources.Count > 0);
            if (track == null || sources.Count == 0) return "";
            Undo.RegisterCompleteObjectUndo(track, GenBake.UndoName);
            var generated = track.GetClips().Where(c => !GenOutputs.IsSourceCopy(c)).OrderBy(c => c.start).ToList();
            var unused = track.GetClips().Where(GenOutputs.IsSourceCopy).ToList();
            var folder = Folder(timeline);
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(GenRecipes.Folder(timeline), FolderName);
            var work = Work(timeline);
            var problems = new List<string>();
            var written = new HashSet<string>();
            foreach (var source in sources)
            {
                var start = source.Start;
                var end = source.Start + source.Duration;
                foreach (var clip in generated)
                {
                    if (clip.start <= start + Seam && clip.end > start) start = clip.end;
                    else if (clip.start > start + Seam && clip.start < end) end = clip.start;
                }

                if (end - start < ShortestPiece) continue;
                var character = CharacterFor(run, source);
                if (character == null)
                {
                    problems.Add("'" + source.Frame.Clip.displayName + "' has no character to play on");
                    continue;
                }

                if (!TryFrames(source, start, end, character, work, out var frames, out var error))
                {
                    problems.Add("'" + source.Frame.Clip.displayName + "': " + error);
                    continue;
                }

                var piece = new GenPiece { Source = source, Chain = source.Frame.Track as ChainTrack, Start = start, End = end, Frames = frames };
                var before = pieces.LastOrDefault(p => p.Chain != null && p.Chain == piece.Chain && Math.Abs(p.End - start) <= Seam);
                if (before != null && piece.Chain.Chain.Seams.Blends)
                {
                    var last = SeamFix.Frame(before.Frames.Poses, before.Frames.Count - 1);
                    var beyond = SeamFix.Frame(before.Frames.Poses, Math.Max(0, before.Frames.Count - 2));
                    frames.Poses = SeamFix.Blend(frames.Poses, frames.Count, Rate, new SeamSide(last, beyond), null, piece.Chain.Chain.Seams);
                }

                piece.Name = TimelineBridge.SafeName(source.Frame.Clip.displayName) + "_" + Tick(start) + "-" + Tick(end);
                if (!Write(piece, folder, work, character, GenOutputs.PlacesHumanoid(RigFor(run, source), character), out error))
                {
                    problems.Add("'" + source.Frame.Clip.displayName + "': " + error);
                    continue;
                }

                written.Add(AssetDatabase.GetAssetPath(piece.Saclip));
                written.Add(AssetDatabase.GetAssetPath(piece.Clip));
                var name = source.Frame.Clip.displayName + GenOutputs.SourceSuffix;
                var copy = unused.FirstOrDefault(c => c.displayName == name);
                if (copy != null)
                {
                    unused.Remove(copy);
                    if (copy.asset != null) Undo.RecordObject(copy.asset, GenBake.UndoName);
                }
                else
                {
                    copy = track.CreateClip(piece.Clip);
                    if (copy.asset != null) Undo.RegisterCreatedObjectUndo(copy.asset, GenBake.UndoName);
                }

                var asset = (AnimationPlayableAsset)copy.asset;
                asset.clip = piece.Clip;
                asset.loop = AnimationPlayableAsset.LoopMode.Off;
                asset.applyFootIK = false;
                asset.ResetOffsets();
                copy.start = start;
                copy.timeScale = 1;
                copy.clipIn = 0;
                copy.duration = end - start;
                copy.easeInDuration = 0;
                copy.easeOutDuration = 0;
                copy.displayName = name;
                piece.Placed = copy;
                pieces.Add(piece);
            }

            foreach (var leftover in unused) timeline.DeleteClip(leftover);
            Prune(folder, written);
            GenOutputs.RecalculateHolds(track);
            var matched = TimelineBridge.MatchChain(timeline, track, run.Director, out var why);
            var seams = why == "" ? TimelineBridge.MeasureSeams(timeline, track, run.Director, out _) : new List<TimelineSeam>();
            run.Seams.Clear();
            run.Seams.AddRange(seams);
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            if (TimelineEditor.inspectedAsset == timeline) TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            var text = "Generated track: " + pieces.Count + (pieces.Count == 1 ? " source piece" : " source pieces") + " around the generated motion, so it plays from start to end; " +
                       (why == "" ? "hips matched at " + matched + (matched == 1 ? " seam" : " seams") + (seams.Count > 0 ? ", largest height step " + (seams.Max(s => s.HeightStep) * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm, largest sideways step " + (seams.Max(s => s.SideStep) * 100f).ToString("0.00", CultureInfo.InvariantCulture) + " cm." : ".")
                           : "hips not matched (" + why + "); bake with the timeline's director in an open scene to match them.");
            if (problems.Count > 0) text += " Not placed: " + string.Join("; ", problems) + ".";
            return text;
        }

        static string Tick(double seconds) => GenRules.Ticks(seconds).ToString(CultureInfo.InvariantCulture);

        static bool Write(GenPiece piece, string folder, string work, GameObject character, bool humanoid, out string error)
        {
            if (!CanonicalClip.TryEncode(piece.Frames, out var bytes, out error)) return false;
            var saclipPath = folder + "/" + piece.Name + ".saclip";
            var full = Path.GetFullPath(saclipPath);
            if (!File.Exists(full) || !File.ReadAllBytes(full).SequenceEqual(bytes))
            {
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(saclipPath, ImportAssetOptions.ForceUpdate);
            }

            piece.Saclip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(saclipPath);
            if (piece.Saclip == null)
            {
                error = "Unity did not import " + saclipPath;
                return false;
            }

            var animPath = folder + "/" + piece.Name + (humanoid ? "_Humanoid.anim" : ".anim");
            Directory.CreateDirectory(work);
            var stampPath = Path.Combine(work, Path.GetFileName(animPath) + ".stamp");
            var stamp = Stamp(bytes, character, humanoid);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            if (existing != null && File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            {
                piece.Clip = existing;
                return true;
            }

            if (humanoid)
            {
                if (!SaClipHumanoidExport.TryBuild(piece.Saclip, character, out var clip, out error)) return false;
                piece.Clip = SaClipExport.SaveAs(clip, animPath);
            }
            else
            {
                if (!SaClipExport.TryExport(piece.Saclip, character, animPath, out var saved, out error)) return false;
                piece.Clip = saved;
            }

            AssetDatabase.ImportAsset(animPath, ImportAssetOptions.ForceUpdate);
            piece.Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            File.WriteAllText(stampPath, stamp);
            return piece.Clip != null;
        }

        static string Stamp(byte[] bytes, GameObject character, bool humanoid)
        {
            var path = AssetDatabase.GetAssetPath(character);
            return GenChain.Digest(bytes) + "|" + path + "|" + (path == "" ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString()) + "|" + (humanoid ? "humanoid" : "generic");
        }

        static void Prune(string folder, HashSet<string> keep)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || keep.Contains(path)) continue;
                if (path.EndsWith(".saclip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) AssetDatabase.DeleteAsset(path);
            }
        }
    }
}

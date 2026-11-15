using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class ChainBlock
    {
        public string Role = "";
        public AnimationClip Motion;
        public double ClipIn;
        public double Seconds;
    }

    public static class ChainDemo
    {
        public const string Folder = "Assets/SA Inbetween Chain Demo";
        public const string Name = "Run Jump Land Roll Walk";
        public const string SceneName = "Chain Demo";
        public const string CharacterName = "Chain Demo Character";
        public const string DirectorName = "Chain";
        public const string TrackName = "Chain";
        public const double GapSeconds = 0.8;

        public static string ScenePath => Folder + "/" + SceneName + ".unity";

        public static string TimelinePath => Folder + "/" + Name + ".playable";

        static readonly (string Role, string[] Want, string[] Fallback, double ClipIn, double Seconds)[] Roles =
        {
            ("run", new[] { "run_f", "run" }, new string[0], 0.0, 0.8),
            ("jump", new[] { "jump_running", "jump" }, new string[0], 0.0, 0.5),
            ("land", new[] { "land_idlemedium", "land_idlesoft", "land" }, new string[0], 0.0, 0.7),
            ("roll", new[] { "roll", "tumble", "somersault", "dive" }, new[] { "idle_crouching", "crouch" }, 0.0, 0.8),
            ("walk", new[] { "walk_f", "walk" }, new string[0], 0.0, 1.2),
        };

        static readonly string[] Avoid = { "strafe", "turn", "toidle", "toidle", "torun", "towalk", "tocrouch", "tostand", "fem", "additive", "up25", "down25", "shuffle", "bck", "fwdstrafe", "sprint", "fall" };

        public static List<ChainBlock> Pick(IEnumerable<AnimationClip> clips, out string problem)
        {
            problem = "";
            var pool = clips.Where(c => c != null && c.humanMotion && !c.name.StartsWith("__preview__", StringComparison.Ordinal)).Distinct().ToList();
            var blocks = new List<ChainBlock>();
            foreach (var role in Roles)
            {
                var pick = Best(pool, role.Want) ?? Best(pool, role.Fallback);
                if (pick == null)
                {
                    problem = "no Humanoid clip for '" + role.Role + "' (looked for " + string.Join(", ", role.Want.Concat(role.Fallback)) + ").";
                    return null;
                }

                var seconds = Math.Min(role.Seconds, Math.Max(0.2, pick.length - role.ClipIn));
                blocks.Add(new ChainBlock { Role = role.Role, Motion = pick, ClipIn = role.ClipIn, Seconds = seconds });
            }

            return blocks;
        }

        static AnimationClip Best(List<AnimationClip> pool, string[] words)
        {
            foreach (var word in words)
            {
                var match = pool.Where(c => Plain(c.name).Contains(word.Replace("_", "")))
                    .OrderBy(c => Avoid.Count(a => Plain(c.name).Contains(a)))
                    .ThenBy(c => Plain(c.name).Contains("rootmotion") || Regex.IsMatch(c.name, "_RM(V)?(_|$)") ? 1 : 0)
                    .ThenBy(c => Plain(c.name).Contains("mod") ? 1 : 0)
                    .ThenBy(c => c.name.Length)
                    .ThenBy(c => AssetDatabase.GetAssetPath(c) + "/" + c.name, StringComparer.Ordinal)
                    .FirstOrDefault(c => Avoid.Count(a => Plain(c.name).Contains(a)) == 0);
                if (match != null) return match;
            }

            return null;
        }

        static string Plain(string name) => (name ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "");

        public static List<AnimationClip> ProjectClips(string root = "Assets")
        {
            var clips = new List<AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Folder + "/", StringComparison.Ordinal) || path.Contains("_Generated/")) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip clip && !clips.Contains(clip))
                        clips.Add(clip);
            }

            return clips;
        }

        public static TimelineAsset Layout(string path, IReadOnlyList<ChainBlock> blocks, GameObject character, double gapSeconds)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(timeline, path);
            var track = timeline.CreateTrack<ChainTrack>(null, TrackName);
            var chain = ChainPayload.Default;
            chain.Character = character;
            track.Chain = chain;
            var time = 0.0;
            foreach (var block in blocks)
            {
                var clip = track.CreateClip<SourceClip>();
                var source = (SourceClip)clip.asset;
                source.Payload = new SourcePayload { Kind = SourceKind.UnityClip, Motion = block.Motion };
                clip.start = time;
                clip.clipIn = block.ClipIn;
                clip.duration = block.Seconds;
                clip.displayName = block.Role + " (" + block.Motion.name + ")";
                time = clip.end + gapSeconds;
            }

            ChainSync.Sync(track);
            GenOutputs.Track(timeline, true);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        [MenuItem("Assets/Create/SA/Inbetween Chain Timeline", false, 302)]
        static void FromSelection()
        {
            var clips = Selection.objects.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            var character = Selection.objects.OfType<GameObject>().FirstOrDefault(g => g.GetComponentInChildren<Animator>(true) != null);
            if (clips.Count < 2 || character == null)
            {
                EditorUtility.DisplayDialog("Inbetween Chain Timeline", "Select two or more animation clips (in the order they should play) and the character (model or prefab) they play on, then try again.", "OK");
                return;
            }

            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clips[0]))?.Replace('\\', '/') ?? "Assets";
            if (!AssetDatabase.IsValidFolder(folder) || folder.StartsWith("Packages/", StringComparison.Ordinal)) folder = "Assets";
            var blocks = clips.Select(c => new ChainBlock { Role = c.name, Motion = c, ClipIn = 0, Seconds = Math.Min(1.5, Math.Max(0.2, c.length)) }).ToList();
            var timeline = Layout(AssetDatabase.GenerateUniqueAssetPath(folder + "/" + character.name + " Chain.playable"), blocks, character, GapSeconds);
            Selection.activeObject = timeline;
            EditorGUIUtility.PingObject(timeline);
        }

        [MenuItem("SA/Inbetween/Create Chain Demo", false, 23)]
        static void FromMenu()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Chain demo", "Leave Play mode first.", "OK");
                return;
            }

            var picks = DemoScene.Find();
            if (picks.Character == null)
            {
                EditorUtility.DisplayDialog("Chain demo", picks.Problem, "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var timeline = Create(picks.Character, ProjectClips(), NewSceneMode.Single, out _, out var director, out var error);
            if (timeline == null)
            {
                EditorUtility.DisplayDialog("Chain demo", error, "OK");
                return;
            }

            Selection.activeObject = director.gameObject;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            EditorUtility.DisplayDialog("Chain demo", "The timeline '" + Name + "' has one Chain row: run, jump, land, roll and walk blocks with an in-between in every gap. The in-betweens bake on their own in a few seconds each (see the progress bar at the bottom right; Cancel stops it), then press Play in the Timeline window.", "OK");
        }

        public static TimelineAsset Create(GameObject model, IEnumerable<AnimationClip> clips, NewSceneMode mode, out Scene scene, out PlayableDirector director, out string error)
        {
            scene = default;
            director = null;
            var blocks = Pick(clips, out error);
            if (blocks == null) return null;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null) AssetDatabase.DeleteAsset(TimelinePath);
            var modelPath = AssetDatabase.GetAssetPath(model);
            Layout(TimelinePath, blocks, model, GapSeconds);
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
            if (mode == NewSceneMode.Additive) SceneManager.SetActiveScene(scene);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            SceneManager.MoveGameObjectToScene(ground, scene);
            var character = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            character.name = CharacterName;
            character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = character.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = false;
            var holder = new GameObject(DirectorName);
            SceneManager.MoveGameObjectToScene(holder, scene);
            director = holder.AddComponent<PlayableDirector>();
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Loop;
            director.playableAsset = timeline;
            foreach (var track in timeline.GetOutputTracks())
                if (track is GenTrack || track is AnimationTrack)
                    director.SetGenericBinding(track, animator);
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<Camera>(out var camera))
                {
                    camera.transform.position = new Vector3(4.5f, 2.2f, 4.5f);
                    camera.transform.LookAt(new Vector3(0f, 0.9f, 2.5f));
                }

            EditorSceneManager.SaveScene(scene, ScenePath);
            return timeline;
        }
    }
}

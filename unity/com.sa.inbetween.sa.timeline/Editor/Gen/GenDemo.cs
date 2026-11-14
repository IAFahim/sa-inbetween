using System.Collections.Generic;
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
    public static class GenDemo
    {
        public const string Folder = "Assets/SA Inbetween Generation Demo";
        public const string SceneName = "Generation Demo";
        public const string CharacterName = "Generation Demo Character";
        public const string DirectorName = "Generation";
        public const string Landing = "Jump to Landing";
        public const string Takes = "Jump to Landing, Three Takes";

        public static string ScenePath => Folder + "/" + SceneName + ".unity";

        public static string TimelinePath(string name) => Folder + "/" + name + ".playable";

        [MenuItem("SA/Inbetween/Create Generation Demo", false, 20)]
        static void FromMenu()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Generation demo", "Leave Play mode first, then choose SA > Inbetween > Create Generation Demo.", "OK");
                return;
            }

            var picks = DemoScene.Find();
            if (!picks.Ready)
            {
                EditorUtility.DisplayDialog("Generation demo", picks.Problem, "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var timeline = Create(picks, NewSceneMode.Single, out _, out var director, out var error);
            if (timeline == null)
            {
                EditorUtility.DisplayDialog("Generation demo", error, "OK");
                return;
            }

            Selection.activeObject = director.gameObject;
            EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            EditorUtility.DisplayDialog("Generation demo", "The timeline '" + Landing + "' is open: the jump, the Generate clip that lands it and the landing clip, with a Retarget / Bake and an Export clip under them.\n\nSelect the Generate clip and press Bake Timeline in the Inspector (about 10 seconds), then press Play in the Timeline window.", "OK");
        }

        public static TimelineAsset Create(DemoPicks picks, NewSceneMode mode, out Scene scene, out PlayableDirector director, out string error)
        {
            scene = default;
            director = null;
            error = "";
            if (!picks.Ready)
            {
                error = picks.Problem;
                return null;
            }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(Folder));
            var start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = picks.Start, Character = picks.StartModel };
            var goal = new ClipFrame { Kind = SourceKind.UnityClip, Motion = picks.Goal, Character = picks.GoalModel };
            var rate = picks.Start.frameRate > 0 ? picks.Start.frameRate : 30f;
            var leave = picks.StartFrame > 0 ? picks.StartFrame / (double)rate : 0;
            foreach (var name in new[] { Landing, Takes })
            {
                var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath(name));
                if (existing != null) AssetDatabase.DeleteAsset(TimelinePath(name));
                GenLayout.Create(TimelinePath(name), new List<ClipFrame> { start, goal }, picks.Character, DemoScene.Duration, name == Takes ? 3 : 1, leave, SaClipFbxExport.CanExport(out _));
            }

            var characterPath = AssetDatabase.GetAssetPath(picks.Character);
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
            if (mode == NewSceneMode.Additive) SceneManager.SetActiveScene(scene);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath(Landing));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
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
                    camera.transform.position = new Vector3(1.6f, 1.5f, 4.2f);
                    camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                }

            EditorSceneManager.SaveScene(scene, ScenePath);
            return timeline;
        }
    }
}

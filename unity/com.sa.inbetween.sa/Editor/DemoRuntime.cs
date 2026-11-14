using System.Collections.Generic;
using SA.Editor;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SA.Inbetween.Clips.Editor
{
    public static class DemoRuntime
    {
        public const string HolderName = "Inbetween Demo SA Runtime";
        public const string CharacterName = "Inbetween Demo SA Character";
        public static readonly Vector3 SaCharacterPlace = new Vector3(1.4f, 0f, 0f);
        static readonly Vector3 CameraPlace = new Vector3(2.4f, 1.5f, 4.8f);
        static readonly Vector3 CameraFocus = new Vector3(0.7f, 0.9f, 0f);

        public static string SubScenePath => DemoScene.Folder + "/" + HolderName + ".unity";

        public static SubScene Find(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == HolderName && root.TryGetComponent<SubScene>(out var sub) && sub.SceneAsset != null && AssetDatabase.GetAssetPath(sub.SceneAsset) == SubScenePath)
                    return sub;
            return null;
        }

        public static bool TryPrepare(InbetweenRecipe recipe, out string error)
        {
            error = "";
            if (Application.isPlaying) { error = "Leave Play mode first (press the Play button again)."; return false; }
            if (recipe == null || recipe.Output == null) { error = "Press Generate first."; return false; }
            var model = SaClipExport.CharacterFor(recipe);
            if (model == null) { error = "The recipe has no character."; return false; }
            if (!InbetweenDiagnostics.IsHumanoid(model)) { error = "'" + model.name + "' has no Humanoid avatar, and the SA runtime plays Humanoid characters. Set its Rig to Humanoid (Make demo scene offers Set to Humanoid)."; return false; }
            var family = FamilyClip(recipe);
            if (family == null) { error = "The recipe has no Humanoid Unity clip to set up the SA character with."; return false; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScene.ScenePath) { error = "Open the demo scene first: press Make demo scene again, or open " + DemoScene.ScenePath + "."; return false; }
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene)) { error = "The demo scene could not be saved. Save it (ctrl-S), then press the button again."; return false; }
            if (Find(scene) != null && AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath) != null) return true;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name != HolderName && root.GetComponentInChildren<SubScene>(true) != null)
                {
                    error = "The demo scene already has a SubScene ('" + root.name + "'), and the SA runtime preview needs exactly one SA character. Press Make demo scene again for a fresh demo scene, then press 4 Play in SA runtime.";
                    return false;
                }

            var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, sub);
                instance.name = CharacterName;
                instance.transform.SetPositionAndRotation(SaCharacterPlace, Quaternion.identity);
                var rig = CharacterSetup.Configure(instance);
                rig.Engine = Engine.Cpu;
                rig.DiscoverClips = false;
                rig.Autoplay = null;
                var shared = rig.gameObject.AddComponent<SharedFamilyAuthoring>();
                shared.Rig = rig;
                shared.Clips = new List<AnimationClip> { family };
                shared.Autoplay = family;
                if (!EditorSceneManager.SaveScene(sub, SubScenePath)) { error = "Could not save " + SubScenePath + ". Check that the folder is writable."; return false; }
            }
            finally
            {
                EditorSceneManager.CloseScene(sub, true);
            }

            AssetDatabase.ImportAsset(SubScenePath, ImportAssetOptions.ForceSynchronousImport);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == HolderName) Object.DestroyImmediate(root);
            var holder = new GameObject(HolderName);
            SceneManager.MoveGameObjectToScene(holder, scene);
            var subScene = holder.AddComponent<SubScene>();
            subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
            subScene.AutoLoadScene = true;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<Camera>(out var camera))
                {
                    camera.transform.position = CameraPlace;
                    camera.transform.LookAt(CameraFocus);
                }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { error = "The demo scene could not be saved."; return false; }
            return true;
        }

        public static bool TryPlay(InbetweenRecipe recipe, out string error)
        {
            if (Application.isPlaying)
            {
                error = "";
                if (recipe == null || recipe.Output == null) { error = "Press Generate first."; return false; }
                RecipePreview.Play(recipe.Output);
                return true;
            }

            if (RecipeRunner.AnyRunning()) { error = "Wait until generating finishes, then press the button again."; return false; }
            if (!TryPrepare(recipe, out error)) return false;
            RecipePreview.Play(recipe.Output);
            return true;
        }

        static AnimationClip FamilyClip(InbetweenRecipe recipe)
        {
            foreach (var goal in recipe.Goals)
                if (goal.Kind == SourceKind.UnityClip && goal.Motion != null && goal.Motion.humanMotion)
                    return goal.Motion;
            return recipe.Start.Kind == SourceKind.UnityClip && recipe.Start.Motion != null && recipe.Start.Motion.humanMotion ? recipe.Start.Motion : null;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using SA.Editor;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SA.Inbetween.Clips.Editor
{
    public static class SaCharacterSetup
    {
        public const string PlayerName = "SA Clip Player";
        public const string Suffix = " SA Character";

        public static string FamilyClipPath(SaClipAsset clip, GameObject model)
        {
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip))?.Replace('\\', '/') ?? "Assets";
            if (folder.StartsWith("Packages/", System.StringComparison.Ordinal)) folder = "Assets";
            return folder + "/" + clip.name + "_" + TimelineSafe(model.name) + "_Family.anim";
        }

        static string TimelineSafe(string name) => string.Join("_", (name ?? "Character").Split(Path.GetInvalidFileNameChars()));

        public static bool TryFamilyClip(SaClipAsset clip, GameObject model, out AnimationClip family, out string error)
        {
            family = null;
            var path = FamilyClipPath(clip, model);
            if (InbetweenDiagnostics.IsHumanoid(model))
            {
                if (!SaClipHumanoidExport.TryBuild(clip, model, out var built, out error)) return false;
                family = SaClipExport.SaveAs(built, path);
                return family != null;
            }

            return SaClipExport.TryExport(clip, model, path, out family, out error);
        }

        public static bool TryCreate(GameObject model, SaClipAsset clip, Scene scene, Vector3 place, out SaClipPlayer player, out string error)
        {
            player = null;
            error = "";
            if (Application.isPlaying) { error = "Leave Play mode first."; return false; }
            if (model == null || !EditorUtility.IsPersistent(model)) { error = "Choose the character's model or prefab from the Project window."; return false; }
            if (model.GetComponentInChildren<Animator>(true) == null) { error = "'" + model.name + "' has no Animator. Choose a character model or prefab."; return false; }
            if (clip == null || clip.Bytes == null || clip.Bytes.Length == 0) { error = "Choose a baked .saclip."; return false; }
            if (!SaClipPose.TryLoad(clip.Bytes, out _, out error)) { error = "The .saclip cannot be read: " + error; return false; }
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) { error = "Save the scene first, so the character's SubScene can sit next to it."; return false; }
            if (!TryFamilyClip(clip, model, out var family, out error)) return false;
            var folder = Path.GetDirectoryName(scene.path)?.Replace('\\', '/') ?? "Assets";
            var subPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + TimelineSafe(model.name) + Suffix + ".unity");
            var modelPath = AssetDatabase.GetAssetPath(model);
            var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), sub);
                instance.name = model.name + Suffix;
                instance.transform.SetPositionAndRotation(place, Quaternion.identity);
                var rig = CharacterSetup.Configure(instance);
                rig.Engine = Engine.Cpu;
                rig.DiscoverClips = false;
                rig.Autoplay = null;
                var shared = rig.gameObject.AddComponent<SharedFamilyAuthoring>();
                shared.Rig = rig;
                shared.Clips = new List<AnimationClip> { family };
                shared.Autoplay = null;
                shared.TransitionSeconds = 0f;
                if (!EditorSceneManager.SaveScene(sub, subPath)) { error = "Could not save " + subPath + "."; return false; }
            }
            finally
            {
                EditorSceneManager.CloseScene(sub, true);
            }

            AssetDatabase.ImportAsset(subPath, ImportAssetOptions.ForceSynchronousImport);
            SceneManager.SetActiveScene(scene);
            var holder = new GameObject(model.name + Suffix);
            SceneManager.MoveGameObjectToScene(holder, scene);
            var subScene = holder.AddComponent<SubScene>();
            subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(subPath);
            subScene.AutoLoadScene = true;
            var playerObject = new GameObject(PlayerName);
            SceneManager.MoveGameObjectToScene(playerObject, scene);
            player = playerObject.AddComponent<SaClipPlayer>();
            player.Clip = clip;
            player.Loop = true;
            player.PlayOnStart = true;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { error = "The scene could not be saved."; return false; }
            return true;
        }

        [MenuItem("Assets/SA/Add SA Character That Plays This Clip", false, 313)]
        static void FromSelection()
        {
            SaClipAsset clip = null;
            GameObject model = null;
            foreach (var selected in Selection.objects)
            {
                if (selected is SaClipAsset c && clip == null) clip = c;
                else if (selected is GameObject g && model == null && g.GetComponentInChildren<Animator>(true) != null) model = g;
            }

            if (clip == null || model == null)
            {
                EditorUtility.DisplayDialog("SA character", "Select a .saclip and the character's model or prefab in the Project window, then try again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!TryCreate(model, clip, SceneManager.GetActiveScene(), Vector3.zero, out var player, out var error))
            {
                EditorUtility.DisplayDialog("SA character", error, "OK");
                return;
            }

            Selection.activeObject = player.gameObject;
            EditorUtility.DisplayDialog("SA character", "Added '" + model.name + Suffix + "' (a SubScene with the SA character) and '" + PlayerName + "' playing '" + clip.name + "'. Press Play: the SA runtime plays the clip; no NVIDIA file is needed. It works the same in a build.", "OK");
        }

        [MenuItem("Assets/SA/Add SA Character That Plays This Clip", true)]
        static bool CanFromSelection() => Selection.GetFiltered<SaClipAsset>(SelectionMode.Assets).Length > 0;
    }
}

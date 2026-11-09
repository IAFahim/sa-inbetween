using System.IO;
using UnityEditor;
using UnityEngine;
#if SA_FBX
using System;
using UnityEditor.Animations;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEditor.SceneManagement;
#endif

namespace SA.Inbetween.Clips.Editor
{
    public static class SaClipFbxExport
    {
        public const string NativeProblem = "The FBX SDK native library could not load";

#if SA_FBX
        public static bool NativeReady(out string error)
        {
            error = "";
            try
            {
                var manager = Autodesk.Fbx.FbxManager.Create();
                manager?.Destroy();
                return true;
            }
            catch (Exception e) when (e is TypeInitializationException || e is DllNotFoundException)
            {
                var inner = e;
                while (inner.InnerException != null) inner = inner.InnerException;
                error = NativeProblem + ": " + inner.Message.Split('\n')[0];
                return false;
            }
        }
#endif

        public const string MissingPackage = "Install the FBX Exporter package (com.unity.formats.fbx, built into the Editor) to export FBX.";

#if SA_FBX
        public static bool Available => true;
#else
        public static bool Available => false;
#endif

        public static string ExportPath(InbetweenRecipe recipe)
        {
            var path = SaClipExport.ExportPath(recipe);
            return path == "" ? "" : path.Substring(0, path.Length - ".anim".Length) + ".fbx";
        }

        public static bool TryExport(InbetweenRecipe recipe, out string path, out string error)
        {
            path = ExportPath(recipe);
            var character = SaClipExport.CharacterFor(recipe);
            if (recipe.Output == null) { error = "Generate the motion first."; return false; }
            if (character == null) { error = "Choose the character to export for."; return false; }
            if (path == "") { error = "Save the recipe as an asset first."; return false; }
            return TryExport(recipe.Output, character, path, out error);
        }

        static void ClearDirty(UnityEngine.SceneManagement.Scene scene)
        {
            var method = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(null, new object[] { scene });
        }

        public static bool TryExport(SaClipAsset source, GameObject character, string path, out string error)
        {
#if SA_FBX
            if (!NativeReady(out error)) return false;
            if (!SaClipExport.TryBuild(source, character, out var clip, out error)) return false;
            clip.name = Path.GetFileNameWithoutExtension(path);
            var clean = new System.Collections.Generic.List<UnityEngine.SceneManagement.Scene>();
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (open.isLoaded && !open.isDirty) clean.Add(open);
            }

            var scene = EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(character, scene);
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(character);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
            }
            instance.name = character.name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var controller = new AnimatorController { name = clip.name, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                controller.AddLayer("Base Layer");
                controller.layers[0].stateMachine.AddState(clip.name).motion = clip;
                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = null;
                animator.runtimeAnimatorController = controller;
                var options = new ExportModelOptions
                {
                    ExportFormat = ExportFormat.Binary,
                    ModelAnimIncludeOption = Include.ModelAndAnim,
                    ObjectPosition = ObjectPosition.WorldAbsolute,
                };
                var written = ModelExporter.ExportObject(Path.GetFullPath(path), instance, options);
                if (string.IsNullOrEmpty(written))
                {
                    error = "The FBX Exporter did not write " + path;
                    return false;
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                error = "";
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(controller, true);
                UnityEngine.Object.DestroyImmediate(clip);
                foreach (var open in clean)
                    if (open.IsValid() && open.isDirty) ClearDirty(open);
            }
#else
            error = MissingPackage;
            return false;
#endif
        }
    }
}

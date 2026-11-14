using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if SA_FBX
using System;
using UnityEditor.Animations;
using UnityEditor.Formats.Fbx.Exporter;
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
                error = LinuxLibraryMissing("libxml2.so.2")
                    ? NativeProblem + " because the system library libxml2.so.2 is missing. Link libxml2.so.16 as libxml2.so.2 in a folder on LD_LIBRARY_PATH and restart Unity (sa-inbetween docs/install.md, \"FBX export on Linux\")."
                    : NativeProblem + ": " + inner.Message.Split('\n')[0];
                return false;
            }
        }
#endif

        const int LoadLazily = 1;

        [System.Runtime.InteropServices.DllImport("libdl.so.2", EntryPoint = "dlopen")]
        static extern System.IntPtr OpenLibrary(string fileName, int flags);

        [System.Runtime.InteropServices.DllImport("libdl.so.2", EntryPoint = "dlclose")]
        static extern int CloseLibrary(System.IntPtr handle);

        public static bool LinuxLibraryMissing(string fileName)
        {
            if (Application.platform != RuntimePlatform.LinuxEditor)
                return false;
            try
            {
                var handle = OpenLibrary(fileName, LoadLazily);
                if (handle == System.IntPtr.Zero)
                    return true;
                CloseLibrary(handle);
                return false;
            }
            catch (System.Exception e) when (e is System.DllNotFoundException || e is System.EntryPointNotFoundException)
            {
                return false;
            }
        }

        public const string MissingPackage = "To export FBX, add the FBX Exporter: \"com.unity.formats.fbx\": \"5.1.6\" in Packages/manifest.json (it ships with the Editor).";

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
                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    importer.importAnimation = true;
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.clipAnimations = new ModelImporterClipAnimation[0];
                    importer.SaveAndReimport();
                }

                var hasTake = false;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip imported && !imported.name.StartsWith("__preview__", StringComparison.Ordinal) && imported.length > 0f) hasTake = true;
                if (!hasTake)
                {
                    error = "The FBX was written but Unity found no animation take in it: " + path;
                    return false;
                }

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

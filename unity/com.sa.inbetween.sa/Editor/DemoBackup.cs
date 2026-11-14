using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SA.Inbetween.Clips.Editor
{
    public static class DemoBackup
    {
        public const string FolderName = "SA Inbetween Demo Backup";
        public const string GenerationDemoFolder = "Assets/SA Inbetween Generation Demo";
        public const string SaMaterialsFolder = "Assets/SA/Materials";
        const string StampFile = "backup.txt";

        public static readonly string[] MirroredFolders = { DemoScene.Folder, GenerationDemoFolder };
        public static readonly string[] AddedFolders = { SaMaterialsFolder };

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string Root => Path.Combine(ProjectRoot, FolderName);

        public static bool Exists => File.Exists(Path.Combine(Root, StampFile)) && Directory.Exists(Path.Combine(Root, DemoScene.Folder));

        public static string Describe()
        {
            if (!Exists) return "";
            var stamp = File.ReadAllLines(Path.Combine(Root, StampFile));
            return stamp.Length > 0 ? stamp[0] : "";
        }

        public static bool TrySave(out string report)
        {
            report = "";
            if (Application.isPlaying) { report = "Leave Play mode first."; return false; }
            var recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
            if (recipe == null || recipe.Output == null) { report = "Make the demo first: Make demo scene, then Generate and Save. The backup keeps that finished result."; return false; }
            if (RecipeRunner.AnyRunning()) { report = "Wait until generating finishes, then save the backup."; return false; }
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (open.isDirty && IsBackedUp(open.path) && !EditorSceneManager.SaveScene(open)) { report = "Save the open scene " + open.path + " first."; return false; }
            }

            AssetDatabase.SaveAssets();
            var temporary = Root + ".partial";
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            var files = 0;
            foreach (var folder in Folders())
                if (AssetDatabase.IsValidFolder(folder))
                {
                    files += CopyTree(Path.Combine(ProjectRoot, folder), Path.Combine(temporary, folder));
                    var meta = Path.Combine(ProjectRoot, folder + ".meta");
                    if (File.Exists(meta)) Copy(meta, Path.Combine(temporary, folder + ".meta"));
                }

            var stamp = "Backup of the finished demo, saved " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (" + files + " files).";
            File.WriteAllText(Path.Combine(temporary, StampFile), stamp + "\n" + string.Join("\n", Folders()) + "\n");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.Move(temporary, Root);
            report = stamp + " It is in the project folder, next to Assets: " + FolderName + ". Restore it with SA > Inbetween > Demo Backup > Restore, or the Use the backup button in the First Bridge window.";
            return true;
        }

        public static bool TryRestore(out string report)
        {
            report = "";
            if (!Exists) { report = "There is no backup yet. Make the demo once, then choose SA > Inbetween > Demo Backup > Save."; return false; }
            if (Application.isPlaying) { report = "Leave Play mode first (press the Play button again), then restore."; return false; }
            if (RecipeRunner.AnyRunning()) { report = "Something is still generating. Press Cancel first, then restore."; return false; }
            var reopen = "";
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (IsBackedUp(open.path) && reopen == "") reopen = open.path;
            }

            if (reopen != "")
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RecipeRunner.ForgetAll();
            var restored = 0;
            var removed = 0;
            var written = new List<string>();
            foreach (var folder in MirroredFolders)
            {
                var saved = Path.Combine(Root, folder);
                var live = Path.Combine(ProjectRoot, folder);
                if (!Directory.Exists(saved) || !Directory.Exists(live)) continue;
                foreach (var file in Directory.GetFiles(live, "*", SearchOption.AllDirectories))
                    if (!File.Exists(Path.Combine(saved, Path.GetRelativePath(live, file))))
                        removed++;
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(live)) Directory.Delete(live, true);
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var folder in MirroredFolders)
                {
                    var saved = Path.Combine(Root, folder);
                    if (!Directory.Exists(saved)) continue;
                    restored += CopyTree(saved, Path.Combine(ProjectRoot, folder), written);
                    var meta = Path.Combine(Root, folder + ".meta");
                    if (File.Exists(meta)) Copy(meta, Path.Combine(ProjectRoot, folder + ".meta"));
                }

                foreach (var folder in AddedFolders)
                {
                    var saved = Path.Combine(Root, folder);
                    if (Directory.Exists(saved)) restored += CopyTree(saved, Path.Combine(ProjectRoot, folder), written);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var path in ImportOrder(written))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var scene = reopen != "" ? reopen : DemoScene.ScenePath;
            if (File.Exists(Path.Combine(ProjectRoot, scene))) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
            var state = recipe == null ? "the demo recipe is missing from the backup" : "the demo recipe says " + StateText(RecipeRunner.StateOf(recipe));
            report = "Restored the backup (" + Describe() + "): " + restored + " files back" + (removed > 0 ? ", " + removed + " newer files removed" : "") + "; " + state + ".";
            return recipe != null;
        }

        static string StateText(RecipeState state) => state == RecipeState.UpToDate ? "Up to date" : state.ToString();

        static IEnumerable<string> Folders()
        {
            foreach (var folder in MirroredFolders) yield return folder;
            foreach (var folder in AddedFolders) yield return folder;
        }

        static bool IsBackedUp(string assetPath)
        {
            foreach (var folder in MirroredFolders)
                if (assetPath.StartsWith(folder + "/", StringComparison.Ordinal))
                    return true;
            return false;
        }

        static int CopyTree(string from, string to, List<string> written = null)
        {
            var count = 0;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal)) continue;
                var target = Path.Combine(to, Path.GetRelativePath(from, file));
                Copy(file, target);
                if (written != null && !target.EndsWith(".meta", StringComparison.Ordinal))
                    written.Add(Path.GetRelativePath(ProjectRoot, target).Replace('\\', '/'));
                count++;
            }

            return count;
        }

        static void Copy(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to) ?? ".");
            File.Copy(from, to, true);
            File.SetLastWriteTimeUtc(to, DateTime.UtcNow);
        }

        static IEnumerable<string> ImportOrder(List<string> paths)
        {
            foreach (var path in paths)
                if (!path.EndsWith(".asset", StringComparison.Ordinal) && !path.EndsWith(".unity", StringComparison.Ordinal) && !path.EndsWith(".playable", StringComparison.Ordinal))
                    yield return path;
            foreach (var path in paths)
                if (path.EndsWith(".asset", StringComparison.Ordinal) || path.EndsWith(".playable", StringComparison.Ordinal))
                    yield return path;
            foreach (var path in paths)
                if (path.EndsWith(".unity", StringComparison.Ordinal))
                    yield return path;
        }

        [MenuItem("SA/Inbetween/Demo Backup/Save", false, 40)]
        static void SaveFromMenu()
        {
            var ok = TrySave(out var report);
            EditorUtility.DisplayDialog("Demo backup", report, "OK");
            if (ok) Debug.Log("SA Inbetween: " + report);
        }

        [MenuItem("SA/Inbetween/Demo Backup/Restore", false, 41)]
        static void RestoreFromMenu()
        {
            if (!Exists)
            {
                EditorUtility.DisplayDialog("Demo backup", "There is no backup yet. Make the demo once (Generate and Save), then choose SA > Inbetween > Demo Backup > Save.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Restore the demo backup", Describe() + "\n\nThis puts the demo folders back as they were then. Changes made to them since are replaced.", "Restore", "Cancel")) return;
            var ok = TryRestore(out var report);
            EditorUtility.DisplayDialog("Demo backup", report, "OK");
            if (ok) DemoWindow.Open(false);
        }
    }
}

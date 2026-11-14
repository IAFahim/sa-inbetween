using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class ClipFileUndo : ScriptableObject
    {
        public string Path = "";
        public InbetweenRecipe Recipe;
        public string Snapshot = "";
        public int Version;

        [NonSerialized] int applied = -1;

        void OnEnable() => applied = Version;

        public bool Pending => applied != Version;

        public static string Store => System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "SaInbetween", "undo");

        public void Commit(byte[] after)
        {
            Snapshot = Keep(after);
            Version++;
            applied = Version;
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedo;
        }

        static void OnUndoRedo(in UndoRedoInfo info)
        {
            if (info.undoName != RecipeVariations.KeepUndoName && info.undoName != RecipeRunner.GenerateUndoName)
                return;
            foreach (var record in Resources.FindObjectsOfTypeAll<ClipFileUndo>())
                if (record.Pending)
                    record.Sync();
        }

        public static ClipFileUndo For(string path, InbetweenRecipe recipe)
        {
            ClipFileUndo found = null;
            foreach (var record in Resources.FindObjectsOfTypeAll<ClipFileUndo>())
                if (string.Equals(record.Path, path, StringComparison.OrdinalIgnoreCase))
                    found = record;
            if (found == null)
            {
                found = CreateInstance<ClipFileUndo>();
                found.hideFlags = HideFlags.HideAndDontSave;
                found.Path = path;
            }

            found.Recipe = recipe;
            found.Snapshot = Keep(File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>());
            found.applied = found.Version;
            return found;
        }

        static string Keep(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            using var sha = SHA1.Create();
            var name = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Directory.CreateDirectory(Store);
            var file = System.IO.Path.Combine(Store, name);
            if (!File.Exists(file)) File.WriteAllBytes(file, bytes);
            return file;
        }

        public static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        public void Sync()
        {
            applied = Version;
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                return;
            if (Snapshot != "" && !File.Exists(Snapshot))
            {
                Debug.LogWarning("SA Inbetween: undo cannot restore " + Path + " because its saved copy in Library/SaInbetween/undo is gone.");
                return;
            }

            var wanted = Snapshot == "" ? Array.Empty<byte>() : File.ReadAllBytes(Snapshot);
            var current = File.Exists(Path) ? File.ReadAllBytes(Path) : Array.Empty<byte>();
            if (Same(current, wanted))
                return;
            try
            {
                if (wanted.Length == 0)
                    AssetDatabase.DeleteAsset(Path);
                else
                {
                    File.WriteAllBytes(Path, wanted);
                    AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("SA Inbetween: undo could not restore " + Path + ": " + e.Message);
                return;
            }

            if (Recipe == null)
                return;
            if (wanted.Length > 0 && Recipe.Output == null)
                Recipe.Output = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Path);
            EditorUtility.SetDirty(Recipe);
            AssetDatabase.SaveAssetIfDirty(Recipe);
        }
    }
}

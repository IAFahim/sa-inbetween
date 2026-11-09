using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class ClipFileUndo : ScriptableObject
    {
        public string Path = "";
        public InbetweenRecipe Recipe;
        public byte[] Bytes = Array.Empty<byte>();

        [InitializeOnLoadMethod]
        static void Hook()
        {
            Undo.undoRedoEvent -= OnUndoRedo;
            Undo.undoRedoEvent += OnUndoRedo;
        }

        static void OnUndoRedo(in UndoRedoInfo info)
        {
            if (info.undoName != RecipeVariations.KeepUndoName)
                return;
            foreach (var record in Resources.FindObjectsOfTypeAll<ClipFileUndo>())
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
            found.Bytes = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
            return found;
        }

        public static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        public void Sync()
        {
            var current = File.Exists(Path) ? File.ReadAllBytes(Path) : Array.Empty<byte>();
            if (Same(current, Bytes))
                return;
            if (Bytes.Length == 0)
                AssetDatabase.DeleteAsset(Path);
            else
            {
                File.WriteAllBytes(Path, Bytes);
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            }

            if (Recipe == null)
                return;
            if (Bytes.Length > 0 && Recipe.Output == null)
                Recipe.Output = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Path);
            EditorUtility.SetDirty(Recipe);
            AssetDatabase.SaveAssetIfDirty(Recipe);
        }
    }
}

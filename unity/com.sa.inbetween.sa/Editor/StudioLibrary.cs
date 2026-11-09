using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [Serializable]
    public sealed class LibraryEntry
    {
        public string name;
        public string group;
        public float time;
        public string[] files;
    }

    [Serializable]
    sealed class LibraryFile
    {
        public int version;
        public List<LibraryEntry> entries;
    }

    public sealed class StudioLibrary
    {
        public readonly List<LibraryEntry> Entries = new List<LibraryEntry>();
        public string Folder = "";

        public string[] Labels()
        {
            var labels = new string[Entries.Count];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = Entries[i].group + "/" + Entries[i].name;
            return labels;
        }

        public string[] PathsOf(int index)
        {
            var e = Entries[index];
            var paths = new string[e.files.Length];
            for (var i = 0; i < paths.Length; i++)
                paths[i] = Path.Combine(Folder, e.files[i]);
            return paths;
        }

        public int IndexOf(string name)
        {
            for (var i = 0; i < Entries.Count; i++)
                if (Entries[i].name == name)
                    return i;
            return -1;
        }

        public static bool TryLoad(string libraryJson, out StudioLibrary library, out string error)
        {
            library = new StudioLibrary();
            error = "";
            if (string.IsNullOrEmpty(libraryJson) || !File.Exists(libraryJson))
            {
                error = "No pose library found";
                return false;
            }

            LibraryFile file;
            try
            {
                file = JsonUtility.FromJson<LibraryFile>(File.ReadAllText(libraryJson));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                error = "Cannot read the pose library: " + e.Message;
                return false;
            }

            if (file == null || file.version != 1 || file.entries == null || file.entries.Count == 0)
            {
                error = "The pose library is empty or has an unknown version";
                return false;
            }

            library.Folder = Path.GetDirectoryName(libraryJson) ?? "";
            foreach (var e in file.entries)
            {
                if (e == null || string.IsNullOrEmpty(e.name) || e.files == null || e.files.Length != 4)
                    continue;
                var ok = true;
                foreach (var f in e.files)
                    ok &= !string.IsNullOrEmpty(f) && File.Exists(Path.Combine(library.Folder, f));
                if (ok)
                    library.Entries.Add(e);
            }

            if (library.Entries.Count == 0)
            {
                error = "The pose library lists no complete poses";
                return false;
            }

            return true;
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public static class InbetweenSettings
    {
        const string ToolsKey = "SA.Inbetween.tools";
        const string VerifyKey = "SA.Inbetween.verify";

        public static string ToolsFolder
        {
            get => EditorPrefs.GetString(ToolsKey, "");
            set => EditorPrefs.SetString(ToolsKey, value ?? "");
        }

        public static bool VerifyModel
        {
            get => EditorPrefs.GetBool(VerifyKey, true);
            set => EditorPrefs.SetBool(VerifyKey, value);
        }

        public static string ProjectFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SaInbetween");

        public static StudioTools Tools()
        {
            var tools = StudioDiscovery.Find(ToolsFolder);
            return tools.Complete ? tools : Merge(tools, StudioDiscovery.Find(ProjectFolder));
        }

        static StudioTools Merge(StudioTools a, StudioTools b)
        {
            if (a.Cli == "") a.Cli = b.Cli;
            if (a.Bake == "") a.Bake = b.Bake;
            if (a.Model == "") a.Model = b.Model;
            if (a.Family == "") a.Family = b.Family;
            if (a.Rest == "") a.Rest = b.Rest;
            return a;
        }

        [SettingsProvider]
        public static SettingsProvider Provider() => new SettingsProvider("Project/SA Inbetween", SettingsScope.Project)
        {
            label = "SA Inbetween",
            guiHandler = _ => Draw(),
            keywords = new[] { "inbetween", "motion", "bridge", "generate" },
        };

        static void Draw()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Inbetween makes the motion between two poses. It needs a folder holding the generator, the baker, the model and your character's family file. Put that folder in <your project>/SaInbetween and it is found on its own, or pick it here.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var folder = EditorGUILayout.TextField(new GUIContent("Tools folder", "Searched up to four levels deep"), ToolsFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(64)))
            {
                var picked = EditorUtility.OpenFolderPanel("Select the tools folder", string.IsNullOrEmpty(folder) ? "" : folder, "");
                if (!string.IsNullOrEmpty(picked)) folder = picked;
                GUIUtility.ExitGUI();
            }

            if (EditorGUI.EndChangeCheck()) ToolsFolder = folder;
            EditorGUILayout.EndHorizontal();
            VerifyModel = EditorGUILayout.Toggle(new GUIContent("Check model file", "Verifies the model before each run. A fraction of a second."), VerifyModel);
            var t = Tools();
            EditorGUILayout.Space(4);
            Row("Generator", t.Cli);
            Row("Baker", t.Bake);
            Row("Model", t.Model);
            Row("Character family", t.Family);
            Row("Rest pose", t.Rest);
            if (!t.Complete)
                EditorGUILayout.HelpBox("Still missing: " + string.Join(", ", t.Missing()), MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Ready. Select two clips in the Project window and choose Assets > Create > SA > Inbetween Bridge.", MessageType.None);
        }

        static void Row(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150));
            var found = path != "";
            var old = GUI.color;
            GUI.color = found ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.95f, 0.5f, 0.4f);
            GUILayout.Label(found ? "found" : "missing", GUILayout.Width(55));
            GUI.color = old;
            EditorGUILayout.LabelField(found ? Path.GetFileName(path) : "", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public static class InbetweenSettings
    {
        const string ToolsKey = "SA.Inbetween.tools";
        const string VerifyKey = "SA.Inbetween.verify";
        const string StepLimitKey = "SA.Inbetween.stepLimitSeconds";
        public const float DefaultStepLimitSeconds = 120f;

        public static float StepLimitSeconds
        {
            get => EditorPrefs.GetFloat(StepLimitKey, DefaultStepLimitSeconds);
            set => EditorPrefs.SetFloat(StepLimitKey, value);
        }

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

        public static string SetupAdvice(StudioTools t)
        {
            if (t.Complete) return "";
            var steps = new System.Collections.Generic.List<string>();
            if (t.Cli == "" || t.Bake == "" || t.Family == "")
                steps.Add("Download SaInbetween-tools for " + (Application.platform == RuntimePlatform.WindowsEditor ? "Windows (win-x64 .zip)" : "Linux (linux-x64 .tar.gz)") + " from the sa-inbetween release page and unpack it into the project folder, next to Assets, so the folder is called SaInbetween (docs/install.md step 2).");
            if (t.Model == "")
                steps.Add("Put NVIDIA's model mmm_v6_soma_fp32.onnx (757,864,273 bytes) in SaInbetween/model (docs/install.md step 3).");
            if (t.Rest == "")
                steps.Add("Put soma_rest.pose (2,156 bytes) in SaInbetween (docs/install.md step 3).");
            return "Missing: " + string.Join(", ", t.Missing()) + ". To fix: " + string.Join(" ", steps) + " Inbetween finds the files on its own; this message goes away when they are there.";
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
                EditorGUILayout.HelpBox(SetupAdvice(t), MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Ready. New here? Choose SA > Inbetween > Create Demo Scene. Otherwise select two clips in the Project window and choose Assets > Create > SA > Inbetween Bridge.", MessageType.None);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy diagnostics", GUILayout.Width(140)))
                InbetweenDiagnostics.Copy(null);
            if (GUILayout.Button("Install guide", GUILayout.Width(110)))
                Application.OpenURL(InbetweenDiagnostics.InstallUrl);
            EditorGUILayout.EndHorizontal();
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

using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class InbetweenStudioWindow : EditorWindow
    {
        const string Key = "SA.Inbetween.Studio.";

        StudioSettings s = new StudioSettings();
        StudioProcess job;
        string stage = "";
        string status = "Ready";
        MessageType statusType = MessageType.Info;
        Vector2 logScroll;
        bool bakeAfter;

        [MenuItem("Window/SA/Inbetween Studio")]
        static void Open()
        {
            var w = GetWindow<InbetweenStudioWindow>("Inbetween Studio");
            w.minSize = new Vector2(420, 480);
        }

        void OnEnable()
        {
            s.CliPath = EditorPrefs.GetString(Key + "cli", "");
            s.BakePath = EditorPrefs.GetString(Key + "bake", "");
            s.ModelPath = EditorPrefs.GetString(Key + "model", "");
            s.FamilyPath = EditorPrefs.GetString(Key + "family", "");
            s.OutputFolder = EditorPrefs.GetString(Key + "out", "Assets/Generated");
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            job?.Kill();
        }

        void Save()
        {
            EditorPrefs.SetString(Key + "cli", s.CliPath);
            EditorPrefs.SetString(Key + "bake", s.BakePath);
            EditorPrefs.SetString(Key + "model", s.ModelPath);
            EditorPrefs.SetString(Key + "family", s.FamilyPath);
            EditorPrefs.SetString(Key + "out", s.OutputFolder);
        }

        void OnGUI()
        {
            var busy = job != null && job.Running;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            s.CliPath = FileRow("sa-inbetween", s.CliPath, "Select sa-inbetween (executable or .dll)");
            s.BakePath = FileRow("sa-soma-bake", s.BakePath, "Select sa-soma-bake (executable or .dll)");
            s.ModelPath = FileRow("Model (.onnx)", s.ModelPath, "Select mmm_v6_soma_fp32.onnx", "onnx");
            s.FamilyPath = FileRow("Family", s.FamilyPath, "Select humanoid-v1.safamily", "safamily");
            if (EditorGUI.EndChangeCheck())
                Save();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
            s.ContextFile = FileRow("Context", s.ContextFile, "Select a .saib or .pose context", "");
            s.ContextEnd = EditorGUILayout.IntField(new GUIContent("Context end frame", "-1 uses the last frame of a .saib"), s.ContextEnd);
            for (var i = 0; i < s.Targets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                s.Targets[i] = EditorGUILayout.TextField("Target " + (i + 1), s.Targets[i]);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    var p = EditorUtility.OpenFilePanel("Target pose or motion", "", "");
                    if (!string.IsNullOrEmpty(p)) s.Targets[i] = p;
                }

                using (new EditorGUI.DisabledScope(s.Targets.Count == 1))
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    {
                        s.Targets.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add chained target", GUILayout.Width(150)))
                s.Targets.Add("");
            EditorGUILayout.HelpBox("A target is a .pose, a .saib:FRAME, or up to four comma separated poses.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            s.ClipName = EditorGUILayout.TextField("Clip name", s.ClipName);
            s.OutputFolder = EditorGUILayout.TextField("Folder", s.OutputFolder);
            s.TokensMin = EditorGUILayout.IntSlider("Tokens min", s.TokensMin, 6, 16);
            s.TokensMax = EditorGUILayout.IntSlider("Tokens max", s.TokensMax, 6, 16);
            s.Correct = EditorGUILayout.Toggle("Endpoint correction", s.Correct);
            s.VerifyModel = EditorGUILayout.Toggle("Verify model hash", s.VerifyModel);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Generate and bake", GUILayout.Height(28)))
                    Run(true);
                if (GUILayout.Button("Generate only", GUILayout.Height(28)))
                    Run(false);
                if (GUILayout.Button("Bake only", GUILayout.Height(28)))
                    StartBake();
                EditorGUILayout.EndHorizontal();
            }

            if (busy && GUILayout.Button("Cancel"))
                job.Kill();

            EditorGUILayout.HelpBox((busy ? stage + "... " : "") + status, busy ? MessageType.Info : statusType);
            if (job != null)
            {
                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(100));
                EditorGUILayout.SelectableLabel(job.Log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        string FileRow(string label, string value, string title, string ext = "")
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.TextField(label, value);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                var p = EditorUtility.OpenFilePanel(title, string.IsNullOrEmpty(value) ? "" : Path.GetDirectoryName(value), ext);
                if (!string.IsNullOrEmpty(p)) value = p;
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
            return value;
        }

        void Run(bool thenBake)
        {
            bakeAfter = thenBake;
            if (!StudioCommands.TryGenerate(s, out var cmd, out var err))
            {
                Fail(err);
                return;
            }

            Directory.CreateDirectory(s.OutputFolder);
            Begin("Generating", cmd);
        }

        void StartBake()
        {
            bakeAfter = false;
            if (!StudioCommands.TryBake(s, out var cmd, out var err))
            {
                Fail(err);
                return;
            }

            if (!File.Exists(StudioCommands.SaibPath(s)))
            {
                Fail("No " + StudioCommands.SaibPath(s) + " yet; generate first");
                return;
            }

            Begin("Baking", cmd);
        }

        void Begin(string name, StudioCommand cmd)
        {
            job = new StudioProcess();
            stage = name;
            if (!job.Start(cmd, out var err))
            {
                Fail(err);
                return;
            }

            status = name + "...";
            statusType = MessageType.Info;
        }

        void Fail(string message)
        {
            status = message;
            statusType = MessageType.Error;
            Repaint();
        }

        void Tick()
        {
            if (job == null || job.Running || job.ExitCode == int.MinValue || job.Handled)
            {
                if (job != null && job.Running) Repaint();
                return;
            }

            var code = job.ExitCode;
            var finished = stage;
            job.MarkHandled();
            if (code != 0)
            {
                Fail(finished + " failed (exit " + code + "). See the log.");
                return;
            }

            if (finished == "Generating" && bakeAfter)
            {
                StartBake();
                return;
            }

            if (finished == "Baking")
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(StudioCommands.ClipPath(s).Replace('\\', '/'));
                status = clip != null ? "Done. Imported " + StudioCommands.ClipPath(s) + ". Assign it to a SaClipPlayer." : "Baked, but the editor did not import the clip.";
                statusType = clip != null ? MessageType.Info : MessageType.Warning;
                if (clip != null) EditorGUIUtility.PingObject(clip);
            }
            else
            {
                AssetDatabase.Refresh();
                status = "Generated " + StudioCommands.SaibPath(s) + ". Bake it next.";
                statusType = MessageType.Info;
            }

            Repaint();
        }
    }
}

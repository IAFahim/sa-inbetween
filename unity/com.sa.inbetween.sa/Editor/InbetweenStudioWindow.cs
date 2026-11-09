using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class InbetweenStudioWindow : EditorWindow
    {
        const string Key = "SA.Inbetween.Studio.";

        readonly StudioSettings s = new StudioSettings();
        StudioLibrary library = new StudioLibrary();
        StudioTools tools;
        string toolsFolder = "";
        string libraryError = "";
        int start;
        readonly List<int> goals = new List<int> { 0 };
        bool showSetup;
        bool showAdvanced;
        StudioProcess job;
        string stage = "";
        string status = "";
        MessageType statusType = MessageType.None;
        Vector2 logScroll;
        SaClipAsset lastClip;
        double startedAt;

        [MenuItem("Window/SA/Inbetween Studio")]
        static void Open()
        {
            var w = GetWindow<InbetweenStudioWindow>("Inbetween Studio");
            w.minSize = new Vector2(380, 420);
        }

        void OnEnable()
        {
            toolsFolder = EditorPrefs.GetString(Key + "tools", "");
            s.OutputFolder = EditorPrefs.GetString(Key + "out", "Assets/Generated");
            Discover();
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            job?.Kill();
        }

        void Discover()
        {
            tools = StudioDiscovery.Find(toolsFolder);
            s.CliPath = tools.Cli;
            s.BakePath = tools.Bake;
            s.ModelPath = tools.Model;
            s.FamilyPath = tools.Family;
            library = new StudioLibrary();
            libraryError = "";
            if (tools.Library != "" && !StudioLibrary.TryLoad(tools.Library, out library, out libraryError))
                library = new StudioLibrary();
            start = Mathf.Clamp(library.IndexOf(EditorPrefs.GetString(Key + "start", "")), 0, Mathf.Max(0, library.Entries.Count - 1));
            goals[0] = Mathf.Clamp(library.IndexOf(EditorPrefs.GetString(Key + "goal", "")), 0, Mathf.Max(0, library.Entries.Count - 1));
            showSetup = !tools.Complete || library.Entries.Count == 0;
        }

        void OnGUI()
        {
            var busy = job != null && job.Running;
            var ready = tools.Complete && library.Entries.Count > 0;

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Inbetween Studio", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Pick where the character starts and where it ends up. Studio makes the motion in between.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);

            showSetup = EditorGUILayout.Foldout(showSetup, ready ? "Setup (ready)" : "Setup (needs attention)", true);
            if (showSetup)
                SetupGUI();

            if (!ready)
            {
                EditorGUILayout.HelpBox("Choose the tools folder above to begin.", MessageType.Info);
                return;
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
            var labels = library.Labels();
            start = EditorGUILayout.Popup("Start pose", start, labels);
            for (var i = 0; i < goals.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                goals[i] = EditorGUILayout.Popup(goals.Count == 1 ? "Goal pose" : "Goal " + (i + 1), goals[i], labels);
                if (goals.Count > 1 && GUILayout.Button("-", GUILayout.Width(24)))
                {
                    goals.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (goals.Count < 4 && GUILayout.Button("Add another goal after this", GUILayout.Width(190)))
                goals.Add(goals[goals.Count - 1]);

            using (new EditorGUI.DisabledScope(busy))
            {
                var typed = EditorGUILayout.TextField(new GUIContent("Name", "File name for the new clip"), DefaultName());
                if (!busy)
                    s.ClipName = typed;
            }

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                s.OutputFolder = EditorGUILayout.TextField("Save in", s.OutputFolder);
                s.TokensMin = EditorGUILayout.IntSlider(new GUIContent("Shortest", "Fewer tokens give a faster move"), s.TokensMin, 6, 16);
                s.TokensMax = EditorGUILayout.IntSlider(new GUIContent("Longest", "More tokens give a slower, longer move"), s.TokensMax, 6, 16);
                s.Correct = EditorGUILayout.Toggle(new GUIContent("Land exactly on the goal", "Endpoint correction"), s.Correct);
                s.VerifyModel = EditorGUILayout.Toggle("Check model file", s.VerifyModel);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(6);
            using (new EditorGUI.DisabledScope(busy))
                if (GUILayout.Button(goals.Count > 1 ? "Generate motion (" + goals.Count + " goals)" : "Generate motion", GUILayout.Height(32)))
                    Run();
            if (busy && GUILayout.Button("Cancel"))
                job.Kill();

            if (busy)
                EditorGUILayout.HelpBox(stage + "... " + (EditorApplication.timeSinceStartup - startedAt).ToString("0") + " s", MessageType.Info);
            else if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, statusType);

            if (lastClip != null && !busy)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Preview on character", GUILayout.Height(26)))
                    Preview(lastClip);
                if (GUILayout.Button("Show clip", GUILayout.Height(26)))
                {
                    Selection.activeObject = lastClip;
                    EditorGUIUtility.PingObject(lastClip);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (job != null && !string.IsNullOrEmpty(job.Log) && (busy || statusType == MessageType.Error))
            {
                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(80), GUILayout.MaxHeight(200));
                EditorGUILayout.SelectableLabel(job.Log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        void SetupGUI()
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            toolsFolder = EditorGUILayout.TextField(new GUIContent("Tools folder", "A folder holding sa-inbetween, sa-soma-bake, the model, a .safamily file and the pose library"), toolsFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var p = EditorUtility.OpenFolderPanel("Select the tools folder", string.IsNullOrEmpty(toolsFolder) ? "" : toolsFolder, "");
                if (!string.IsNullOrEmpty(p)) toolsFolder = p;
                GUIUtility.ExitGUI();
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(Key + "tools", toolsFolder);
                Discover();
            }

            EditorGUILayout.EndHorizontal();
            Row("Generator", tools.Cli);
            Row("Baker", tools.Bake);
            Row("Model", tools.Model);
            Row("Character family", tools.Family);
            Row("Pose library", tools.Library != "" && library.Entries.Count > 0 ? tools.Library : "");
            if (libraryError != "" && tools.Library != "")
                EditorGUILayout.HelpBox(libraryError, MessageType.Warning);
            EditorGUI.indentLevel--;
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

        string DefaultName()
        {
            if (job != null && job.Running)
                return runName;
            if (!string.IsNullOrEmpty(s.ClipName) && s.ClipName != "motion" && s.ClipName != lastAuto)
                return s.ClipName;
            lastAuto = Slug(library.Entries[start].name) + "_to_" + Slug(library.Entries[goals[goals.Count - 1]].name);
            return lastAuto;
        }

        string lastAuto = "";
        string runName = "";

        static string Slug(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in name)
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
            return sb.ToString().Trim('_');
        }

        void Run()
        {
            runName = DefaultName();
            s.ClipName = runName;
            s.Contexts = new List<string>(library.PathsOf(start));
            s.Targets = new List<string>();
            foreach (var g in goals)
                s.Targets.Add(string.Join(",", library.PathsOf(g)));
            EditorPrefs.SetString(Key + "start", library.Entries[start].name);
            EditorPrefs.SetString(Key + "goal", library.Entries[goals[0]].name);
            EditorPrefs.SetString(Key + "out", s.OutputFolder);
            lastClip = null;
            if (!StudioCommands.TryGenerate(s, out var cmd, out var err))
            {
                Fail(err);
                return;
            }

            Directory.CreateDirectory(s.OutputFolder);
            Begin("Generating", cmd);
        }

        void Begin(string name, StudioCommand cmd)
        {
            job = new StudioProcess();
            stage = name;
            startedAt = EditorApplication.timeSinceStartup;
            if (!job.Start(cmd, out var err))
            {
                Fail(err);
                return;
            }

            status = "";
            statusType = MessageType.None;
        }

        void Fail(string message)
        {
            status = message;
            statusType = MessageType.Error;
            Repaint();
        }

        void Tick()
        {
            if (job == null)
                return;
            if (job.Running)
            {
                Repaint();
                return;
            }

            if (job.ExitCode == int.MinValue || job.Handled)
                return;
            job.MarkHandled();
            var code = job.ExitCode;
            var finished = stage;
            s.ClipName = runName;
            if (code != 0)
            {
                Fail(finished + " failed (exit " + code + "). The log below says why.");
                return;
            }

            if (finished == "Generating")
            {
                if (!StudioCommands.TryBake(s, out var cmd, out var err))
                {
                    Fail(err);
                    return;
                }

                Begin("Baking", cmd);
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            var path = StudioCommands.ClipPath(s).Replace('\\', '/');
            lastClip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(path);
            if (lastClip != null)
            {
                status = "Done: " + path;
                statusType = MessageType.Info;
                EditorGUIUtility.PingObject(lastClip);
            }
            else
            {
                Fail("The clip was baked but Unity did not import " + path);
            }

            Repaint();
        }

        void Preview(SaClipAsset clip)
        {
            var player = Object.FindFirstObjectByType<SaClipPlayer>();
            if (player == null)
            {
                var go = new GameObject("Inbetween Preview");
                Undo.RegisterCreatedObjectUndo(go, "Create Inbetween Preview");
                player = go.AddComponent<SaClipPlayer>();
            }

            Undo.RecordObject(player, "Assign clip");
            player.Clip = clip;
            player.Loop = true;
            player.PlayOnStart = true;
            EditorUtility.SetDirty(player);
            if (Application.isPlaying)
            {
                player.Stop();
                player.Play();
            }
            else
            {
                EditorApplication.isPlaying = true;
            }

            status = "Previewing " + clip.name + ". Exit Play mode to stop.";
            statusType = MessageType.Info;
        }
    }
}

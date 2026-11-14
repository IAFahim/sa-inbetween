using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class DemoWindow : EditorWindow
    {
        [SerializeField] InbetweenRecipe recipe;
        [SerializeField] string message = "";
        [SerializeField] MessageType kind = MessageType.Info;
        [SerializeField] bool previewing;
        [SerializeField] bool waitingForGenerate;

        DemoPicks picks;
        readonly CharacterPreview actor = new CharacterPreview();
        double clock;
        double lastTick;
        bool paused;
        Vector2 scroll;

        public InbetweenRecipe Recipe => recipe;
        public string Message => message;
        public MessageType Kind => kind;
        public bool Previewing => previewing;
        public CharacterPreview Actor => actor;

        internal void Use(InbetweenRecipe target)
        {
            recipe = target;
            waitingForGenerate = false;
            previewing = false;
        }

        public static DemoWindow Open(bool build)
        {
            var window = GetWindow<DemoWindow>(false, "First Bridge", true);
            window.minSize = new Vector2(380f, 560f);
            if (build) window.Build();
            else window.Load();
            window.Show();
            return window;
        }

        void OnEnable()
        {
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            if (recipe == null) recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            actor.Dispose();
        }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = now - lastTick;
            lastTick = now;
            Refresh();
            if (recipe != null && (RecipeRunner.IsRunning(recipe) || previewing && !paused))
            {
                if (previewing && !paused) clock += dt;
                Repaint();
            }
        }

        public void Refresh()
        {
            if (!waitingForGenerate || recipe == null) return;
            var run = RecipeRunner.Get(recipe);
            if (run == null || !run.Done) return;
            waitingForGenerate = false;
            if (run.Error != "")
                Say(run.Error + (DemoBackup.Exists ? "\n\nIn a hurry? Press Use the backup for the result made earlier." : ""), MessageType.Error);
            else if (recipe.Output != null)
                Say("Done: " + recipe.Output.name + " is next to the recipe. " + Next(), MessageType.Info);
        }

        void Load()
        {
            if (recipe == null) recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
            if (recipe == null) Build();
            else Say(Next(), MessageType.Info);
        }

        public void Build()
        {
            previewing = false;
            waitingForGenerate = false;
            picks = DemoScene.Find();
            if (!picks.Ready)
            {
                Say(picks.Problem, MessageType.Warning);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Say("The demo scene was not made, because the open scene has unsaved changes you chose to keep. Save it, then press Make demo scene.", MessageType.Warning);
                return;
            }

            var description = DemoScene.Describe(picks);
            recipe = DemoScene.Create(picks, NewSceneMode.Single, out _);
            picks = null;
            Selection.activeObject = recipe;
            Say("The demo scene is ready: " + description + "\n\n" + Next(), MessageType.Info);
        }

        string Next()
        {
            if (recipe == null) return "Press Make demo scene.";
            var tools = InbetweenSettings.Tools();
            if (!tools.Complete) return "Before step 1, Inbetween needs its tools folder: " + string.Join(", ", tools.Missing()) + " missing. Press Open Project Settings to see what is missing, and docs/install.md steps 2 and 3 for where to get it.";
            switch (RecipeRunner.StateOf(recipe))
            {
                case RecipeState.Incomplete: return RecipeRunner.Problem(recipe) + " Press Make demo scene to pick clips again.";
                case RecipeState.NotGenerated: return "Step 1: press Generate. It takes about 10 seconds; the first time it also reads both clips once.";
                case RecipeState.OutOfDate: return "The recipe changed since the last Generate. Press Generate to make it again.";
                case RecipeState.UpToDate: return Saved()
                    ? "Done. Press Preview to watch the bridge here, Play at the top of the Editor to see it in the Game view, or 4 Play in SA runtime to see it on SA's animation runtime."
                    : "Step 2: press Preview to watch it here. Step 3: press Save to keep it as Unity clips and an FBX.";
                default: return "";
            }
        }

        bool Saved() => recipe.SavedClip != null && (System.IO.File.Exists(SaClipHumanoidExport.ExportPath(recipe)) || !InbetweenDiagnostics.IsHumanoid(SaClipExport.CharacterFor(recipe)));

        void Say(string text, MessageType type)
        {
            message = text;
            kind = type;
            Repaint();
        }

        public bool CanGenerate()
        {
            if (recipe == null) return false;
            var state = RecipeRunner.StateOf(recipe);
            return state != RecipeState.Running && state != RecipeState.Incomplete && state != RecipeState.NeedsSetup && !RecipeVariations.IsRunning(recipe);
        }

        public bool HasResult() => recipe != null && recipe.Output != null && !RecipeRunner.IsRunning(recipe);

        public bool Generate()
        {
            previewing = false;
            if (recipe == null) { Say("Press Make demo scene first.", MessageType.Warning); return false; }
            if (!RecipeRunner.Start(recipe, out var error))
            {
                Say(error, MessageType.Error);
                return false;
            }

            waitingForGenerate = true;
            Say("Generating... This takes about 10 seconds. You can keep working; do not press Play until it says Done.", MessageType.Info);
            return true;
        }

        public void CancelGenerate()
        {
            if (recipe == null) return;
            RecipeRunner.Cancel(recipe);
            Refresh();
        }

        public bool TogglePreview()
        {
            if (!previewing && !HasResult())
            {
                Say("Press Generate first; Preview plays its result.", MessageType.Warning);
                return false;
            }

            previewing = !previewing;
            clock = 0;
            paused = false;
            if (previewing)
            {
                Selection.activeObject = recipe;
                Say("Playing the bridge on " + SaClipExport.CharacterFor(recipe)?.name + ". Drag in the view to turn it, scroll to zoom. Step 3: press Save.", MessageType.Info);
            }

            Repaint();
            return true;
        }

        public bool Save()
        {
            if (!DemoScene.TrySave(recipe, out var saved, out var error))
            {
                Say("Could not save: " + error, MessageType.Error);
                return false;
            }

            Say("Saved:\n" + saved + "\n\nPress Play at the top of the Editor: the demo character plays the bridge in a loop in the Game view. Use the clip like any other: drag it onto an Animator state or a Timeline track. 4 Play in SA runtime plays it on SA's animation runtime.", MessageType.Info);
            EditorGUIUtility.PingObject(recipe.SavedClip);
            return true;
        }

        public bool PlayInSaRuntime()
        {
            if (!DemoRuntime.TryPlay(recipe, out var error))
            {
                Say("Could not play in the SA runtime: " + error, MessageType.Error);
                return false;
            }

            Say("Entering Play mode. On the right, " + SaClipExport.CharacterFor(recipe)?.name + " is driven by SA's animation runtime (ECS) from " + recipe.Output.name + ".saclip; on the left, Unity's Animator plays the saved Humanoid clip. Press Play again to stop.", MessageType.Info);
            return true;
        }

        public bool UseBackup()
        {
            previewing = false;
            waitingForGenerate = false;
            var ok = DemoBackup.TryRestore(out var report);
            recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(DemoScene.RecipePath);
            Say(report + (ok ? "\n\n" + Next() : ""), ok ? MessageType.Info : MessageType.Error);
            return ok;
        }

        void OnGUI()
        {
            Refresh();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Your first bridge: from mid-air to a landing", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Inbetween makes the motion between two moments of your clips. This demo starts at the top of a jump and lands on a standing pose. Press the buttons in order.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);
            Checklist();
            EditorGUILayout.Space(6);
            Buttons();
            EditorGUILayout.Space(6);
            if (message != "") EditorGUILayout.HelpBox(message, kind);
            Running();
            Preview();
            EditorGUILayout.Space(8);
            Footer();
            EditorGUILayout.EndScrollView();
        }

        void Checklist()
        {
            var tools = InbetweenSettings.Tools();
            Row(tools.Complete, tools.Complete ? "Tools folder: found" : "Tools folder: missing " + string.Join(", ", tools.Missing()));
            if (!tools.Complete)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open Project Settings")) SettingsService.OpenProjectSettings("Project/SA Inbetween");
                if (GUILayout.Button("Install guide")) Application.OpenURL(InbetweenDiagnostics.InstallUrl);
                EditorGUILayout.EndHorizontal();
            }

            var character = recipe == null ? null : SaClipExport.CharacterFor(recipe);
            Row(character != null && InbetweenDiagnostics.IsHumanoid(character), character == null ? "Character: not picked yet" : "Character: " + character.name + (InbetweenDiagnostics.IsHumanoid(character) ? " (Humanoid)" : " (no Humanoid avatar: set its Rig to Humanoid)"));
            var problem = recipe == null ? "" : RecipeRunner.Problem(recipe);
            Row(recipe != null && problem == "", recipe == null ? "Clips: not picked yet" : problem != "" ? "Clips: " + problem : "Clips: " + Source(recipe.Start) + " to " + Source(recipe.Goals[0]));
            if (picks != null && picks.NotHumanoid.Count > 0 && !picks.Ready)
                if (GUILayout.Button("Set to Humanoid (" + picks.NotHumanoid.Count + " model files)"))
                {
                    var changed = DemoScene.SetHumanoid(picks.NotHumanoid);
                    Say(changed + " model file(s) are now Humanoid. Press Make demo scene.", MessageType.Info);
                    picks = null;
                }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(recipe == null ? "Make demo scene" : "Make demo scene again")) Build();
            using (new EditorGUI.DisabledScope(recipe == null))
                if (GUILayout.Button("Show recipe"))
                {
                    Selection.activeObject = recipe;
                    EditorGUIUtility.PingObject(recipe);
                }

            EditorGUILayout.EndHorizontal();
        }

        static string Source(ClipFrame f)
        {
            if (f.Kind != SourceKind.UnityClip || f.Motion == null) return f.Clip != null ? f.Clip.name : "?";
            return "'" + f.Motion.name + "'" + (f.Where == FrameWhere.Frame ? " frame " + f.Frame : f.Where == FrameWhere.Start ? " start" : f.Where == FrameWhere.End ? " end" : " " + f.Seconds.ToString("0.00") + " s");
        }

        static void Row(bool ok, string text)
        {
            var old = GUI.color;
            EditorGUILayout.BeginHorizontal();
            GUI.color = ok ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.95f, 0.6f, 0.35f);
            GUILayout.Label(ok ? "OK" : "!", EditorStyles.boldLabel, GUILayout.Width(22));
            GUI.color = old;
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        void Buttons()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!CanGenerate()))
                if (GUILayout.Button("1  Generate", GUILayout.Height(34))) Generate();
            using (new EditorGUI.DisabledScope(!HasResult()))
                if (GUILayout.Button(previewing ? "2  Stop preview" : "2  Preview", GUILayout.Height(34))) TogglePreview();
            using (new EditorGUI.DisabledScope(!HasResult() || SaClipExport.CharacterFor(recipe) == null))
                if (GUILayout.Button("3  Save", GUILayout.Height(34))) Save();
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!HasResult() || !InbetweenDiagnostics.IsHumanoid(SaClipExport.CharacterFor(recipe))))
                if (GUILayout.Button(new GUIContent("4  Play in SA runtime", "Sets up an SA character in a SubScene next to the demo character (once) and enters Play mode with the bridge playing on SA's ECS animation runtime"), GUILayout.Height(26)))
                {
                    PlayInSaRuntime();
                    GUIUtility.ExitGUI();
                }
        }

        void Running()
        {
            if (recipe == null) return;
            var run = RecipeRunner.Get(recipe);
            if (run == null || run.Done) return;
            EditorGUILayout.HelpBox(run.Stage + "... (step " + (run.Finished + 1) + " of " + run.Total + ")", MessageType.Info);
            if (GUILayout.Button("Cancel")) CancelGenerate();
        }

        void Preview()
        {
            if (!previewing || recipe == null || recipe.Output == null) return;
            var character = SaClipExport.CharacterFor(recipe);
            var rect = GUILayoutUtility.GetRect(10f, 280f, GUILayout.ExpandWidth(true));
            if (character == null || !actor.Bind(character, recipe.Output.Bytes))
            {
                EditorGUILayout.HelpBox("Cannot show the preview: " + (character == null ? "the recipe has no character." : actor.Error), MessageType.Warning);
                return;
            }

            var duration = actor.Duration;
            var t = paused ? Mathf.Min((float)clock, duration) : PreviewMath.Wrap(clock, duration);
            if (actor.Orbit(rect)) Repaint();
            actor.Draw(rect, t);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "Play" : "Pause", GUILayout.Width(60)))
            {
                paused = !paused;
                clock = t;
            }

            EditorGUI.BeginChangeCheck();
            var scrub = GUILayout.HorizontalSlider(t, 0f, duration, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                paused = true;
                clock = scrub;
            }

            GUILayout.Label(PreviewMath.Label(t, duration), EditorStyles.miniLabel, GUILayout.Width(92));
            EditorGUILayout.EndHorizontal();
        }

        void Footer()
        {
            if (DemoBackup.Exists)
            {
                EditorGUILayout.LabelField("Ready-made result", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(DemoBackup.Describe() + " If Generate is slow or fails, put it back in one click.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(recipe != null && RecipeRunner.IsRunning(recipe) || Application.isPlaying))
                    if (GUILayout.Button("Use the backup"))
                    {
                        UseBackup();
                        GUIUtility.ExitGUI();
                    }
                EditorGUILayout.Space(6);
            }

            EditorGUILayout.LabelField("Something wrong?", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy diagnostics"))
            {
                InbetweenDiagnostics.Copy(recipe);
                Say("Diagnostics are on the clipboard. Paste them into a bug report or a message.", MessageType.Info);
            }

            if (GUILayout.Button("Report a bug")) InbetweenDiagnostics.ReportBug(recipe);
            if (GUILayout.Button("Designer guide")) Application.OpenURL(InbetweenDiagnostics.GuideUrl);
            EditorGUILayout.EndHorizontal();
        }
    }
}

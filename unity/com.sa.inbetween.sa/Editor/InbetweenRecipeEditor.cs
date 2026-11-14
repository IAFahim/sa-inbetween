using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [CustomEditor(typeof(InbetweenRecipe))]
    public sealed class InbetweenRecipeEditor : UnityEditor.Editor
    {
        const string GhostsKey = "SA.Inbetween.preview.ghosts";
        const string SceneKey = "SA.Inbetween.preview.scene";

        Vector2 logScroll;
        readonly SkeletonPreview preview = new SkeletonPreview();
        readonly SkeletonRenderer view = new SkeletonRenderer();
        readonly CharacterPreview actor = new CharacterPreview();
        readonly List<SkeletonRenderer> cardViews = new List<SkeletonRenderer>();
        readonly List<SkeletonClip> cardClips = new List<SkeletonClip>();
        VariationRun cardsFor;
        double clock;
        double lastTick;
        bool playing = true;

        static bool Ghosts
        {
            get => EditorPrefs.GetBool(GhostsKey, true);
            set => EditorPrefs.SetBool(GhostsKey, value);
        }

        static bool InScene
        {
            get => EditorPrefs.GetBool(SceneKey, true);
            set => EditorPrefs.SetBool(SceneKey, value);
        }

        public override bool RequiresConstantRepaint() => RecipeRunner.IsRunning((InbetweenRecipe)target) || RecipeVariations.IsRunning((InbetweenRecipe)target);

        void OnEnable()
        {
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            SkeletonSceneDrawer.Hide(this);
            view.Dispose();
            actor.Dispose();
            foreach (var v in cardViews) v.Dispose();
            cardViews.Clear();
        }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = now - lastTick;
            lastTick = now;
            if (!playing || target == null)
                return;
            clock += dt;
            Refresh((InbetweenRecipe)target);
            Repaint();
        }

        float TimeFor(float duration) => playing ? PreviewMath.Wrap(clock, duration) : Mathf.Min((float)clock, duration);

        void Refresh(InbetweenRecipe recipe)
        {
            preview.Bind(recipe);
            preview.Evaluate(recipe, TimeFor(preview.Duration), Ghosts);
            if (InScene && preview.Ready)
                SkeletonSceneDrawer.Show(this, preview.Layers);
            else
                SkeletonSceneDrawer.Hide(this);
        }

        public override void OnInspectorGUI()
        {
            var recipe = (InbetweenRecipe)target;
            serializedObject.Update();
            DrawStatus(recipe);
            EditorGUILayout.Space(6);

            var busy = RecipeRunner.IsRunning(recipe) || RecipeVariations.IsRunning(recipe);
            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUILayout.LabelField("Start from", EditorStyles.boldLabel);
                DrawFrame(serializedObject.FindProperty("Start"), true);
                EditorGUILayout.Space(4);
                var goals = serializedObject.FindProperty("Goals");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(goals.arraySize > 1 ? "Then move to, in order" : "Move to", EditorStyles.boldLabel);
                if (goals.arraySize > 1 && GUILayout.Button("Swap start and goal", GUILayout.Width(130)))
                    Swap(recipe);
                EditorGUILayout.EndHorizontal();
                for (var i = 0; i < goals.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.BeginVertical();
                    DrawFrame(goals.GetArrayElementAtIndex(i), false);
                    EditorGUILayout.EndVertical();
                    if (goals.arraySize > 1 && GUILayout.Button("-", GUILayout.Width(22)))
                    {
                        goals.DeleteArrayElementAtIndex(i);
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (goals.arraySize < InbetweenRecipe.MaxGoals && GUILayout.Button("+ Add another goal after this", GUILayout.Width(200)))
                {
                    goals.arraySize++;
                    var added = goals.GetArrayElementAtIndex(goals.arraySize - 1);
                    added.FindPropertyRelative("Where").enumValueIndex = (int)FrameWhere.Start;
                }

                EditorGUILayout.Space(6);
                var seconds = serializedObject.FindProperty("DurationSeconds");
                EditorGUILayout.Slider(seconds, InbetweenRecipe.MinSeconds, InbetweenRecipe.MaxSeconds, new GUIContent("Duration (s)", "How long each move takes"));
                var frames = StudioCommands.FramesForTokens(StudioCommands.TokensForSeconds(seconds.floatValue));
                EditorGUILayout.LabelField(" ", "About " + (frames / StudioCommands.FrameRate).ToString("0.00") + " s, " + frames + " frames per move. Longer needs more goals.", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LandExactly"), new GUIContent("Land exactly on the goal", "Corrects the end so the pose matches the goal"));
                EditorGUILayout.Space(4);
                DrawCharacter(serializedObject, recipe);
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(8);
            DrawActions(recipe, busy);
            DrawPreview(recipe, busy);
            EditorGUILayout.Space(8);
            DrawVariations(recipe, busy);
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Copy diagnostics", "Copies versions, tool files, the last error and its log to the clipboard, for a bug report"), EditorStyles.miniButton, GUILayout.Width(110)))
                InbetweenDiagnostics.Copy(recipe);
            if (GUILayout.Button("Report a bug", EditorStyles.miniButton, GUILayout.Width(90)))
                InbetweenDiagnostics.ReportBug(recipe);
            EditorGUILayout.EndHorizontal();
        }

        void DrawStatus(InbetweenRecipe recipe)
        {
            var state = RecipeRunner.StateOf(recipe);
            var run = RecipeRunner.Get(recipe);
            switch (state)
            {
                case RecipeState.Incomplete:
                    EditorGUILayout.HelpBox(RecipeRunner.Problem(recipe), MessageType.Warning);
                    break;
                case RecipeState.NeedsSetup:
                    EditorGUILayout.HelpBox("Inbetween needs its tools before it can generate. " + InbetweenSettings.SetupAdvice(InbetweenSettings.Tools()), MessageType.Warning);
                    if (GUILayout.Button("Open Project Settings", GUILayout.Width(170)))
                        SettingsService.OpenProjectSettings("Project/SA Inbetween");
                    break;
                case RecipeState.NotGenerated:
                    EditorGUILayout.HelpBox("Ready. Press Generate to make the motion.", MessageType.Info);
                    break;
                case RecipeState.UpToDate:
                    EditorGUILayout.HelpBox("Up to date: the clip matches these settings.", MessageType.None);
                    break;
                case RecipeState.OutOfDate:
                    if (recipe.BuiltFrom == RecipeRunner.Hash(recipe) && SaClipExport.SavedClipStale(recipe))
                        EditorGUILayout.HelpBox("The Unity clip (" + recipe.name + ".anim) is missing or older than the result. Press Export > Animation Clip, or Generate again.", MessageType.Warning);
                    else
                        EditorGUILayout.HelpBox("Settings changed since the last generate (or a source clip changed). Press Generate to refresh the clip.", MessageType.Warning);
                    break;
                case RecipeState.Running:
                    EditorGUILayout.HelpBox(run.Stage + "... (step " + (run.Finished + 1) + " of " + run.Total + ")", MessageType.Info);
                    break;
                case RecipeState.Failed:
                    EditorGUILayout.HelpBox(run.Error, MessageType.Error);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Copy diagnostics", "Copies versions, tool files, this error and its log to the clipboard"), GUILayout.Width(130)))
                        InbetweenDiagnostics.Copy(recipe);
                    if (GUILayout.Button("Report a bug", GUILayout.Width(100)))
                        InbetweenDiagnostics.ReportBug(recipe);
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(run.Log))
                    {
                        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(60), GUILayout.MaxHeight(160));
                        EditorGUILayout.SelectableLabel(run.Log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
                        EditorGUILayout.EndScrollView();
                    }

                    break;
            }
        }

        static void DrawFrame(SerializedProperty frame, bool isStart)
        {
            var clip = frame.FindPropertyRelative("Clip");
            var where = frame.FindPropertyRelative("Where");
            var seconds = frame.FindPropertyRelative("Seconds");
            var kind = frame.FindPropertyRelative("Kind");
            var motion = frame.FindPropertyRelative("Motion");
            var unity = (SourceKind)kind.enumValueIndex == SourceKind.UnityClip;
            EditorGUILayout.BeginHorizontal();
            kind.enumValueIndex = EditorGUILayout.Popup(kind.enumValueIndex, new[] { ".saclip", "Unity clip", "Pose" }, GUILayout.Width(84));
            if ((SourceKind)kind.enumValueIndex == SourceKind.Pose)
            {
                var pose = frame.FindPropertyRelative("Pose");
                pose.objectReferenceValue = EditorGUILayout.ObjectField(pose.objectReferenceValue, typeof(SaPoseAsset), false);
                EditorGUILayout.EndHorizontal();
                return;
            }
            if (unity)
                motion.objectReferenceValue = EditorGUILayout.ObjectField(motion.objectReferenceValue, typeof(AnimationClip), false);
            else
                EditorGUILayout.PropertyField(clip, GUIContent.none);
            var labels = new[] { "end of clip", "start of clip", "at time (s)", "at frame" };
            where.enumValueIndex = EditorGUILayout.Popup(where.enumValueIndex, labels, GUILayout.Width(110));
            if ((FrameWhere)where.enumValueIndex == FrameWhere.Time)
                seconds.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(seconds.floatValue, GUILayout.Width(52)));
            else if ((FrameWhere)where.enumValueIndex == FrameWhere.Frame)
            {
                var number = frame.FindPropertyRelative("Frame");
                number.intValue = Mathf.Max(0, EditorGUILayout.IntField(number.intValue, GUILayout.Width(52)));
                var rate = unity && motion.objectReferenceValue is AnimationClip picked && picked.frameRate > 0f ? picked.frameRate : ClipFrame.ClipFrameRate;
                EditorGUILayout.LabelField("= " + (number.intValue / rate).ToString("0.00") + " s", EditorStyles.miniLabel, GUILayout.Width(64));
            }
            if (!unity && GUILayout.Button(new GUIContent("Capture", "Captures the current pose of the character selected in the scene and uses it here"), GUILayout.Width(60)))
                Capture(frame, isStart);
            EditorGUILayout.EndHorizontal();
            if (!unity)
                return;
            var character = frame.FindPropertyRelative("Character");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("  on character", GUILayout.Width(84));
            character.objectReferenceValue = EditorGUILayout.ObjectField(character.objectReferenceValue, typeof(GameObject), false);
            EditorGUILayout.EndHorizontal();
            if (character.objectReferenceValue is GameObject go && !(go.GetComponentInChildren<Animator>(true) is Animator animator && animator.avatar != null && animator.avatar.isHuman))
                EditorGUILayout.HelpBox("This character has no Humanoid Avatar, so its bones are matched by name. For Mixamo, Synty and other humanoids select the model, set Inspector > Rig > Animation Type to Humanoid and press Apply.", MessageType.Warning);
        }

        static void Capture(SerializedProperty frame, bool isStart)
        {
            var recipe = (InbetweenRecipe)frame.serializedObject.targetObject;
            var posed = PoseCapture.CharacterOf(Selection.activeGameObject);
            if (posed == null)
            {
                EditorUtility.DisplayDialog("Capture pose", "Select the posed character in the scene (a GameObject with an Animator), then press Capture again.", "OK");
                GUIUtility.ExitGUI();
                return;
            }

            var role = isStart ? "start" : "goal";
            if (!PoseCapture.TryCapture(posed, PoseCapture.PathFor(recipe, role), out var asset, out var error))
            {
                EditorUtility.DisplayDialog("Capture pose", error, "OK");
                GUIUtility.ExitGUI();
                return;
            }

            frame.FindPropertyRelative("Kind").enumValueIndex = (int)SourceKind.SaClip;
            frame.FindPropertyRelative("Clip").objectReferenceValue = asset;
            frame.FindPropertyRelative("Where").enumValueIndex = (int)FrameWhere.Start;
            frame.serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        static void Swap(InbetweenRecipe recipe)
        {
            Undo.RecordObject(recipe, "Swap start and goal");
            var last = recipe.Goals.Count - 1;
            var start = recipe.Start;
            recipe.Start = recipe.Goals[last];
            recipe.Goals[last] = start;
            EditorUtility.SetDirty(recipe);
        }

        void DrawActions(InbetweenRecipe recipe, bool busy)
        {
            var state = RecipeRunner.StateOf(recipe);
            var ready = state == RecipeState.NotGenerated || state == RecipeState.OutOfDate || state == RecipeState.UpToDate || state == RecipeState.Failed;
            using (new EditorGUI.DisabledScope(!ready || RecipeVariations.IsRunning(recipe)))
                if (GUILayout.Button(recipe.Output == null ? "Generate" : "Generate again", GUILayout.Height(32)))
                {
                    if (!RecipeRunner.Start(recipe, out var error))
                        EditorUtility.DisplayDialog("Cannot generate", error, "OK");
                }

            if (RecipeRunner.IsRunning(recipe) && GUILayout.Button("Cancel"))
                RecipeRunner.Cancel(recipe);
        }

        void DrawPreview(InbetweenRecipe recipe, bool busy)
        {
            if (recipe.Output == null)
                return;
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Result", recipe.Output, typeof(SaClipAsset), false);
                if (recipe.SaveAnimationClip || recipe.SavedClip != null)
                    EditorGUILayout.ObjectField("Unity clip", recipe.SavedClip, typeof(AnimationClip), false);
            }
            Refresh(recipe);
            if (!preview.Ready)
                EditorGUILayout.HelpBox("Cannot preview this clip: " + preview.Error, MessageType.Warning);
            else
                DrawStrip(recipe);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(busy))
                if (GUILayout.Button("Preview on character", GUILayout.Height(24)))
                    RecipePreview.Play(recipe.Output);
            if (GUILayout.Button("Show clip", GUILayout.Height(24)))
            {
                Selection.activeObject = recipe.Output;
                EditorGUIUtility.PingObject(recipe.Output);
            }

            EditorGUILayout.EndHorizontal();
            DrawExport(recipe, busy);
        }

        void DrawExport(InbetweenRecipe recipe, bool busy)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
            var character = SaClipExport.CharacterFor(recipe);
            using (new EditorGUI.DisabledScope(busy || character == null))
                if (GUILayout.Button(new GUIContent("Export > Animation Clip", "Writes " + recipe.name + ".anim next to the recipe: a Generic clip for this character, starting at the origin facing +Z. Play it on the model set to a Generic rig."), GUILayout.Height(24)))
                {
                    if (!SaClipExport.TrySave(recipe, out var error))
                        EditorUtility.DisplayDialog("Cannot export", error, "OK");
                    else
                        EditorGUIUtility.PingObject(recipe.SavedClip);
                    GUIUtility.ExitGUI();
                }
            var human = character != null && character.GetComponentInChildren<Animator>(true) is Animator animator && animator.avatar != null && animator.avatar.isHuman;
            using (new EditorGUI.DisabledScope(busy || !human))
                if (GUILayout.Button(new GUIContent("Export > Humanoid clip", "Writes " + recipe.name + "_Humanoid.anim next to the recipe: muscle curves for the Character's Humanoid avatar, playable on any Humanoid character"), GUILayout.Height(24)))
                {
                    if (!SaClipHumanoidExport.TryExport(recipe, out var saved, out var error))
                        EditorUtility.DisplayDialog("Cannot export", error, "OK");
                    else
                        EditorGUIUtility.PingObject(saved);
                    GUIUtility.ExitGUI();
                }
            using (new EditorGUI.DisabledScope(busy || character == null || !SaClipFbxExport.Available))
                if (GUILayout.Button(new GUIContent("Export > FBX", "Writes " + recipe.name + ".fbx next to the recipe: the Character with the clip, for other tools or to reimport as Humanoid"), GUILayout.Height(24)))
                {
                    if (!SaClipFbxExport.TryExport(recipe, out var path, out var error))
                        EditorUtility.DisplayDialog("Cannot export", error, "OK");
                    else
                        EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path));
                    GUIUtility.ExitGUI();
                }
            if (!SaClipFbxExport.Available)
                EditorGUILayout.LabelField(SaClipFbxExport.MissingPackage, EditorStyles.wordWrappedMiniLabel);
            if (character == null)
                EditorGUILayout.LabelField("Choose a Character above to export for it.", EditorStyles.wordWrappedMiniLabel);
        }

        static void DrawCharacter(SerializedObject serialized, InbetweenRecipe recipe)
        {
            var character = serialized.FindProperty("Character");
            EditorGUILayout.PropertyField(character, new GUIContent("Character", "The character the result is previewed on and saved for. Filled from a Unity clip source when empty."));
            if (character.objectReferenceValue == null)
            {
                var fallback = SaClipExport.CharacterFor(recipe);
                if (fallback != null) character.objectReferenceValue = fallback;
            }

            EditorGUILayout.PropertyField(serialized.FindProperty("SaveAnimationClip"), new GUIContent("Also save a Unity clip (.anim)", "After each Generate or Keep, writes " + recipe.name + ".anim next to the recipe for the Character, ready for the Animation window and Timeline"));
        }

        void DrawStrip(InbetweenRecipe recipe)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(56)))
            {
                playing = !playing;
                clock = preview.Time;
                lastTick = EditorApplication.timeSinceStartup;
            }

            EditorGUI.BeginChangeCheck();
            var t = GUILayout.HorizontalSlider(preview.Time, 0f, preview.Duration, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                playing = false;
                clock = t;
                Refresh(recipe);
            }

            GUILayout.Label(PreviewMath.Label(preview.Time, preview.Duration), EditorStyles.miniLabel, GUILayout.Width(92));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var ghosts = EditorGUILayout.ToggleLeft(new GUIContent("Ghost start and goal", "Shows the start pose (green) and the goal pose (orange) faded"), Ghosts, GUILayout.Width(150));
            var inScene = EditorGUILayout.ToggleLeft(new GUIContent("Show in Scene view", "Draws the skeleton at the scene origin"), InScene);
            if (EditorGUI.EndChangeCheck())
            {
                Ghosts = ghosts;
                InScene = inScene;
                Refresh(recipe);
            }

            EditorGUILayout.EndHorizontal();
            var rect = GUILayoutUtility.GetRect(10f, 220f, GUILayout.ExpandWidth(true));
            var character = SaClipExport.CharacterFor(recipe);
            if (character != null && actor.Bind(character, recipe.Output.Bytes))
            {
                if (actor.Orbit(rect)) Repaint();
                actor.Draw(rect, preview.Time);
                return;
            }

            if (character != null && actor.Error != "")
                EditorGUILayout.HelpBox("Cannot show " + character.name + ": " + actor.Error + " Showing the skeleton.", MessageType.Warning);
            if (view.Orbit(rect)) Repaint();
            view.Draw(rect, preview.Layers);
        }

        void DrawVariations(InbetweenRecipe recipe, bool busy)
        {
            EditorGUILayout.LabelField("Variations", EditorStyles.boldLabel);
            var state = RecipeRunner.StateOf(recipe);
            var ready = state == RecipeState.NotGenerated || state == RecipeState.OutOfDate || state == RecipeState.UpToDate || state == RecipeState.Failed;
            var run = RecipeVariations.Get(recipe);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ready || busy))
                if (GUILayout.Button(new GUIContent("Make variations", "Generates up to " + RecipeVariations.MaxCandidates + " versions with a shorter, longer or model-picked duration, then lets you keep one"), GUILayout.Height(24)))
                {
                    if (!RecipeVariations.Start(recipe, out var error))
                        EditorUtility.DisplayDialog("Cannot make variations", error, "OK");
                    run = RecipeVariations.Get(recipe);
                }

            if (run != null && !run.Done && GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(24)))
                RecipeVariations.Cancel(recipe);
            if (run != null && run.Done && GUILayout.Button("Discard", GUILayout.Width(70), GUILayout.Height(24)))
            {
                RecipeVariations.Discard(recipe);
                run = null;
            }

            EditorGUILayout.EndHorizontal();
            if (run == null)
            {
                EditorGUILayout.LabelField("The same settings always give the same clip. Variations try nearby durations and landings so you can pick one.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (!run.Done)
                EditorGUILayout.HelpBox(run.Stage + "... (step " + Mathf.Min(run.Finished + 1, run.Total) + " of " + run.Total + ")", MessageType.Info);
            else if (run.Error != "")
                EditorGUILayout.HelpBox(run.Error, MessageType.Error);
            DrawCards(recipe, run);
        }

        void DrawCards(InbetweenRecipe recipe, VariationRun run)
        {
            if (cardsFor != run)
            {
                cardsFor = run;
                cardClips.Clear();
            }

            while (cardClips.Count < run.Candidates.Count) cardClips.Add(new SkeletonClip());
            while (cardViews.Count < run.Candidates.Count) cardViews.Add(new SkeletonRenderer { Distance = 5.6f });
            if (run.Candidates.Count == 0)
                return;
            var width = (EditorGUIUtility.currentViewWidth - 40f) / run.Candidates.Count;
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < run.Candidates.Count; i++)
            {
                var candidate = run.Candidates[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width));
                GUILayout.Label(RecipeVariations.Describe(candidate.Spec), EditorStyles.wordWrappedMiniLabel);
                var rect = GUILayoutUtility.GetRect(10f, 120f, GUILayout.ExpandWidth(true));
                var clip = cardClips[i];
                if (candidate.Ready && clip.BindFile(candidate.ClipFile) && clip.Evaluate(TimeFor(clip.Duration)))
                {
                    if (cardViews[i].Orbit(rect)) Repaint();
                    cardViews[i].Draw(rect, new[] { new SkeletonLayer { World = clip.World, Color = SkeletonPreview.ResultColor } });
                    GUILayout.Label(clip.Duration.ToString("0.00") + " s, " + clip.Pose.SampleCount + " frames", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    GUI.Label(rect, candidate.Error != "" ? candidate.Error : clip.Error != "" ? clip.Error : run.Done ? "Not made" : "Waiting...", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.Label(" ", EditorStyles.centeredGreyMiniLabel);
                }

                using (new EditorGUI.DisabledScope(!candidate.Ready || !run.Done || RecipeRunner.IsRunning(recipe)))
                    if (GUILayout.Button("Keep this one"))
                    {
                        if (!RecipeVariations.Keep(recipe, i, out var error))
                            EditorUtility.DisplayDialog("Cannot keep this variation", error, "OK");
                        else
                            clock = 0;
                        GUIUtility.ExitGUI();
                    }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    [InitializeOnLoad]
    public static class RecipePreview
    {
        public const string PendingKey = "SA.Inbetween.preview.pending";

        static RecipePreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                var clip = Pending();
                SessionState.EraseString(PendingKey);
                if (clip != null)
                {
                    Show(clip);
                    ShowGameView();
                }
                else if (Object.FindAnyObjectByType<SaClipPlayer>() != null)
                    ShowGameView();
            };
        }

        public static void ShowGameView()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type != null)
                EditorWindow.GetWindow(type).Show();
        }

        public static SaClipAsset Pending()
        {
            var guid = SessionState.GetString(PendingKey, "");
            return guid == "" ? null : AssetDatabase.LoadAssetAtPath<SaClipAsset>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public static void Request(SaClipAsset clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            if (path == "") SessionState.EraseString(PendingKey);
            else SessionState.SetString(PendingKey, AssetDatabase.AssetPathToGUID(path));
        }

        public static void Play(SaClipAsset clip)
        {
            if (Application.isPlaying)
            {
                Show(clip);
                ShowGameView();
                return;
            }

            Request(clip);
            EditorApplication.isPlaying = true;
        }

        static void Show(SaClipAsset clip)
        {
            var player = Object.FindAnyObjectByType<SaClipPlayer>();
            if (player == null)
                player = new GameObject("Inbetween Preview").AddComponent<SaClipPlayer>();
            player.Clip = clip;
            player.Loop = true;
            player.PlayOnStart = true;
            player.Stop();
            player.Play();
        }
    }

    public static class BridgeMenu
    {
        [MenuItem("Assets/Create/SA/Inbetween Bridge", false, 300)]
        static void Create()
        {
            var recipe = Build(Selection.objects, out var target);
            AssetDatabase.CreateAsset(recipe, AssetDatabase.GenerateUniqueAssetPath(target));
            AssetDatabase.SaveAssets();
            Selection.activeObject = recipe;
            EditorGUIUtility.PingObject(recipe);
        }

        public static InbetweenRecipe Build(Object[] selected, out string target)
        {
            var sources = new List<ClipFrame>();
            var names = new List<string>();
            var models = new List<GameObject>();
            var folder = "";
            foreach (var picked in selected)
            {
                var path = AssetDatabase.GetAssetPath(picked);
                if (picked is SaClipAsset saclip)
                    Add(sources, names, new ClipFrame { Kind = SourceKind.SaClip, Clip = saclip }, saclip.name);
                else if (picked is AnimationClip motion && !IsPreviewTake(motion))
                    Add(sources, names, new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Character = ModelOf(path) }, TakeName(motion));
                else if (picked is GameObject model && EditorUtility.IsPersistent(model))
                    models.Add(model);
                else if (AssetDatabase.IsValidFolder(path))
                    folder = path;
            }

            GameObject character = null;
            var takesFromModels = sources.Count == 0;
            foreach (var model in models)
            {
                if (takesFromModels && FirstTake(AssetDatabase.GetAssetPath(model)) is AnimationClip take)
                    Add(sources, names, new ClipFrame { Kind = SourceKind.UnityClip, Motion = take, Character = model }, TakeName(take));
                else if (character == null)
                    character = model;
            }

            if (folder == "" && sources.Count > 0)
                folder = System.IO.Path.GetDirectoryName(SourcePath(sources[0]))?.Replace('\\', '/') ?? "";

            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Character = character;
            if (sources.Count > 0)
            {
                recipe.Start = WithCharacter(sources[0], FrameWhere.End, character);
                recipe.Goals.Clear();
                if (sources.Count == 1)
                    recipe.Goals.Add(WithCharacter(sources[0], FrameWhere.Start, character));
                for (var i = 1; i < sources.Count && i <= InbetweenRecipe.MaxGoals; i++)
                    recipe.Goals.Add(WithCharacter(sources[i], FrameWhere.Start, character));
            }

            var name = sources.Count == 0 ? "Inbetween" : names[0] + (sources.Count > 1 ? "_to_" + names[names.Count - 1] : "_loop");
            target = (folder == "" ? "Assets" : folder) + "/" + name + ".asset";
            return recipe;
        }

        static void Add(List<ClipFrame> sources, List<string> names, ClipFrame source, string name)
        {
            sources.Add(source);
            names.Add(name);
        }

        static string SourcePath(ClipFrame source) => AssetDatabase.GetAssetPath(source.Kind == SourceKind.SaClip ? (Object)source.Clip : source.Motion);

        static ClipFrame WithCharacter(ClipFrame source, FrameWhere where, GameObject character)
        {
            source.Where = where;
            if (source.Kind == SourceKind.UnityClip && source.Character == null)
                source.Character = character;
            return source;
        }

        static bool IsPreviewTake(AnimationClip clip) => clip.name.StartsWith("__preview__", System.StringComparison.Ordinal);

        static GameObject ModelOf(string clipPath) => AssetDatabase.LoadMainAssetAtPath(clipPath) as GameObject;

        static AnimationClip FirstTake(string modelPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath))
                if (asset is AnimationClip clip && !IsPreviewTake(clip))
                    return clip;
            return null;
        }

        public static string TakeName(AnimationClip clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            if (!(AssetDatabase.LoadMainAssetAtPath(path) is GameObject))
                return clip.name;
            var takes = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (asset is AnimationClip other && !IsPreviewTake(other))
                    takes++;
            return takes == 1 ? System.IO.Path.GetFileNameWithoutExtension(path) : clip.name;
        }
    }
}

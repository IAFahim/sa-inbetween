using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [CustomEditor(typeof(InbetweenRecipe))]
    public sealed class InbetweenRecipeEditor : UnityEditor.Editor
    {
        Vector2 logScroll;

        public override bool RequiresConstantRepaint() => RecipeRunner.IsRunning((InbetweenRecipe)target);

        public override void OnInspectorGUI()
        {
            var recipe = (InbetweenRecipe)target;
            serializedObject.Update();
            DrawStatus(recipe);
            EditorGUILayout.Space(6);

            var busy = RecipeRunner.IsRunning(recipe);
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
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(8);
            DrawActions(recipe, busy);
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
                    EditorGUILayout.HelpBox("Inbetween needs its tools before it can generate: " + string.Join(", ", InbetweenSettings.Tools().Missing()) + ".", MessageType.Warning);
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
                    EditorGUILayout.HelpBox("Settings changed since the last generate (or a source clip changed). Press Generate to refresh the clip.", MessageType.Warning);
                    break;
                case RecipeState.Running:
                    EditorGUILayout.HelpBox(run.Stage + "... (step " + (run.Finished + 1) + " of " + run.Total + ")", MessageType.Info);
                    break;
                case RecipeState.Failed:
                    EditorGUILayout.HelpBox(run.Error, MessageType.Error);
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
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(clip, GUIContent.none);
            var labels = isStart
                ? new[] { "end of clip", "start of clip", "at time" }
                : new[] { "end of clip", "start of clip", "at time" };
            where.enumValueIndex = EditorGUILayout.Popup(where.enumValueIndex, labels, GUILayout.Width(110));
            if ((FrameWhere)where.enumValueIndex == FrameWhere.Time)
                seconds.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(seconds.floatValue, GUILayout.Width(52)));
            EditorGUILayout.EndHorizontal();
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
            using (new EditorGUI.DisabledScope(!ready))
                if (GUILayout.Button(recipe.Output == null ? "Generate" : "Generate again", GUILayout.Height(32)))
                {
                    if (!RecipeRunner.Start(recipe, out var error))
                        EditorUtility.DisplayDialog("Cannot generate", error, "OK");
                }

            if (busy && GUILayout.Button("Cancel"))
                RecipeRunner.Cancel(recipe);

            if (recipe.Output == null)
                return;
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Result", recipe.Output, typeof(SaClipAsset), false);
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
        }
    }

    [InitializeOnLoad]
    public static class RecipePreview
    {
        static RecipePreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<SaClipPlayer>() != null)
                    ShowGameView();
            };
        }

        public static void ShowGameView()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type != null)
                EditorWindow.GetWindow(type).Show();
        }

        public static void Play(SaClipAsset clip)
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
                EditorApplication.isPlaying = true;
            ShowGameView();
        }
    }

    public static class BridgeMenu
    {
        [MenuItem("Assets/Create/SA/Inbetween Bridge", false, 300)]
        static void Create()
        {
            var clips = Selection.GetFiltered<SaClipAsset>(SelectionMode.Assets);
            var folder = "Assets";
            var name = "Inbetween";
            if (clips.Length > 0)
            {
                var path = AssetDatabase.GetAssetPath(clips[0]);
                folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                name = clips[0].name + (clips.Length > 1 ? "_to_" + clips[clips.Length - 1].name : "_loop");
            }
            else if (Selection.activeObject != null)
            {
                var path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (AssetDatabase.IsValidFolder(path)) folder = path;
            }

            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            if (clips.Length > 0)
            {
                recipe.Start = new ClipFrame { Clip = clips[0], Where = FrameWhere.End };
                recipe.Goals.Clear();
                if (clips.Length == 1)
                    recipe.Goals.Add(new ClipFrame { Clip = clips[0], Where = FrameWhere.Start });
                for (var i = 1; i < clips.Length && i <= InbetweenRecipe.MaxGoals; i++)
                    recipe.Goals.Add(new ClipFrame { Clip = clips[i], Where = FrameWhere.Start });
            }

            var target = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
            AssetDatabase.CreateAsset(recipe, target);
            AssetDatabase.SaveAssets();
            Selection.activeObject = recipe;
            EditorGUIUtility.PingObject(recipe);
        }
    }
}

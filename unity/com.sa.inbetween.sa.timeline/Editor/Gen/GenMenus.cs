using System.Collections.Generic;
using System.IO;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    [MenuEntry("Bake Generation Timeline", MenuPriority.TimelineActionSection.start + 600)]
    sealed class BakeTimelineAction : TimelineAction
    {
        public override ActionValidity Validate(ActionContext context) =>
            GenPlanner.HasGenTracks(context.timeline) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(ActionContext context) => GenMenus.Bake(context.timeline, context.director);
    }

    public static class GenMenus
    {
        public const string Title = "Bake Generation Timeline";

        public static bool Bake(TimelineAsset timeline, PlayableDirector director)
        {
            if (timeline == null)
            {
                EditorUtility.DisplayDialog(Title, "Select a timeline that has an SA Inbetween Generation Track, or open it in the Timeline window.", "OK");
                return false;
            }

            var run = GenBake.Start(timeline, director, out var error);
            if (run != null) return true;
            EditorUtility.DisplayDialog(Title, error, "OK");
            return false;
        }

        [MenuItem("Assets/SA/Bake Generation Timeline", false, 312)]
        static void BakeSelected()
        {
            var timeline = Selection.activeObject as TimelineAsset;
            Bake(timeline, GenPlanCache.DirectorOf(timeline));
        }

        [MenuItem("Assets/SA/Bake Generation Timeline", true)]
        static bool CanBakeSelected() => GenPlanner.HasGenTracks(Selection.activeObject as TimelineAsset);

        [MenuItem("SA/Inbetween/Bake Generation Timeline", false, 21)]
        static void BakeInspected()
        {
            var timeline = TimelineEditor.inspectedAsset != null ? TimelineEditor.inspectedAsset : Selection.activeObject as TimelineAsset;
            Bake(timeline, GenPlanCache.DirectorOf(timeline));
        }

        [MenuItem("Assets/Create/SA/Inbetween Generation Timeline", false, 301)]
        static void CreateFromSelection()
        {
            var recipe = BridgeMenu.Build(Selection.objects, out var target);
            try
            {
                var sources = new List<ClipFrame>();
                if (recipe.Start.HasSource) sources.Add(recipe.Start);
                foreach (var goal in recipe.Goals)
                    if (goal.HasSource) sources.Add(goal);
                var folder = Path.GetDirectoryName(target)?.Replace('\\', '/') ?? "Assets";
                if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";
                var name = Path.GetFileNameWithoutExtension(target) + " Generation";
                var timeline = GenLayout.Create(AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".playable"), sources, recipe.Character, GenLayout.GenerateSeconds, 1);
                Selection.activeObject = timeline;
                EditorGUIUtility.PingObject(timeline);
            }
            finally
            {
                Object.DestroyImmediate(recipe);
            }
        }
    }
}

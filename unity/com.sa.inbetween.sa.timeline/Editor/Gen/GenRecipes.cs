using System.Collections.Generic;
using System.IO;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenRecipes
    {
        public const string FolderSuffix = "_Generated";
        public const string PlanExtension = ".genplan.json";

        public static readonly GenVariant[] Order = { GenVariant.AsSet, GenVariant.Shorter, GenVariant.Longer, GenVariant.OtherLanding };

        public static string Folder(TimelineAsset timeline)
        {
            var path = timeline != null ? AssetDatabase.GetAssetPath(timeline) : "";
            if (path == "") return "";
            return (Path.GetDirectoryName(path) ?? "Assets").Replace('\\', '/') + "/" + timeline.name + FolderSuffix;
        }

        public static string PlanPath(TimelineAsset timeline)
        {
            var folder = Folder(timeline);
            return folder == "" ? "" : folder + "/" + timeline.name + PlanExtension;
        }

        public static List<GenVariant> Variants(GeneratePayload payload)
        {
            var count = Mathf.Clamp(payload.Variations, 1, Order.Length);
            var list = new List<GenVariant> { payload.Keep };
            foreach (var variant in Order)
                if (list.Count < count && variant != payload.Keep)
                    list.Add(variant);
            return list;
        }

        public static string RecipeName(GenJob job, GenVariant variant) => variant == GenVariant.AsSet ? job.Name : job.Name + "_" + variant;

        public static string RecipePath(TimelineAsset timeline, GenJob job, GenVariant variant)
        {
            var folder = Folder(timeline);
            return folder == "" ? "" : folder + "/" + RecipeName(job, variant) + ".asset";
        }

        public static bool Settings(GenJob job, GenVariant variant, out float seconds, out bool landExactly, out string problem)
        {
            seconds = (float)job.MoveSeconds;
            landExactly = job.Payload.LandExactly;
            problem = "";
            var tokens = StudioCommands.TokensForSeconds(seconds);
            switch (variant)
            {
                case GenVariant.Shorter:
                    if (tokens - 1 < StudioCommands.MinTokens)
                    {
                        problem = "a Shorter version is not possible: " + GenRules.Text(StudioCommands.SecondsForTokens(StudioCommands.MinTokens)) + " s is already the shortest move.";
                        return false;
                    }

                    seconds = StudioCommands.SecondsForTokens(tokens - 1);
                    return true;
                case GenVariant.Longer:
                    if (tokens + 1 > StudioCommands.MaxTokens)
                    {
                        problem = "a Longer version is not possible: " + GenRules.Text(StudioCommands.SecondsForTokens(StudioCommands.MaxTokens)) + " s is already the longest move.";
                        return false;
                    }

                    seconds = StudioCommands.SecondsForTokens(tokens + 1);
                    return true;
                case GenVariant.OtherLanding:
                    landExactly = !landExactly;
                    return true;
                default:
                    return true;
            }
        }

        public static bool Configure(InbetweenRecipe recipe, GenJob job, GenVariant variant)
        {
            var before = JsonUtility.ToJson(recipe);
            Settings(job, variant, out var seconds, out var landExactly, out _);
            recipe.Character = job.Character;
            recipe.Start = job.From;
            recipe.Goals = new List<ClipFrame>(job.Goals);
            recipe.DurationSeconds = seconds;
            recipe.LandExactly = landExactly;
            recipe.SaveAnimationClip = job.Character != null && (job.Export.Anim || (job.Export.PlaceOnTimeline && !GenOutputs.PlacesHumanoid(job)));
            return JsonUtility.ToJson(recipe) != before;
        }

        public static bool NeedsGeneration(InbetweenRecipe recipe) => recipe.Output == null || recipe.BuiltFrom != RecipeRunner.Hash(recipe);

        public static InbetweenRecipe Ensure(TimelineAsset timeline, GenJob job, GenVariant variant, out string error)
        {
            error = "";
            var folder = Folder(timeline);
            if (folder == "")
            {
                error = "Save the timeline as an asset first.";
                return null;
            }

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder)?.Replace('\\', '/'), Path.GetFileName(folder));
            var path = RecipePath(timeline, job, variant);
            var recipe = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(path);
            if (recipe == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    error = "'" + path + "' exists and is not an Inbetween recipe. Rename the Generate clip " + job.Frame.Where + " or move that file.";
                    return null;
                }

                recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                Configure(recipe, job, variant);
                AssetDatabase.CreateAsset(recipe, path);
                AssetDatabase.SaveAssetIfDirty(recipe);
                return recipe;
            }

            if (Configure(recipe, job, variant))
            {
                EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssetIfDirty(recipe);
            }

            return recipe;
        }
    }
}

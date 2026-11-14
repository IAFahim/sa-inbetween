using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class RecipeFiles : AssetModificationProcessor
    {
        public static readonly string[] Suffixes = { ".saclip", ".anim", "_Humanoid.anim", ".fbx", "_start_pose.saclip", "_goal_pose.saclip" };

        static AssetMoveResult OnWillMoveAsset(string source, string destination)
        {
            if (AssetDatabase.GetMainAssetTypeAtPath(source) != typeof(InbetweenRecipe)) return AssetMoveResult.DidNotMove;
            var moves = Companions(source, destination, AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(source));
            if (moves.Count > 0) EditorApplication.delayCall += () => Move(moves);
            return AssetMoveResult.DidNotMove;
        }

        public static List<(string From, string To)> Companions(string recipeFrom, string recipeTo, InbetweenRecipe recipe)
        {
            var moves = new List<(string, string)>();
            var fromFolder = Folder(recipeFrom);
            var toFolder = Folder(recipeTo);
            var fromName = Path.GetFileNameWithoutExtension(recipeFrom);
            var toName = Path.GetFileNameWithoutExtension(recipeTo);
            if (string.Equals(fromFolder, toFolder, StringComparison.Ordinal) && string.Equals(fromName, toName, StringComparison.Ordinal)) return moves;
            foreach (var suffix in Suffixes)
            {
                var from = fromFolder + "/" + fromName + suffix;
                var to = toFolder + "/" + toName + suffix;
                if (recipe != null && suffix == ".saclip" && !string.Equals(AssetDatabase.GetAssetPath(recipe.Output), from, StringComparison.Ordinal)) continue;
                if (recipe != null && suffix == ".anim" && !string.Equals(AssetDatabase.GetAssetPath(recipe.SavedClip), from, StringComparison.Ordinal)) continue;
                if (recipe != null && IsSource(recipe, from)) continue;
                if (System.IO.File.Exists(from) && !System.IO.File.Exists(to)) moves.Add((from, to));
            }

            return moves;
        }

        static bool IsSource(InbetweenRecipe recipe, string path)
        {
            var sources = new List<UnityEngine.Object> { recipe.Character, recipe.Start.Motion, recipe.Start.Character, recipe.Start.Clip };
            foreach (var g in recipe.Goals) { sources.Add(g.Motion); sources.Add(g.Character); sources.Add(g.Clip); }
            foreach (var source in sources)
                if (source != null && string.Equals(AssetDatabase.GetAssetPath(source), path, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static string Folder(string path) => (Path.GetDirectoryName(path) ?? "Assets").Replace('\\', '/');

        public static void Move(List<(string From, string To)> moves)
        {
            foreach (var (from, to) in moves)
            {
                if (!System.IO.File.Exists(from) || System.IO.File.Exists(to)) continue;
                var error = AssetDatabase.MoveAsset(from, to);
                if (error != "") UnityEngine.Debug.LogWarning("SA Inbetween: could not move " + from + " with its recipe: " + error);
            }
        }
    }
}

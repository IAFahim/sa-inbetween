using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using UnityEngine;

namespace SA.Inbetween.Clips.Tests
{
    public sealed class ModelPicksTests
    {
        static StudioSettings Settings() => new StudioSettings
        {
            CliPath = "/t/sa-inbetween",
            BakePath = "/t/sa-soma-bake",
            ModelPath = "/m/model.onnx",
            FamilyPath = "/f/humanoid-v1.safamily",
            OutputFolder = "Assets/Generated",
            Contexts = new System.Collections.Generic.List<string> { "/c/a0.pose", "/c/a1.pose", "/c/a2.pose", "/c/a3.pose" },
            Targets = new System.Collections.Generic.List<string> { "/p/a.pose" },
            ClipName = "hop",
        };

        [Test]
        public void Model_picks_length_lets_the_generator_choose_within_one_step_and_is_part_of_the_recipe_hash()
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                recipe.DurationSeconds = 1.0f;
                var tokens = StudioCommands.TokensForSeconds(recipe.DurationSeconds);
                var fixedLength = RecipeRunner.Hash(recipe);
                var asSet = Settings();
                RecipeRunner.ApplyLength(asSet, recipe);
                Assert.AreEqual(tokens, asSet.TokenOverride);
                Assert.IsTrue(StudioCommands.TryGenerate(asSet, out var fixedCommand, out var error), error);
                Assert.AreEqual(tokens.ToString(), fixedCommand.Args[fixedCommand.Args.IndexOf("--override") + 1]);

                recipe.ModelPicksLength = true;
                Assert.AreNotEqual(fixedLength, RecipeRunner.Hash(recipe), "letting the model pick the length must make the recipe out of date");
                var picked = Settings();
                RecipeRunner.ApplyLength(picked, recipe);
                Assert.AreEqual(0, picked.TokenOverride);
                Assert.AreEqual(tokens - 1, picked.TokensMin);
                Assert.AreEqual(tokens + 1, picked.TokensMax);
                Assert.IsTrue(StudioCommands.TryGenerate(picked, out var pickedCommand, out error), error);
                Assert.IsFalse(pickedCommand.Args.Contains("--override"));
                Assert.AreEqual((tokens - 1).ToString(), pickedCommand.Args[pickedCommand.Args.IndexOf("--tokens-min") + 1]);
                Assert.AreEqual((tokens + 1).ToString(), pickedCommand.Args[pickedCommand.Args.IndexOf("--tokens-max") + 1]);

                recipe.ModelPicksLength = false;
                Assert.AreEqual(fixedLength, RecipeRunner.Hash(recipe), "recipes that do not use it keep their hash");
                recipe.DurationSeconds = InbetweenRecipe.MinSeconds;
                recipe.ModelPicksLength = true;
                var shortest = Settings();
                RecipeRunner.ApplyLength(shortest, recipe);
                Assert.GreaterOrEqual(shortest.TokensMin, StudioCommands.MinTokens);
                Assert.IsTrue(StudioCommands.TryGenerate(shortest, out _, out error), error);
            }
            finally
            {
                Object.DestroyImmediate(recipe);
            }
        }
    }
}

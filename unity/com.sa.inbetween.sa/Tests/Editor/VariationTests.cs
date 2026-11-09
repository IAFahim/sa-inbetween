using System.Collections.Generic;
using NUnit.Framework;
using SA.Inbetween.Clips.Editor;

namespace SA.Inbetween.Clips.Tests
{
    public class VariationTests
    {
        static StudioSettings Base() => new StudioSettings
        {
            CliPath = "/t/sa-inbetween",
            BakePath = "/t/bake",
            ModelPath = "/m/model.onnx",
            FamilyPath = "/f/humanoid-v1.safamily",
            OutputFolder = "/w",
            WorkFolder = "/w",
            Contexts = new List<string> { "/w/start_0.pose", "/w/start_1.pose", "/w/start_2.pose", "/w/start_3.pose" },
            Targets = new List<string> { "/w/goal0_0.pose,/w/goal0_1.pose,/w/goal0_2.pose,/w/goal0_3.pose" },
            ClipName = "Walk_to_Dance",
            TokenOverride = 9,
        };

        [Test]
        public void Plan_tries_shorter_longer_and_model_picked()
        {
            var plan = RecipeVariations.Plan(9, true);
            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(9, plan[0].TokenOverride);
            Assert.AreEqual(8, plan[1].TokenOverride);
            Assert.AreEqual(10, plan[2].TokenOverride);
            Assert.AreEqual(0, plan[3].TokenOverride);
            Assert.AreEqual(8, plan[3].TokensMin);
            Assert.AreEqual(10, plan[3].TokensMax);
            foreach (var spec in plan) Assert.IsTrue(spec.Correct);
        }

        [Test]
        public void Plan_at_the_edges_fills_with_the_other_landing()
        {
            var low = RecipeVariations.Plan(6, true);
            Assert.AreEqual(4, low.Count);
            CollectionAssert.AreEqual(new[] { 6, 7, 0, 6 }, low.ConvertAll(s => s.TokenOverride));
            Assert.AreEqual(6, low[2].TokensMin);
            Assert.AreEqual(7, low[2].TokensMax);
            Assert.IsFalse(low[3].Correct);
            Assert.AreEqual("Other landing", low[3].Kind);

            var high = RecipeVariations.Plan(16, false);
            CollectionAssert.AreEqual(new[] { 16, 15, 0, 16 }, high.ConvertAll(s => s.TokenOverride));
            Assert.IsTrue(high[3].Correct);

            Assert.AreEqual(6, RecipeVariations.Plan(2, true)[0].TokenOverride);
        }

        [Test]
        public void Spares_replace_duplicates_with_the_other_landing()
        {
            var spares = RecipeVariations.Spares(9, true);
            CollectionAssert.AreEqual(new[] { 9, 8, 10 }, spares.ConvertAll(s => s.TokenOverride));
            foreach (var spec in spares) Assert.IsFalse(spec.Correct);
            CollectionAssert.AreEqual(new[] { 7 }, RecipeVariations.Spares(6, true).ConvertAll(s => s.TokenOverride));
            CollectionAssert.AreEqual(new[] { 15 }, RecipeVariations.Spares(16, false).ConvertAll(s => s.TokenOverride));
            var all = RecipeVariations.All(12, true);
            for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                Assert.IsFalse(all[i].TokenOverride == all[j].TokenOverride && all[i].Correct == all[j].Correct && all[i].TokensMin == all[j].TokensMin && all[i].TokensMax == all[j].TokensMax);
        }

        [Test]
        public void Same_as_finds_an_earlier_identical_clip()
        {
            var earlier = new List<byte[]> { new byte[] { 1 }, null, new byte[] { 2, 3 } };
            Assert.AreEqual(2, RecipeVariations.SameAs(earlier, new byte[] { 2, 3 }));
            Assert.AreEqual(0, RecipeVariations.SameAs(earlier, new byte[] { 1 }));
            Assert.AreEqual(-1, RecipeVariations.SameAs(earlier, new byte[] { 4 }));
        }

        [Test]
        public void Every_planned_variation_makes_valid_commands_in_its_own_folder()
        {
            var folders = new HashSet<string>();
            for (var tokens = StudioCommands.MinTokens; tokens <= StudioCommands.MaxTokens; tokens++)
            {
                var plan = RecipeVariations.All(tokens, tokens % 2 == 0);
                for (var i = 0; i < plan.Count; i++)
                {
                    var s = RecipeVariations.Apply(Base(), plan[i], "/w/variations/" + i);
                    Assert.IsTrue(StudioCommands.TryGenerate(s, out var generate, out var error), error);
                    Assert.IsTrue(StudioCommands.TryBake(s, out var bake, out error), error);
                    Assert.AreEqual(plan[i].TokenOverride != 0, generate.Args.Contains("--override"));
                    Assert.AreEqual(!plan[i].Correct, generate.Args.Contains("--no-correct"));
                    Assert.AreEqual(plan[i].TokensMin.ToString(), generate.Args[generate.Args.IndexOf("--tokens-min") + 1]);
                    StringAssert.StartsWith("/w/variations/" + i, StudioCommands.ClipPath(s).Replace('\\', '/'));
                    Assert.AreEqual(Base().Contexts[0], generate.Args[generate.Args.IndexOf("--context") + 1]);
                    folders.Add(StudioCommands.SaibPath(s));
                    Assert.AreEqual(StudioCommands.ClipPath(s), bake.Args[bake.Args.Count - 1]);
                }
            }

            Assert.AreEqual(7, folders.Count);
        }

        [Test]
        public void Describe_names_the_kind_and_length()
        {
            var plan = RecipeVariations.Plan(9, true);
            Assert.AreEqual("As set, 1.00 s", RecipeVariations.Describe(plan[0]));
            Assert.AreEqual("Model picks, 0.87-1.13 s", RecipeVariations.Describe(plan[3]));
            StringAssert.EndsWith("loose", RecipeVariations.Describe(RecipeVariations.Plan(6, true)[3]));
        }

        [Test]
        public void Kept_seconds_round_trip_to_the_same_tokens()
        {
            Assert.AreEqual(9, RecipeVariations.TokensForFrames(30, 1));
            Assert.AreEqual(9, RecipeVariations.TokensForFrames(60, 2));
            Assert.AreEqual(StudioCommands.MaxTokens, RecipeVariations.TokensForFrames(500, 1));
            var plan = RecipeVariations.Plan(9, true);
            for (var i = 0; i < plan.Count; i++)
            {
                var seconds = RecipeVariations.KeptSeconds(plan[i], StudioCommands.FramesForTokens(10), 1);
                var expected = plan[i].TokenOverride != 0 ? plan[i].TokenOverride : 10;
                Assert.AreEqual(expected, StudioCommands.TokensForSeconds(seconds));
            }
        }

        [Test]
        public void File_undo_compares_contents()
        {
            Assert.IsTrue(ClipFileUndo.Same(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
            Assert.IsFalse(ClipFileUndo.Same(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
            Assert.IsFalse(ClipFileUndo.Same(new byte[0], new byte[] { 1 }));
        }
    }
}

using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEngine;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class GenModelPicksTests
    {
        [Test]
        public void A_Model_Picks_take_writes_its_own_recipe_that_lets_the_model_pick_the_length()
        {
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 2, Keep = GenVariant.ModelPicks }), Is.EqualTo(new[] { GenVariant.ModelPicks, GenVariant.AsSet }));
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 4 }), Has.No.Member(GenVariant.ModelPicks), "four takes keep their old order");
            var job = new GenJob { Name = "Hop", MoveSeconds = 1.0, Payload = new GeneratePayload { LandExactly = true, Variations = 1, Keep = GenVariant.ModelPicks } };
            job.Goals.Add(new ClipFrame { Kind = SourceKind.SaClip, Where = FrameWhere.Start });
            Assert.That(GenRecipes.RecipeName(job, GenVariant.ModelPicks), Is.EqualTo("Hop_ModelPicks"));
            Assert.That(GenRecipes.Settings(job, GenVariant.ModelPicks, out var seconds, out var landExactly, out var problem), Is.True, problem);
            Assert.That(seconds, Is.EqualTo(1.0f).Within(1e-6));
            Assert.That(landExactly, Is.True);
            var picked = ScriptableObject.CreateInstance<InbetweenRecipe>();
            var asSet = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                GenRecipes.Configure(picked, job, GenVariant.ModelPicks);
                GenRecipes.Configure(asSet, job, GenVariant.AsSet);
                Assert.That(picked.ModelPicksLength, Is.True);
                Assert.That(asSet.ModelPicksLength, Is.False);
                Assert.That(picked.DurationSeconds, Is.EqualTo(asSet.DurationSeconds));
                Assert.That(GenRecipes.Configure(picked, job, GenVariant.ModelPicks), Is.False, "configuring it again changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(picked);
                Object.DestroyImmediate(asSet);
            }
        }
    }
}

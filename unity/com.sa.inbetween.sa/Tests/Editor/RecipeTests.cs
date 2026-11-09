using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using UnityEngine;

namespace SA.Inbetween.Clips.Tests
{
    public class RecipeTests
    {
        [Test]
        public void Times_follow_the_role_of_the_frame()
        {
            var end = new ClipFrame { Where = FrameWhere.End };
            var start = new ClipFrame { Where = FrameWhere.Start };
            Assert.AreEqual("e-3f,e-2f,e-1f,e", RecipeRunner.Times(end, true));
            Assert.AreEqual("e-3f,e-2f,e-1f,e", RecipeRunner.Times(end, false));
            Assert.AreEqual("s,s,s,s", RecipeRunner.Times(start, true));
            Assert.AreEqual("s,s+1f,s+2f,s+3f", RecipeRunner.Times(start, false));
            var at = new ClipFrame { Where = FrameWhere.Time, Seconds = 0.5f };
            Assert.AreEqual("0.4,0.4333,0.4667,0.5", RecipeRunner.Times(at, true));
            Assert.AreEqual("0.5,0.5333,0.5667,0.6", RecipeRunner.Times(at, false));
            var frame = new ClipFrame { Where = FrameWhere.Frame, Frame = 15 };
            Assert.AreEqual("0.4,0.4333,0.4667,0.5", RecipeRunner.Times(frame, true));
            Assert.AreEqual("0.5,0.5333,0.5667,0.6", RecipeRunner.Times(frame, false));
            at.Seconds = 0.05f;
            Assert.AreEqual("0,0,0.0167,0.05", RecipeRunner.Times(at, true));
        }

        [Test]
        public void Hash_changes_with_settings_and_is_stable()
        {
            var r = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                var h = RecipeRunner.Hash(r);
                Assert.AreEqual(h, RecipeRunner.Hash(r));
                r.DurationSeconds = 1.5f;
                Assert.AreNotEqual(h, RecipeRunner.Hash(r));
                h = RecipeRunner.Hash(r);
                r.LandExactly = false;
                Assert.AreNotEqual(h, RecipeRunner.Hash(r));
            }
            finally
            {
                Object.DestroyImmediate(r);
            }
        }

        [Test]
        public void Incomplete_recipe_names_the_problem()
        {
            var r = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                StringAssert.Contains("starts from", RecipeRunner.Problem(r));
                Assert.AreEqual(RecipeState.Incomplete, RecipeRunner.StateOf(r));
            }
            finally
            {
                Object.DestroyImmediate(r);
            }
        }
    }
}

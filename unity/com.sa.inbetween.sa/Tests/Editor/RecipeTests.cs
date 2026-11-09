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
        public void Unity_clip_frames_count_at_the_clip_frame_rate()
        {
            var motion = new AnimationClip { frameRate = 24f };
            try
            {
                var frame = new ClipFrame { Kind = SourceKind.UnityClip, Motion = motion, Where = FrameWhere.Frame, Frame = 12 };
                Assert.AreEqual(0.5f, frame.AnchorSeconds, 1e-6f);
                Assert.AreEqual("0.4,0.4333,0.4667,0.5", RecipeRunner.Times(frame, true));
                frame.Kind = SourceKind.SaClip;
                Assert.AreEqual(0.4f, frame.AnchorSeconds, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(motion);
            }
        }

        [Test]
        public void Unity_clip_sources_need_a_clip_and_a_character_and_change_the_hash()
        {
            var r = ScriptableObject.CreateInstance<InbetweenRecipe>();
            var motion = new AnimationClip { name = "m" };
            var character = new GameObject("c");
            try
            {
                var before = RecipeRunner.Hash(r);
                r.Start = new ClipFrame { Kind = SourceKind.UnityClip, Where = FrameWhere.Frame, Frame = 3 };
                StringAssert.Contains("Unity clip", RecipeRunner.Problem(r));
                r.Start.Motion = motion;
                StringAssert.Contains("character", RecipeRunner.Problem(r));
                r.Start.Character = character;
                StringAssert.DoesNotContain("start", RecipeRunner.Problem(r).ToLowerInvariant());
                var unity = RecipeRunner.Hash(r);
                Assert.AreNotEqual(before, unity);
                r.Start.Frame = 4;
                Assert.AreNotEqual(unity, RecipeRunner.Hash(r));
            }
            finally
            {
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(motion);
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

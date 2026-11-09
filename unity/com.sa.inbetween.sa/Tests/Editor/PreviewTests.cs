using NUnit.Framework;
using SA.Inbetween.Clips.Editor;
using UnityEngine;

namespace SA.Inbetween.Clips.Tests
{
    public class PreviewTests
    {
        [Test]
        public void Reference_skeleton_stands_on_the_ground_facing_forward()
        {
            var world = new Vector3[SaClipPose.BoneCount];
            SaClipPose.Reference(world);
            Assert.AreEqual(Vector3.zero, world[0]);
            Assert.That(Vector3.Distance(new Vector3(0f, 1f, 0f), world[SaClipPose.Hips]), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(new Vector3(0f, 1.68f, 0f), world[SaClipPose.Head]), Is.LessThan(1e-5f));
            Assert.Less(world[13].x, 0f);
            Assert.Greater(world[32].x, 0f);
            Assert.Greater(world[51].z, world[50].z);
        }

        [Test]
        public void Parents_come_before_their_children()
        {
            Assert.AreEqual(-1, SaClipPose.Parent(0));
            for (var i = 1; i < SaClipPose.BoneCount; i++)
            {
                Assert.GreaterOrEqual(SaClipPose.Parent(i), 0);
                Assert.Less(SaClipPose.Parent(i), i);
            }
        }

        [Test]
        public void Bad_bytes_are_rejected_with_a_reason()
        {
            Assert.IsFalse(SaClipPose.TryLoad(new byte[0], out var pose, out var error));
            Assert.IsNull(pose);
            StringAssert.Contains("no data", error);
            Assert.IsFalse(SaClipPose.TryLoad(new byte[256], out _, out error));
            StringAssert.Contains("rejected", error);
            var clip = new SkeletonClip();
            Assert.IsFalse(clip.Bind(new byte[] { 1, 2, 3 }));
            Assert.AreNotEqual("", clip.Error);
            Assert.AreEqual(0f, clip.Duration);
        }

        [Test]
        public void Frame_seconds_follow_where_in_the_clip()
        {
            Assert.AreEqual(1.5f, PreviewMath.FrameSeconds(new ClipFrame { Where = FrameWhere.End }, 1.5f));
            Assert.AreEqual(0f, PreviewMath.FrameSeconds(new ClipFrame { Where = FrameWhere.Start }, 1.5f));
            Assert.AreEqual(0.4f, PreviewMath.FrameSeconds(new ClipFrame { Where = FrameWhere.Time, Seconds = 0.4f }, 1.5f));
            Assert.AreEqual(1.5f, PreviewMath.FrameSeconds(new ClipFrame { Where = FrameWhere.Time, Seconds = 9f }, 1.5f));
        }

        [Test]
        public void Wrap_loops_the_clock_over_the_clip()
        {
            Assert.AreEqual(0.25f, PreviewMath.Wrap(1.25, 1f), 1e-5f);
            Assert.AreEqual(0f, PreviewMath.Wrap(3.0, 1.5f), 1e-5f);
            Assert.AreEqual(0f, PreviewMath.Wrap(5.0, 0f));
            Assert.AreEqual("0.50 / 1.00 s", PreviewMath.Label(0.5f, 1f));
        }

        [Test]
        public void Fingers_get_no_joint_spheres_and_the_head_is_largest()
        {
            Assert.AreEqual(0f, PreviewMath.JointRadius(20));
            Assert.AreEqual(0f, PreviewMath.JointRadius(40));
            Assert.Greater(PreviewMath.JointRadius(SaClipPose.Head), PreviewMath.JointRadius(SaClipPose.Hips));
            Assert.Greater(PreviewMath.JointRadius(50), 0f);
        }

        [Test]
        public void Preview_without_a_result_has_no_layers()
        {
            var r = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                var preview = new SkeletonPreview();
                preview.Bind(r);
                preview.Evaluate(r, 0.5f, true);
                Assert.IsFalse(preview.Ready);
                Assert.AreEqual(0, preview.Layers.Count);
            }
            finally
            {
                Object.DestroyImmediate(r);
            }
        }
    }
}

using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class TimelineRecipeTests
    {
        TimelineAsset timeline;
        AnimationTrack track;
        AnimationClip jump;
        AnimationClip idle;
        AnimationClip other;

        static AnimationClip Clip(string name, float seconds, bool loop = false)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, seconds, 1));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        [SetUp]
        public void SetUp()
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            track = timeline.CreateTrack<AnimationTrack>(null, "Body");
            jump = Clip("A_Jump", 2f);
            idle = Clip("A Idle", 3f, true);
            other = Clip("Other", 1f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(jump);
            Object.DestroyImmediate(idle);
            Object.DestroyImmediate(other);
        }

        TimelineClip Add(AnimationClip clip, double start)
        {
            var c = track.CreateClip(clip);
            c.start = start;
            return c;
        }

        [Test]
        public void Neighbours_are_found_in_time_order()
        {
            var b = Add(idle, 3.0);
            var a = Add(jump, 0.0);
            var c = Add(other, 7.0);
            Assert.That(TimelineBridge.Next(a), Is.SameAs(b));
            Assert.That(TimelineBridge.Next(b), Is.SameAs(c));
            Assert.That(TimelineBridge.Previous(b), Is.SameAs(a));
            Assert.That(TimelineBridge.NeighbourProblem(a, b), Is.Empty);
            Assert.That(TimelineBridge.NeighbourProblem(a, c), Does.Contain("next to each other"));
            Assert.That(TimelineBridge.NeighbourProblem(b, a), Does.Contain("start before"));
        }

        [Test]
        public void Other_tracks_are_rejected()
        {
            var a = Add(jump, 0.0);
            var otherTrack = timeline.CreateTrack<AnimationTrack>(null, "Other");
            var b = otherTrack.CreateClip(idle);
            Assert.That(TimelineBridge.NeighbourProblem(a, b), Does.Contain("same Animation Track"));
        }

        [Test]
        public void Full_clips_map_to_end_and_start()
        {
            var a = Add(jump, 0.0);
            var b = Add(idle, 3.0);
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                TimelineBridge.Configure(recipe, a, b, null, BridgePlan.DurationFor(BridgePlan.Gap(a.end, b.start)));
                Assert.That(recipe.Start.Kind, Is.EqualTo(SourceKind.UnityClip));
                Assert.That(recipe.Start.Motion, Is.SameAs(jump));
                Assert.That(recipe.Start.Where, Is.EqualTo(FrameWhere.End));
                Assert.That(recipe.Goals, Has.Count.EqualTo(1));
                Assert.That(recipe.Goals[0].Motion, Is.SameAs(idle));
                Assert.That(recipe.Goals[0].Where, Is.EqualTo(FrameWhere.Start));
                Assert.That(recipe.DurationSeconds, Is.EqualTo(1.0f).Within(1e-5));
                Assert.That(recipe.SaveAnimationClip, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(recipe);
            }
        }

        [Test]
        public void Trimmed_clips_map_to_exact_times()
        {
            var a = Add(jump, 0.0);
            a.clipIn = 0.25;
            a.duration = 1.0; // A plays 0.25..1.25 of its clip
            var b = Add(idle, 1.75);
            b.clipIn = 0.5;
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                TimelineBridge.Configure(recipe, a, b, null, BridgePlan.DurationFor(b.start - a.end));
                Assert.That(recipe.Start.Where, Is.EqualTo(FrameWhere.Time));
                Assert.That(recipe.Start.Seconds, Is.EqualTo(1.25f).Within(1e-5));
                Assert.That(recipe.Start.AnchorSeconds, Is.EqualTo(1.25f).Within(1e-5));
                Assert.That(recipe.Goals[0].Where, Is.EqualTo(FrameWhere.Time));
                Assert.That(recipe.Goals[0].Seconds, Is.EqualTo(0.5f).Within(1e-5));
                Assert.That(recipe.DurationSeconds, Is.EqualTo(0.75f).Within(1e-5));
            }
            finally
            {
                Object.DestroyImmediate(recipe);
            }
        }

        [Test]
        public void Configure_reports_changes_only()
        {
            var a = Add(jump, 0.0);
            var b = Add(idle, 3.0);
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                Assert.That(TimelineBridge.Configure(recipe, a, b, null, 1.0), Is.True);
                Assert.That(TimelineBridge.Configure(recipe, a, b, null, 1.0), Is.False);
                b.clipIn = 0.4;
                Assert.That(TimelineBridge.Configure(recipe, a, b, null, 1.0), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(recipe);
            }
        }

        [Test]
        public void Recipe_name_is_file_safe()
        {
            var a = Add(jump, 0.0);
            var b = Add(idle, 3.0);
            Assert.That(TimelineBridge.RecipeName(a, b), Is.EqualTo("Bridge_A_Jump_to_A_Idle"));
        }

        [Test]
        public void Bridge_link_is_the_file_next_to_the_recipe()
        {
            const string folder = "Assets/zz_sa_timeline_link_test";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "zz_sa_timeline_link_test");
            try
            {
                var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                AssetDatabase.CreateAsset(recipe, folder + "/Bridge_X.asset");
                var generic = Clip("g", 1f);
                AssetDatabase.CreateAsset(generic, folder + "/Bridge_X.anim");
                var humanoid = Clip("h", 1f);
                AssetDatabase.CreateAsset(humanoid, folder + "/Bridge_X_Humanoid.anim");
                var unrelated = Clip("u", 1f);
                AssetDatabase.CreateAsset(unrelated, folder + "/Unrelated.anim");

                Assert.That(TimelineBridge.RecipeOf(track.CreateClip(generic)), Is.SameAs(recipe));
                Assert.That(TimelineBridge.RecipeOf(track.CreateClip(humanoid)), Is.SameAs(recipe));
                Assert.That(TimelineBridge.RecipeOf(track.CreateClip(unrelated)), Is.Null);
                Assert.That(TimelineBridge.IsBridge(track.CreateClip(jump)), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void Delta_takes_one_pose_onto_another_with_yaw_only()
        {
            var from = new Pose(new Vector3(1, 1, 0), Quaternion.Euler(10, 30, 0));
            var to = new Pose(new Vector3(-2, 1.2f, 4), Quaternion.Euler(10, 120, 0));
            var d = TimelineBridge.Delta(to, from);
            var moved = d.MultiplyPoint3x4(from.position);
            Assert.That(Vector3.Distance(moved, to.position), Is.LessThan(1e-4f));
            Assert.That(d.rotation.eulerAngles.x, Is.EqualTo(0).Within(1e-3));
            Assert.That(d.rotation.eulerAngles.z, Is.EqualTo(0).Within(1e-3));
            Assert.That(Quaternion.Angle(d.rotation * from.rotation, to.rotation), Is.LessThan(0.5f));
        }
    }
}

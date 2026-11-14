using System.Linq;
using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using Tl.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.TlGlue.Tests
{
    public sealed class TlSettingsTests
    {
        TimelineAsset timeline;
        TimelineAsset next;
        GenTrack steps;
        AnimationClip idle;
        GameObject hero;

        [SetUp]
        public void SetUp()
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            next = ScriptableObject.CreateInstance<TimelineAsset>();
            steps = timeline.CreateTrack<GenTrack>(null, "Steps");
            idle = new AnimationClip { name = "Idle", frameRate = 30f };
            idle.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 2, 1));
            var source = steps.CreateClip<SourceClip>();
            ((SourceClip)source.asset).Payload = new SourcePayload { Kind = SourceKind.UnityClip, Motion = idle };
            source.start = 0;
            source.duration = 2;
            var generate = steps.CreateClip<GenerateClip>();
            generate.start = 2;
            generate.duration = 1;
            AssetDatabase.CreateFolder("Assets", "zz_gen_tl_test");
            var go = new GameObject("Hero");
            go.AddComponent<Animator>();
            hero = PrefabUtility.SaveAsPrefabAsset(go, "Assets/zz_gen_tl_test/Hero.prefab");
            Object.DestroyImmediate(go);
            var retarget = steps.CreateClip<RetargetClip>();
            retarget.start = 0;
            retarget.duration = 3;
            ((RetargetClip)retarget.asset).Payload = new RetargetPayload { Character = hero };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(next);
            Object.DestroyImmediate(idle);
            AssetDatabase.DeleteAsset("Assets/zz_gen_tl_test");
        }

        TlTimelineSettings Settings(TlEnd end)
        {
            timeline.CreateMarkerTrack();
            var settings = timeline.markerTrack.CreateMarker<TlTimelineSettings>(0);
            settings.End = end;
            settings.Next = end == TlEnd.Next ? next : null;
            return settings;
        }

        [Test]
        public void Without_tl_settings_a_generate_clip_at_the_end_has_nothing_to_land_on()
        {
            var plan = GenPlanner.Plan(timeline, null);
            Assert.That(plan.Loop, Is.False);
            Assert.That(string.Join("\n", plan.Faults.Select(f => f.Message)), Does.Contain("there is nothing to land on"));
        }

        [Test]
        public void Tl_loop_wraps_the_goal_to_the_first_source()
        {
            Settings(TlEnd.Loop);
            var plan = GenPlanner.Plan(timeline, null);
            Assert.That(plan.Loop, Is.True);
            Assert.That(plan.Faults, Is.Empty, string.Join("\n", plan.Faults.Select(f => f.Message)));
            var job = plan.Jobs.Single();
            Assert.That(job.Goals.Single().Motion, Is.SameAs(idle));
            Assert.That(job.Goals.Single().Where, Is.EqualTo(FrameWhere.Start));
            Assert.That(job.Name, Is.EqualTo("Idle_loop"));
            Assert.That(GenPlanJson.Write(plan, false, out _), Does.Contain("\"loop\": true"));
        }

        [Test]
        public void Tl_next_names_the_timeline_baked_after_this_one()
        {
            next.name = "After";
            Settings(TlEnd.Next);
            var plan = GenPlanner.Plan(timeline, null);
            Assert.That(plan.Next, Is.SameAs(next));
            Assert.That(plan.Loop, Is.False);
            Assert.That(GenPlanJson.Write(plan, false, out _), Does.Contain("\"next\": \"After\""));
        }
    }
}

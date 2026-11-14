using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class GenPlanTests
    {
        const string Folder = "Assets/zz_gen_plan_test";

        TimelineAsset timeline;
        GenTrack steps;
        GenTrack bake;
        GameObject hero;
        AnimationClip jump;
        AnimationClip idle;

        static AnimationClip Clip(string name, float seconds, bool loop = false)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, seconds, 1));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        [OneTimeSetUp]
        public void Character()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "zz_gen_plan_test");
            var go = new GameObject("Hero");
            go.AddComponent<Animator>();
            hero = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/Hero.prefab");
            Object.DestroyImmediate(go);
        }

        [OneTimeTearDown]
        public void RemoveCharacter() => AssetDatabase.DeleteAsset(Folder);

        [SetUp]
        public void SetUp()
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "Plan";
            steps = timeline.CreateTrack<GenTrack>(null, "Steps");
            bake = timeline.CreateTrack<GenTrack>(null, "Bake");
            jump = Clip("A_Jump", 2f);
            idle = Clip("A Idle", 3f, true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(jump);
            Object.DestroyImmediate(idle);
        }

        TimelineClip Source(GenTrack track, AnimationClip motion, double start, double duration = 0, double clipIn = 0)
        {
            var clip = track.CreateClip<SourceClip>();
            var asset = (SourceClip)clip.asset;
            asset.Payload = new SourcePayload { Kind = SourceKind.UnityClip, Motion = motion };
            clip.start = start;
            clip.duration = duration > 0 ? duration : asset.duration;
            clip.clipIn = clipIn;
            clip.displayName = motion.name;
            return clip;
        }

        TimelineClip Generate(GenTrack track, double start, double duration, string name = null)
        {
            var clip = track.CreateClip<GenerateClip>();
            clip.start = start;
            clip.duration = duration;
            if (name != null) clip.displayName = name;
            return clip;
        }

        TimelineClip Retarget(GenTrack track, double start, double duration)
        {
            var clip = track.CreateClip<RetargetClip>();
            clip.start = start;
            clip.duration = duration;
            ((RetargetClip)clip.asset).Payload = new RetargetPayload { Character = hero };
            return clip;
        }

        T Add<T>(GenTrack track, double start, double duration) where T : GenClip
        {
            var clip = track.CreateClip<T>();
            clip.start = start;
            clip.duration = duration;
            return (T)clip.asset;
        }

        GenPlan Plan() => GenPlanner.Plan(timeline, null);

        static string Faults(GenPlan plan) => string.Join("\n", plan.Faults.Select(f => f.Message));

        [Test]
        public void New_clips_get_their_defaults_and_names()
        {
            var generate = Generate(steps, 0, 1);
            var asset = (GenerateClip)generate.asset;
            Assert.That(generate.displayName, Is.EqualTo("Generate"));
            Assert.That(asset.Payload.LandExactly, Is.True);
            Assert.That(asset.Payload.Variations, Is.EqualTo(1));
            var export = Add<ExportClip>(bake, 0, 1);
            Assert.That(export.Payload.Anim && export.Payload.HumanoidAnim && export.Payload.PlaceOnTimeline && !export.Payload.Fbx, Is.True);
            Assert.That(Add<LabelClip>(bake, 0, 1).Payload.Label, Is.EqualTo("Inbetween"));
        }

        [Test]
        public void Every_clip_type_has_one_step()
        {
            Assert.That(GenPlanner.StepProblems, Is.Empty);
            foreach (var type in TypeCache.GetTypesDerivedFrom<GenClip>().Where(t => !t.IsAbstract))
                Assert.That(GenPlanner.Steps.ContainsKey(type), Is.True, type.FullName + " has no step");
        }

        [Test]
        public void The_bridge_layout_gives_the_bridge_recipe()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Retarget(bake, 0, 6);
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            Assert.That(plan.Jobs, Has.Count.EqualTo(1));
            var job = plan.Jobs[0];
            Assert.That(job.Name, Is.EqualTo("A_Jump_to_A_Idle"));
            Assert.That(job.From.Where, Is.EqualTo(FrameWhere.End));
            Assert.That(job.Goals[0].Where, Is.EqualTo(FrameWhere.Start));
            Assert.That(job.Character, Is.SameAs(hero));
            AssertSameAsBridge(job, 0, 2, 0, 3, 3, 0);
        }

        [Test]
        public void Trimmed_sources_give_the_bridge_recipe()
        {
            Source(steps, jump, 0, 1.0, 0.25);
            Generate(steps, 1.0, 0.75);
            Source(steps, idle, 1.75, 3.0, 0.5);
            Retarget(bake, 0, 5);
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            var job = plan.Jobs[0];
            Assert.That(job.From.Where, Is.EqualTo(FrameWhere.Time));
            Assert.That(job.From.Seconds, Is.EqualTo(1.25f).Within(1e-5));
            Assert.That(job.Goals[0].Where, Is.EqualTo(FrameWhere.Time));
            Assert.That(job.Goals[0].Seconds, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(job.MoveSeconds, Is.EqualTo(0.75).Within(1e-9));
            AssertSameAsBridge(job, 0, 1.0, 0.25, 1.75, 3.0, 0.5);
        }

        void AssertSameAsBridge(GenJob job, double aStart, double aDuration, double aClipIn, double bStart, double bDuration, double bClipIn)
        {
            var other = ScriptableObject.CreateInstance<TimelineAsset>();
            var mine = ScriptableObject.CreateInstance<InbetweenRecipe>();
            var bridge = ScriptableObject.CreateInstance<InbetweenRecipe>();
            try
            {
                var track = other.CreateTrack<AnimationTrack>(null, "Body");
                var a = track.CreateClip(jump);
                a.start = aStart;
                a.duration = aDuration;
                a.clipIn = aClipIn;
                var b = track.CreateClip(idle);
                b.start = bStart;
                b.duration = bDuration;
                b.clipIn = bClipIn;
                TimelineBridge.Configure(bridge, a, b, hero, BridgePlan.DurationFor(BridgePlan.Gap(a.end, b.start)));
                GenRecipes.Configure(mine, job, GenVariant.AsSet);
                Assert.That(RecipeRunner.Hash(mine), Is.EqualTo(RecipeRunner.Hash(bridge)));
                Assert.That(mine.Start.Where, Is.EqualTo(bridge.Start.Where));
                Assert.That(mine.Start.Seconds, Is.EqualTo(bridge.Start.Seconds));
                Assert.That(mine.Goals[0].Where, Is.EqualTo(bridge.Goals[0].Where));
                Assert.That(mine.Goals[0].Seconds, Is.EqualTo(bridge.Goals[0].Seconds));
                Assert.That(mine.DurationSeconds, Is.EqualTo(bridge.DurationSeconds));
                Assert.That(mine.Character, Is.SameAs(bridge.Character));
            }
            finally
            {
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(mine);
                Object.DestroyImmediate(bridge);
            }
        }

        [Test]
        public void Leaving_a_clip_early_starts_from_its_pose_at_that_time()
        {
            Source(steps, jump, 0, 1.8);
            Generate(bake, 1.0, 0.8);
            Source(steps, idle, 2.5);
            Retarget(bake, 0, 5);
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            var job = plan.Jobs[0];
            Assert.That(job.From.Where, Is.EqualTo(FrameWhere.Time));
            Assert.That(job.From.Seconds, Is.EqualTo(1.0f).Within(1e-5));
            Assert.That(job.Goals[0].Where, Is.EqualTo(FrameWhere.Start));
            Assert.That(job.FromText, Does.Contain("at 1.00 s (frame 30)"));
        }

        [Test]
        public void Stages_not_rows_decide_the_order()
        {
            var top = timeline.CreateTrack<GenTrack>(null, "Top");
            var export = top.CreateClip<ExportClip>();
            export.start = 0;
            export.duration = 6;
            ((ExportClip)export.asset).Payload = new ExportPayload { Anim = true, Fbx = true };
            Retarget(top, 0, 6);
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            Assert.That(plan.Jobs[0].Character, Is.SameAs(hero));
            Assert.That(plan.Jobs[0].HasExport && plan.Jobs[0].Export.Fbx && !plan.Jobs[0].Export.PlaceOnTimeline, Is.True);
        }

        [Test]
        public void A_goal_pose_inside_chains_two_moves_through_the_source_under_it()
        {
            var wave = Clip("Wave", 2f);
            try
            {
                Source(steps, jump, 0);
                Generate(steps, 2, 2.4);
                Source(steps, idle, 4.4);
                Source(bake, wave, 3.0, 1.0);
                var goal = bake.CreateClip<GoalPoseClip>();
                goal.start = 3.2;
                goal.duration = 0.2;
                Retarget(timeline.CreateTrack<GenTrack>(null, "Third"), 0, 7);
                var plan = Plan();
                Assert.That(plan.Faults, Is.Empty, Faults(plan));
                var job = plan.Jobs[0];
                Assert.That(job.Moves, Is.EqualTo(2));
                Assert.That(job.Goals, Has.Count.EqualTo(2));
                Assert.That(job.MoveSeconds, Is.EqualTo(1.2).Within(1e-9));
                Assert.That(job.Goals[0].Motion, Is.SameAs(wave));
                Assert.That(job.Goals[0].Where, Is.EqualTo(FrameWhere.Time));
                Assert.That(job.Goals[0].Seconds, Is.EqualTo(0.2f).Within(1e-5));
                Assert.That(job.Goals[1].Motion, Is.SameAs(idle));
                Assert.That(job.From.Motion, Is.SameAs(jump));
                Assert.That(plan.Notes, Is.Empty);
                goal.start = 2.5;
                var uneven = Plan();
                Assert.That(uneven.Notes.Single().Message, Does.Contain("reached at 3.20 s, not 2.50 s"));
            }
            finally
            {
                Object.DestroyImmediate(wave);
            }
        }

        [Test]
        public void Label_and_export_clips_apply_to_the_generations_they_cover()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Retarget(bake, 0, 6);
            var label = bake.CreateClip<LabelClip>();
            label.start = 1;
            label.duration = 1.5;
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            Assert.That(plan.Jobs[0].Labels, Is.EqualTo(new[] { "Inbetween" }));
            Assert.That(plan.Jobs[0].HasExport, Is.False);
            Assert.That(plan.Jobs[0].Export.Anim && plan.Jobs[0].Export.PlaceOnTimeline, Is.True);
        }

        [Test]
        public void Pose_clips_override_start_and_goal()
        {
            var pose = ScriptableObject.CreateInstance<SaPoseAsset>();
            pose.name = "Crouch";
            pose.Values = new float[SaPoseAsset.FloatCount];
            try
            {
                Source(steps, jump, 0);
                Generate(steps, 2, 1);
                Source(steps, idle, 3);
                var start = bake.CreateClip<StartPoseClip>();
                start.start = 1.9;
                start.duration = 0.2;
                ((StartPoseClip)start.asset).Payload = new PosePayload { From = GenPoseFrom.PoseAsset, Pose = pose };
                var goal = bake.CreateClip<GoalPoseClip>();
                goal.start = 3.0;
                goal.duration = 0.2;
                ((GoalPoseClip)goal.asset).Payload = new PosePayload { From = GenPoseFrom.SourceFrame, Frame = 12 };
                Retarget(bake, 0, 6);
                var plan = Plan();
                Assert.That(plan.Faults, Is.Empty, Faults(plan));
                var job = plan.Jobs[0];
                Assert.That(job.From.Kind, Is.EqualTo(SourceKind.Pose));
                Assert.That(job.From.Pose, Is.SameAs(pose));
                Assert.That(job.Goals[0].Where, Is.EqualTo(FrameWhere.Frame));
                Assert.That(job.Goals[0].Frame, Is.EqualTo(12));
                Assert.That(job.Goals[0].Motion, Is.SameAs(idle));
                Assert.That(job.Name, Is.EqualTo("Crouch_to_A_Idle"));
            }
            finally
            {
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void A_named_generate_clip_names_its_files()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1, "Land Soft");
            Source(steps, idle, 3);
            Retarget(bake, 0, 6);
            var plan = Plan();
            Assert.That(plan.Jobs[0].Name, Is.EqualTo("Land_Soft"));
            Assert.That(GenPlanner.IsDefaultName("Generate 2"), Is.True);
            Assert.That(GenPlanner.IsDefaultName("Generated"), Is.False);
        }

        [Test]
        public void Variants_put_the_kept_one_first()
        {
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 3 }), Is.EqualTo(new[] { GenVariant.AsSet, GenVariant.Shorter, GenVariant.Longer }));
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 1, Keep = GenVariant.Longer }), Is.EqualTo(new[] { GenVariant.Longer }));
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 2, Keep = GenVariant.OtherLanding }), Is.EqualTo(new[] { GenVariant.OtherLanding, GenVariant.AsSet }));
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 4, Keep = GenVariant.Longer }), Is.EqualTo(new[] { GenVariant.Longer, GenVariant.AsSet, GenVariant.Shorter, GenVariant.OtherLanding }));
            Assert.That(GenRecipes.Variants(new GeneratePayload { Variations = 0 }), Is.EqualTo(new[] { GenVariant.AsSet }));
            var job = new GenJob { MoveSeconds = 1.0, Payload = new GeneratePayload { LandExactly = true }, Name = "Land" };
            Assert.That(GenRecipes.Settings(job, GenVariant.Shorter, out var shorter, out _, out _), Is.True);
            Assert.That(shorter, Is.EqualTo(StudioCommands.SecondsForTokens(StudioCommands.TokensForSeconds(1f) - 1)).Within(1e-6));
            Assert.That(GenRecipes.Settings(job, GenVariant.OtherLanding, out _, out var land, out _) && !land, Is.True);
            Assert.That(GenRecipes.RecipeName(job, GenVariant.AsSet), Is.EqualTo("Land"));
            Assert.That(GenRecipes.RecipeName(job, GenVariant.Longer), Is.EqualTo("Land_Longer"));
            var shortest = new GenJob { MoveSeconds = 0.6, Payload = new GeneratePayload { LandExactly = true } };
            Assert.That(GenRecipes.Settings(shortest, GenVariant.Shorter, out _, out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("already the shortest"));
        }

        [Test]
        public void An_impossible_alternate_is_skipped_with_a_note()
        {
            Source(steps, jump, 0);
            var generate = Generate(steps, 2, 0.6);
            var asset = (GenerateClip)generate.asset;
            asset.Payload = new GeneratePayload { LandExactly = true, Variations = 2 };
            Source(steps, idle, 2.6);
            Retarget(bake, 0, 6);
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            Assert.That(plan.Jobs[0].Variants, Is.EqualTo(new[] { GenVariant.AsSet }));
            Assert.That(plan.Notes.Single().Message, Does.Contain("Shorter version is not possible"));
        }

        [Test]
        public void Every_fault_names_the_clip_and_the_fix()
        {
            Generate(steps, 0, 1);
            var plan = Plan();
            var fault = plan.Faults.Single(f => f.Message.Contains("nothing to start from"));
            Assert.That(fault.Message, Does.StartWith("'Generate' (track 'Steps', 0.00 s): "));
            Assert.That(fault.Message, Does.Contain("Put a Source clip"));
            Assert.That(fault.Clip, Is.Not.Null);
            Assert.That(plan.FaultsFor(fault.Clip), Is.Not.Empty);
        }

        [Test]
        public void Nothing_to_land_on()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Retarget(bake, 0, 3);
            Assert.That(Faults(Plan()), Does.Contain("there is nothing to land on"));
        }

        [TestCase(2.5, "at most 1.93 s")]
        [TestCase(0.3, "at least 0.60 s")]
        public void A_window_the_generator_cannot_fill(double seconds, string expected)
        {
            Source(steps, jump, 0);
            Generate(steps, 2, seconds);
            Source(steps, idle, 2 + seconds);
            Retarget(bake, 0, 8);
            Assert.That(Faults(Plan()), Does.Contain(expected));
        }

        [Test]
        public void Overlapping_generate_clips_fail()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1, "One");
            Generate(bake, 2.5, 1, "Two");
            Source(steps, idle, 4);
            Retarget(timeline.CreateTrack<GenTrack>(null, "Third"), 0, 7);
            var plan = Plan();
            Assert.That(plan.Faults.Count(f => f.Message.Contains("Two generations cannot fill the same time")), Is.EqualTo(2));
        }

        [Test]
        public void Two_sources_at_one_edge_fail()
        {
            Source(steps, jump, 0);
            Source(bake, idle, 0, 2.0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Assert.That(Faults(Plan()), Does.Contain("two Source clips meet its start"));
        }

        [Test]
        public void Unity_clips_need_a_character()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Assert.That(Faults(Plan()), Does.Contain("no character is set to play it on"));
        }

        [Test]
        public void Two_generate_clips_with_one_name_fail()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1, "Land");
            Source(steps, idle, 3);
            Generate(steps, 6, 1, "Land");
            Source(steps, jump, 7);
            Retarget(bake, 0, 9);
            Assert.That(Plan().Faults.Count(f => f.Message.Contains("also writes 'Land'")), Is.EqualTo(2));
        }

        [Test]
        public void Unused_and_empty_clips_say_so()
        {
            var empty = steps.CreateClip<SourceClip>();
            empty.start = 0;
            empty.duration = 1;
            var pose = bake.CreateClip<StartPoseClip>();
            pose.start = 5;
            pose.duration = 0.2;
            Retarget(bake, 8, 1);
            var plan = Plan();
            var text = Faults(plan);
            Assert.That(text, Does.Contain("choose the Unity clip it plays"));
            Assert.That(text, Does.Contain("touches the start of no Generate clip"));
            Assert.That(text, Does.Contain("covers no Generate clip"));
        }

        [Test]
        public void Muted_tracks_are_left_out()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Retarget(bake, 0, 6);
            var muted = timeline.CreateTrack<GenTrack>(null, "Muted");
            Generate(muted, 2, 5);
            muted.muted = true;
            var plan = Plan();
            Assert.That(plan.Faults, Is.Empty, Faults(plan));
            Assert.That(plan.Jobs, Has.Count.EqualTo(1));
        }

        [Test]
        public void The_plan_file_is_the_same_bytes_every_time_and_in_any_culture()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 0.8);
            Source(steps, idle, 2.8);
            Retarget(bake, 0, 6);
            var first = GenPlanJson.Write(Plan(), true, out var digest);
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var second = GenPlanJson.Write(Plan(), true, out var again);
                Assert.That(second, Is.EqualTo(first));
                Assert.That(again, Is.EqualTo(digest));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }

            Assert.That(digest, Does.StartWith("sha256:"));
            Assert.That(first, Does.Contain("\"tickRate\": 30"));
            Assert.That(first, Does.Contain("\"seconds\": 0.8"));
            Assert.That(first, Does.Contain("\"type\": \"GenerateClip\", \"start\": 60, \"end\": 84"));
            Assert.That(first, Does.Contain("\"name\": \"A_Jump_to_A_Idle\""));
        }

        [Test]
        public void Bake_refuses_before_writing_anything()
        {
            Source(steps, jump, 0);
            Generate(steps, 2, 1);
            Source(steps, idle, 3);
            Retarget(bake, 0, 6);
            var plan = Plan();
            Assert.That(GenBake.Problem(plan), Does.Contain("Save the timeline as an asset first"));
            Assert.That(GenBake.Start(timeline, null, out var error), Is.Null);
            Assert.That(error, Does.Contain("Save the timeline"));
            var path = Folder + "/Refuse.playable";
            var saved = GenLayout.Create(path, new[] { new ClipFrame { Kind = SourceKind.UnityClip, Motion = jump }, new ClipFrame { Kind = SourceKind.UnityClip, Motion = idle } }, hero, 1.0, 1);
            try
            {
                var laidOut = GenPlanner.Plan(saved, null);
                Assert.That(laidOut.Faults, Is.Empty, Faults(laidOut));
                Assert.That(laidOut.Jobs.Single().Name, Is.EqualTo("A_Jump_to_A_Idle"));
                Assert.That(GenOutputs.Track(saved, false), Is.Not.Null);
                var none = new StudioTools { Cli = "", Bake = "", Model = "", Family = "", Rest = "" };
                var problem = GenBake.Problem(laidOut, none);
                Assert.That(problem, Does.StartWith("Inbetween needs its tools before it can bake."));
                Assert.That(problem, Does.Contain("mmm_v6_soma_fp32.onnx"));
                Assert.That(AssetDatabase.IsValidFolder(GenRecipes.Folder(saved)), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class ChainTests
    {
        const string Synty = "Assets/Synty/AnimationBaseLocomotion";
        const string Character = Synty + "/Meshes/PolygonSyntyCharacter.fbx";
        const string Clips = Synty + "/Animations/Polygon/Masculine";
        const double Timeout = 600.0;

        static TimelineClip Block(ChainTrack track, AnimationClip motion, double start, double seconds)
        {
            var clip = track.CreateClip<SourceClip>();
            ((SourceClip)clip.asset).Payload = new SourcePayload { Kind = SourceKind.UnityClip, Motion = motion };
            clip.start = start;
            clip.duration = seconds;
            clip.displayName = motion != null ? motion.name : "block";
            return clip;
        }

        static List<TimelineClip> Gaps(ChainTrack track) => track.GetClips().Where(c => c.asset is GenerateClip).OrderBy(c => c.start).ToList();

        [Test]
        public void Every_gap_between_two_blocks_gets_an_in_between_that_follows_the_blocks()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var motion = new AnimationClip { name = "Move" };
            try
            {
                var track = timeline.CreateTrack<ChainTrack>(null, "Chain");
                var a = Block(track, motion, 0.0, 1.0);
                var b = Block(track, motion, 1.8, 1.0);
                var c = Block(track, motion, 3.6, 1.0);
                Assert.That(ChainSync.Sync(track), Is.True);
                var gaps = Gaps(track);
                Assert.That(gaps.Count, Is.EqualTo(2));
                Assert.That(gaps[0].start, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(gaps[0].end, Is.EqualTo(1.8).Within(1e-9));
                Assert.That(gaps[1].start, Is.EqualTo(2.8).Within(1e-9));
                Assert.That(gaps[0].displayName, Is.EqualTo(ChainTrack.GapName));
                Assert.That(ChainSync.Sync(track), Is.False, "a second sync changes nothing");

                var settings = (GenerateClip)gaps[0].asset;
                var payload = settings.Payload;
                payload.Variations = 2;
                settings.Payload = payload;
                b.start = 2.0;
                Assert.That(ChainSync.Sync(track), Is.True);
                gaps = Gaps(track);
                Assert.That(gaps[0].end, Is.EqualTo(2.0).Within(1e-9), "the gap before the moved block grows");
                Assert.That(gaps[1].start, Is.EqualTo(3.0).Within(1e-9), "the gap after it shrinks");
                Assert.That(((GenerateClip)gaps[0].asset).Payload.Variations, Is.EqualTo(2), "a gap keeps its settings when a block moves");

                timeline.DeleteClip(b);
                ChainSync.Sync(track);
                gaps = Gaps(track);
                Assert.That(gaps.Count, Is.EqualTo(1));
                Assert.That(gaps[0].start, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(gaps[0].end, Is.EqualTo(3.6).Within(1e-9));

                c.start = a.end;
                ChainSync.Sync(track);
                Assert.That(Gaps(track), Is.Empty, "touching blocks leave no gap");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Dropping_a_clip_from_the_Project_window_on_the_row_makes_a_block_that_plays_it()
        {
            var editor = typeof(UnityEditor.Timeline.TimelineEditor).Assembly;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var handled = (System.Collections.IEnumerable)editor.GetType("UnityEditor.Timeline.TypeUtility").GetMethod("GetPlayableAssetsHandledByTrack", flags).Invoke(null, new object[] { typeof(ChainTrack) });
            Assert.That(handled.Cast<Type>(), Does.Contain(typeof(SourceClip)), "a Chain row takes Source clips");
            var fieldType = editor.GetType("UnityEditor.Timeline.ObjectReferenceField");
            var fields = (Array)fieldType.GetMethod("FindObjectReferences", flags, null, new[] { typeof(Type) }, null).Invoke(null, new object[] { typeof(SourceClip) });
            var found = fields.Cast<object>().Select(f => (Path: (string)fieldType.GetField("propertyPath").GetValue(f), Type: (Type)fieldType.GetField("type").GetValue(f))).ToList();
            Assert.That(found.Any(f => f.Type == typeof(AnimationClip) && f.Path == "Payload.Motion"), Is.True,
                "Timeline drops an AnimationClip on the row as a Source clip with this field set: " + string.Join(", ", found.Select(f => f.Path + ":" + f.Type.Name)));
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var motion = new AnimationClip { name = "Run" };
            try
            {
                var track = timeline.CreateTrack<ChainTrack>(null, "Chain");
                var dropped = track.CreateClip<SourceClip>();
                var serialized = new SerializedObject(dropped.asset);
                serialized.FindProperty("Payload.Motion").objectReferenceValue = motion;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var source = (SourceClip)dropped.asset;
                Assert.That(source.Payload.Kind, Is.EqualTo(SourceKind.UnityClip), "a new block is a Unity clip block");
                Assert.That(source.Payload.Motion, Is.SameAs(motion));
                Assert.That(source.HasSource, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void A_chain_plans_one_generation_per_gap_with_the_chains_settings_and_unique_names()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var motion = new AnimationClip { name = "Walk" };
            try
            {
                var track = timeline.CreateTrack<ChainTrack>(null, "Chain");
                var chain = ChainPayload.Default;
                chain.Seams = new SeamSettings { Curve = SeamCurve.Quintic, Seconds = 0.3f, Feet = false };
                chain.Export = new ExportPayload { Anim = true, HumanoidAnim = false, PlaceOnTimeline = true, PlaceSources = true };
                track.Chain = chain;
                for (var i = 0; i < 4; i++) Block(track, motion, i * 1.8, 1.0);
                ChainSync.Sync(track);
                var plan = GenPlanner.Plan(timeline, null);
                Assert.That(plan.Jobs.Count, Is.EqualTo(3));
                Assert.That(plan.Jobs.Select(j => j.Name).Distinct().Count(), Is.EqualTo(3), string.Join(", ", plan.Jobs.Select(j => j.Name)));
                foreach (var job in plan.Jobs)
                {
                    Assert.That(job.Seam.Curve, Is.EqualTo(SeamCurve.Quintic));
                    Assert.That(job.Seam.Seconds, Is.EqualTo(0.3f));
                    Assert.That(job.Export.HumanoidAnim, Is.False);
                    Assert.That(job.Chain, Is.SameAs(track));
                }

                var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                GenRecipes.Configure(recipe, plan.Jobs[0], GenVariant.AsSet);
                Assert.That(recipe.Seam.Curve, Is.EqualTo(SeamCurve.Quintic), "the recipe carries the chain's seam settings");
                UnityEngine.Object.DestroyImmediate(recipe);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void The_right_click_entries_apply_to_chain_tracks_and_their_gaps_only()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var motion = new AnimationClip { name = "Move" };
            try
            {
                var track = timeline.CreateTrack<ChainTrack>(null, "Chain");
                var animation = timeline.CreateTrack<AnimationTrack>(null, "Body");
                var block = Block(track, motion, 0.0, 1.0);
                Block(track, motion, 2.0, 1.0);
                ChainSync.Sync(track);
                var gap = Gaps(track).Single();
                Assert.That(new BakeTrackAction().Validate(new TrackAsset[] { track }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(new BakeTrackAction().Validate(new TrackAsset[] { animation }), Is.EqualTo(ActionValidity.NotApplicable));
                Assert.That(new RevealTrackAction().Validate(new TrackAsset[] { track }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(new RegenerateTrackAction().Validate(new TrackAsset[] { track }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(new BakeGapAction().Validate(new[] { gap }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(new BakeGapAction().Validate(new[] { block }), Is.EqualTo(ActionValidity.NotApplicable));
                Assert.That(new RegenerateGapAction().Validate(new[] { gap }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(new RevealGapAction().Validate(new[] { gap }), Is.EqualTo(ActionValidity.Valid));
                Assert.That(ChainCommands.BakeGaps(new[] { block }, false, out var error), Is.Null);
                Assert.That(error, Does.Contain("Generate clip"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(motion);
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        static IEnumerator Wait(GenBakeRun run)
        {
            var begun = EditorApplication.timeSinceStartup;
            while (!run.Done && EditorApplication.timeSinceStartup - begun < Timeout) yield return null;
            Assert.That(run.Done, Is.True, "the bake did not finish in " + Timeout + " s: " + run.Stage);
        }

        static string Seams(GenBakeRun run) => string.Join("\n", run.Seams.Select(s => s.Before + " > " + s.After + " at " + s.Time.ToString("0.000", CultureInfo.InvariantCulture) + " s: height " + (s.HeightStep * 100f).ToString("0.000", CultureInfo.InvariantCulture) + " cm, side " + (s.SideStep * 100f).ToString("0.000", CultureInfo.InvariantCulture) + " cm, " + s.Degrees.ToString("0.00", CultureInfo.InvariantCulture) + " deg"));

        [UnityTest, Category("RealAssets")]
        public IEnumerator Run_jump_land_roll_walk_on_one_row_bakes_every_gap_and_plays_with_flat_seams()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(Character);
            if (character == null || !AssetDatabase.IsValidFolder(Clips)) Assert.Ignore("Synty test assets missing");
            if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing");
            if (AssetDatabase.IsValidFolder(ChainDemo.Folder)) Assert.Ignore(ChainDemo.Folder + " already exists; this test would overwrite it");
            var active = EditorSceneManager.GetActiveScene();
            var replace = active.path == "";
            Scene scene = default;
            try
            {
                var timeline = ChainDemo.Create(character, ChainDemo.ProjectClips(Clips), replace ? NewSceneMode.Single : NewSceneMode.Additive, out scene, out var director, out var error);
                Assert.That(timeline, Is.Not.Null, error);
                var track = ChainSync.Tracks(timeline).Single();
                var blocks = ChainSync.Blocks(track);
                var roles = string.Join(", ", blocks.Select(b => b.displayName));
                TestContext.WriteLine("blocks: " + roles);
                Assert.That(blocks.Count, Is.EqualTo(5), roles);
                Assert.That(Gaps(track).Count, Is.EqualTo(4));
                Assert.That(GenPlanCache.DirectorOf(timeline), Is.SameAs(director));

                ChainAuto.Forget();
                Assert.That(ChainAuto.Check(timeline, director, 100.0), Is.Null, "the first look only notes the chain");
                Assert.That(ChainAuto.Check(timeline, director, 100.2), Is.Null, "it waits for the edits to settle");
                var run = ChainAuto.Check(timeline, director, 102.0);
                Assert.That(run, Is.Not.Null, "the missing in-betweens bake on their own");
                Assert.That(run.Options.Auto, Is.True);
                yield return Wait(run);
                var report = string.Join("\n", run.Report);
                TestContext.WriteLine(report + "\n" + Seams(run));
                Assert.That(run.Error, Is.Empty, report);
                Assert.That(run.Generated, Is.EqualTo(4), report);
                Assert.That(report, Does.Contain("hips matched at 8 seams"), report);

                var played = GenOutputs.Track(timeline, false).GetClips().OrderBy(c => c.start).ToList();
                Assert.That(played.Count, Is.EqualTo(9), string.Join(", ", played.Select(c => c.displayName)));
                for (var i = 1; i < played.Count; i++) Assert.That(played[i].start, Is.EqualTo(played[i - 1].end).Within(1e-6), played[i].displayName);
                Assert.That(run.Seams.Count, Is.EqualTo(8), Seams(run));
                foreach (var seam in run.Seams)
                {
                    Assert.That(seam.HeightStep, Is.LessThanOrEqualTo(0.01f), Seams(run));
                    Assert.That(seam.SideStep, Is.LessThanOrEqualTo(0.01f), Seams(run));
                    Assert.That(seam.Degrees, Is.LessThanOrEqualTo(3f), Seams(run));
                }

                var chain = run.Chains.Single();
                Assert.That(chain.Pieces.Count, Is.EqualTo(9));
                Assert.That(chain.Pieces.Count(p => p.Kind == ChainPieceKind.InBetween), Is.EqualTo(4));
                Assert.That(chain.Whole, Is.Not.Null);
                Assert.That(chain.Duration, Is.EqualTo((float)(played.Last().end - played.First().start)).Within(0.02f));
                Assert.That(SaClipPose.TryLoad(chain.Whole.Bytes, out var whole, out error), Is.True, error);
                Assert.That(whole.Duration, Is.EqualTo(chain.Duration).Within(0.05f));
                var stem = GenChain.Stem(timeline, track);
                Assert.That(File.Exists(stem + ".saclip") && File.Exists(stem + "_Humanoid.anim") && File.Exists(stem + ".asset"), Is.True, stem);
                Assert.That(ChainAuto.Stale(GenPlanner.Plan(timeline, director)), Is.False, "after the bake nothing is stale");
                var outputs = ChainCommands.Outputs(timeline, null);
                Assert.That(outputs, Does.Contain(stem + ".saclip"));
                Assert.That(outputs.Count(p => p.EndsWith(".saclip", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(5));
                Assert.That(ChainCommands.Reveal(timeline, new[] { Gaps(track)[0] }).Length, Is.GreaterThan(0));

                var again = ChainCommands.BakeAll(timeline, false, out error);
                Assert.That(again, Is.Not.Null, error);
                yield return Wait(again);
                Assert.That(again.Generated, Is.EqualTo(0), string.Join("\n", again.Report));
                Assert.That(again.Reused, Is.EqualTo(4));
                Assert.That(again.Chains.Single().Digest, Is.EqualTo(chain.Digest), "the same chain gives the same digest");

                var walk = ChainSync.Blocks(track).Last();
                walk.start += 0.1;
                var moved = ChainCommands.BakeAll(timeline, false, out error);
                Assert.That(moved, Is.Not.Null, error);
                yield return Wait(moved);
                Assert.That(moved.Error, Is.Empty, string.Join("\n", moved.Report));
                Assert.That(moved.Generated, Is.EqualTo(1), "only the gap before the moved block is generated again");
                Assert.That(moved.Reused, Is.EqualTo(3));

                var gap = Gaps(track)[1];
                var one = ChainCommands.BakeGaps(new[] { gap }, true, out error);
                Assert.That(one, Is.Not.Null, error);
                yield return Wait(one);
                Assert.That(one.Generated, Is.EqualTo(1), "Regenerate this gap generates that gap only");

                var cancelled = ChainCommands.BakeAll(timeline, true, out error);
                Assert.That(cancelled, Is.Not.Null, error);
                GenBake.Cancel(cancelled);
                Assert.That(cancelled.Done, Is.True);
                Assert.That(cancelled.Error, Does.StartWith("Cancelled"));
                Assert.That(GenBake.Running(timeline), Is.Null);
            }
            finally
            {
                if (scene.IsValid() && replace) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else if (scene.IsValid())
                {
                    if (active.IsValid()) SceneManager.SetActiveScene(active);
                    EditorSceneManager.CloseScene(scene, true);
                }

                AssetDatabase.DeleteAsset(ChainDemo.Folder);
                ChainAuto.Forget();
            }
        }
    }
}

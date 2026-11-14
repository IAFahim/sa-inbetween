using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class GenBakeTests
    {
        const string Folder = "Assets/zz_gen_bake_test";
        const string Character = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string InAir = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/InAir/";
        const string Jump = "A_Jump_Walking_RootMotionVertical_Masc";
        const string JumpTake = "A_Jump_Walking_RootMotionVertical_Masc_Full";
        const string Land = "A_Land_IdleSoft_Masc";
        const int StartFrame = 60;
        const double Seconds = 0.8;
        const double Timeout = 400.0;

        static AnimationClip Take(string path, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        [TearDown]
        public void Clean() => AssetDatabase.DeleteAsset(Folder);

        static IEnumerator Wait(GenBakeRun run)
        {
            var begun = EditorApplication.timeSinceStartup;
            while (!run.Done && EditorApplication.timeSinceStartup - begun < Timeout) yield return null;
            Assert.That(run.Done, Is.True, "the bake did not finish in " + Timeout + " s: " + run.Stage);
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator Baking_a_jump_timeline_writes_the_bridge_files_and_places_the_clip()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(Character);
            var jump = Take(InAir + Jump + ".fbx", JumpTake);
            var land = Take(InAir + Land + ".fbx", Land);
            if (character == null || jump == null || land == null) Assert.Ignore("Synty test assets missing");
            if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "zz_gen_bake_test");

            var leave = StartFrame / (double)jump.frameRate;
            var timeline = GenLayout.Create(Folder + "/Jump.playable",
                new[] { new ClipFrame { Kind = SourceKind.UnityClip, Motion = jump, Character = character }, new ClipFrame { Kind = SourceKind.UnityClip, Motion = land, Character = character } },
                character, Seconds, 1, leave);
            var bakeTrack = timeline.GetOutputTracks().OfType<GenTrack>().Single(t => t.name == GenLayout.BakeTrack);
            var export = (ExportClip)bakeTrack.GetClips().Single(c => c.asset is ExportClip).asset;
#if SA_FBX
            var fbx = SaClipFbxExport.NativeReady(out _);
#else
            var fbx = false;
#endif
            export.Payload = new ExportPayload { Anim = true, HumanoidAnim = true, Fbx = fbx, PlaceOnTimeline = true };
            var label = bakeTrack.CreateClip<LabelClip>();
            label.start = 0;
            label.duration = timeline.duration;
            ((LabelClip)label.asset).Payload = new LabelPayload { Label = "GenBakeTest" };
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            var run = GenBake.Start(timeline, null, out var error);
            Assert.That(run, Is.Not.Null, error);
            yield return Wait(run);
            Assert.That(run.Error, Is.Empty, string.Join("\n", run.Report));
            Assert.That(run.Generated, Is.EqualTo(1));
            var job = run.Jobs.Single();
            Assert.That(job.Job.Name, Is.EqualTo(JumpTake + "_to_" + Land));
            var recipe = job.Kept;
            Assert.That(recipe.Output, Is.Not.Null);
            Assert.That(recipe.Start.Where, Is.EqualTo(FrameWhere.Time));
            Assert.That(recipe.Start.Frame, Is.EqualTo(StartFrame));
            var stem = GenRecipes.Folder(timeline) + "/" + job.Job.Name;
            foreach (var file in new[] { stem + ".saclip", stem + ".anim", stem + "_Humanoid.anim" })
            {
                Assert.That(File.Exists(file), Is.True, file);
                Assert.That(AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(file)), Does.Contain("GenBakeTest"), file);
            }

            if (fbx) Assert.That(File.Exists(stem + ".fbx"), Is.True, stem + ".fbx");
            Assert.That(File.Exists(GenRecipes.PlanPath(timeline)), Is.True);
            Assert.That(run.Digest, Does.StartWith("sha256:"));

            var generated = GenOutputs.Track(timeline, false);
            var placed = generated.GetClips().Single();
            Assert.That(placed.displayName, Is.EqualTo(job.Job.Name));
            Assert.That(((AnimationPlayableAsset)placed.asset).clip, Is.SameAs(AssetDatabase.LoadAssetAtPath<AnimationClip>(stem + "_Humanoid.anim")));
            Assert.That(placed.start, Is.EqualTo(leave).Within(1e-6));
            Assert.That(((AnimationPlayableAsset)placed.asset).applyFootIK, Is.False);

            var bridge = ScriptableObject.CreateInstance<InbetweenRecipe>();
            var other = ScriptableObject.CreateInstance<TimelineAsset>();
            var body = other.CreateTrack<AnimationTrack>(null, "Body");
            var a = body.CreateClip(jump);
            a.start = 0;
            a.duration = leave;
            var b = body.CreateClip(land);
            b.start = leave + Seconds;
            TimelineBridge.Configure(bridge, a, b, character, BridgePlan.DurationFor(BridgePlan.Gap(a.end, b.start)));
            Assert.That(RecipeRunner.Hash(bridge), Is.EqualTo(RecipeRunner.Hash(recipe)), "the timeline bakes the Bridge's recipe");
            AssetDatabase.CreateAsset(bridge, Folder + "/Bridge.asset");
            Object.DestroyImmediate(other);
            Assert.That(RecipeRunner.Start(bridge, out error), Is.True, error);
            var begun = EditorApplication.timeSinceStartup;
            while (RecipeRunner.IsRunning(bridge) && EditorApplication.timeSinceStartup - begun < Timeout) yield return null;
            Assert.That(RecipeRunner.Get(bridge).Error, Is.Empty);
            Assert.That(bridge.Output.Bytes, Is.EqualTo(recipe.Output.Bytes), "same .saclip bytes as the Bridge");

            var again = GenBake.Start(timeline, null, out error);
            Assert.That(again, Is.Not.Null, error);
            yield return Wait(again);
            Assert.That(again.Error, Is.Empty, string.Join("\n", again.Report));
            Assert.That(again.Generated, Is.EqualTo(0));
            Assert.That(again.Reused, Is.EqualTo(1));
            Assert.That(again.Digest, Is.EqualTo(run.Digest));
            Assert.That(GenOutputs.Track(timeline, false).GetClips().Count(), Is.EqualTo(1));
        }
    }
}

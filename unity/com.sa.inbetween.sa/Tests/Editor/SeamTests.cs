using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SA.Animation;
using SA.Editor;
using SA.Inbetween.Clips.Editor;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Clips.Tests
{
    public class SeamTests
    {
        const string Folder = "Assets/zz_seam_test";
        const string SyntyCharacter = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string InAir = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/InAir/";
        const string JumpTake = "A_Jump_Walking_RootMotionVertical_Masc_Full";
        const string Land = "A_Land_IdleSoft_Masc";
        const float Hz = 30f;

        static Pose[] Reference() => (Pose[])SharedFamilyBake.CanonicalReferencePoses().Clone();

        static Pose[] Clip(int count, Func<int, Pose[], Pose[]> frame)
        {
            var poses = new Pose[count * SeamFix.Bones];
            for (var f = 0; f < count; f++) Array.Copy(frame(f, Reference()), 0, poses, f * SeamFix.Bones, SeamFix.Bones);
            return poses;
        }

        [TestCase(SeamCurve.CriticallyDamped, 0.1f)]
        [TestCase(SeamCurve.Quintic, 0.4f)]
        public void A_seam_offset_starts_at_the_join_with_the_joins_velocity_and_dies_away(SeamCurve curve, float seconds)
        {
            Assert.That(SeamDecay.Offset(curve, seconds, 0.05f, 0.3f, 0.0), Is.EqualTo(0.05f).Within(1e-7f));
            var h = 1e-5;
            var slope = (SeamDecay.Offset(curve, seconds, 0.05f, 0.3f, h) - SeamDecay.Offset(curve, seconds, 0.05f, 0.3f, 0.0)) / h;
            Assert.That(slope, Is.EqualTo(0.3).Within(2e-3));
            Assert.That(Math.Abs(SeamDecay.Offset(curve, seconds, 0.05f, 0.3f, 1.5)), Is.LessThan(1e-4f));
            var previous = double.MaxValue;
            for (var t = 0.2; t < 1.5; t += 0.05)
            {
                var x = Math.Abs(SeamDecay.Offset(curve, seconds, 0.05f, 0f, t));
                Assert.That(x, Is.LessThanOrEqualTo(previous + 1e-9), "no overshoot without an initial velocity");
                previous = x;
            }
        }

        [Test]
        public void Blending_lands_exactly_on_both_neighbours_and_leaves_the_middle_alone()
        {
            const int count = 60;
            var raw = Clip(count, (f, p) => p);
            var start = Reference();
            start[SeamFix.Hips].Position = start[SeamFix.Hips].Position + new Float3(0f, 0.036f, 0f);
            start[12].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.3f), start[12].Rotation));
            var goal = Reference();
            goal[SeamFix.Hips].Position = goal[SeamFix.Hips].Position + new Float3(0f, -0.061f, 0f);
            goal[50].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(new Float3(0f, 0f, 1f), -0.2f), goal[50].Rotation));
            var blended = SeamFix.Blend(raw, count, Hz, new SeamSide(start, start), new SeamSide(goal, goal), SeamSettings.Default);
            SeamFix.Join(start, SeamFix.Frame(blended, 0), out var height, out var pop, out _);
            Assert.That(height, Is.LessThan(1e-5));
            Assert.That(pop, Is.LessThan(0.05));
            SeamFix.Join(SeamFix.Frame(blended, count - 1), goal, out height, out pop, out _);
            Assert.That(height, Is.LessThan(1e-5));
            Assert.That(pop, Is.LessThan(0.05));
            SeamFix.Join(SeamFix.Frame(raw, count / 2), SeamFix.Frame(blended, count / 2), out height, out pop, out _);
            Assert.That(height, Is.LessThan(1e-4), "the middle of a 2 s clip keeps its own height");
            Assert.That(pop, Is.LessThan(0.01));
        }

        [Test]
        public void The_hips_yaw_at_a_join_is_left_to_placement()
        {
            var raw = Clip(20, (f, p) => p);
            var start = Reference();
            start[SeamFix.Hips].Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 1.2f), start[SeamFix.Hips].Rotation));
            var blended = SeamFix.Blend(raw, 20, Hz, new SeamSide(start, start), null, SeamSettings.Default);
            Assert.That(SeamFix.Degrees(blended[SeamFix.Hips].Rotation, raw[SeamFix.Hips].Rotation), Is.LessThan(0.01));
        }

        [Test]
        public void Seams_off_returns_the_clip_unchanged()
        {
            var raw = Clip(10, (f, p) => p);
            var start = Reference();
            start[SeamFix.Hips].Position = start[SeamFix.Hips].Position + new Float3(0f, 0.1f, 0f);
            var blended = SeamFix.Blend(raw, 10, Hz, new SeamSide(start, start), null, SeamSettings.Off);
            Assert.That(blended[SeamFix.Hips].Position.Y, Is.EqualTo(raw[SeamFix.Hips].Position.Y));
        }

        [Test]
        public void Two_bone_IK_puts_the_ankle_on_a_reachable_target_and_keeps_the_bone_lengths()
        {
            var random = new System.Random(11);
            var worst = 0.0;
            for (var trial = 0; trial < 300; trial++)
            {
                var a = new Float3(0.1f, 0.9f, 0f);
                var aRotation = Quaternion.AxisAngle(Unit(random), (float)(random.NextDouble() * 1.0));
                var bRotation = Quaternion.Multiply(aRotation, Quaternion.AxisAngle(Unit(random), (float)(random.NextDouble() * 0.7)));
                var b = a + Quaternion.Rotate(aRotation, new Float3(0f, -0.42f, 0f));
                var c = b + Quaternion.Rotate(bRotation, new Float3(0f, -0.4f, 0f));
                var target = c + new Float3((float)(random.NextDouble() - 0.5) * 0.2f, (float)(random.NextDouble() - 0.5) * 0.2f, (float)(random.NextDouble() - 0.5) * 0.2f);
                if ((target - a).Length > 0.8f || (target - a).Length < 0.1f) continue;
                FootFix.TwoBone(a, b, c, target, Quaternion.Identity, ref aRotation, ref bRotation);
                var knee = a + Quaternion.Rotate(aRotation, new Float3(0f, -0.42f, 0f));
                var ankle = knee + Quaternion.Rotate(bRotation, new Float3(0f, -0.4f, 0f));
                worst = Math.Max(worst, (ankle - target).Length);
            }

            Assert.That(worst, Is.LessThan(1e-3), "worst miss " + worst * 1000 + " mm");
        }

        static Float3 Unit(System.Random random)
        {
            var v = new Float3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
            return v / v.Length;
        }

        [Test]
        public void A_planted_foot_that_slides_is_locked_and_the_ends_stay_put()
        {
            const int count = 20;
            var raw = Clip(count, (f, p) => p);
            var root = new RigidPose[count];
            for (var f = 0; f < count; f++) root[f] = new RigidPose(new Float3(0.005f * f, 0f, 0f), Quaternion.Identity);
            var weights = new float[count * 4];
            for (var f = 5; f < 15; f++)
            {
                weights[f * 4] = 1f;
                weights[f * 4 + 1] = 1f;
            }

            var parents = SharedFamilyBake.CanonicalParentsArray();
            var fixedPoses = FootFix.Apply(raw, count, root, parents, weights, Hz, out var report);
            Assert.That(report.SlideBefore, Is.GreaterThan(0.05), "the feet slid 4.5 cm each while planted");
            Assert.That(report.SlideAfter, Is.LessThan(report.SlideBefore * 0.05), "slide after " + report.SlideAfter);
            Assert.That(report.LockedFrames, Is.GreaterThan(0));
            foreach (var f in new[] { 0, count - 1 })
            {
                SeamFix.Join(SeamFix.Frame(raw, f), SeamFix.Frame(fixedPoses, f), out var height, out var pop, out _);
                Assert.That(pop, Is.LessThan(1e-3), "frame " + f + " is a join and is never changed");
            }
        }

        [Test]
        public void A_sole_below_the_ground_is_lifted_to_it()
        {
            const int count = 9;
            var parents = SharedFamilyBake.CanonicalParentsArray();
            var root = new RigidPose[count];
            for (var f = 0; f < count; f++) root[f] = RigidPose.Identity;
            var raw = Clip(count, (f, p) =>
            {
                if (f == 4) p[SeamFix.Hips].Position = p[SeamFix.Hips].Position + new Float3(0f, -0.06f, 0f);
                return p;
            });
            var ground = FootFix.Ground(raw, count, root, parents);
            var before = FootFix.SoleOf(FootFix.Model(raw, 4, root[4], parents)[50]).Y;
            Assert.That(before, Is.LessThan(ground - 0.03f));
            var fixedPoses = FootFix.Apply(raw, count, root, parents, new float[count * 4], Hz, out var report);
            var after = FootFix.SoleOf(FootFix.Model(fixedPoses, 4, root[4], parents)[50]).Y;
            Assert.That(after, Is.GreaterThanOrEqualTo(ground - 2e-3f));
            Assert.That(report.ClampedFrames, Is.GreaterThan(0));
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator Synty_jump_seams_before_and_after_inertialization() => Measure(true);

        [UnityTest, Category("RealAssets")]
        public IEnumerator Synty_jump_with_a_loose_landing_blends_into_the_goal() => Measure(false);

        static IEnumerator Measure(bool landExactly)
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var jump = RealJumpTests.Take(InAir + "A_Jump_Walking_RootMotionVertical_Masc.fbx", JumpTake);
            var land = RealJumpTests.Take(InAir + Land + ".fbx", Land);
            if (character == null || jump == null || land == null) Assert.Ignore("real assets missing");
            if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "zz_seam_test");
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = jump, Character = character, Where = FrameWhere.Frame, Frame = 60 };
            recipe.Goals.Clear();
            recipe.Goals.Add(new ClipFrame { Kind = SourceKind.UnityClip, Motion = land, Character = character, Where = FrameWhere.Start });
            recipe.DurationSeconds = 0.8f;
            recipe.LandExactly = landExactly;
            recipe.SaveAnimationClip = false;
            var path = Folder + "/seam_" + (landExactly ? "exact" : "loose") + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(recipe, path);
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var report = new StringBuilder();
            try
            {
                Assert.That(RecipeRunner.Start(recipe, out var error), Is.True, error);
                var begun = EditorApplication.timeSinceStartup;
                while (RecipeRunner.IsRunning(recipe) && EditorApplication.timeSinceStartup - begun < 400.0) yield return null;
                var run = RecipeRunner.Get(recipe);
                Assert.That(run.Done && run.Error == "", Is.True, run.Error + "\n" + run.Log);
                Assert.That(FamilyClips.TryPlan(instance, out var plan, out error), Is.True, error);
                var work = RecipeRunner.WorkFolder(recipe);
                var rawFile = SeamBake.RawFile(Path.Combine(work, recipe.name + ".saclip"));
                Assert.That(File.Exists(rawFile), Is.True, "the generator's own clip is kept as " + rawFile);
                Assert.That(CanonicalClip.TryLoad(File.ReadAllBytes(rawFile), out var rawClip, out error), Is.True, error);
                Assert.That(CanonicalClip.TryLoad(recipe.Output.Bytes, out var fixedClip, out error), Is.True, error);
                var start = SeamBake.Side(recipe.Start, work, true, out error);
                var goal = SeamBake.Side(recipe.Goals[0], work, false, out error);
                Assert.That(start != null && goal != null, Is.True, error);
                var raw = CanonicalClip.Frames(rawClip);
                var done = CanonicalClip.Frames(fixedClip);
                var ratio = plan.HeightRatio;
                var gates = new System.Collections.Generic.List<string>();
                void Join(string name, Pose[] a, Pose[] b, Pose[] c, Pose[] d)
                {
                    SeamFix.Join(a, b, out var h0, out var p0, out var bone0);
                    SeamFix.Join(c, d, out var h1, out var p1, out var bone1);
                    report.Append(name).Append(": height step ").Append(Cm(h0 * ratio)).Append(" -> ").Append(Cm(h1 * ratio)).Append(", rotation pop ")
                        .Append(p0.ToString("0.00", CultureInfo.InvariantCulture)).Append(" deg (bone ").Append(bone0).Append(") -> ")
                        .Append(p1.ToString("0.00", CultureInfo.InvariantCulture)).Append(" deg (bone ").Append(bone1).Append(")\n");
                    if (h1 * ratio * 100.0 > 1.0) gates.Add(name + " height step " + Cm(h1 * ratio) + " > 1 cm");
                    if (p1 > 3.0) gates.Add(name + " rotation pop " + p1.ToString("0.00", CultureInfo.InvariantCulture) + " deg > 3 deg");
                }

                Join("start join", start.At, SeamFix.Frame(raw.Poses, 0), start.At, SeamFix.Frame(done.Poses, 0));
                Join("goal join", SeamFix.Frame(raw.Poses, raw.Count - 1), goal.At, SeamFix.Frame(done.Poses, done.Count - 1), goal.At);
                var parents = SharedFamilyBake.CanonicalParentsArray();
                var ground = FootFix.Ground(raw.Poses, raw.Count, raw.Root, parents);
                double slideRaw = 0, slideDone = 0;
                foreach (var leg in Leg.Both)
                {
                    var contact = FootFix.Contacts(raw.Poses, raw.Count, raw.Root, parents, raw.Contacts, Hz, leg, ground);
                    slideRaw += FootFix.Slide(raw.Poses, raw.Count, raw.Root, parents, contact, leg);
                    slideDone += FootFix.Slide(done.Poses, done.Count, done.Root, parents, contact, leg);
                }

                report.Append("foot slide while planted: ").Append(Cm(slideRaw * ratio)).Append(" -> ").Append(Cm(slideDone * ratio)).Append('\n');
                report.Append("runner: ").Append(run.Seams).Append('\n');
                if (slideDone > slideRaw + 1e-4) gates.Add("foot slide grew");
                RealJumpTests.Report("seams_" + (landExactly ? "exact" : "loose"), report.ToString());
                Assert.That(gates, Is.Empty, string.Join("; ", gates) + "\n" + report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                RealJumpTests.Remove(recipe);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        static string Cm(double metres) => (metres * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + " cm";
    }
}

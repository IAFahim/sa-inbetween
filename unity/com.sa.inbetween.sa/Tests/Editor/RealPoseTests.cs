using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SA.Animation;
using SA.Inbetween.Clips.Editor;
using SA.Motion.Numerics;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Clips.Tests
{
    public class RealPoseTests
    {
        const float Hz = 30f;
        const string Folder = "Assets/zz_w7_test";
        const string SyntyCharacter = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string InAir = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/InAir/";
        const int Apex = 41;

        [UnityTest, Category("RealAssets")]
        public IEnumerator CapturedPoseMatchesTheClipFrameAndDrivesARecipe()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var jump = RealJumpTests.Take(InAir + "A_Jump_Walking_RootMotionVertical_Masc.fbx", "A_Jump_Walking_RootMotionVertical_Masc_Full");
            if (character == null || jump == null) Assert.Ignore("Synty assets missing");
            if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "zz_w7_test");
            var report = new StringBuilder();
            var posed = (GameObject)PrefabUtility.InstantiatePrefab(character);
            InbetweenRecipe recipe = null;
            InbetweenRecipe poseRecipe = null;
            try
            {
                jump.SampleAnimation(posed, Apex / Hz);
                Assert.IsTrue(PoseCapture.TryCanonical(posed, out var captured, out var error), error);
                Assert.IsTrue(SaClipConvert.TryConvert(character, jump, out var sourceBytes, out error), error);
                var source = RealJumpTests.Load(sourceBytes);
                var truth = RealJumpTests.World(RealJumpTests.Locals(source, Apex), RigidPose.Identity);
                var got = RealJumpTests.World(captured, RigidPose.Identity);
                double canonical = 0;
                for (var c = 1; c < 56; c++)
                    if (RealJumpTests.Body(c))
                        canonical = Math.Max(canonical, RealJumpTests.Degrees(truth[c].Rotation, got[c].Rotation));
                report.Append("capture vs clip frame ").Append(Apex).Append(", canonical body rotation: ").Append(canonical.ToString("0.0000", CultureInfo.InvariantCulture))
                    .Append(" deg, hips height difference ").Append((Math.Abs(truth[1].Position.Y - got[1].Position.Y) * 100.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" cm (canonical)\n");

                var capturePath = Folder + "/W7_goal_pose.saclip";
                Assert.IsTrue(PoseCapture.TryCapture(posed, capturePath, out var capturedAsset, out error), error);
                var work = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "SaInbetween", "w7");
                Directory.CreateDirectory(work);
                var sourceFile = Path.Combine(work, "source.saclip");
                File.WriteAllBytes(sourceFile, sourceBytes);
                var tools = InbetweenSettings.Tools();
                var settings = new StudioSettings { BakePath = tools.Bake, FamilyPath = tools.Family, RestPath = tools.Rest, WorkFolder = work, OutputFolder = work };
                var fromCapture = Path.Combine(work, "capture.pose");
                var fromClip = Path.Combine(work, "clip.pose");
                Assert.IsTrue(StudioCommands.TryPoses(settings, Path.GetFullPath(capturePath), "s", fromCapture, out var command, out error), error);
                RealJumpTests.RunSync(command);
                Assert.IsTrue(StudioCommands.TryPoses(settings, sourceFile, (Apex / Hz).ToString("R", CultureInfo.InvariantCulture), fromClip, out command, out error), error);
                RealJumpTests.RunSync(command);
                var poseDegrees = PoseDegrees(File.ReadAllBytes(fromCapture), File.ReadAllBytes(fromClip), out var worstJoint);
                report.Append(".pose of the capture vs .pose of clip frame ").Append(Apex).Append(": max joint rotation ").Append(poseDegrees.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" deg (joint ").Append(worstJoint).Append(")\n");
                Assert.LessOrEqual(canonical, 0.5, report.ToString());
                Assert.LessOrEqual(poseDegrees, 0.5, report.ToString());

                File.Copy(fromCapture, Folder + "/W7_goal.pose", true);
                AssetDatabase.ImportAsset(Folder + "/W7_goal.pose", ImportAssetOptions.ForceUpdate);
                var poseAsset = AssetDatabase.LoadAssetAtPath<SaPoseAsset>(Folder + "/W7_goal.pose");
                Assert.IsNotNull(poseAsset);
                Assert.IsTrue(poseAsset.Valid, poseAsset.Problem);
                poseRecipe = Recipe("W7_PoseGoal", character, jump, new ClipFrame { Kind = SourceKind.Pose, Pose = poseAsset, Where = FrameWhere.Start });
                var steps = new System.Collections.Generic.List<RecipeRun.Step>();
                Assert.IsTrue(RecipeRunner.PrepareSources(poseRecipe, steps, out var prepared, out error), error);
                Assert.AreEqual(1, steps.Count, "a pose goal needs no extraction step");
                for (var i = 0; i < 4; i++)
                    CollectionAssert.AreEqual(File.ReadAllBytes(fromCapture), File.ReadAllBytes(Path.Combine(prepared.WorkFolder, "goal0_" + i + ".pose")));

                recipe = Recipe("W7_CapturedGoal", character, jump, new ClipFrame { Kind = SourceKind.SaClip, Clip = capturedAsset, Where = FrameWhere.Start });
                Assert.IsTrue(RecipeRunner.Start(recipe, out error), error);
                var begun = EditorApplication.timeSinceStartup;
                while (RecipeRunner.IsRunning(recipe) && EditorApplication.timeSinceStartup - begun < 400.0)
                    yield return null;
                var run = RecipeRunner.Get(recipe);
                Assert.IsTrue(run.Done && run.Error == "", run.Error + "\n" + run.Log);
                Assert.IsNotNull(recipe.Output);
                Assert.IsNotNull(recipe.SavedClip, "the Unity clip was not saved after Generate");
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));
                var output = RealJumpTests.Load(recipe.Output.Bytes);
                var last = RealJumpTests.World(RealJumpTests.Locals(output, output.SampleCount - 1), RigidPose.Identity);
                double end = 0;
                for (var c = 1; c < 56; c++)
                    if (RealJumpTests.Body(c))
                        end = Math.Max(end, RealJumpTests.Degrees(last[c].Rotation, got[c].Rotation));
                report.Append("recipe jump frame 30 -> captured pose: ").Append(output.SampleCount).Append(" frames, last frame vs captured pose ").Append(end.ToString("0.0000", CultureInfo.InvariantCulture)).Append(" deg, saved ").Append(AssetDatabase.GetAssetPath(recipe.SavedClip)).Append('\n');
                Assert.LessOrEqual(end, 1.0, report.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(posed);
                RealJumpTests.Report("w7", report.ToString());
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        static InbetweenRecipe Recipe(string name, GameObject character, AnimationClip start, ClipFrame goal)
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = start, Character = character, Where = FrameWhere.Frame, Frame = 30 };
            recipe.Goals.Clear();
            recipe.Goals.Add(goal);
            recipe.DurationSeconds = 0.8f;
            recipe.Character = character;
            AssetDatabase.CreateAsset(recipe, Folder + "/" + name + ".asset");
            return recipe;
        }

        static double PoseDegrees(byte[] a, byte[] b, out int worst)
        {
            worst = -1;
            var fa = new float[a.Length / 4];
            var fb = new float[b.Length / 4];
            Buffer.BlockCopy(a, 0, fa, 0, a.Length);
            Buffer.BlockCopy(b, 0, fb, 0, b.Length);
            double max = 0;
            for (var j = 0; j < SaPoseAsset.Joints; j++)
            {
                var at = j * 7 + 3;
                var degrees = RealJumpTests.Degrees(new Quaternion(fa[at], fa[at + 1], fa[at + 2], fa[at + 3]), new Quaternion(fb[at], fb[at + 1], fb[at + 2], fb[at + 3]));
                if (degrees > max)
                {
                    max = degrees;
                    worst = j;
                }
            }

            return max;
        }
    }
}

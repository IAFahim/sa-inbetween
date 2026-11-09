using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using SA.Animation;
using SA.Animation.Content;
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
    public class RealJumpTests
    {
        const float Hz = 30f;
        const string Folder = "Assets/zz_w1_test";
        const string SyntyCharacter = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string NeophyteCharacter = "Assets/zz_w1_test/Neophyte_RIG.fbx";
        const string InAir = "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine/InAir/";
        const string Jump = "A_Jump_Walking_RootMotionVertical_Masc";
        const string JumpTake = "A_Jump_Walking_RootMotionVertical_Masc_Full";
        const string Running = "A_Jump_Running_RootMotion_Masc";
        const string Land = "A_Land_IdleSoft_Masc";
        const int StartFrame = 60;
        const int RunningFrame = 8;
        const float Duration = 0.8f;
        static readonly Float3 Sole = new Float3(0f, -0.1f, 0.08f);

        sealed class Gates
        {
            public readonly StringBuilder Report = new StringBuilder();
            public readonly List<string> Failures = new List<string>();

            public void Check(string gate, double value, double limit, string unit, bool enforced)
            {
                var pass = value <= limit;
                Report.Append(gate).Append(": ").Append(value.ToString("0.0000", CultureInfo.InvariantCulture)).Append(' ').Append(unit)
                    .Append(" (gate ").Append(limit.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ').Append(unit).Append(", ")
                    .Append(pass ? "pass" : "FAIL").Append(enforced ? "" : ", reported").Append(")\n");
                if (!pass && enforced) Failures.Add(gate + " = " + value.ToString("0.0000", CultureInfo.InvariantCulture) + " " + unit);
            }

            public void Note(string text) => Report.Append(text).Append('\n');
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator SyntyJumpGates() => Run("synty", SyntyCharacter, true);

        [UnityTest, Category("RealAssets"), Explicit("Needs a Mixamo Neophyte copy in Assets/zz_w1_test")]
        public IEnumerator NeophyteJumpGates() => Run("neophyte", NeophyteCharacter, false);

        static IEnumerator Run(string name, string characterPath, bool primary)
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(characterPath);
            var jump = Take(InAir + Jump + ".fbx", JumpTake);
            var land = Take(InAir + Land + ".fbx", Land);
            if (character == null || jump == null || land == null) Assert.Ignore("real assets missing for " + name);
            if (!InbetweenSettings.Tools().Complete) Assert.Ignore("inbetween tools missing");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "zz_w1_test");
            var recipe = Recipe(name + "_J1", character, jump, StartFrame, land);
            var running = primary ? Recipe(name + "_G4", character, Take(InAir + Running + ".fbx", Running), RunningFrame, land) : null;
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = character.name;
            var gates = new Gates();
            try
            {
                Assert.IsTrue(RecipeRunner.Start(recipe, out var error), error);
                var begun = EditorApplication.timeSinceStartup;
                while (RecipeRunner.IsRunning(recipe) && EditorApplication.timeSinceStartup - begun < 400.0)
                    yield return null;
                var run = RecipeRunner.Get(recipe);
                Assert.IsTrue(run.Done && run.Error == "", run.Error + "\n" + run.Log);
                Assert.IsNotNull(recipe.Output, "result not imported");
                Assert.AreEqual(RecipeState.UpToDate, RecipeRunner.StateOf(recipe));
                Assert.IsTrue(FamilyClips.TryPlan(instance, out var plan, out error), error);
                gates.Note(name + ": " + character.name + ", start " + JumpTake + " frame " + StartFrame + ", goal " + Land + " frame 0, duration " + Duration + " s, height ratio "
                    + plan.HeightRatio.ToString("0.0000", CultureInfo.InvariantCulture));
                Mapping(plan, name == "neophyte", gates);
                ImportRoundTrip(plan, instance, jump, gates);
                var work = RecipeRunner.WorkFolder(recipe);
                var source = Load(RecipeRunner.SourceFile(recipe.Start, work));
                var goal = Load(RecipeRunner.SourceFile(recipe.Goals[0], work));
                var output = Load(recipe.Output.Bytes);
                var anchor = (int)Math.Round(recipe.Start.AnchorSeconds * Hz);
                gates.Note("source " + source.SampleCount + " frames, anchor index " + anchor + ", output " + output.SampleCount + " frames (" + output.Duration.ToString("0.000", CultureInfo.InvariantCulture) + " s)");
                ContextFidelity(plan, work, source, anchor, primary, gates);
                Seam(plan, source, output, anchor, "G1", primary, gates);
                End(plan, goal, output, primary, gates);
                Plausibility(plan, source, output, anchor, primary, gates);
                Export(recipe, plan, instance, gates);
                if (running != null) RootFix(running, plan, gates);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                Report(name, gates.Report.ToString());
                Remove(recipe);
                if (running != null) Remove(running);
            }

            Assert.IsEmpty(gates.Failures, string.Join("; ", gates.Failures) + "\n" + gates.Report);
        }

        static InbetweenRecipe Recipe(string name, GameObject character, AnimationClip start, int frame, AnimationClip goal)
        {
            var recipe = ScriptableObject.CreateInstance<InbetweenRecipe>();
            recipe.Start = new ClipFrame { Kind = SourceKind.UnityClip, Motion = start, Character = character, Where = FrameWhere.Frame, Frame = frame };
            recipe.Goals.Clear();
            recipe.Goals.Add(new ClipFrame { Kind = SourceKind.UnityClip, Motion = goal, Character = character, Where = FrameWhere.Start });
            recipe.DurationSeconds = Duration;
            recipe.LandExactly = true;
            var path = Folder + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(recipe, path);
            return recipe;
        }

        internal static void Remove(InbetweenRecipe recipe)
        {
            if (recipe == null) return;
            var path = AssetDatabase.GetAssetPath(recipe);
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? Folder;
            AssetDatabase.DeleteAsset(folder + "/" + recipe.name + ".saclip");
            AssetDatabase.DeleteAsset(folder + "/" + recipe.name + ".anim");
            AssetDatabase.DeleteAsset(path);
        }

        internal static AnimationClip Take(string path, string name)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && clip.name == name)
                    return clip;
            return null;
        }

        internal static SharedClip Load(string file) => Load(File.ReadAllBytes(file));

        internal static SharedClip Load(byte[] bytes)
        {
            SharedFamilyBake.FamilyId(out var a, out var b, out var c, out var d);
            Assert.AreEqual(AnimationError.None, SharedClipReader.Load(bytes, new AssetId(a, b, c, d), SharedFamilyBake.CanonicalReferencePoses(), out var clip));
            return clip;
        }

        internal static bool Body(int c) => c > 0 && c < 7 || c >= 10 && c <= 13 || c >= 29 && c <= 32 || c >= 48;

        internal static Pose[] Locals(SharedClip clip, int frame)
        {
            var output = new Pose[FamilyClips.CanonicalBones];
            frame = Math.Max(0, Math.Min(clip.SampleCount - 1, frame));
            Assert.AreEqual(AnimationError.None, clip.Sample(clip.Duration * frame / (clip.SampleCount - 1), SharedFamilyBake.CanonicalReferencePoses(), output));
            return output;
        }

        internal static RigidPose[] World(Pose[] locals, RigidPose root)
        {
            var placed = (Pose[])locals.Clone();
            Assert.IsTrue(RigidMath.TryCompose(root, new RigidPose(placed[1].Position, Quaternion.Normalize(placed[1].Rotation)), out var hips));
            placed[1] = new Pose { Position = hips.Position, Rotation = hips.Rotation, Scale = Float3.One };
            var model = new RigidPose[FamilyClips.CanonicalBones];
            Assert.AreEqual(AnimationError.None, RigidMath.Model(SharedFamilyBake.CanonicalParentsArray(), placed, model));
            return model;
        }

        static RigidPose Relative(SharedClip clip, int anchor, int frame)
        {
            anchor = Math.Max(0, Math.Min(clip.SampleCount - 1, anchor));
            frame = Math.Max(0, Math.Min(clip.SampleCount - 1, frame));
            Assert.IsTrue(RigidMath.TryInverse(clip.Root[anchor], out var inverse));
            Assert.IsTrue(RigidMath.TryCompose(inverse, clip.Root[frame], out var relative));
            return relative;
        }

        static RigidPose[] SourceWorld(SharedClip clip, int anchor, int frame) => World(Locals(clip, frame), Relative(clip, anchor, frame));

        static RigidPose[] OutputWorld(SharedClip clip, int frame) => World(Locals(clip, frame), clip.Root[Math.Max(0, Math.Min(clip.SampleCount - 1, frame))]);

        static float SoleHeight(RigidPose[] model, int foot) => (model[foot].Position + Quaternion.Rotate(Quaternion.Normalize(model[foot].Rotation), Sole)).Y;

        static float Lowest(RigidPose[] model) => Math.Min(SoleHeight(model, 50), SoleHeight(model, 54));

        internal static double Degrees(Quaternion a, Quaternion b)
        {
            double x = a.X, y = a.Y, z = a.Z, w = a.W;
            var n = Math.Sqrt(x * x + y * y + z * z + w * w) * Math.Sqrt((double)b.X * b.X + (double)b.Y * b.Y + (double)b.Z * b.Z + (double)b.W * b.W);
            var rw = (w * b.W + x * b.X + y * b.Y + z * b.Z) / n;
            var rx = (w * b.X - x * b.W - y * b.Z + z * b.Y) / n;
            var ry = (w * b.Y + x * b.Z - y * b.W - z * b.X) / n;
            var rz = (w * b.Z - x * b.Y + y * b.X - z * b.W) / n;
            return 2.0 * Math.Atan2(Math.Sqrt(rx * rx + ry * ry + rz * rz), Math.Abs(rw)) * 180.0 / Math.PI;
        }

        internal static double Length(Float3 v) => Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);

        static Dictionary<string, int> MixamoRoles()
        {
            var roles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "Hips", 1 }, { "Spine", 2 }, { "Spine1", 3 }, { "Spine2", 4 }, { "Neck", 5 }, { "Head", 6 },
                { "LeftShoulder", 10 }, { "LeftArm", 11 }, { "LeftForeArm", 12 }, { "LeftHand", 13 },
                { "RightShoulder", 29 }, { "RightArm", 30 }, { "RightForeArm", 31 }, { "RightHand", 32 },
                { "LeftUpLeg", 48 }, { "LeftLeg", 49 }, { "LeftFoot", 50 }, { "LeftToeBase", 51 },
                { "RightUpLeg", 52 }, { "RightLeg", 53 }, { "RightFoot", 54 }, { "RightToeBase", 55 }
            };
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            for (var f = 0; f < fingers.Length; f++)
                for (var j = 0; j < 3; j++)
                {
                    roles["LeftHand" + fingers[f] + (j + 1)] = 14 + f * 3 + j;
                    roles["RightHand" + fingers[f] + (j + 1)] = 33 + f * 3 + j;
                }
            return roles;
        }

        static void Mapping(FamilyRigPlan plan, bool mixamo, Gates gates)
        {
            var missing = 0;
            foreach (var role in SharedFamilyBake.RequiredRoles)
                if (plan.BoneOfCanonical[role] < 0)
                    missing++;
            gates.Check("I3 required roles unmapped", missing, 0, "roles", true);
            if (!mixamo) return;
            var expected = MixamoRoles();
            var differences = 0;
            for (var b = 1; b < plan.BoneCount; b++)
            {
                var bone = plan.Bones[b].name;
                var bare = bone.Substring(bone.LastIndexOf(':') + 1);
                var want = expected.TryGetValue(bare, out var role) ? role : -1;
                if (want != plan.CanonicalOfBone[b] && !(want < 0 && plan.CanonicalOfBone[b] < 0)) differences++;
            }
            gates.Check("I3 map differences vs the Mixamo table", differences, 0, "bones", true);
        }

        internal static Pose[] Direct(FamilyRigPlan plan, GameObject instance, AnimationClip clip, float seconds, bool lift)
        {
            var all = instance.GetComponentsInChildren<Transform>(true);
            var count = plan.BoneCount;
            var output = new Pose[count];
            var animator = instance.GetComponentInChildren<Animator>(true);
            var top = instance.transform;
            top.localPosition = Vector3.zero;
            top.localRotation = UnityEngine.Quaternion.identity;
            for (var b = 1; b < count; b++)
            {
                plan.Bones[b].localRotation = new UnityEngine.Quaternion(plan.Rest[b].Rotation.X, plan.Rest[b].Rotation.Y, plan.Rest[b].Rotation.Z, plan.Rest[b].Rotation.W);
                plan.Bones[b].localPosition = new Vector3(plan.Rest[b].Position.X, plan.Rest[b].Position.Y, plan.Rest[b].Position.Z);
            }
            clip.SampleAnimation(animator != null ? animator.gameObject : instance, seconds);
            var moved = new RigidPose(new Float3(top.localPosition.x, top.localPosition.y, top.localPosition.z),
                Quaternion.Normalize(new Quaternion(top.localRotation.x, top.localRotation.y, top.localRotation.z, top.localRotation.w)));
            output[0] = Pose.Identity;
            for (var b = 1; b < count; b++)
            {
                var t = plan.Bones[b];
                var local = new RigidPose(new Float3(t.localPosition.x, t.localPosition.y, t.localPosition.z),
                    Quaternion.Normalize(new Quaternion(t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w)));
                if (lift && plan.Parents[b] == 0) Assert.IsTrue(RigidMath.TryCompose(moved, local, out local));
                output[b] = new Pose { Position = local.Position, Rotation = local.Rotation, Scale = Float3.One };
            }
            top.localPosition = Vector3.zero;
            top.localRotation = UnityEngine.Quaternion.identity;
            return output;
        }

        internal static RigidPose[] TargetModel(FamilyRigPlan plan, Pose[] locals, int offset)
        {
            var count = plan.BoneCount;
            var parents = new int[count];
            for (var b = 0; b < count; b++) parents[b] = plan.Parents[b];
            var model = new RigidPose[count];
            Assert.AreEqual(AnimationError.None, RigidMath.Model(parents, new ReadOnlySpan<Pose>(locals, offset, count), model));
            return model;
        }

        internal static void Compare(FamilyRigPlan plan, RigidPose[] a, RigidPose[] b, ref double degrees, ref double metres)
        {
            for (var bone = 1; bone < plan.BoneCount; bone++)
                if (Body(plan.CanonicalOfBone[bone]))
                    degrees = Math.Max(degrees, Degrees(a[bone].Rotation, b[bone].Rotation));
            foreach (var role in new[] { 13, 32, 50, 54 })
            {
                var bone = plan.BoneOfCanonical[role];
                metres = Math.Max(metres, Length(a[bone].Position - b[bone].Position));
            }
        }

        static void ImportRoundTrip(FamilyRigPlan plan, GameObject instance, AnimationClip clip, Gates gates)
        {
            Assert.IsTrue(FamilyClips.TrySample(plan, clip, Hz, out var canonical, out var root, out var start, out var frames, out var error), error);
            Assert.IsTrue(FamilyClips.TryEncode(plan, canonical, root, Hz, out var bytes, out error), error);
            var loaded = Load(bytes);
            var decoded = new Pose[frames * FamilyClips.CanonicalBones];
            var placed = new RigidPose[frames];
            for (var f = 0; f < frames; f++)
            {
                Array.Copy(Locals(loaded, f), 0, decoded, f * FamilyClips.CanonicalBones, FamilyClips.CanonicalBones);
                Assert.IsTrue(RigidMath.TryCompose(start, loaded.Root[f], out placed[f]));
            }
            Assert.IsTrue(FamilyClips.TryApply(plan, decoded, placed, out var applied, out error), error);
            double degrees = 0, metres = 0;
            for (var f = 0; f < frames; f++)
            {
                var direct = TargetModel(plan, Direct(plan, instance, clip, Math.Min(f / Hz, clip.length), true), 0);
                Compare(plan, direct, TargetModel(plan, applied, f * plan.BoneCount), ref degrees, ref metres);
            }
            gates.Check("I1 world rotation, " + frames + " frames", degrees, 1.0, "deg", true);
            gates.Check("I1 hand/foot position", metres * 100.0, 1.0, "cm", true);
        }

        static void ContextFidelity(FamilyRigPlan plan, string work, SharedClip source, int anchor, bool enforced, Gates gates)
        {
            var pose = Path.Combine(work, "start_3.pose");
            Assert.IsTrue(File.Exists(pose), "no context pose " + pose);
            var folder = Path.Combine(work, "i2");
            Directory.CreateDirectory(folder);
            var tools = InbetweenSettings.Tools();
            var settings = new StudioSettings { BakePath = tools.Bake, FamilyPath = tools.Family, WorkFolder = folder, OutputFolder = folder, ClipName = "context" };
            var body = File.ReadAllBytes(pose);
            var saib = new byte[52 + 2 * body.Length + 2 * 28 + 2 * 16];
            BitConverter.GetBytes(0x42494153u).CopyTo(saib, 0);
            BitConverter.GetBytes((ushort)1).CopyTo(saib, 4);
            BitConverter.GetBytes((ushort)30).CopyTo(saib, 8);
            BitConverter.GetBytes((ushort)2).CopyTo(saib, 10);
            BitConverter.GetBytes((ushort)77).CopyTo(saib, 12);
            body.CopyTo(saib, 52);
            body.CopyTo(saib, 52 + body.Length);
            File.WriteAllBytes(StudioCommands.SaibPath(settings), saib);
            Assert.IsTrue(StudioCommands.TryBake(settings, out var bake, out var error), error);
            RunSync(bake);
            var context = World(Locals(Load(StudioCommands.ClipPath(settings)), 0), RigidPose.Identity);
            var truth = World(Locals(source, anchor), RigidPose.Identity);
            var parents = SharedFamilyBake.CanonicalParentsArray();
            double worst = 0;
            for (var c = 2; c < FamilyClips.CanonicalBones; c++)
            {
                if (!Body(c) || c == 48 || c == 52 || c == 10 || c == 29 || c == 5) continue;
                var a = context[c].Position - context[parents[c]].Position;
                var b = truth[c].Position - truth[parents[c]].Position;
                var cos = ((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z) / (Length(a) * Length(b));
                worst = Math.Max(worst, Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI);
            }
            gates.Check("I2 context bone direction", worst, 2.0, "deg", enforced);
            gates.Check("I2 context hips height", Math.Abs(context[1].Position.Y - truth[1].Position.Y) * plan.HeightRatio * 100.0, 1.0, "cm", enforced);
        }

        static void Seam(FamilyRigPlan plan, SharedClip source, SharedClip output, int anchor, string gate, bool enforced, Gates gates)
        {
            var first = OutputWorld(output, 0);
            var truth = SourceWorld(source, anchor, anchor);
            double jump = 0;
            for (var c = 1; c < FamilyClips.CanonicalBones; c++)
                if (Body(c))
                    jump = Math.Max(jump, Length(first[c].Position - truth[c].Position));
            var outVelocity = (OutputWorld(output, 1)[1].Position - first[1].Position) * (Hz * plan.HeightRatio);
            var sourceVelocity = (truth[1].Position - SourceWorld(source, anchor, anchor - 1)[1].Position) * (Hz * plan.HeightRatio);
            gates.Check(gate + " seam joint position jump", jump * plan.HeightRatio * 100.0, 3.0, "cm", enforced);
            gates.Check(gate + " seam hips velocity difference", Length(outVelocity - sourceVelocity), 0.6, "m/s", enforced);
            gates.Note("  hips velocity source (" + Vec(sourceVelocity) + ") output (" + Vec(outVelocity) + ") m/s");
        }

        static string Vec(Float3 v) => v.X.ToString("0.000", CultureInfo.InvariantCulture) + ", " + v.Y.ToString("0.000", CultureInfo.InvariantCulture) + ", " + v.Z.ToString("0.000", CultureInfo.InvariantCulture);

        static void End(FamilyRigPlan plan, SharedClip goal, SharedClip output, bool enforced, Gates gates)
        {
            var last = World(Locals(output, output.SampleCount - 1), RigidPose.Identity);
            var best = double.MaxValue;
            var atZero = 0.0;
            for (var g = 0; g < 4; g++)
            {
                var target = World(Locals(goal, g), RigidPose.Identity);
                double worst = 0;
                for (var c = 1; c < FamilyClips.CanonicalBones; c++)
                    if (Body(c))
                        worst = Math.Max(worst, Degrees(last[c].Rotation, target[c].Rotation));
                if (g == 0) atZero = worst;
                best = Math.Min(best, worst);
            }
            gates.Check("G2 last frame vs goal frame 0, body rotation", atZero, 1.0, "deg", enforced);
            gates.Note("  best of goal frames 0..3: " + best.ToString("0.000", CultureInfo.InvariantCulture) + " deg");
        }

        static void Plausibility(FamilyRigPlan plan, SharedClip source, SharedClip output, int anchor, bool enforced, Gates gates)
        {
            var overlap = Math.Min(output.SampleCount, source.SampleCount - anchor);
            double squares = 0, mpjpe = 0;
            var joints = 0;
            var lowest = double.MaxValue;
            int outTouch = -1, sourceTouch = -1;
            for (var k = 0; k < output.SampleCount; k++)
            {
                var o = OutputWorld(output, k);
                var low = Lowest(o) * plan.HeightRatio;
                lowest = Math.Min(lowest, low);
                if (outTouch < 0 && k > 0 && low < 0.03) outTouch = k;
                if (k >= overlap) continue;
                var s = SourceWorld(source, anchor, anchor + k);
                var dy = (o[1].Position.Y - s[1].Position.Y) * plan.HeightRatio;
                squares += dy * dy;
                for (var c = 1; c < FamilyClips.CanonicalBones; c++)
                    if (Body(c))
                    {
                        mpjpe += Length(o[c].Position - s[c].Position) * plan.HeightRatio;
                        joints++;
                    }
            }
            for (var k = 1; anchor + k < source.SampleCount; k++)
                if (Lowest(SourceWorld(source, anchor, anchor + k)) * plan.HeightRatio < 0.03)
                {
                    sourceTouch = k;
                    break;
                }
            gates.Check("G3 hips Y RMSE over " + overlap + " frames", Math.Sqrt(squares / Math.Max(1, overlap)) * 100.0, 8.0, "cm", enforced);
            var touch = outTouch < 0 || sourceTouch < 0 ? 99 : Math.Abs(outTouch - sourceTouch);
            gates.Check("G3 touchdown frame difference (output " + outTouch + ", source " + sourceTouch + ")", touch, 3.0, "frames", enforced);
            gates.Check("G3 foot penetration", Math.Max(0.0, -lowest) * 100.0, 3.0, "cm", enforced);
            gates.Check("G3 MPJPE (soft)", mpjpe / Math.Max(1, joints) * 100.0, 12.0, "cm", false);
            gates.Note("  start anchor lowest sole " + (Lowest(SourceWorld(source, anchor, anchor)) * plan.HeightRatio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + " cm");
        }

        static void Export(InbetweenRecipe recipe, FamilyRigPlan plan, GameObject instance, Gates gates)
        {
            Assert.IsTrue(SaClipExport.TryExport(recipe, out var anim, out var error), error);
            Assert.AreEqual(SaClipExport.ExportPath(recipe), AssetDatabase.GetAssetPath(anim));
            Assert.IsTrue(SaClipExport.TrySampleLocals(plan, recipe.Output.Bytes, out var applied, out var frames, out error), error);
            double degrees = 0, metres = 0;
            var animator = instance.GetComponentInChildren<Animator>(true);
            var avatar = animator != null ? animator.avatar : null;
            if (animator != null) animator.avatar = null;
            try
            {
                for (var f = 0; f < frames; f++)
                {
                    var played = TargetModel(plan, Direct(plan, instance, anim, f / Hz, false), 0);
                    Compare(plan, played, TargetModel(plan, applied, f * plan.BoneCount), ref degrees, ref metres);
                }
            }
            finally
            {
                if (animator != null) animator.avatar = avatar;
            }
            gates.Note("E1 plays the Generic .anim with the avatar removed: a Humanoid Animator ignores transform curves on human bones");
            gates.Check("E1 exported .anim vs applied .saclip, " + frames + " frames, rotation", degrees, 0.1, "deg", true);
            gates.Check("E1 hand/foot position", metres * 1000.0, 1.0, "mm", true);
        }

        static void RootFix(InbetweenRecipe recipe, FamilyRigPlan plan, Gates gates)
        {
            var steps = new List<RecipeRun.Step>();
            Assert.IsTrue(RecipeRunner.PrepareSources(recipe, steps, out var settings, out var error), error);
            foreach (var step in steps) RunSync(step.Command);
            var work = settings.WorkFolder;
            var source = Load(RecipeRunner.SourceFile(recipe.Start, work));
            var anchor = (int)Math.Round(recipe.Start.AnchorSeconds * Hz);
            gates.Note("G4 running jump: " + Running + " frame " + RunningFrame + ", anchor index " + anchor + " of " + source.SampleCount);
            settings.ClipName = "with_root";
            Assert.IsTrue(StudioCommands.TryGenerate(settings, out var generate, out error), error);
            Assert.IsTrue(StudioCommands.TryBake(settings, out var bake, out error), error);
            RunSync(generate);
            RunSync(bake);
            var with = Load(StudioCommands.ClipPath(settings));
            var file = RecipeRunner.SourceFile(recipe.Start, work);
            var pattern = Path.Combine(work, "noroot_{}.pose");
            Assert.IsTrue(StudioCommands.TryPoses(settings, file, RecipeRunner.Times(recipe.Start, true), pattern, false, out var poses, out error), error);
            RunSync(poses);
            settings.Contexts = new List<string>();
            for (var i = 0; i < 4; i++) settings.Contexts.Add(pattern.Replace("{}", i.ToString(CultureInfo.InvariantCulture)));
            settings.ClipName = "without_root";
            Assert.IsTrue(StudioCommands.TryGenerate(settings, out generate, out error), error);
            Assert.IsTrue(StudioCommands.TryBake(settings, out bake, out error), error);
            RunSync(generate);
            RunSync(bake);
            var without = Load(StudioCommands.ClipPath(settings));
            var truth = SourceWorld(source, anchor, anchor)[1].Position - SourceWorld(source, anchor, anchor - 1)[1].Position;
            var reference = new Float3(truth.X, 0f, truth.Z) * (Hz * plan.HeightRatio);
            var errors = new double[2];
            var slot = 0;
            foreach (var pair in new[] { ("with --root last", with, false), ("without", without, false) })
            {
                var v = (OutputWorld(pair.Item2, 1)[1].Position - OutputWorld(pair.Item2, 0)[1].Position) * (Hz * plan.HeightRatio);
                var horizontal = new Float3(v.X, 0f, v.Z);
                errors[slot] = Length(horizontal - reference);
                gates.Check("G4 horizontal hips velocity difference " + pair.Item1, errors[slot], 0.5, "m/s", pair.Item3);
                slot++;
                gates.Note("  source (" + Vec(reference) + ") output (" + Vec(horizontal) + ") m/s");
                var speeds = new StringBuilder("  output horizontal speed by frame:");
                for (var k = 1; k < Math.Min(6, pair.Item2.SampleCount); k++)
                    speeds.Append(' ').Append(Horizontal(OutputWorld(pair.Item2, k)[1].Position - OutputWorld(pair.Item2, k - 1)[1].Position, plan).ToString("0.00", CultureInfo.InvariantCulture));
                gates.Note(speeds.ToString());
            }
            if (errors[0] > errors[1] * 0.5) gates.Failures.Add("G4 root fix must cut the horizontal velocity error by half: " + errors[0].ToString("0.000", CultureInfo.InvariantCulture) + " vs " + errors[1].ToString("0.000", CultureInfo.InvariantCulture));
            var trend = new StringBuilder("  source horizontal speed frames " + (anchor - 3) + ".." + Math.Min(source.SampleCount - 1, anchor + 3) + ":");
            for (var k = anchor - 3; k <= Math.Min(source.SampleCount - 1, anchor + 3); k++)
                trend.Append(' ').Append(Horizontal(SourceWorld(source, anchor, k)[1].Position - SourceWorld(source, anchor, k - 1)[1].Position, plan).ToString("0.00", CultureInfo.InvariantCulture));
            gates.Note(trend.ToString());
        }

        static double Horizontal(Float3 step, FamilyRigPlan plan) => Math.Sqrt((double)step.X * step.X + (double)step.Z * step.Z) * Hz * plan.HeightRatio;

        internal static void RunSync(StudioCommand command)
        {
            var process = new StudioProcess();
            Assert.IsTrue(process.Start(command, out var error), error);
            var begun = DateTime.UtcNow;
            while (process.Running && (DateTime.UtcNow - begun).TotalSeconds < 300) Thread.Sleep(20);
            Assert.AreEqual(0, process.ExitCode, string.Join(" ", command.Args) + "\n" + process.Log);
        }

        internal static void Report(string name, string text)
        {
            var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa", "j1");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".txt"), text);
            TestContext.WriteLine(text);
        }
    }
}

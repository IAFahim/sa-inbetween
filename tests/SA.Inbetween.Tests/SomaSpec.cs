using System;
using System.Linq;
using CsCheck;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

[Trait(Packets.Name, Packets.P02)]
public class BlockSpec
{
    [Fact]
    public void Copy_and_Of_compare_by_content()
    {
        var a = Blocks.Copy<float>(new[] { 1f, 2f });
        var b = Blocks.Of(1f, 2f);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));
    }

    [Fact]
    public void Empty_and_default_have_length_zero_and_are_equal()
    {
        Assert.Equal(0, Blocks.Empty<int>().Length);
        Assert.Equal(0, default(Block<int>).Length);
        Assert.True(default(Block<int>).Span.IsEmpty);
        Assert.Equal(Blocks.Empty<int>(), default);
    }

    [Fact]
    public void Of_copies_its_input()
    {
        var source = new[] { 1, 2, 3 };
        var block = Blocks.Of(source);
        source[0] = 99;
        Assert.Equal(1, block.Span[0]);
    }

    [Fact]
    public void BitwiseEqual_distinguishes_signed_zero_and_matches_nan()
    {
        Assert.False(Blocks.BitwiseEqual(Blocks.Of(0f), Blocks.Of(-0f)));
        Assert.True(Blocks.BitwiseEqual(Blocks.Of(float.NaN), Blocks.Of(float.NaN)));
        Assert.False(Blocks.BitwiseEqual(Blocks.Of(1f), Blocks.Of(1f, 2f)));
    }
}

[Trait(Packets.Name, Packets.P02)]
public class ResultSpec
{
    [Fact]
    public void Ok_exposes_value_only()
    {
        var r = Result.Ok<int, Fault>(5);
        Assert.True(r.IsOk);
        Assert.True(r.TryGetValue(out var v));
        Assert.Equal(5, v);
        Assert.False(r.TryGetFault(out _));
        Assert.Equal("ok5", r.Match(x => "ok" + x.ToString(System.Globalization.CultureInfo.InvariantCulture), f => "error"));
    }

    [Fact]
    public void Error_exposes_fault_only()
    {
        var r = Result.Error<int, Fault>(Faults.NotImplemented);
        Assert.False(r.IsOk);
        Assert.False(r.TryGetValue(out _));
        Assert.True(r.TryGetFault(out var f));
        Assert.Equal(Faults.NotImplemented, f);
        Assert.Equal(FaultCode.NotImplemented, r.Match(x => FaultCode.None, x => x.Code));
    }

    [Fact]
    public void Query_syntax_short_circuits_on_first_fault()
    {
        var calls = 0;
        var result =
            from a in Result.Ok<int, Fault>(1)
            from b in Result.Error<int, Fault>(Faults.Of(FaultCode.Timeout, 1, 2))
            from c in Result.Ok<int, Fault>(++calls)
            select a + b + c;
        Assert.Equal(Faults.Of(FaultCode.Timeout, 1, 2), Ok.Fault(result));
        Assert.Equal(0, calls);
        Assert.Equal(3, Ok.Value(Result.Ok<int, Fault>(1).Select(x => x + 2)));
        Assert.Equal(3, Ok.Value(Result.Ok<int, Fault>(1).SelectMany(x => Result.Ok<int, Fault>(x + 2))));
    }

    [Fact]
    public void Equality_is_structural()
    {
        Assert.Equal(Result.Ok<int, Fault>(1), Result.Ok<int, Fault>(1));
        Assert.True(Result.Ok<int, Fault>(1) != Result.Error<int, Fault>(Faults.NotImplemented));
        Assert.True(Result.Error<int, Fault>(Faults.NotImplemented) == Result.Error<int, Fault>(Faults.NotImplemented));
        Assert.Equal(Result.Ok<int, Fault>(1).GetHashCode(), Result.Ok<int, Fault>(1).GetHashCode());
        Assert.False(Result.Ok<int, Fault>(1).Equals((object)1));
    }

    [Fact]
    public void Faults_Of_builds_exact_fault()
    {
        Assert.Equal(new Fault(FaultCode.Timeout, 2, 3), Faults.Of(FaultCode.Timeout, 2, 3));
        Assert.Equal(new Fault(FaultCode.NotImplemented, 0, 0), Faults.NotImplemented);
    }
}

[Trait(Packets.Name, Packets.P02)]
public class JointSpec
{
    private static readonly string[] L1Names =
    {
        "Hips", "Spine1", "Spine2", "Chest", "Neck1", "Neck2", "Head", "Jaw", "LeftEye", "RightEye",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandThumbEnd", "LeftHandMiddleEnd",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandThumbEnd", "RightHandMiddleEnd",
        "LeftLeg", "LeftShin", "LeftFoot", "LeftToeBase", "RightLeg", "RightShin", "RightFoot", "RightToeBase",
    };

    private static readonly int[] L1SkeletonIndices =
    {
        0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 18, 28, 39, 40, 41, 42, 46, 56, 67, 68, 69, 70, 72, 73, 74, 75,
    };

    [Fact]
    public void Counts_match_L1()
    {
        Assert.Equal(30, SomaJoints.Count);
        Assert.Equal(77, SkeletonJoints.Count);
        Assert.Equal(0, SkeletonJoints.Root.Index);
    }

    [Fact]
    public void Every_model_joint_has_the_L1_name_and_skeleton_index()
    {
        for (var i = 0; i < SomaJoints.Count; i++)
        {
            var joint = Ok.Value(SomaJoints.FromIndex(i));
            Assert.Equal(i, joint.Index);
            Assert.Equal(L1Names[i], SomaJoints.Name(joint));
            Assert.Equal(L1SkeletonIndices[i], SomaJoints.ToSkeleton(joint).Index);
        }
    }

    [Fact]
    public void Named_joints_have_their_L1_index()
    {
        var named = new[]
        {
            SomaJoints.Hips, SomaJoints.Spine1, SomaJoints.Spine2, SomaJoints.Chest, SomaJoints.Neck1, SomaJoints.Neck2,
            SomaJoints.Head, SomaJoints.Jaw, SomaJoints.LeftEye, SomaJoints.RightEye, SomaJoints.LeftShoulder,
            SomaJoints.LeftArm, SomaJoints.LeftForeArm, SomaJoints.LeftHand, SomaJoints.LeftHandThumbEnd,
            SomaJoints.LeftHandMiddleEnd, SomaJoints.RightShoulder, SomaJoints.RightArm, SomaJoints.RightForeArm,
            SomaJoints.RightHand, SomaJoints.RightHandThumbEnd, SomaJoints.RightHandMiddleEnd, SomaJoints.LeftLeg,
            SomaJoints.LeftShin, SomaJoints.LeftFoot, SomaJoints.LeftToeBase, SomaJoints.RightLeg, SomaJoints.RightShin,
            SomaJoints.RightFoot, SomaJoints.RightToeBase,
        };
        Assert.Equal(Enumerable.Range(0, 30), named.Select(j => j.Index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    [InlineData(int.MinValue)]
    public void SomaJoint_FromIndex_rejects_out_of_range(int index) =>
        Assert.Equal(Faults.Of(FaultCode.JointIndexOutOfRange, 0, index), Ok.Fault(SomaJoints.FromIndex(index)));

    [Theory]
    [InlineData(-1)]
    [InlineData(77)]
    public void SkeletonJoint_FromIndex_rejects_out_of_range(int index) =>
        Assert.Equal(Faults.Of(FaultCode.JointIndexOutOfRange, 0, index), Ok.Fault(SkeletonJoints.FromIndex(index)));

    [Fact]
    public void SkeletonJoint_FromIndex_accepts_all_77() =>
        Assert.All(Enumerable.Range(0, 77), i => Assert.Equal(i, Ok.Value(SkeletonJoints.FromIndex(i)).Index));
}

[Trait(Packets.Name, Packets.P02)]
public class PoseParseSpec
{
    [Fact]
    public void Constants_match_layout()
    {
        Assert.Equal(7, Poses.FloatsPerJoint);
        Assert.Equal(539, Poses.SkeletonFloatCount);
        Assert.Equal(210, Poses.ModelFloatCount);
        Assert.Equal(1e-3f, Poses.UnitQuaternionTolerance);
    }

    [Fact]
    public void Parse_accepts_identity_and_copies_values()
    {
        var values = Fixtures.IdentityFloats(77);
        var pose = Ok.Value(Poses.Parse(values));
        values[0] = 5f;
        Assert.Equal(0f, pose.Values.Span[0]);
        Assert.Equal(539, pose.Values.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(538)]
    [InlineData(540)]
    public void Parse_rejects_wrong_length(int length) =>
        Assert.Equal(Faults.Of(FaultCode.PoseLength, 0, length), Ok.Fault(Poses.Parse(new float[length])));

    [Fact]
    public void Parse_rejects_non_finite_with_float_index()
    {
        var values = Fixtures.IdentityFloats(77);
        values[15] = float.PositiveInfinity;
        Assert.Equal(Faults.Of(FaultCode.PoseValueNotFinite, 15, 0), Ok.Fault(Poses.Parse(values)));
        values[15] = 0f;
        values[8] = float.NaN;
        Assert.Equal(Faults.Of(FaultCode.PoseValueNotFinite, 8, 0), Ok.Fault(Poses.Parse(values)));
    }

    [Fact]
    public void Parse_rejects_non_unit_quaternion_with_joint_index()
    {
        var values = Fixtures.IdentityFloats(77);
        values[(5 * 7) + 6] = 1.01f;
        Assert.Equal(Faults.Of(FaultCode.PoseQuaternionNotUnit, 5, 0), Ok.Fault(Poses.Parse(values)));
    }

    [Fact]
    public void ParseModel_requires_210_floats()
    {
        Assert.Equal(210, Fixtures.IdentityModelPose().Values.Length);
        Assert.Equal(Faults.Of(FaultCode.PoseLength, 0, 539), Ok.Fault(Poses.ParseModel(Fixtures.IdentityFloats(77))));
    }

    [Fact]
    public void Joint_reads_seven_floats_of_that_joint()
    {
        var pose = Fixtures.NumberedPose(10f);
        var t = Poses.Joint(pose, Ok.Value(SkeletonJoints.FromIndex(3)));
        Assert.Equal(new Vector3<SomaModel>(13f, -3f, 1.5f), t.Translation);
        Assert.Equal(new Quaternion<SomaModel>(0f, 0f, 0f, 1f), t.Rotation);
        var m = Poses.Joint(Fixtures.IdentityModelPose(), SomaJoints.RightToeBase);
        Assert.Equal(1f, m.Rotation.W);
    }

    [Fact]
    public void Parse_is_total_and_deterministic() =>
        Props.Check(Gen.Float.Array[0, 600], values =>
            Assert.Equal(Poses.Parse(values), Poses.Parse(values)));
}

[Trait(Packets.Name, Packets.P07)]
public class UnitsSpec
{
    [Fact]
    public void Known_values()
    {
        Assert.Equal(100f, Units.ToCentimeters(new Meters(1f)).Value);
        Assert.Equal(0.25f, Units.ToMeters(new Centimeters(25f)).Value);
        Near.Equal(180f, Units.ToDegrees(new Radians(MathF.PI)).Value, 1e-4f);
        Near.Equal(MathF.PI / 2f, Units.ToRadians(new Degrees(90f)).Value, 1e-6f);
    }

    [Fact]
    public void Length_round_trip() =>
        Props.Check(Gens.Finite, m => Near.Equal(m, Units.ToMeters(Units.ToCentimeters(new Meters(m))).Value, Math.Abs(m) * 1e-6f));

    [Fact]
    public void Angle_round_trip() =>
        Props.Check(Gen.Float[-10f, 10f], r => Near.Equal(r, Units.ToRadians(Units.ToDegrees(new Radians(r))).Value, 1e-5f));
}

[Trait(Packets.Name, Packets.P07)]
public class RotationsSpec
{
    private const float Tol = 1e-5f;
    private static readonly float S = MathF.Sqrt(0.5f);

    [Fact]
    public void Identity_is_0001() =>
        Assert.Equal(new Quaternion<SomaModel>(0f, 0f, 0f, 1f), Rotations.Identity<SomaModel>());

    [Fact]
    public void Multiply_by_identity_is_neutral() =>
        Props.Check(Gens.Rotation<SomaModel>(), q =>
        {
            Near.Equal(q, Rotations.Multiply(Rotations.Identity<SomaModel>(), q), Tol);
            Near.Equal(q, Rotations.Multiply(q, Rotations.Identity<SomaModel>()), Tol);
        });

    [Fact]
    public void Multiply_is_associative() =>
        Props.Check(Gen.Select(Gens.Rotation<SomaModel>(), Gens.Rotation<SomaModel>(), Gens.Rotation<SomaModel>()), t =>
            Near.Equal(
                Rotations.Multiply(Rotations.Multiply(t.Item1, t.Item2), t.Item3),
                Rotations.Multiply(t.Item1, Rotations.Multiply(t.Item2, t.Item3)),
                1e-4f));

    [Fact]
    public void Multiply_composes_right_to_left()
    {
        var aboutY = new Quaternion<SomaModel>(0f, S, 0f, S);
        var aboutX = new Quaternion<SomaModel>(S, 0f, 0f, S);
        var v = new Vector3<SomaModel>(0f, 0f, 1f);
        var composed = Rotations.Rotate(Rotations.Multiply(aboutY, aboutX), v);
        Near.Equal(Rotations.Rotate(aboutY, Rotations.Rotate(aboutX, v)), composed, Tol);
    }

    [Fact]
    public void Conjugate_inverts_unit_rotation() =>
        Props.Check(Gens.Rotation<SomaModel>(), q =>
        {
            Assert.Equal(new Quaternion<SomaModel>(-q.X, -q.Y, -q.Z, q.W), Rotations.Conjugate(q));
            Near.SameRotation(Rotations.Identity<SomaModel>(), Rotations.Multiply(q, Rotations.Conjugate(q)), 1e-5f);
        });

    [Fact]
    public void Normalize_gives_unit_length_and_keeps_direction()
    {
        var n = Rotations.Normalize(new Quaternion<SomaModel>(0f, 0f, 0f, 2f));
        Assert.Equal(new Quaternion<SomaModel>(0f, 0f, 0f, 1f), n);
        Props.Check(Gens.Rotation<SomaModel>(), q =>
        {
            var scaled = Rotations.Normalize(new Quaternion<SomaModel>(q.X * 3f, q.Y * 3f, q.Z * 3f, q.W * 3f));
            Near.Equal(q, scaled, 1e-5f);
        });
    }

    [Fact]
    public void Rotate_quarter_turn_about_y_maps_x_to_minus_z()
    {
        var q = Rotations.FromAxisAngle(new Vector3<SomaModel>(0f, 1f, 0f), new Radians(MathF.PI / 2f));
        Near.Equal(new Quaternion<SomaModel>(0f, S, 0f, S), q, Tol);
        Near.Equal(new Vector3<SomaModel>(0f, 0f, -1f), Rotations.Rotate(q, new Vector3<SomaModel>(1f, 0f, 0f)), Tol);
    }

    [Fact]
    public void Rotate_preserves_length() =>
        Props.Check(Gen.Select(Gens.Rotation<SomaModel>(), Gens.Vector<SomaModel>()), t =>
        {
            var r = Rotations.Rotate(t.Item1, t.Item2);
            var a = MathF.Sqrt((t.Item2.X * t.Item2.X) + (t.Item2.Y * t.Item2.Y) + (t.Item2.Z * t.Item2.Z));
            var b = MathF.Sqrt((r.X * r.X) + (r.Y * r.Y) + (r.Z * r.Z));
            Near.Equal(a, b, Math.Max(1e-3f, a * 1e-5f));
        });

    [Fact]
    public void AngleBetween_known_and_zero_for_same_or_negated()
    {
        var q = new Quaternion<SomaModel>(0f, S, 0f, S);
        Near.Equal(MathF.PI / 2f, Rotations.AngleBetween(Rotations.Identity<SomaModel>(), q).Value, 1e-5f);
        Props.Check(Gens.Rotation<SomaModel>(), r =>
        {
            Near.Equal(0f, Rotations.AngleBetween(r, r).Value, 1e-3f);
            Near.Equal(0f, Rotations.AngleBetween(r, new Quaternion<SomaModel>(-r.X, -r.Y, -r.Z, -r.W)).Value, 1e-3f);
        });
    }

    [Fact]
    public void Rotation6D_of_identity_is_first_two_matrix_columns() =>
        Assert.Equal(
            new Rotation6D<SomaModel>(new(1f, 0f, 0f), new(0f, 1f, 0f)),
            Rotations.ToRotation6D(Rotations.Identity<SomaModel>()));

    [Fact]
    public void Rotation6D_columns_are_rotated_axes() =>
        Props.Check(Gens.Rotation<SomaModel>(), q =>
        {
            var six = Rotations.ToRotation6D(q);
            Near.Equal(Rotations.Rotate(q, new Vector3<SomaModel>(1f, 0f, 0f)), six.XAxis, 1e-5f);
            Near.Equal(Rotations.Rotate(q, new Vector3<SomaModel>(0f, 1f, 0f)), six.YAxis, 1e-5f);
        });

    [Fact]
    public void Rotation6D_round_trip_is_same_rotation_with_non_negative_w() =>
        Props.Check(Gens.Rotation<SomaModel>(), q =>
        {
            var back = Rotations.FromRotation6D(Rotations.ToRotation6D(q));
            Near.SameRotation(q, back, 1e-5f);
            Assert.True(back.W >= 0f);
        });

    [Fact]
    public void FromRotation6D_orthonormalizes_like_MotionBricks()
    {
        var six = new Rotation6D<SomaModel>(new(2f, 0f, 0f), new(1f, 3f, 0f));
        Near.Equal(Rotations.Identity<SomaModel>(), Rotations.FromRotation6D(six), 1e-6f);
    }
}

[Trait(Packets.Name, Packets.P07)]
public class SpaceConversionsSpec
{
    private static readonly float S = MathF.Sqrt(0.5f);

    [Fact]
    public void ModelToRuntime_turns_y_up_into_z_up()
    {
        Assert.Equal(new Vector3<SomaRuntimeZUp>(1f, -3f, 2f), SpaceConversions.ModelToRuntime(new Vector3<SomaModel>(1f, 2f, 3f)));
        Near.Equal(new Quaternion<SomaRuntimeZUp>(S, 0f, 0f, S), SpaceConversions.ModelToRuntime(Rotations.Identity<SomaModel>()), 1e-6f);
    }

    [Fact]
    public void Runtime_round_trip() =>
        Props.Check(Gen.Select(Gens.Vector<SomaModel>(), Gens.Rotation<SomaModel>()), t =>
        {
            Near.Equal(t.Item1, SpaceConversions.RuntimeToModel(SpaceConversions.ModelToRuntime(t.Item1)), 1e-3f);
            Near.SameRotation(t.Item2, SpaceConversions.RuntimeToModel(SpaceConversions.ModelToRuntime(t.Item2)), 1e-5f);
        });

    [Fact]
    public void ModelToUnity_mirrors_x()
    {
        Assert.Equal(new Vector3<UnityWorld>(-1f, 2f, 3f), SpaceConversions.ModelToUnity(new Vector3<SomaModel>(1f, 2f, 3f)));
        Assert.Equal(new Quaternion<UnityWorld>(0.1f, -0.2f, -0.3f, 0.9f), SpaceConversions.ModelToUnity(new Quaternion<SomaModel>(0.1f, 0.2f, 0.3f, 0.9f)));
    }

    [Fact]
    public void Unity_round_trip_is_exact() =>
        Props.Check(Gen.Select(Gens.Vector<SomaModel>(), Gens.Rotation<SomaModel>()), t =>
        {
            Assert.Equal(t.Item1, SpaceConversions.UnityToModel(SpaceConversions.ModelToUnity(t.Item1)));
            Assert.Equal(t.Item2, SpaceConversions.UnityToModel(SpaceConversions.ModelToUnity(t.Item2)));
        });
}

[Trait(Packets.Name, Packets.P07)]
public class ModelPoseMappingSpec
{
    [Fact]
    public void ToModelPose_gathers_the_30_model_joints() =>
        Props.Check(Gens.Pose, pose =>
        {
            var model = Poses.ToModelPose(pose);
            Assert.Equal(Poses.ModelFloatCount, model.Values.Length);
            for (var i = 0; i < SomaJoints.Count; i++)
            {
                var joint = Ok.Value(SomaJoints.FromIndex(i));
                Assert.Equal(Poses.Joint(pose, SomaJoints.ToSkeleton(joint)), Poses.Joint(model, joint));
            }
        });

    [Fact]
    public void WithModelPose_scatters_and_keeps_other_joints() =>
        Props.Check(Gen.Select(Gens.Pose, Gens.ModelPose), t =>
        {
            var merged = Poses.WithModelPose(t.Item1, t.Item2);
            Assert.Equal(t.Item2, Poses.ToModelPose(merged));
            var modelIndices = Enumerable.Range(0, 30).Select(i => SomaJoints.ToSkeleton(Ok.Value(SomaJoints.FromIndex(i))).Index).ToHashSet();
            for (var j = 0; j < SkeletonJoints.Count; j++)
            {
                if (!modelIndices.Contains(j))
                {
                    var joint = Ok.Value(SkeletonJoints.FromIndex(j));
                    Assert.Equal(Poses.Joint(t.Item1, joint), Poses.Joint(merged, joint));
                }
            }
        });
}

using System;

namespace SA.Inbetween.Soma;

public readonly record struct Pose
{
    internal Pose(Block<float> values)
    {
        Values = values;
    }

    public Block<float> Values { get; }
}

public readonly record struct ModelPose
{
    internal ModelPose(Block<float> values)
    {
        Values = values;
    }

    public Block<float> Values { get; }
}

public static class Poses
{
    public const int FloatsPerJoint = 7;
    public const int SkeletonFloatCount = SkeletonJoints.Count * FloatsPerJoint;
    public const int ModelFloatCount = SomaJoints.Count * FloatsPerJoint;
    public const float UnitQuaternionTolerance = 1e-3f;

    public static Result<Pose, Fault> Parse(ReadOnlySpan<float> values) =>
        Check(values, SkeletonFloatCount).Select(block => new Pose(block));

    public static Result<ModelPose, Fault> ParseModel(ReadOnlySpan<float> values) =>
        Check(values, ModelFloatCount).Select(block => new ModelPose(block));

    public static RigidTransform<SomaModel> Joint(Pose pose, SkeletonJoint joint) =>
        Read(pose.Values.Span, joint.Index);

    public static RigidTransform<SomaModel> Joint(ModelPose pose, SomaJoint joint) =>
        Read(pose.Values.Span, joint.Index);

    public static ModelPose ToModelPose(Pose pose)
    {
        var source = pose.Values.Span;
        if (source.Length != SkeletonFloatCount)
        {
            return default;
        }

        var values = new float[ModelFloatCount];
        for (var m = 0; m < SomaJoints.Count; m++)
        {
            source.Slice(SomaJoints.SkeletonIndex(m) * FloatsPerJoint, FloatsPerJoint).CopyTo(values.AsSpan(m * FloatsPerJoint));
        }

        return new ModelPose(new Block<float>(values));
    }

    public static Pose WithModelPose(Pose reference, ModelPose model)
    {
        var source = model.Values.Span;
        if (reference.Values.Length != SkeletonFloatCount || source.Length != ModelFloatCount)
        {
            return default;
        }

        var values = reference.Values.Span.ToArray();
        for (var m = 0; m < SomaJoints.Count; m++)
        {
            source.Slice(m * FloatsPerJoint, FloatsPerJoint).CopyTo(values.AsSpan(SomaJoints.SkeletonIndex(m) * FloatsPerJoint));
        }

        return new Pose(new Block<float>(values));
    }

    private static RigidTransform<SomaModel> Read(ReadOnlySpan<float> values, int joint)
    {
        var offset = joint * FloatsPerJoint;
        if (offset + FloatsPerJoint > values.Length)
        {
            return new(new(float.NaN, float.NaN, float.NaN), new(float.NaN, float.NaN, float.NaN, float.NaN));
        }

        return new(
            new(values[offset], values[offset + 1], values[offset + 2]),
            new(values[offset + 3], values[offset + 4], values[offset + 5], values[offset + 6]));
    }

    private static Result<Block<float>, Fault> Check(ReadOnlySpan<float> values, int expectedLength)
    {
        if (values.Length != expectedLength)
        {
            return Result.Error<Block<float>, Fault>(Faults.Of(FaultCode.PoseLength, 0, values.Length));
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i]))
            {
                return Result.Error<Block<float>, Fault>(Faults.Of(FaultCode.PoseValueNotFinite, i, 0));
            }
        }

        for (var joint = 0; joint < expectedLength / FloatsPerJoint; joint++)
        {
            var q = values.Slice((joint * FloatsPerJoint) + 3, 4);
            var norm = Math.Sqrt((q[0] * (double)q[0]) + (q[1] * (double)q[1]) + (q[2] * (double)q[2]) + (q[3] * (double)q[3]));
            if (Math.Abs(norm - 1.0) > UnitQuaternionTolerance)
            {
                return Result.Error<Block<float>, Fault>(Faults.Of(FaultCode.PoseQuaternionNotUnit, joint, 0));
            }
        }

        return Result.Ok<Block<float>, Fault>(Blocks.Copy(values));
    }
}

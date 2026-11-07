namespace SA.Inbetween.Soma;

public readonly record struct SomaJoint
{
    internal SomaJoint(int index)
    {
        Index = index;
    }

    public int Index { get; }
}

public readonly record struct SkeletonJoint
{
    internal SkeletonJoint(int index)
    {
        Index = index;
    }

    public int Index { get; }
}

public static class SomaJoints
{
    public const int Count = 30;

    public static readonly SomaJoint Hips = new(0);
    public static readonly SomaJoint Spine1 = new(1);
    public static readonly SomaJoint Spine2 = new(2);
    public static readonly SomaJoint Chest = new(3);
    public static readonly SomaJoint Neck1 = new(4);
    public static readonly SomaJoint Neck2 = new(5);
    public static readonly SomaJoint Head = new(6);
    public static readonly SomaJoint Jaw = new(7);
    public static readonly SomaJoint LeftEye = new(8);
    public static readonly SomaJoint RightEye = new(9);
    public static readonly SomaJoint LeftShoulder = new(10);
    public static readonly SomaJoint LeftArm = new(11);
    public static readonly SomaJoint LeftForeArm = new(12);
    public static readonly SomaJoint LeftHand = new(13);
    public static readonly SomaJoint LeftHandThumbEnd = new(14);
    public static readonly SomaJoint LeftHandMiddleEnd = new(15);
    public static readonly SomaJoint RightShoulder = new(16);
    public static readonly SomaJoint RightArm = new(17);
    public static readonly SomaJoint RightForeArm = new(18);
    public static readonly SomaJoint RightHand = new(19);
    public static readonly SomaJoint RightHandThumbEnd = new(20);
    public static readonly SomaJoint RightHandMiddleEnd = new(21);
    public static readonly SomaJoint LeftLeg = new(22);
    public static readonly SomaJoint LeftShin = new(23);
    public static readonly SomaJoint LeftFoot = new(24);
    public static readonly SomaJoint LeftToeBase = new(25);
    public static readonly SomaJoint RightLeg = new(26);
    public static readonly SomaJoint RightShin = new(27);
    public static readonly SomaJoint RightFoot = new(28);
    public static readonly SomaJoint RightToeBase = new(29);

    private static readonly string[] Names =
    {
        "Hips", "Spine1", "Spine2", "Chest", "Neck1", "Neck2", "Head", "Jaw", "LeftEye", "RightEye",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "LeftHandThumbEnd", "LeftHandMiddleEnd",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandThumbEnd", "RightHandMiddleEnd",
        "LeftLeg", "LeftShin", "LeftFoot", "LeftToeBase", "RightLeg", "RightShin", "RightFoot", "RightToeBase",
    };

    private static readonly int[] SkeletonIndices =
    {
        0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 18, 28, 39, 40, 41, 42, 46, 56, 67, 68, 69, 70, 72, 73, 74, 75,
    };

    public static Result<SomaJoint, Fault> FromIndex(int index) =>
        index >= 0 && index < Count
            ? Result.Ok<SomaJoint, Fault>(new SomaJoint(index))
            : Result.Error<SomaJoint, Fault>(Faults.Of(FaultCode.JointIndexOutOfRange, 0, index));

    public static string Name(SomaJoint joint) => Names[joint.Index];

    public static SkeletonJoint ToSkeleton(SomaJoint joint) => new(SkeletonIndices[joint.Index]);
}

public static class SkeletonJoints
{
    public const int Count = 77;

    public static readonly SkeletonJoint Root = new(0);

    public static Result<SkeletonJoint, Fault> FromIndex(int index) =>
        index >= 0 && index < Count
            ? Result.Ok<SkeletonJoint, Fault>(new SkeletonJoint(index))
            : Result.Error<SkeletonJoint, Fault>(Faults.Of(FaultCode.JointIndexOutOfRange, 0, index));
}

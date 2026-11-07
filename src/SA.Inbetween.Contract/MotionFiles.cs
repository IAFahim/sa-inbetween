using System;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public enum AxisConvention
{
    SomaModelYUpRightHanded = 0,
}

public enum LengthUnit
{
    Meters = 0,
}

public readonly record struct MotionFile(
    Recipe Recipe,
    Sha256 RecipeHash,
    int FramesPerSecond,
    AxisConvention Axes,
    LengthUnit Units,
    Block<Pose> Frames,
    Block<RigidTransform<SomaModel>> RootWorld,
    Block<FootContacts> Contacts);

public static class MotionFiles
{
    public const uint Magic = 0x42494153;
    public const ushort Version = 1;
    public const int HeaderSize = 48;

    public static MotionFile From(GenerationResult result, Recipe recipe) => default;

    public static int SizeOf(MotionFile file) => -1;

    public static Result<int, Fault> Write(MotionFile file, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<MotionFile, Fault> Read(ReadOnlySpan<byte> source) =>
        Result.Error<MotionFile, Fault>(Faults.NotImplemented);
}

using System;
using System.Buffers.Binary;
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

    public static MotionFile From(GenerationResult result, Recipe recipe)
    {
        var frames = result.Frames.Span;
        var roots = new RigidTransform<SomaModel>[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            roots[i] = Poses.Joint(frames[i], SkeletonJoints.Root);
        }

        return new MotionFile(
            recipe,
            Recipes.Hash(recipe),
            Limits.FramesPerSecond,
            AxisConvention.SomaModelYUpRightHanded,
            LengthUnit.Meters,
            result.Frames,
            Blocks.Of(roots),
            result.Contacts);
    }

    public static int SizeOf(MotionFile file) =>
        HeaderSize + 4 + Recipes.CanonicalSize(file.Recipe)
            + (file.Frames.Length * Poses.SkeletonFloatCount * 4)
            + (file.Frames.Length * 7 * 4)
            + (file.Frames.Length * 4 * 4);

    public static Result<int, Fault> Write(MotionFile file, Span<byte> destination)
    {
        var recipeSize = Recipes.CanonicalSize(file.Recipe);
        var frames = file.Frames.Span;
        var total = HeaderSize + 4 + recipeSize
            + (frames.Length * Poses.SkeletonFloatCount * 4)
            + (frames.Length * 7 * 4)
            + (frames.Length * 4 * 4);
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4), Version);
        destination[6] = (byte)file.Axes;
        destination[7] = (byte)file.Units;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8), (ushort)file.FramesPerSecond);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10), (ushort)frames.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(12), SkeletonJoints.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(14), 0);
        file.RecipeHash.Bytes.Span.CopyTo(destination.Slice(16, Sha256s.ByteCount));
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(48), (uint)recipeSize);
        var recipe = Recipes.WriteCanonical(file.Recipe, destination.Slice(52, recipeSize));
        if (recipe.TryGetFault(out var recipeFault))
        {
            return Result.Error<int, Fault>(recipeFault);
        }

        var offset = 52 + recipeSize;
        for (var i = 0; i < frames.Length; i++)
        {
            WritePose(destination, offset, frames[i]);
            offset += Poses.SkeletonFloatCount * 4;
        }

        var roots = file.RootWorld.Span;
        for (var i = 0; i < roots.Length; i++)
        {
            WriteTransform(destination, offset, roots[i]);
            offset += 28;
        }

        var contacts = file.Contacts.Span;
        for (var i = 0; i < contacts.Length; i++)
        {
            WriteContacts(destination, offset, contacts[i]);
            offset += 16;
        }

        return Result.Ok<int, Fault>(offset);
    }

    public static Result<MotionFile, Fault> Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            return Fail<MotionFile>(FaultCode.MotionFileTruncated, 0, source.Length);
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (magic != Magic)
        {
            return Fail<MotionFile>(FaultCode.MotionFileBadMagic, 0, (int)magic);
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4));
        if (version != Version)
        {
            return Fail<MotionFile>(FaultCode.MotionFileUnsupportedVersion, 4, version);
        }

        var axes = source[6];
        if (axes > (byte)AxisConvention.SomaModelYUpRightHanded)
        {
            return Fail<MotionFile>(FaultCode.MotionFileFieldOutOfRange, 6, axes);
        }

        var units = source[7];
        if (units > (byte)LengthUnit.Meters)
        {
            return Fail<MotionFile>(FaultCode.MotionFileFieldOutOfRange, 7, units);
        }

        var fps = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(8));
        var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(10));
        var joints = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(12));
        if (joints != SkeletonJoints.Count)
        {
            return Fail<MotionFile>(FaultCode.MotionFileFieldOutOfRange, 12, joints);
        }

        if (source.Length < HeaderSize + 4)
        {
            return Fail<MotionFile>(FaultCode.MotionFileTruncated, 0, source.Length);
        }

        var recipeLength = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(48));
        var total = (long)HeaderSize + 4 + recipeLength + ((long)frameCount * 2200);
        if ((long)source.Length < total)
        {
            return Fail<MotionFile>(FaultCode.MotionFileTruncated, 0, source.Length);
        }

        var recipe = Recipes.ReadCanonical(source.Slice(52, (int)recipeLength));
        if (recipe.TryGetFault(out var recipeFault))
        {
            return Result.Error<MotionFile, Fault>(recipeFault);
        }

        recipe.TryGetValue(out var parsedRecipe);
        var stored = Sha256s.FromBytes(source.Slice(16, Sha256s.ByteCount));
        stored.TryGetValue(out var storedHash);
        var computed = Recipes.Hash(parsedRecipe);
        if (!computed.Bytes.Span.SequenceEqual(storedHash.Bytes.Span))
        {
            return Fail<MotionFile>(FaultCode.RecipeHashMismatch, 16, 0);
        }

        var offset = 52 + (int)recipeLength;
        var frames = new Pose[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            var pose = ReadPose(source, offset);
            if (pose.TryGetFault(out var poseFault))
            {
                return Result.Error<MotionFile, Fault>(poseFault);
            }

            pose.TryGetValue(out frames[i]);
            offset += Poses.SkeletonFloatCount * 4;
        }

        var roots = new RigidTransform<SomaModel>[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            roots[i] = ReadTransform(source, offset);
            offset += 28;
        }

        var contacts = new FootContacts[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            contacts[i] = ReadContacts(source, offset);
            offset += 16;
        }

        return Result.Ok<MotionFile, Fault>(new MotionFile(
            parsedRecipe,
            storedHash,
            fps,
            (AxisConvention)axes,
            (LengthUnit)units,
            Blocks.Of(frames),
            Blocks.Of(roots),
            Blocks.Of(contacts)));
    }

    private static void WriteFloat(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), BitConverter.SingleToInt32Bits(value));

    private static float ReadFloat(ReadOnlySpan<byte> source, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset)));

    private static void WritePose(Span<byte> destination, int offset, Pose pose)
    {
        var values = pose.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            WriteFloat(destination, offset + (i * 4), values[i]);
        }
    }

    private static Result<Pose, Fault> ReadPose(ReadOnlySpan<byte> source, int offset)
    {
        var values = new float[Poses.SkeletonFloatCount];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadFloat(source, offset + (i * 4));
        }

        return Poses.Parse(values);
    }

    private static void WriteTransform(Span<byte> destination, int offset, RigidTransform<SomaModel> transform)
    {
        WriteFloat(destination, offset, transform.Translation.X);
        WriteFloat(destination, offset + 4, transform.Translation.Y);
        WriteFloat(destination, offset + 8, transform.Translation.Z);
        WriteFloat(destination, offset + 12, transform.Rotation.X);
        WriteFloat(destination, offset + 16, transform.Rotation.Y);
        WriteFloat(destination, offset + 20, transform.Rotation.Z);
        WriteFloat(destination, offset + 24, transform.Rotation.W);
    }

    private static RigidTransform<SomaModel> ReadTransform(ReadOnlySpan<byte> source, int offset) =>
        new(
            new Vector3<SomaModel>(ReadFloat(source, offset), ReadFloat(source, offset + 4), ReadFloat(source, offset + 8)),
            new Quaternion<SomaModel>(
                ReadFloat(source, offset + 12),
                ReadFloat(source, offset + 16),
                ReadFloat(source, offset + 20),
                ReadFloat(source, offset + 24)));

    private static void WriteContacts(Span<byte> destination, int offset, FootContacts contacts)
    {
        WriteFloat(destination, offset, contacts.LeftHeel);
        WriteFloat(destination, offset + 4, contacts.LeftToe);
        WriteFloat(destination, offset + 8, contacts.RightHeel);
        WriteFloat(destination, offset + 12, contacts.RightToe);
    }

    private static FootContacts ReadContacts(ReadOnlySpan<byte> source, int offset) =>
        new(
            ReadFloat(source, offset),
            ReadFloat(source, offset + 4),
            ReadFloat(source, offset + 8),
            ReadFloat(source, offset + 12));

    private static Result<T, Fault> Fail<T>(FaultCode code, int index, int value) =>
        Result.Error<T, Fault>(Faults.Of(code, index, value));
}

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public enum Backend
{
    Sidecar = 0,
    Ported = 1,
}

public enum Precision
{
    Fp32 = 0,
    Fp16Mixed = 1,
}

public readonly record struct Sha256
{
    internal Sha256(Block<byte> bytes)
    {
        Bytes = bytes;
    }

    public Block<byte> Bytes { get; }
}

public readonly record struct Recipe(Backend Backend, Precision Precision, Sha256 Model, GenerationRequest Inputs);

public static class ModelPins
{
    public const string SomaFp32OnnxSha256 = "856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb";
    public const long SomaFp32OnnxBytes = 757864273;
    public const string SomaN1JsonSha256 = "ed41ab5bf481383fa46bdf0322b0c1b96426973e5038b731bc31276121051aaa";
    public const string MotionBricksCommit = "54fc563aeaaf819a753d72cc9fdc6713989cdd43";
}

public static class Sha256s
{
    public const int ByteCount = 32;

    private const string HexDigits = "0123456789abcdef";

    public static Result<Sha256, Fault> FromBytes(System.ReadOnlySpan<byte> bytes) =>
        bytes.Length == ByteCount
            ? Result.Ok<Sha256, Fault>(new Sha256(Blocks.Copy(bytes)))
            : Result.Error<Sha256, Fault>(Faults.Of(FaultCode.Sha256Invalid, 0, bytes.Length));

    public static Result<Sha256, Fault> ParseHex(string text)
    {
        if (text.Length != ByteCount * 2)
        {
            return Result.Error<Sha256, Fault>(Faults.Of(FaultCode.Sha256Invalid, 0, text.Length));
        }

        var bytes = new byte[ByteCount];
        for (var i = 0; i < ByteCount; i++)
        {
            var hi = HexValue(text[i * 2]);
            if (hi < 0)
            {
                return Result.Error<Sha256, Fault>(Faults.Of(FaultCode.Sha256Invalid, 1, i * 2));
            }

            var lo = HexValue(text[(i * 2) + 1]);
            if (lo < 0)
            {
                return Result.Error<Sha256, Fault>(Faults.Of(FaultCode.Sha256Invalid, 1, (i * 2) + 1));
            }

            bytes[i] = (byte)((hi << 4) | lo);
        }

        return FromBytes(bytes);
    }

    public static string ToHex(Sha256 hash)
    {
        var bytes = hash.Bytes.Span;
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = HexDigits[bytes[i] >> 4];
            chars[(i * 2) + 1] = HexDigits[bytes[i] & 0xF];
        }

        return new string(chars);
    }

    public static Sha256 Of(System.ReadOnlySpan<byte> data)
    {
        using (var sha = SHA256.Create())
        {
            var input = data.ToArray();
            var bytes = sha.ComputeHash(input);
            var result = FromBytes(bytes);
            result.TryGetValue(out var hash);
            return hash;
        }
    }

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return (c - 'a') + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return (c - 'A') + 10;
        }

        return -1;
    }
}

public static class Recipes
{
    public static int CanonicalSize(Recipe recipe)
    {
        var size = 2 + Sha256s.ByteCount + 12 + 1;
        size += recipe.Inputs.Context.Length * Poses.SkeletonFloatCount * 4;
        size += 1;
        var targets = recipe.Inputs.Targets.Span;
        for (var i = 0; i < targets.Length; i++)
        {
            size += 7 + (targets[i].Poses.Length * Poses.SkeletonFloatCount * 4);
        }

        return size;
    }

    public static Result<int, Fault> WriteCanonical(Recipe recipe, System.Span<byte> destination)
    {
        var parsed = Requests.Parse(recipe.Inputs);
        if (parsed.TryGetFault(out var parseFault))
        {
            return Result.Error<int, Fault>(parseFault);
        }

        var size = CanonicalSize(recipe);
        if (destination.Length < size)
        {
            return Result.Error<int, Fault>(Faults.Of(FaultCode.WireBufferTooSmall, 0, size));
        }

        var offset = 0;
        destination[offset] = (byte)recipe.Backend;
        offset += 1;
        destination[offset] = (byte)recipe.Precision;
        offset += 1;
        recipe.Model.Bytes.Span.CopyTo(destination.Slice(offset));
        offset += Sha256s.ByteCount;
        var config = recipe.Inputs.Config;
        destination[offset] = (byte)config.AllowedTokenMin;
        offset += 1;
        destination[offset] = (byte)config.AllowedTokenMax;
        offset += 1;
        destination[offset] = config.CorrectGeneratedDiff ? (byte)1 : (byte)0;
        offset += 1;
        destination[offset] = config.WantTiming ? (byte)1 : (byte)0;
        offset += 1;
        WriteFloat(destination, offset, config.HeadingAlignmentX);
        offset += 4;
        WriteFloat(destination, offset, config.HeadingAlignmentZ);
        offset += 4;
        var context = recipe.Inputs.Context.Span;
        destination[offset] = (byte)context.Length;
        offset += 1;
        for (var i = 0; i < context.Length; i++)
        {
            WritePose(destination, offset, context[i]);
            offset += Poses.SkeletonFloatCount * 4;
        }

        var targets = recipe.Inputs.Targets.Span;
        destination[offset] = (byte)targets.Length;
        offset += 1;
        for (var t = 0; t < targets.Length; t++)
        {
            var target = targets[t];
            destination[offset] = (byte)target.Poses.Length;
            offset += 1;
            destination[offset] = (byte)target.PosesToUse;
            offset += 1;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset), (ushort)target.TransitionEarlyByFrames);
            offset += 2;
            destination[offset] = (byte)target.TokenCountOverride;
            offset += 1;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset), (ushort)target.IkPinMask);
            offset += 2;
            var poses = target.Poses.Span;
            for (var p = 0; p < poses.Length; p++)
            {
                WritePose(destination, offset, poses[p]);
                offset += Poses.SkeletonFloatCount * 4;
            }
        }

        return Result.Ok<int, Fault>(offset);
    }

    public static Result<Recipe, Fault> ReadCanonical(System.ReadOnlySpan<byte> source)
    {
        if (source.Length < 47)
        {
            return Fail<Recipe>(FaultCode.WireTruncated, 0, source.Length);
        }

        var backend = source[0];
        if (backend > 1)
        {
            return Fail<Recipe>(FaultCode.WireFieldOutOfRange, 0, backend);
        }

        var precision = source[1];
        if (precision > 1)
        {
            return Fail<Recipe>(FaultCode.WireFieldOutOfRange, 1, precision);
        }

        var model = Sha256s.FromBytes(source.Slice(2, Sha256s.ByteCount));
        model.TryGetValue(out var modelHash);
        var tokenMin = source[34];
        var tokenMax = source[35];
        var correct = source[36];
        if (correct > 1)
        {
            return Fail<Recipe>(FaultCode.WireFieldOutOfRange, 36, correct);
        }

        var wantTiming = source[37];
        if (wantTiming > 1)
        {
            return Fail<Recipe>(FaultCode.WireFieldOutOfRange, 37, wantTiming);
        }

        var headingX = ReadFloat(source, 38);
        var headingZ = ReadFloat(source, 42);
        var contextCount = source[46];
        var offset = 47;
        if (offset + (contextCount * Poses.SkeletonFloatCount * 4) + 1 > source.Length)
        {
            return Fail<Recipe>(FaultCode.WireTruncated, 0, source.Length);
        }

        var contextPoses = new Pose[contextCount];
        for (var i = 0; i < contextCount; i++)
        {
            var pose = ReadPose(source, offset);
            if (pose.TryGetFault(out var poseFault))
            {
                return Result.Error<Recipe, Fault>(poseFault);
            }

            pose.TryGetValue(out contextPoses[i]);
            offset += Poses.SkeletonFloatCount * 4;
        }

        var targetCount = source[offset];
        offset += 1;
        var targets = new TargetSpec[targetCount];
        for (var t = 0; t < targetCount; t++)
        {
            if (offset + 7 > source.Length)
            {
                return Fail<Recipe>(FaultCode.WireTruncated, 0, source.Length);
            }

            var poseCount = source[offset];
            var posesToUse = source[offset + 1];
            var early = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + 2));
            var tokenOverride = source[offset + 4];
            var ikMask = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset + 5));
            offset += 7;
            if (offset + (poseCount * Poses.SkeletonFloatCount * 4) > source.Length)
            {
                return Fail<Recipe>(FaultCode.WireTruncated, 0, source.Length);
            }

            var poses = new Pose[poseCount];
            for (var p = 0; p < poseCount; p++)
            {
                var pose = ReadPose(source, offset);
                if (pose.TryGetFault(out var poseFault))
                {
                    return Result.Error<Recipe, Fault>(poseFault);
                }

                pose.TryGetValue(out poses[p]);
                offset += Poses.SkeletonFloatCount * 4;
            }

            targets[t] = new TargetSpec(Blocks.Of(poses), posesToUse, early, tokenOverride, ikMask);
        }

        if (offset != source.Length)
        {
            return Fail<Recipe>(FaultCode.WireBodyLengthMismatch, 0, source.Length);
        }

        var config = new GenerationConfig(tokenMin, tokenMax, correct == 1, wantTiming == 1, headingX, headingZ);
        var request = new GenerationRequest(Blocks.Of(contextPoses), Blocks.Of(targets), config);
        var parsed = Requests.Parse(request);
        if (parsed.TryGetFault(out var requestFault))
        {
            return Result.Error<Recipe, Fault>(requestFault);
        }

        parsed.TryGetValue(out var valid);
        return Result.Ok<Recipe, Fault>(new Recipe((Backend)backend, (Precision)precision, modelHash, valid.Request));
    }

    public static Sha256 Hash(Recipe recipe)
    {
        var size = CanonicalSize(recipe);
        var buffer = new byte[size];
        var written = WriteCanonical(recipe, buffer);
        if (!written.IsOk)
        {
            return default;
        }

        return Sha256s.Of(buffer);
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

    private static Result<T, Fault> Fail<T>(FaultCode code, int index, int value) =>
        Result.Error<T, Fault>(Faults.Of(code, index, value));
}

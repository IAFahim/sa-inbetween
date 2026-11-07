using System;
using System.Buffers.Binary;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract.Wire;

public enum MessageType : byte
{
    Hello = 1,
    HelloAck = 2,
    Generate = 3,
    GenerateResult = 4,
    Error = 5,
    Reset = 6,
    Ping = 7,
    Cancel = 8,
}

public enum Rig : byte
{
    Soma = 0,
    G1 = 1,
}

public enum WirePrecision : byte
{
    Fp32 = 0,
    Fp16Mixed = 1,
}

public enum WireSpace : byte
{
    ModelYUp = 0,
    WorldZUp = 1,
}

public enum ChainMode : byte
{
    Single = 0,
    ServiceChained = 1,
}

public enum WireErrorCode : ushort
{
    BadRequest = 1,
    NotReady = 2,
    GpuOutOfMemory = 3,
    Internal = 4,
    Cancelled = 5,
}

public readonly record struct WireHeader(ushort Magic, byte Version, MessageType Type, uint RequestId, uint BodyLength);

public readonly record struct Hello(ushort ClientVersion, Rig Rig, WirePrecision Precision, uint Flags);

public readonly record struct HelloAck(
    ushort ServerVersion,
    Rig Rig,
    byte JointCount,
    byte FramesPerSecond,
    WireSpace Space,
    bool EngineReady,
    byte MinFrames,
    byte MaxFrames);

public readonly record struct WireConfig(
    byte AllowedTokenMin,
    byte AllowedTokenMax,
    bool CorrectGeneratedDiff,
    byte Flags,
    float HeadingAlignmentX,
    float HeadingAlignmentZ);

public readonly record struct WireTarget(
    byte PosesToUse,
    ushort TransitionEarlyByFrames,
    byte TokenCountOverride,
    ushort IkPinMask,
    Block<ModelPose> Poses);

public readonly record struct Generate(
    WireConfig Config,
    Block<ModelPose> Context,
    Block<WireTarget> Targets,
    ChainMode ChainMode);

public readonly record struct WireFrame(ModelPose Pose, RigidTransform<SomaModel> RootWorld, FootContacts Contacts);

public readonly record struct GenerateResult(
    byte Sequence,
    bool Last,
    byte TokensChosen,
    float GenerationMilliseconds,
    float TotalMilliseconds,
    Block<WireFrame> Frames);

public readonly record struct ErrorMessage(WireErrorCode Code, Block<byte> MessageUtf8);

public static class WireFormat
{
    public const ushort Magic = 0x4D42;
    public const byte Version = 1;
    public const int HeaderSize = 12;
    public const int PoseSize = 840;
    public const int FrameSize = 884;
    public const int HelloBodySize = 8;
    public const int HelloAckBodySize = 9;
    public const int WireConfigSize = 12;
    public const int GenerateResultFixedSize = 13;
    public const byte WantTimingFlag = 1;
    public const byte F16ResponseFlag = 2;
}

public static class WireCodec
{
    public static Result<WireHeader, Fault> ReadHeader(ReadOnlySpan<byte> source)
    {
        if (source.Length < WireFormat.HeaderSize)
        {
            return Fail<WireHeader>(FaultCode.WireTruncated, 0, source.Length);
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (magic != WireFormat.Magic)
        {
            return Fail<WireHeader>(FaultCode.WireBadMagic, 0, magic);
        }

        var version = source[2];
        if (version != WireFormat.Version)
        {
            return Fail<WireHeader>(FaultCode.WireUnsupportedVersion, 2, version);
        }

        var type = source[3];
        if (type < (byte)MessageType.Hello || type > (byte)MessageType.Cancel)
        {
            return Fail<WireHeader>(FaultCode.WireUnknownMessageType, 3, type);
        }

        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4));
        var bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8));
        return Result.Ok<WireHeader, Fault>(new WireHeader(magic, version, (MessageType)type, requestId, bodyLength));
    }

    public static Result<int, Fault> WriteHello(uint requestId, Hello message, Span<byte> destination)
    {
        var total = WireFormat.HeaderSize + WireFormat.HelloBodySize;
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        WriteHeader(destination, MessageType.Hello, requestId, WireFormat.HelloBodySize);
        var body = destination.Slice(WireFormat.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.ClientVersion);
        body[2] = (byte)message.Rig;
        body[3] = (byte)message.Precision;
        BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(4), message.Flags);
        return Result.Ok<int, Fault>(total);
    }

    public static Result<Hello, Fault> ReadHello(ReadOnlySpan<byte> body)
    {
        if (body.Length < WireFormat.HelloBodySize)
        {
            return Fail<Hello>(FaultCode.WireTruncated, 0, body.Length);
        }

        if (body.Length != WireFormat.HelloBodySize)
        {
            return Fail<Hello>(FaultCode.WireBodyLengthMismatch, 0, body.Length);
        }

        var rig = body[2];
        if (rig > (byte)Rig.G1)
        {
            return Fail<Hello>(FaultCode.WireFieldOutOfRange, 2, rig);
        }

        var precision = body[3];
        if (precision > (byte)WirePrecision.Fp16Mixed)
        {
            return Fail<Hello>(FaultCode.WireFieldOutOfRange, 3, precision);
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4));
        return Result.Ok<Hello, Fault>(new Hello(version, (Rig)rig, (WirePrecision)precision, flags));
    }

    public static Result<int, Fault> WriteHelloAck(uint requestId, HelloAck message, Span<byte> destination)
    {
        var total = WireFormat.HeaderSize + WireFormat.HelloAckBodySize;
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        WriteHeader(destination, MessageType.HelloAck, requestId, WireFormat.HelloAckBodySize);
        var body = destination.Slice(WireFormat.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.ServerVersion);
        body[2] = (byte)message.Rig;
        body[3] = message.JointCount;
        body[4] = message.FramesPerSecond;
        body[5] = (byte)message.Space;
        body[6] = message.EngineReady ? (byte)1 : (byte)0;
        body[7] = message.MinFrames;
        body[8] = message.MaxFrames;
        return Result.Ok<int, Fault>(total);
    }

    public static Result<HelloAck, Fault> ReadHelloAck(ReadOnlySpan<byte> body)
    {
        if (body.Length < WireFormat.HelloAckBodySize)
        {
            return Fail<HelloAck>(FaultCode.WireTruncated, 0, body.Length);
        }

        if (body.Length != WireFormat.HelloAckBodySize)
        {
            return Fail<HelloAck>(FaultCode.WireBodyLengthMismatch, 0, body.Length);
        }

        var rig = body[2];
        if (rig > (byte)Rig.G1)
        {
            return Fail<HelloAck>(FaultCode.WireFieldOutOfRange, 2, rig);
        }

        var space = body[5];
        if (space > (byte)WireSpace.WorldZUp)
        {
            return Fail<HelloAck>(FaultCode.WireFieldOutOfRange, 5, space);
        }

        var ready = body[6];
        if (ready > 1)
        {
            return Fail<HelloAck>(FaultCode.WireFieldOutOfRange, 6, ready);
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(body);
        return Result.Ok<HelloAck, Fault>(new HelloAck(
            version, (Rig)rig, body[3], body[4], (WireSpace)space, ready == 1, body[7], body[8]));
    }

    public static int SizeOf(Generate message)
    {
        var size = WireFormat.HeaderSize + WireFormat.WireConfigSize + 1
            + (message.Context.Length * WireFormat.PoseSize) + 1;
        var targets = message.Targets.Span;
        for (var i = 0; i < targets.Length; i++)
        {
            size += 7 + (targets[i].Poses.Length * WireFormat.PoseSize);
        }

        return size + 1;
    }

    public static Result<int, Fault> WriteGenerate(uint requestId, Generate message, Span<byte> destination)
    {
        var total = SizeOf(message);
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        WriteHeader(destination, MessageType.Generate, requestId, (uint)(total - WireFormat.HeaderSize));
        var offset = WireFormat.HeaderSize;
        var config = message.Config;
        destination[offset] = config.AllowedTokenMin;
        destination[offset + 1] = config.AllowedTokenMax;
        destination[offset + 2] = config.CorrectGeneratedDiff ? (byte)1 : (byte)0;
        destination[offset + 3] = config.Flags;
        WriteFloat(destination, offset + 4, config.HeadingAlignmentX);
        WriteFloat(destination, offset + 8, config.HeadingAlignmentZ);
        offset += WireFormat.WireConfigSize;
        var context = message.Context.Span;
        destination[offset] = (byte)context.Length;
        offset += 1;
        for (var i = 0; i < context.Length; i++)
        {
            WriteModelPose(destination, offset, context[i]);
            offset += WireFormat.PoseSize;
        }

        var targets = message.Targets.Span;
        destination[offset] = (byte)targets.Length;
        offset += 1;
        for (var t = 0; t < targets.Length; t++)
        {
            var target = targets[t];
            destination[offset] = target.PosesToUse;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 1), target.TransitionEarlyByFrames);
            destination[offset + 3] = target.TokenCountOverride;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 4), target.IkPinMask);
            var poses = target.Poses.Span;
            destination[offset + 6] = (byte)poses.Length;
            offset += 7;
            for (var p = 0; p < poses.Length; p++)
            {
                WriteModelPose(destination, offset, poses[p]);
                offset += WireFormat.PoseSize;
            }
        }

        destination[offset] = (byte)message.ChainMode;
        offset += 1;
        return Result.Ok<int, Fault>(offset);
    }

    public static Result<Generate, Fault> ReadGenerate(ReadOnlySpan<byte> body)
    {
        if (body.Length < WireFormat.WireConfigSize + 1)
        {
            return Fail<Generate>(FaultCode.WireTruncated, 0, body.Length);
        }

        var tokenMin = body[0];
        var tokenMax = body[1];
        var correct = body[2];
        if (correct > 1)
        {
            return Fail<Generate>(FaultCode.WireFieldOutOfRange, 2, correct);
        }

        var flags = body[3];
        if ((flags & ~WireFormat.WantTimingFlag) != 0)
        {
            return Fail<Generate>(FaultCode.WireFieldOutOfRange, 3, flags);
        }

        var headingX = ReadFloat(body, 4);
        var headingZ = ReadFloat(body, 8);
        var contextCount = body[12];
        var offset = WireFormat.WireConfigSize + 1;
        if (body.Length - offset < (contextCount * WireFormat.PoseSize) + 1)
        {
            return Fail<Generate>(FaultCode.WireTruncated, 0, body.Length);
        }

        var context = new ModelPose[contextCount];
        for (var i = 0; i < contextCount; i++)
        {
            var pose = ReadModelPose(body, offset);
            if (pose.TryGetFault(out var poseFault))
            {
                return Fail<Generate>(FaultCode.WireFieldOutOfRange, offset, (int)poseFault.Code);
            }

            pose.TryGetValue(out context[i]);
            offset += WireFormat.PoseSize;
        }

        var targetCount = body[offset];
        offset += 1;
        var targets = new WireTarget[targetCount];
        for (var t = 0; t < targetCount; t++)
        {
            if (body.Length - offset < 7)
            {
                return Fail<Generate>(FaultCode.WireTruncated, 0, body.Length);
            }

            var posesToUse = body[offset];
            var early = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset + 1));
            var tokenOverride = body[offset + 3];
            var ikMask = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset + 4));
            var sent = body[offset + 6];
            offset += 7;
            if (body.Length - offset < (sent * WireFormat.PoseSize) + 1)
            {
                return Fail<Generate>(FaultCode.WireTruncated, 0, body.Length);
            }

            var poses = new ModelPose[sent];
            for (var p = 0; p < sent; p++)
            {
                var pose = ReadModelPose(body, offset);
                if (pose.TryGetFault(out var poseFault))
                {
                    return Fail<Generate>(FaultCode.WireFieldOutOfRange, offset, (int)poseFault.Code);
                }

                pose.TryGetValue(out poses[p]);
                offset += WireFormat.PoseSize;
            }

            targets[t] = new WireTarget(posesToUse, early, tokenOverride, ikMask, Blocks.Of(poses));
        }

        var chain = body[offset];
        if (chain > (byte)ChainMode.ServiceChained)
        {
            return Fail<Generate>(FaultCode.WireFieldOutOfRange, offset, chain);
        }

        offset += 1;
        if (offset != body.Length)
        {
            return Fail<Generate>(FaultCode.WireBodyLengthMismatch, 0, body.Length);
        }

        var config = new WireConfig(tokenMin, tokenMax, correct == 1, flags, headingX, headingZ);
        return Result.Ok<Generate, Fault>(new Generate(
            config, Blocks.Of(context), Blocks.Of(targets), (ChainMode)chain));
    }

    public static int SizeOf(GenerateResult message) =>
        WireFormat.HeaderSize + WireFormat.GenerateResultFixedSize
            + (message.Frames.Length * WireFormat.FrameSize);

    public static Result<int, Fault> WriteGenerateResult(uint requestId, GenerateResult message, Span<byte> destination)
    {
        var total = SizeOf(message);
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        WriteHeader(destination, MessageType.GenerateResult, requestId, (uint)(total - WireFormat.HeaderSize));
        var offset = WireFormat.HeaderSize;
        destination[offset] = message.Sequence;
        destination[offset + 1] = message.Last ? (byte)1 : (byte)0;
        destination[offset + 2] = (byte)message.Frames.Length;
        destination[offset + 3] = 0;
        destination[offset + 4] = message.TokensChosen;
        WriteFloat(destination, offset + 5, message.GenerationMilliseconds);
        WriteFloat(destination, offset + 9, message.TotalMilliseconds);
        offset += WireFormat.GenerateResultFixedSize;
        var frames = message.Frames.Span;
        for (var i = 0; i < frames.Length; i++)
        {
            WriteModelPose(destination, offset, frames[i].Pose);
            offset += WireFormat.PoseSize;
            WriteTransform(destination, offset, frames[i].RootWorld);
            offset += 28;
            WriteContacts(destination, offset, frames[i].Contacts);
            offset += 16;
        }

        return Result.Ok<int, Fault>(offset);
    }

    public static Result<GenerateResult, Fault> ReadGenerateResult(ReadOnlySpan<byte> body)
    {
        if (body.Length < WireFormat.GenerateResultFixedSize)
        {
            return Fail<GenerateResult>(FaultCode.WireTruncated, 0, body.Length);
        }

        var sequence = body[0];
        var last = body[1];
        if (last > 1)
        {
            return Fail<GenerateResult>(FaultCode.WireFieldOutOfRange, 1, last);
        }

        var count = body[2];
        var tokens = body[4];
        var generationMs = ReadFloat(body, 5);
        var totalMs = ReadFloat(body, 9);
        if (body.Length < WireFormat.GenerateResultFixedSize + (count * WireFormat.FrameSize))
        {
            return Fail<GenerateResult>(FaultCode.WireTruncated, 0, body.Length);
        }

        var offset = WireFormat.GenerateResultFixedSize;
        var frames = new WireFrame[count];
        for (var i = 0; i < count; i++)
        {
            var pose = ReadModelPose(body, offset);
            if (pose.TryGetFault(out var poseFault))
            {
                return Fail<GenerateResult>(FaultCode.WireFieldOutOfRange, offset, (int)poseFault.Code);
            }

            pose.TryGetValue(out var modelPose);
            offset += WireFormat.PoseSize;
            var root = ReadTransform(body, offset);
            offset += 28;
            var contacts = ReadContacts(body, offset);
            offset += 16;
            frames[i] = new WireFrame(modelPose, root, contacts);
        }

        if (offset != body.Length)
        {
            return Fail<GenerateResult>(FaultCode.WireBodyLengthMismatch, 0, body.Length);
        }

        return Result.Ok<GenerateResult, Fault>(new GenerateResult(
            sequence, last == 1, tokens, generationMs, totalMs, Blocks.Of(frames)));
    }

    public static int SizeOf(ErrorMessage message) =>
        WireFormat.HeaderSize + 4 + message.MessageUtf8.Length;

    public static Result<int, Fault> WriteError(uint requestId, ErrorMessage message, Span<byte> destination)
    {
        var total = SizeOf(message);
        if (destination.Length < total)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, total);
        }

        WriteHeader(destination, MessageType.Error, requestId, (uint)(total - WireFormat.HeaderSize));
        var body = destination.Slice(WireFormat.HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)message.Code);
        BinaryPrimitives.WriteUInt16LittleEndian(body.Slice(2), (ushort)message.MessageUtf8.Length);
        message.MessageUtf8.Span.CopyTo(body.Slice(4, message.MessageUtf8.Length));
        return Result.Ok<int, Fault>(total);
    }

    public static Result<ErrorMessage, Fault> ReadError(ReadOnlySpan<byte> body)
    {
        if (body.Length < 4)
        {
            return Fail<ErrorMessage>(FaultCode.WireTruncated, 0, body.Length);
        }

        var code = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (code < (ushort)WireErrorCode.BadRequest || code > (ushort)WireErrorCode.Cancelled)
        {
            return Fail<ErrorMessage>(FaultCode.WireFieldOutOfRange, 0, code);
        }

        var messageLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2));
        if (body.Length < 4 + messageLength)
        {
            return Fail<ErrorMessage>(FaultCode.WireTruncated, 0, body.Length);
        }

        if (body.Length != 4 + messageLength)
        {
            return Fail<ErrorMessage>(FaultCode.WireBodyLengthMismatch, 0, body.Length);
        }

        return Result.Ok<ErrorMessage, Fault>(new ErrorMessage(
            (WireErrorCode)code, Blocks.Copy(body.Slice(4, messageLength))));
    }

    public static Result<int, Fault> WriteEmpty(MessageType type, uint requestId, Span<byte> destination)
    {
        if (type != MessageType.Reset && type != MessageType.Ping && type != MessageType.Cancel)
        {
            return Fail<int>(FaultCode.WireUnexpectedMessageType, 0, (int)type);
        }

        if (destination.Length < WireFormat.HeaderSize)
        {
            return Fail<int>(FaultCode.WireBufferTooSmall, 0, WireFormat.HeaderSize);
        }

        WriteHeader(destination, type, requestId, 0);
        return Result.Ok<int, Fault>(WireFormat.HeaderSize);
    }

    public static Fault ToFault(ErrorMessage message) => message.Code switch
    {
        WireErrorCode.BadRequest => Faults.Of(FaultCode.RemoteBadRequest, 0, 0),
        WireErrorCode.NotReady => Faults.Of(FaultCode.RemoteNotReady, 0, 0),
        WireErrorCode.GpuOutOfMemory => Faults.Of(FaultCode.RemoteGpuOutOfMemory, 0, 0),
        WireErrorCode.Internal => Faults.Of(FaultCode.RemoteInternal, 0, 0),
        WireErrorCode.Cancelled => Faults.Of(FaultCode.RemoteCancelled, 0, 0),
        _ => Faults.Of(FaultCode.RemoteInternal, 0, 0),
    };

    private static void WriteHeader(Span<byte> destination, MessageType type, uint requestId, uint bodyLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, WireFormat.Magic);
        destination[2] = WireFormat.Version;
        destination[3] = (byte)type;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), bodyLength);
    }

    private static void WriteFloat(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), BitConverter.SingleToInt32Bits(value));

    private static float ReadFloat(ReadOnlySpan<byte> source, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset)));

    private static void WriteModelPose(Span<byte> destination, int offset, ModelPose pose)
    {
        var values = pose.Values.Span;
        for (var i = 0; i < values.Length; i++)
        {
            WriteFloat(destination, offset + (i * 4), values[i]);
        }
    }

    private static Result<ModelPose, Fault> ReadModelPose(ReadOnlySpan<byte> source, int offset)
    {
        var values = new float[Poses.ModelFloatCount];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadFloat(source, offset + (i * 4));
        }

        return Poses.ParseModel(values);
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

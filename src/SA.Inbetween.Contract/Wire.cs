using System;
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
    public static Result<WireHeader, Fault> ReadHeader(ReadOnlySpan<byte> source) =>
        Result.Error<WireHeader, Fault>(Faults.NotImplemented);

    public static Result<int, Fault> WriteHello(uint requestId, Hello message, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<Hello, Fault> ReadHello(ReadOnlySpan<byte> body) =>
        Result.Error<Hello, Fault>(Faults.NotImplemented);

    public static Result<int, Fault> WriteHelloAck(uint requestId, HelloAck message, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<HelloAck, Fault> ReadHelloAck(ReadOnlySpan<byte> body) =>
        Result.Error<HelloAck, Fault>(Faults.NotImplemented);

    public static int SizeOf(Generate message) => -1;

    public static Result<int, Fault> WriteGenerate(uint requestId, Generate message, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<Generate, Fault> ReadGenerate(ReadOnlySpan<byte> body) =>
        Result.Error<Generate, Fault>(Faults.NotImplemented);

    public static int SizeOf(GenerateResult message) => -1;

    public static Result<int, Fault> WriteGenerateResult(uint requestId, GenerateResult message, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<GenerateResult, Fault> ReadGenerateResult(ReadOnlySpan<byte> body) =>
        Result.Error<GenerateResult, Fault>(Faults.NotImplemented);

    public static int SizeOf(ErrorMessage message) => -1;

    public static Result<int, Fault> WriteError(uint requestId, ErrorMessage message, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<ErrorMessage, Fault> ReadError(ReadOnlySpan<byte> body) =>
        Result.Error<ErrorMessage, Fault>(Faults.NotImplemented);

    public static Result<int, Fault> WriteEmpty(MessageType type, uint requestId, Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Fault ToFault(ErrorMessage message) => Faults.NotImplemented;
}

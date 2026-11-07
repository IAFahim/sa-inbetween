namespace SA.Inbetween.Soma;

public enum FaultCode
{
    None = 0,
    NotImplemented = 1,

    JointIndexOutOfRange = 10,
    PoseLength = 11,
    PoseValueNotFinite = 12,
    PoseQuaternionNotUnit = 13,

    AllowedTokenMinOutOfRange = 20,
    AllowedTokenMaxOutOfRange = 21,
    HeadingAlignmentInvalid = 22,
    ContextPoseCount = 23,
    ContextPoseLength = 24,
    TargetCount = 25,
    TargetPoseCount = 26,
    TargetPoseLength = 27,
    PosesToUseOutOfRange = 28,
    TransitionEarlyOutOfRange = 29,
    TokenCountOverrideOutOfRange = 30,
    IkPinMaskInvalid = 31,

    FrameCountOutOfRange = 40,
    ContactCountMismatch = 41,
    ContactOutOfRange = 42,
    FramePoseLength = 43,

    WireTruncated = 50,
    WireBadMagic = 51,
    WireUnsupportedVersion = 52,
    WireUnknownMessageType = 53,
    WireBodyLengthMismatch = 54,
    WireBufferTooSmall = 55,
    WireFieldOutOfRange = 56,
    WireUnexpectedMessageType = 57,

    MotionFileBadMagic = 60,
    MotionFileUnsupportedVersion = 61,
    MotionFileTruncated = 62,
    MotionFileFieldOutOfRange = 63,
    RecipeHashMismatch = 64,
    Sha256Invalid = 65,

    RemoteBadRequest = 70,
    RemoteNotReady = 71,
    RemoteGpuOutOfMemory = 72,
    RemoteInternal = 73,
    RemoteCancelled = 74,

    BackendUnavailable = 80,
    Timeout = 81,
    Cancelled = 82,
    NetworkShapeMismatch = 83,
    TargetIndexOutOfRange = 84,
}

public readonly record struct Fault(FaultCode Code, int Index, int Value);

public static class Faults
{
    public static readonly Fault NotImplemented = new(FaultCode.NotImplemented, 0, 0);

    public static Fault Of(FaultCode code, int index, int value) => new(code, index, value);
}

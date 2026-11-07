using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public static class Limits
{
    public const int ContextPoseCount = 4;
    public const int MinTargets = 1;
    public const int MaxTargets = 4;
    public const int MinTargetPoses = 1;
    public const int MaxTargetPoses = 4;
    public const int MinTokenCount = 6;
    public const int MaxTokenCount = 16;
    public const int MinFrames = 18;
    public const int MaxFrames = 58;
    public const int FramesPerSecond = 30;
    public const int MaxTransitionEarlyByFrames = 58;
    public const int IkPinMaskBits = 0x000F;
}

public readonly record struct GenerationConfig(
    int AllowedTokenMin,
    int AllowedTokenMax,
    bool CorrectGeneratedDiff,
    bool WantTiming,
    float HeadingAlignmentX,
    float HeadingAlignmentZ);

public readonly record struct TargetSpec(
    Block<Pose> Poses,
    int PosesToUse,
    int TransitionEarlyByFrames,
    int TokenCountOverride,
    int IkPinMask);

public readonly record struct GenerationRequest(
    Block<Pose> Context,
    Block<TargetSpec> Targets,
    GenerationConfig Config);

public readonly record struct ValidRequest
{
    internal ValidRequest(GenerationRequest request)
    {
        Request = request;
    }

    public GenerationRequest Request { get; }
}

public static class GenerationConfigs
{
    public static readonly GenerationConfig Default = new(8, 14, true, false, 1f, 0f);
}

public static class Requests
{
    public static Result<ValidRequest, Fault> Parse(GenerationRequest request)
    {
        var config = request.Config;
        if (config.AllowedTokenMin < Limits.MinTokenCount || config.AllowedTokenMin > Limits.MaxTokenCount)
        {
            return Fail(FaultCode.AllowedTokenMinOutOfRange, 0, config.AllowedTokenMin);
        }

        if (config.AllowedTokenMax < config.AllowedTokenMin || config.AllowedTokenMax > Limits.MaxTokenCount)
        {
            return Fail(FaultCode.AllowedTokenMaxOutOfRange, 0, config.AllowedTokenMax);
        }

        if (!IsFinite(config.HeadingAlignmentX) || !IsFinite(config.HeadingAlignmentZ)
            || (config.HeadingAlignmentX == 0f && config.HeadingAlignmentZ == 0f))
        {
            return Fail(FaultCode.HeadingAlignmentInvalid, 0, 0);
        }

        var context = request.Context.Span;
        if (context.Length != Limits.ContextPoseCount)
        {
            return Fail(FaultCode.ContextPoseCount, 0, context.Length);
        }

        for (var i = 0; i < context.Length; i++)
        {
            if (context[i].Values.Length != Poses.SkeletonFloatCount)
            {
                return Fail(FaultCode.ContextPoseLength, i, context[i].Values.Length);
            }
        }

        var targets = request.Targets.Span;
        if (targets.Length < Limits.MinTargets || targets.Length > Limits.MaxTargets)
        {
            return Fail(FaultCode.TargetCount, 0, targets.Length);
        }

        for (var t = 0; t < targets.Length; t++)
        {
            var target = targets[t];
            var poses = target.Poses.Span;
            if (poses.Length < Limits.MinTargetPoses || poses.Length > Limits.MaxTargetPoses)
            {
                return Fail(FaultCode.TargetPoseCount, t, poses.Length);
            }

            for (var p = 0; p < poses.Length; p++)
            {
                if (poses[p].Values.Length != Poses.SkeletonFloatCount)
                {
                    return Fail(FaultCode.TargetPoseLength, t, p);
                }
            }

            if (target.PosesToUse < Limits.MinTargetPoses || target.PosesToUse > poses.Length)
            {
                return Fail(FaultCode.PosesToUseOutOfRange, t, target.PosesToUse);
            }

            if (target.TransitionEarlyByFrames < 0 || target.TransitionEarlyByFrames > Limits.MaxTransitionEarlyByFrames)
            {
                return Fail(FaultCode.TransitionEarlyOutOfRange, t, target.TransitionEarlyByFrames);
            }

            if (target.TokenCountOverride != 0
                && (target.TokenCountOverride < Limits.MinTokenCount || target.TokenCountOverride > Limits.MaxTokenCount))
            {
                return Fail(FaultCode.TokenCountOverrideOutOfRange, t, target.TokenCountOverride);
            }

            if ((target.IkPinMask & ~Limits.IkPinMaskBits) != 0)
            {
                return Fail(FaultCode.IkPinMaskInvalid, t, target.IkPinMask);
            }
        }

        return Result.Ok<ValidRequest, Fault>(new ValidRequest(request));
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static Result<ValidRequest, Fault> Fail(FaultCode code, int index, int value) =>
        Result.Error<ValidRequest, Fault>(Faults.Of(code, index, value));
}

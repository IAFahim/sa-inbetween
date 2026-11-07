using System;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Contract;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Engine;

public static class NetworkShapes
{
    public const int Frames = 8;
    public const int GlobalRootWidth = 5;
    public const int LocalRootWidth = 4;
    public const int LocalPoseWidth = 267;
    public const int TokenSlots = 11;
    public const int OutputFrames = 64;
    public const int OutputWidth = 366;
    public const int BoundaryFrames = 3;
}

public readonly record struct NetworkInputs(
    Block<float> GlobalRootValues,
    Block<int> HasGlobalRootValues,
    Block<float> LocalRootValues,
    Block<int> HasLocalRootValues,
    Block<float> LocalPoses,
    Block<int> HasLocalPoses,
    Block<int> AllowedPredNumTokens);

public readonly record struct NetworkOutputs(Block<float> PredGlobalMotions, int PredNumTokens);

public interface INetwork
{
    ValueTask<Result<NetworkOutputs, Fault>> RunAsync(NetworkInputs inputs, CancellationToken cancellationToken);
}

public static class NetworkTensors
{
    public static Result<NetworkInputs, Fault> ParseInputs(NetworkInputs inputs)
    {
        var position = inputs.GlobalRootValues.Length != NetworkShapes.Frames * NetworkShapes.GlobalRootWidth ? 0
            : inputs.HasGlobalRootValues.Length != NetworkShapes.Frames ? 1
            : inputs.LocalRootValues.Length != NetworkShapes.Frames * NetworkShapes.LocalRootWidth ? 2
            : inputs.HasLocalRootValues.Length != NetworkShapes.Frames ? 3
            : inputs.LocalPoses.Length != NetworkShapes.Frames * NetworkShapes.LocalPoseWidth ? 4
            : inputs.HasLocalPoses.Length != NetworkShapes.Frames ? 5
            : inputs.AllowedPredNumTokens.Length != NetworkShapes.TokenSlots ? 6
            : -1;
        var length = position switch
        {
            0 => inputs.GlobalRootValues.Length,
            1 => inputs.HasGlobalRootValues.Length,
            2 => inputs.LocalRootValues.Length,
            3 => inputs.HasLocalRootValues.Length,
            4 => inputs.LocalPoses.Length,
            5 => inputs.HasLocalPoses.Length,
            _ => inputs.AllowedPredNumTokens.Length,
        };
        return position < 0
            ? Result.Ok<NetworkInputs, Fault>(inputs)
            : Result.Error<NetworkInputs, Fault>(Faults.Of(FaultCode.NetworkShapeMismatch, position, length));
    }

    public static Result<NetworkOutputs, Fault> ParseOutputs(NetworkOutputs outputs) =>
        outputs.PredGlobalMotions.Length != NetworkShapes.OutputFrames * NetworkShapes.OutputWidth
            ? Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.NetworkShapeMismatch, 0, outputs.PredGlobalMotions.Length))
            : outputs.PredNumTokens < Limits.MinTokenCount || outputs.PredNumTokens > Limits.MaxTokenCount
                ? Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.NetworkShapeMismatch, 1, outputs.PredNumTokens))
                : Result.Ok<NetworkOutputs, Fault>(outputs);

    public static int FrameCountFor(int tokens) => (4 * tokens) - 6;
}

public static class EngineEncoder
{
    public static Result<NetworkInputs, Fault> Encode(ValidRequest request, int targetIndex, Block<Pose> context)
    {
        var fault = EngineInputs.Check(request, targetIndex, context);
        if (fault.Code != FaultCode.None)
        {
            return Result.Error<NetworkInputs, Fault>(fault);
        }

        var target = request.Request.Targets.Span[targetIndex];
        var config = request.Request.Config;
        var contextToken = EngineInputs.Flatten(context.Span, EngineKernels.TokenPoses);
        var targetPoses = EngineInputs.Flatten(target.Poses.Span, target.PosesToUse);
        var targetToken = new float[EngineKernels.TokenPoses * EngineKernels.PoseFloats];
        EngineKernels.PadToken(targetPoses, target.PosesToUse, targetToken);
        var world = new float[EngineKernels.Rows * EngineKernels.PoseFloats];
        var globalRoot = new float[NetworkShapes.Frames * NetworkShapes.GlobalRootWidth];
        var localRoot = new float[NetworkShapes.Frames * NetworkShapes.LocalRootWidth];
        var localPoses = new float[NetworkShapes.Frames * NetworkShapes.LocalPoseWidth];
        var hasGlobal = new int[NetworkShapes.Frames];
        var hasLocalRoot = new int[NetworkShapes.Frames];
        var hasLocalPoses = new int[NetworkShapes.Frames];
        var allowed = new int[NetworkShapes.TokenSlots];
        EngineKernels.Encode(contextToken, targetToken, Xform.Identity, config.HeadingAlignmentX, config.HeadingAlignmentZ, world, globalRoot, localRoot, localPoses);
        EngineKernels.Conditions(target.PosesToUse, EngineInputs.AllowedBits(config, target), hasGlobal, hasLocalRoot, hasLocalPoses, allowed);
        return Result.Ok<NetworkInputs, Fault>(new NetworkInputs(
            Blocks.Of(globalRoot),
            Blocks.Of(hasGlobal),
            Blocks.Of(localRoot),
            Blocks.Of(hasLocalRoot),
            Blocks.Of(localPoses),
            Blocks.Of(hasLocalPoses),
            Blocks.Of(allowed)));
    }
}

public static class EngineDecoder
{
    public static Result<GenerationResult, Fault> Decode(
        ValidRequest request,
        int targetIndex,
        Block<Pose> context,
        NetworkOutputs outputs)
    {
        var fault = EngineInputs.Check(request, targetIndex, context);
        if (fault.Code != FaultCode.None)
        {
            return Result.Error<GenerationResult, Fault>(fault);
        }

        if (NetworkTensors.ParseOutputs(outputs).TryGetFault(out var shape))
        {
            return Result.Error<GenerationResult, Fault>(shape);
        }

        var target = request.Request.Targets.Span[targetIndex];
        var config = request.Request.Config;
        var tokens = outputs.PredNumTokens;
        if (((EngineInputs.AllowedBits(config, target) >> (tokens - Limits.MinTokenCount)) & 1) == 0)
        {
            return Result.Error<GenerationResult, Fault>(Faults.Of(FaultCode.NetworkShapeMismatch, 1, tokens));
        }

        var last = context.Span[EngineKernels.TokenPoses - 1].Values.Span;
        var world = new float[EngineKernels.PoseFloats];
        EngineKernels.ForwardKinematics(last, Xform.Identity, world);
        var alignment = EngineKernels.Alignment(world, config.HeadingAlignmentX, config.HeadingAlignmentZ);
        var n = NetworkTensors.FrameCountFor(tokens);
        var frames = new float[n * EngineKernels.PoseFloats];
        var contacts = new float[n * EngineKernels.Contacts];
        EngineKernels.Decode(outputs.PredGlobalMotions.Span, n, alignment, Xform.Identity, last, frames, contacts);
        if (config.CorrectGeneratedDiff)
        {
            var start = new float[EngineKernels.PoseFloats];
            var end = new float[EngineKernels.PoseFloats];
            EngineKernels.EndpointDeltas(frames, n, last, target.Poses.Span[0].Values.Span, start, end);
            EngineKernels.ApplyEndpointDeltas(frames, n, start, end);
        }

        var poses = new Pose[n];
        var feet = new FootContacts[n];
        for (var f = 0; f < n; f++)
        {
            var parsed = Poses.Parse(frames.AsSpan(f * EngineKernels.PoseFloats, EngineKernels.PoseFloats));
            if (parsed.TryGetFault(out var bad))
            {
                return Result.Error<GenerationResult, Fault>(Faults.Of(FaultCode.FramePoseLength, f, (int)bad.Code));
            }

            parsed.TryGetValue(out poses[f]);
            var c = f * EngineKernels.Contacts;
            feet[f] = new FootContacts(contacts[c], contacts[c + 1], contacts[c + 2], contacts[c + 3]);
        }

        return GenerationResults.Parse(Blocks.Of(poses), Blocks.Of(feet), new Timing(tokens, 0f, 0f));
    }

    public static Block<Pose> NextContext(GenerationResult result)
    {
        var frames = result.Frames.Span;
        return frames.Length < EngineKernels.TokenPoses
            ? Blocks.Empty<Pose>()
            : Blocks.Copy(frames.Slice(frames.Length - EngineKernels.TokenPoses));
    }
}

internal static class EngineInputs
{
    public static Fault Check(ValidRequest request, int targetIndex, Block<Pose> context)
    {
        if (targetIndex < 0 || targetIndex >= request.Request.Targets.Length)
        {
            return Faults.Of(FaultCode.TargetIndexOutOfRange, 0, targetIndex);
        }

        if (context.Length != Limits.ContextPoseCount)
        {
            return Faults.Of(FaultCode.ContextPoseCount, 0, context.Length);
        }

        var poses = context.Span;
        for (var i = 0; i < poses.Length; i++)
        {
            if (poses[i].Values.Length != Poses.SkeletonFloatCount)
            {
                return Faults.Of(FaultCode.ContextPoseLength, i, poses[i].Values.Length);
            }
        }

        return default;
    }

    public static int AllowedBits(GenerationConfig config, TargetSpec target)
    {
        if (target.TokenCountOverride != 0)
        {
            return 1 << (target.TokenCountOverride - Limits.MinTokenCount);
        }

        var bits = 0;
        for (var c = config.AllowedTokenMin; c <= config.AllowedTokenMax; c++)
        {
            bits |= 1 << (c - Limits.MinTokenCount);
        }

        return bits;
    }

    public static float[] Flatten(ReadOnlySpan<Pose> poses, int count)
    {
        var values = new float[count * EngineKernels.PoseFloats];
        for (var i = 0; i < count; i++)
        {
            poses[i].Values.Span.CopyTo(values.AsSpan(i * EngineKernels.PoseFloats));
        }

        return values;
    }
}

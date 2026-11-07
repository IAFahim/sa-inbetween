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
    public static Result<NetworkInputs, Fault> ParseInputs(NetworkInputs inputs) =>
        Result.Error<NetworkInputs, Fault>(Faults.NotImplemented);

    public static Result<NetworkOutputs, Fault> ParseOutputs(NetworkOutputs outputs) =>
        Result.Error<NetworkOutputs, Fault>(Faults.NotImplemented);

    public static int FrameCountFor(int tokens) => -1;
}

public static class EngineEncoder
{
    public static Result<NetworkInputs, Fault> Encode(ValidRequest request, int targetIndex, Block<Pose> context) =>
        Result.Error<NetworkInputs, Fault>(Faults.NotImplemented);
}

public static class EngineDecoder
{
    public static Result<GenerationResult, Fault> Decode(
        ValidRequest request,
        int targetIndex,
        Block<Pose> context,
        NetworkOutputs outputs) =>
        Result.Error<GenerationResult, Fault>(Faults.NotImplemented);

    public static Block<Pose> NextContext(GenerationResult result) => Blocks.Empty<Pose>();
}

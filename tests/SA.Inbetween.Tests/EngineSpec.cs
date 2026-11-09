using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Contract;
using SA.Inbetween.Engine;
using SA.Inbetween.Generators;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

public sealed class FixedNetwork : INetwork
{
    public int Calls { get; private set; }

    public ValueTask<Result<NetworkOutputs, Fault>> RunAsync(NetworkInputs inputs, CancellationToken cancellationToken)
    {
        Calls++;
        var motions = new float[NetworkShapes.OutputFrames * NetworkShapes.OutputWidth];
        for (var i = 0; i < motions.Length; i++)
        {
            motions[i] = (i % 6) switch { 0 => 1f, 4 => 1f, _ => 0f };
        }

        return new(Result.Ok<NetworkOutputs, Fault>(new NetworkOutputs(Blocks.Of(motions), 8)));
    }
}

public static class Tensors
{
    public static NetworkInputs Inputs() => new(
        Blocks.Of(new float[8 * 5]),
        Blocks.Of(new int[8]),
        Blocks.Of(new float[8 * 4]),
        Blocks.Of(new int[8]),
        Blocks.Of(new float[8 * 267]),
        Blocks.Of(new int[8]),
        Blocks.Of(new int[11]));
}

[Trait(Packets.Name, Packets.P11)]
public class NetworkShapesSpec
{
    [Fact]
    public void Shapes_match_the_onnx_signature()
    {
        Assert.Equal((8, 5, 4, 267, 11), (NetworkShapes.Frames, NetworkShapes.GlobalRootWidth, NetworkShapes.LocalRootWidth, NetworkShapes.LocalPoseWidth, NetworkShapes.TokenSlots));
        Assert.Equal((64, 366, 3), (NetworkShapes.OutputFrames, NetworkShapes.OutputWidth, NetworkShapes.BoundaryFrames));
    }

    [Fact]
    public void ParseInputs_accepts_exact_shapes() =>
        Assert.Equal(Tensors.Inputs(), Ok.Value(NetworkTensors.ParseInputs(Tensors.Inputs())));

    [Fact]
    public void ParseInputs_names_the_wrong_tensor_by_position()
    {
        var bad = Tensors.Inputs() with { LocalPoses = Blocks.Of(new float[10]) };
        Assert.Equal(Faults.Of(FaultCode.NetworkShapeMismatch, 4, 10), Ok.Fault(NetworkTensors.ParseInputs(bad)));
        var badMask = Tensors.Inputs() with { AllowedPredNumTokens = Blocks.Of(new int[10]) };
        Assert.Equal(Faults.Of(FaultCode.NetworkShapeMismatch, 6, 10), Ok.Fault(NetworkTensors.ParseInputs(badMask)));
    }

    [Fact]
    public void ParseOutputs_checks_motion_length_and_token_range()
    {
        var good = new NetworkOutputs(Blocks.Of(new float[64 * 366]), 6);
        Assert.Equal(good, Ok.Value(NetworkTensors.ParseOutputs(good)));
        Assert.Equal(Faults.Of(FaultCode.NetworkShapeMismatch, 0, 5), Ok.Fault(NetworkTensors.ParseOutputs(good with { PredGlobalMotions = Blocks.Of(new float[5]) })));
        Assert.Equal(Faults.Of(FaultCode.NetworkShapeMismatch, 1, 17), Ok.Fault(NetworkTensors.ParseOutputs(good with { PredNumTokens = 17 })));
    }
}

[Trait(Packets.Name, Packets.P10)]
public class DecodeSpec
{
    [Theory]
    [InlineData(6, 18)]
    [InlineData(8, 26)]
    [InlineData(16, 58)]
    public void Frame_count_is_four_per_token_minus_boundary(int tokens, int frames) =>
        Assert.Equal(frames, NetworkTensors.FrameCountFor(tokens));

    [Fact]
    public void NextContext_is_the_last_four_frames()
    {
        var result = Fixtures.Result(20);
        Assert.Equal(result.Frames.Span.Slice(16).ToArray(), EngineDecoder.NextContext(result).Span.ToArray());
    }

    [Fact]
    public void Decode_rejects_unknown_target() =>
        Assert.Equal(
            Faults.Of(FaultCode.TargetIndexOutOfRange, 0, 1),
            Ok.Fault(EngineDecoder.Decode(Fixtures.ValidRequest(), 1, Fixtures.ValidRequest().Request.Context, new NetworkOutputs(Blocks.Of(new float[64 * 366]), 8))));
}

[Trait(Packets.Name, Packets.P09)]
public class EncodeSpec
{
    [Fact]
    public void Encode_produces_onnx_shaped_inputs()
    {
        var request = Fixtures.ValidRequest();
        var inputs = Ok.Value(EngineEncoder.Encode(request, 0, request.Request.Context));
        Assert.Equal(Tensors.Inputs().LocalPoses.Length, inputs.LocalPoses.Length);
        Assert.True(NetworkTensors.ParseInputs(inputs).IsOk);
    }

    [Theory]
    [InlineData(8, 14, 0, new[] { 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0 })]
    [InlineData(6, 16, 0, new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(8, 14, 10, new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 })]
    public void Allowed_tokens_follow_config_or_override(int min, int max, int tokenOverride, int[] expected)
    {
        var raw = Fixtures.Request();
        raw = raw with
        {
            Config = raw.Config with { AllowedTokenMin = min, AllowedTokenMax = max },
            Targets = Blocks.Of(Fixtures.Target() with { TokenCountOverride = tokenOverride }),
        };
        var request = Ok.Value(Requests.Parse(raw));
        var inputs = Ok.Value(EngineEncoder.Encode(request, 0, request.Request.Context));
        Assert.Equal(expected, inputs.AllowedPredNumTokens.Span.ToArray());
    }

    [Fact]
    public void Encode_rejects_unknown_target()
    {
        var request = Fixtures.ValidRequest();
        Assert.Equal(Faults.Of(FaultCode.TargetIndexOutOfRange, 0, 3), Ok.Fault(EngineEncoder.Encode(request, 3, request.Request.Context)));
    }
}

[Trait(Packets.Name, Packets.P12)]
public class PortedGeneratorSpec
{
    [Fact]
    public async Task Same_request_gives_same_result_and_one_network_call_per_target()
    {
        var network = new FixedNetwork();
        var generator = new PortedGenerator(network);
        Assert.Same(network, generator.Network);
        var request = Fixtures.ValidRequest(3);
        var first = await generator.GenerateAsync(request, CancellationToken.None);
        var second = await generator.GenerateAsync(request, CancellationToken.None);
        Assert.NotEqual(Faults.NotImplemented, first.Match(_ => default, f => f));
        Assert.Equal(first, second);
        if (first.TryGetValue(out var a) && second.TryGetValue(out var b))
        {
            Assert.Equal(3, a.Length);
            Assert.Equal(6, network.Calls);
            for (var i = 0; i < a.Length; i++)
            {
                var x = a.Span[i].Frames.Span.ToArray().SelectMany(p => p.Values.Span.ToArray()).ToArray();
                var y = b.Span[i].Frames.Span.ToArray().SelectMany(p => p.Values.Span.ToArray()).ToArray();
                Assert.True(Blocks.BitwiseEqual(Blocks.Of(x), Blocks.Of(y)));
            }
        }
    }

    [Fact]
    public async Task Cancelled_token_gives_cancelled_fault()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var result = await new PortedGenerator(new FixedNetwork()).GenerateAsync(Fixtures.ValidRequest(), cancelled.Token);
        Assert.Equal(Faults.Of(FaultCode.Cancelled, 0, 0), Ok.Fault(result));
    }
}

[Trait(Packets.Name, Packets.P06)]
public class SidecarGeneratorSpec
{
    [Fact]
    public async Task No_listener_gives_backend_unavailable()
    {
        var options = new SidecarOptions(1, 200, 1000);
        using var generator = new SidecarGenerator(options);
        Assert.Equal(options, generator.Options);
        var result = await generator.GenerateAsync(Fixtures.ValidRequest(), CancellationToken.None);
        Assert.Equal(Faults.Of(FaultCode.BackendUnavailable, 0, 0), Ok.Fault(result));
    }
}

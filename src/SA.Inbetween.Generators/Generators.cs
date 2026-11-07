using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Contract;
using SA.Inbetween.Contract.Wire;
using SA.Inbetween.Engine;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Generators;

public readonly record struct SidecarOptions(int Port, int ConnectTimeoutMilliseconds, int RequestTimeoutMilliseconds);

public sealed class SidecarGenerator : IMotionGenerator, IDisposable
{
    private const ushort ClientVersion = 1;
    private const int MaxBodyBytes = 4194304;

    private Socket? socket;

    public SidecarGenerator(SidecarOptions options)
    {
        Options = options;
    }

    public SidecarOptions Options { get; }

    public async ValueTask<Result<Block<GenerationResult>, Fault>> GenerateAsync(ValidRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0));
        }

        var connected = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (connected.TryGetFault(out var connectFault))
        {
            return Result.Error<Block<GenerationResult>, Fault>(connectFault);
        }

        connected.TryGetValue(out var current);
        Socket? keep = null;
        try
        {
            using var requestTimeout = new CancellationTokenSource(Math.Max(0, Options.RequestTimeoutMilliseconds));
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestTimeout.Token);
            var token = combined.Token;
            using var stream = new NetworkStream(current, false);
            uint requestId = 1;
            var helloBytes = new byte[WireFormat.HeaderSize + WireFormat.HelloBodySize];
            var helloWritten = WireCodec.WriteHello(requestId, new Hello(ClientVersion, Rig.Soma, WirePrecision.Fp32, 0), helloBytes);
            if (helloWritten.TryGetFault(out var helloFault))
            {
                return Result.Error<Block<GenerationResult>, Fault>(helloFault);
            }

            await stream.WriteAsync(helloBytes, 0, helloBytes.Length, token).ConfigureAwait(false);
            var ack = await ReadBodyAsync(stream, requestId, MessageType.HelloAck, token).ConfigureAwait(false);
            if (ack.TryGetFault(out var ackFault))
            {
                return Result.Error<Block<GenerationResult>, Fault>(ackFault);
            }

            ack.TryGetValue(out var ackBody);
            var helloAck = WireCodec.ReadHelloAck(ackBody);
            if (helloAck.TryGetFault(out var helloAckFault))
            {
                return Result.Error<Block<GenerationResult>, Fault>(helloAckFault);
            }

            var context = request.Request.Context;
            var targetCount = request.Request.Targets.Length;
            var results = new GenerationResult[targetCount];
            for (var k = 0; k < targetCount; k++)
            {
                token.ThrowIfCancellationRequested();
                requestId++;
                var message = BuildGenerate(request, context, k);
                var bytes = new byte[WireCodec.SizeOf(message)];
                var written = WireCodec.WriteGenerate(requestId, message, bytes);
                if (written.TryGetFault(out var writeFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(writeFault);
                }

                await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                var response = await ReadBodyAsync(stream, requestId, MessageType.GenerateResult, token).ConfigureAwait(false);
                if (response.TryGetFault(out var responseFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(responseFault);
                }

                response.TryGetValue(out var responseBody);
                var wireResult = WireCodec.ReadGenerateResult(responseBody);
                if (wireResult.TryGetFault(out var wireFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(wireFault);
                }

                wireResult.TryGetValue(out var generated);
                var mapped = MapResult(generated, context);
                if (mapped.TryGetFault(out var mapFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(mapFault);
                }

                mapped.TryGetValue(out results[k]);
                context = EngineDecoder.NextContext(results[k]);
            }

            keep = current;
            return Result.Ok<Block<GenerationResult>, Fault>(Blocks.Of(results));
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0))
                : Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Timeout, 0, 0));
        }
        catch (TimeoutException)
        {
            return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Timeout, 0, 0));
        }
        catch (SocketException)
        {
            return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (IOException)
        {
            return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (ObjectDisposedException)
        {
            return cancellationToken.IsCancellationRequested
                ? Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0))
                : Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        finally
        {
            if (keep is null)
            {
                current.Dispose();
            }
            else
            {
                var previous = socket;
                socket = keep;
                previous?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        var owned = socket;
        socket = null;
        owned?.Dispose();
    }

    private async ValueTask<Result<Socket, Fault>> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            using var connectTimeout = new CancellationTokenSource(Math.Max(0, Options.ConnectTimeoutMilliseconds));
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeout.Token);
            var attempt = socket.ConnectAsync(IPAddress.Loopback, Options.Port);
            var gate = Task.Delay(Timeout.Infinite, combined.Token);
            var winner = await Task.WhenAny(attempt, gate).ConfigureAwait(false);
            if (winner != attempt)
            {
                socket.Close();
                await SettleAsync(attempt).ConfigureAwait(false);
                return cancellationToken.IsCancellationRequested
                    ? Result.Error<Socket, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0))
                    : Result.Error<Socket, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
            }

            await attempt.ConfigureAwait(false);
            return Result.Ok<Socket, Fault>(socket);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return Result.Error<Socket, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (ObjectDisposedException)
        {
            socket.Dispose();
            return cancellationToken.IsCancellationRequested
                ? Result.Error<Socket, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0))
                : Result.Error<Socket, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            socket.Dispose();
            return Result.Error<Socket, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
    }

    private static Generate BuildGenerate(ValidRequest request, Block<Pose> context, int targetIndex)
    {
        var source = request.Request;
        var config = source.Config;
        var wireConfig = new WireConfig(
            (byte)config.AllowedTokenMin,
            (byte)config.AllowedTokenMax,
            config.CorrectGeneratedDiff,
            config.WantTiming ? WireFormat.WantTimingFlag : (byte)0,
            config.HeadingAlignmentX,
            config.HeadingAlignmentZ);
        var contextPoses = context.Span;
        var wireContext = new ModelPose[contextPoses.Length];
        for (var i = 0; i < wireContext.Length; i++)
        {
            wireContext[i] = Poses.ToModelPose(contextPoses[i]);
        }

        var target = source.Targets.Span[targetIndex];
        var targetPoses = target.Poses.Span;
        var wirePoses = new ModelPose[targetPoses.Length];
        for (var i = 0; i < wirePoses.Length; i++)
        {
            wirePoses[i] = Poses.ToModelPose(targetPoses[i]);
        }

        var wireTarget = new WireTarget(
            (byte)target.PosesToUse,
            (ushort)target.TransitionEarlyByFrames,
            (byte)target.TokenCountOverride,
            (ushort)target.IkPinMask,
            Blocks.Of(wirePoses));
        return new Generate(
            wireConfig,
            Blocks.Of(wireContext),
            Blocks.Of(wireTarget),
            ChainMode.Single);
    }

    private static Result<GenerationResult, Fault> MapResult(GenerateResult wire, Block<Pose> context)
    {
        var frames = wire.Frames.Span;
        var poses = new Pose[frames.Length];
        var contacts = new FootContacts[frames.Length];
        var reference = context.Span[context.Length - 1];
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            var values = Poses.WithModelPose(reference, frame.Pose).Values.Span.ToArray();
            var translation = frame.RootWorld.Translation;
            var rotation = frame.RootWorld.Rotation;
            values[0] = translation.X;
            values[1] = translation.Y;
            values[2] = translation.Z;
            values[3] = rotation.X;
            values[4] = rotation.Y;
            values[5] = rotation.Z;
            values[6] = rotation.W;
            var parsed = Poses.Parse(values);
            if (parsed.TryGetFault(out var parseFault))
            {
                return Result.Error<GenerationResult, Fault>(parseFault);
            }

            parsed.TryGetValue(out poses[i]);
            contacts[i] = frame.Contacts;
            reference = poses[i];
        }

        return GenerationResults.Parse(
            Blocks.Of(poses),
            Blocks.Of(contacts),
            new Timing(wire.TokensChosen, wire.GenerationMilliseconds, wire.TotalMilliseconds));
    }

    private static async ValueTask<Result<byte[], Fault>> ReadBodyAsync(NetworkStream stream, uint requestId, MessageType expected, CancellationToken token)
    {
        var headerResult = await ReadExactlyAsync(stream, WireFormat.HeaderSize, token).ConfigureAwait(false);
        if (headerResult.TryGetFault(out var headerFault))
        {
            return Result.Error<byte[], Fault>(headerFault);
        }

        headerResult.TryGetValue(out var headerBytes);
        var header = WireCodec.ReadHeader(headerBytes);
        if (header.TryGetFault(out var codecFault))
        {
            return Result.Error<byte[], Fault>(codecFault);
        }

        header.TryGetValue(out var head);
        if (head.BodyLength > MaxBodyBytes)
        {
            return Result.Error<byte[], Fault>(Faults.Of(FaultCode.RemoteInternal, 0, 0));
        }

        var bodyResult = await ReadExactlyAsync(stream, (int)head.BodyLength, token).ConfigureAwait(false);
        if (bodyResult.TryGetFault(out var bodyFault))
        {
            return Result.Error<byte[], Fault>(bodyFault);
        }

        bodyResult.TryGetValue(out var body);
        if (head.Type == MessageType.Error)
        {
            var error = WireCodec.ReadError(body);
            if (error.TryGetFault(out var errorFault))
            {
                return Result.Error<byte[], Fault>(errorFault);
            }

            error.TryGetValue(out var message);
            return Result.Error<byte[], Fault>(WireCodec.ToFault(message));
        }

        if (head.Type != expected)
        {
            return Result.Error<byte[], Fault>(Faults.Of(FaultCode.WireUnexpectedMessageType, 0, (int)head.Type));
        }

        if (head.RequestId != requestId)
        {
            return Result.Error<byte[], Fault>(Faults.Of(FaultCode.RemoteInternal, 0, 0));
        }

        return Result.Ok<byte[], Fault>(body);
    }

    private static async ValueTask<Result<byte[], Fault>> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer, offset, count - offset, token).ConfigureAwait(false);
            if (read == 0)
            {
                return Result.Error<byte[], Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
            }

            offset += read;
        }

        return Result.Ok<byte[], Fault>(buffer);
    }

    private static async Task SettleAsync(Task attempt)
    {
        try
        {
            await attempt.ConfigureAwait(false);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}

public sealed class PortedGenerator : IMotionGenerator
{
    public PortedGenerator(INetwork network)
    {
        Network = network;
    }

    public INetwork Network { get; }

    public async ValueTask<Result<Block<GenerationResult>, Fault>> GenerateAsync(ValidRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0));
            }

            var context = request.Request.Context;
            var count = request.Request.Targets.Length;
            var results = new GenerationResult[count];
            for (var k = 0; k < count; k++)
            {
                var encoded = EngineEncoder.Encode(request, k, context);
                if (encoded.TryGetFault(out var encodeFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(encodeFault);
                }

                encoded.TryGetValue(out var inputs);
                var run = await Network.RunAsync(inputs, cancellationToken).ConfigureAwait(false);
                if (run.TryGetFault(out var runFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(runFault);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0));
                }

                run.TryGetValue(out var outputs);
                var decoded = EngineDecoder.Decode(request, k, context, outputs);
                if (decoded.TryGetFault(out var decodeFault))
                {
                    return Result.Error<Block<GenerationResult>, Fault>(decodeFault);
                }

                decoded.TryGetValue(out var result);
                results[k] = result;
                context = EngineDecoder.NextContext(result);
            }

            return Result.Ok<Block<GenerationResult>, Fault>(Blocks.Of(results));
        }
        catch (OperationCanceledException)
        {
            return Result.Error<Block<GenerationResult>, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0));
        }
    }
}

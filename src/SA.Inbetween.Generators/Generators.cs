using System;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Contract;
using SA.Inbetween.Engine;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Generators;

public readonly record struct SidecarOptions(int Port, int ConnectTimeoutMilliseconds, int RequestTimeoutMilliseconds);

public sealed class SidecarGenerator : IMotionGenerator, IDisposable
{
    public SidecarGenerator(SidecarOptions options)
    {
        Options = options;
    }

    public SidecarOptions Options { get; }

    public ValueTask<Result<Block<GenerationResult>, Fault>> GenerateAsync(ValidRequest request, CancellationToken cancellationToken) =>
        new(Result.Error<Block<GenerationResult>, Fault>(Faults.NotImplemented));

    public void Dispose()
    {
    }
}

public sealed class PortedGenerator : IMotionGenerator
{
    public PortedGenerator(INetwork network)
    {
        Network = network;
    }

    public INetwork Network { get; }

    public ValueTask<Result<Block<GenerationResult>, Fault>> GenerateAsync(ValidRequest request, CancellationToken cancellationToken) =>
        new(Result.Error<Block<GenerationResult>, Fault>(Faults.NotImplemented));
}

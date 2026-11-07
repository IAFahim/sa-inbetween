using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SA.Inbetween.Contract;
using SA.Inbetween.Engine;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Onnx;

public sealed class OnnxNetwork : INetwork, IDisposable
{
    private readonly InferenceSession session;

    private bool disposed;

    private OnnxNetwork(InferenceSession session)
    {
        this.session = session;
    }

    public static Result<OnnxNetwork, Fault> Load(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(modelPath);
        }
        catch (FileNotFoundException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (IOException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2));
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2));
        }
        catch (ArgumentException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }
        catch (NotSupportedException)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 0));
        }

        byte[] hash;
        using (var sha = SHA256.Create())
        {
            hash = sha.ComputeHash(bytes);
        }

        const string HexDigits = "0123456789abcdef";
        var chars = new char[64];
        for (var i = 0; i < 32; i++)
        {
            chars[i * 2] = HexDigits[hash[i] >> 4];
            chars[(i * 2) + 1] = HexDigits[hash[i] & 15];
        }

        var hex = new string(chars);
        if (hex != ModelPins.SomaFp32OnnxSha256)
        {
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 1));
        }

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        InferenceSession session;
        try
        {
            session = new InferenceSession(bytes, options);
        }
        catch (OnnxRuntimeException)
        {
            options.Dispose();
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2));
        }
        catch (ArgumentException)
        {
            options.Dispose();
            return Result.Error<OnnxNetwork, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2));
        }

        options.Dispose();
        return Result.Ok<OnnxNetwork, Fault>(new OnnxNetwork(session));
    }

    public ValueTask<Result<NetworkOutputs, Fault>> RunAsync(NetworkInputs inputs, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0)));
        }

        if (disposed)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2)));
        }

        var parsed = NetworkTensors.ParseInputs(inputs);
        if (!parsed.IsOk)
        {
            parsed.TryGetFault(out var parseFault);
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(parseFault));
        }

        try
        {
            var globalRoot = new DenseTensor<float>(new int[] { 1, NetworkShapes.Frames, NetworkShapes.GlobalRootWidth });
            var globalSpan = inputs.GlobalRootValues.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                for (var w = 0; w < NetworkShapes.GlobalRootWidth; w++)
                {
                    globalRoot[0, f, w] = globalSpan[(f * NetworkShapes.GlobalRootWidth) + w];
                }
            }

            var hasGlobal = new DenseTensor<int>(new int[] { 1, NetworkShapes.Frames });
            var hasGlobalSpan = inputs.HasGlobalRootValues.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                hasGlobal[0, f] = hasGlobalSpan[f];
            }

            var localRoot = new DenseTensor<float>(new int[] { 1, NetworkShapes.Frames, NetworkShapes.LocalRootWidth });
            var localSpan = inputs.LocalRootValues.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                for (var w = 0; w < NetworkShapes.LocalRootWidth; w++)
                {
                    localRoot[0, f, w] = localSpan[(f * NetworkShapes.LocalRootWidth) + w];
                }
            }

            var hasLocalRoot = new DenseTensor<int>(new int[] { 1, NetworkShapes.Frames });
            var hasLocalRootSpan = inputs.HasLocalRootValues.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                hasLocalRoot[0, f] = hasLocalRootSpan[f];
            }

            var localPoses = new DenseTensor<float>(new int[] { 1, NetworkShapes.Frames, NetworkShapes.LocalPoseWidth });
            var localPosesSpan = inputs.LocalPoses.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                for (var w = 0; w < NetworkShapes.LocalPoseWidth; w++)
                {
                    localPoses[0, f, w] = localPosesSpan[(f * NetworkShapes.LocalPoseWidth) + w];
                }
            }

            var hasLocalPoses = new DenseTensor<int>(new int[] { 1, NetworkShapes.Frames });
            var hasLocalPosesSpan = inputs.HasLocalPoses.Span;
            for (var f = 0; f < NetworkShapes.Frames; f++)
            {
                hasLocalPoses[0, f] = hasLocalPosesSpan[f];
            }

            var allowed = new DenseTensor<int>(new int[] { 1, NetworkShapes.TokenSlots });
            var allowedSpan = inputs.AllowedPredNumTokens.Span;
            for (var s = 0; s < NetworkShapes.TokenSlots; s++)
            {
                allowed[0, s] = allowedSpan[s];
            }

            var ortInputs = new List<NamedOnnxValue>(7);
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("global_root_values", globalRoot));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("has_global_root_values", hasGlobal));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("local_root_values", localRoot));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("has_local_root_values", hasLocalRoot));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("local_poses", localPoses));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("has_local_poses", hasLocalPoses));
            ortInputs.Add(NamedOnnxValue.CreateFromTensor("allowed_pred_num_tokens", allowed));

            using var results = session.Run(ortInputs);
            Tensor<float>? motionsTensor = null;
            var tokenValue = 0;
            var hasMotions = false;
            var hasToken = false;
            foreach (var result in results)
            {
                if (result.Name == "pred_global_motions" && result.Value is Tensor<float> motions)
                {
                    motionsTensor = motions;
                    hasMotions = true;
                }
                else if (result.Name == "pred_num_tokens" && result.Value is Tensor<int> tokens)
                {
                    tokenValue = tokens.GetValue(0);
                    hasToken = true;
                }
                else if (result.Name == "pred_num_tokens" && result.Value is Tensor<long> longTokens)
                {
                    tokenValue = (int)longTokens.GetValue(0);
                    hasToken = true;
                }
            }

            if (!hasMotions || !hasToken || motionsTensor is null)
            {
                return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2)));
            }

            var count = NetworkShapes.OutputFrames * NetworkShapes.OutputWidth;
            var motionsArray = new float[count];
            for (var i = 0; i < count; i++)
            {
                motionsArray[i] = motionsTensor.GetValue(i);
            }

            var outputs = new NetworkOutputs(Blocks.Of(motionsArray), tokenValue);
            return new ValueTask<Result<NetworkOutputs, Fault>>(NetworkTensors.ParseOutputs(outputs));
        }
        catch (OnnxRuntimeException)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2)));
        }
        catch (OperationCanceledException)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.Cancelled, 0, 0)));
        }
        catch (ArgumentException)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2)));
        }
        catch (InvalidOperationException)
        {
            return new ValueTask<Result<NetworkOutputs, Fault>>(Result.Error<NetworkOutputs, Fault>(Faults.Of(FaultCode.BackendUnavailable, 0, 2)));
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            session.Dispose();
        }
    }
}

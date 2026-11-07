using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Engine;
using SA.Inbetween.Onnx;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Onnx.Tests;

public static class ModelLocation
{
    public static string Path()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "GitHub",
            "motionbricks",
            "motionbricks",
            "assets",
            "onnx",
            "soma",
            "mmm_v6_soma_fp32.onnx");
    }
}

public static class IdentityInputs
{
    public static NetworkInputs Create()
    {
        var globalRoot = new float[NetworkShapes.Frames * NetworkShapes.GlobalRootWidth];
        for (var f = 0; f < NetworkShapes.Frames; f++)
        {
            globalRoot[(f * NetworkShapes.GlobalRootWidth) + 1] = 1f;
            globalRoot[(f * NetworkShapes.GlobalRootWidth) + 3] = 1f;
        }

        var hasGlobal = new int[NetworkShapes.Frames];
        var localRoot = new float[NetworkShapes.Frames * NetworkShapes.LocalRootWidth];
        var hasLocalRoot = new int[NetworkShapes.Frames];
        var localPoses = new float[NetworkShapes.Frames * NetworkShapes.LocalPoseWidth];
        var hasLocalPoses = new int[NetworkShapes.Frames];
        var allowed = new int[NetworkShapes.TokenSlots];
        for (var f = 0; f < NetworkShapes.Frames; f++)
        {
            hasGlobal[f] = 1;
            hasLocalRoot[f] = 1;
            hasLocalPoses[f] = 1;
            localRoot[(f * NetworkShapes.LocalRootWidth) + 3] = 1f;
            for (var j = 0; j < 30; j++)
            {
                var b = 87 + (j * 6);
                localPoses[(f * NetworkShapes.LocalPoseWidth) + b] = 1f;
                localPoses[(f * NetworkShapes.LocalPoseWidth) + b + 4] = 1f;
            }
        }

        allowed[0] = 1;
        return new NetworkInputs(
            Blocks.Of(globalRoot),
            Blocks.Of(hasGlobal),
            Blocks.Of(localRoot),
            Blocks.Of(hasLocalRoot),
            Blocks.Of(localPoses),
            Blocks.Of(hasLocalPoses),
            Blocks.Of(allowed));
    }
}

public sealed class NpyData
{
    public NpyData(int[] shape, float[] floats, int[] ints)
    {
        Shape = shape;
        Floats = floats;
        Ints = ints;
    }

    public int[] Shape { get; }

    public float[] Floats { get; }

    public int[] Ints { get; }
}

public static class NpyFiles
{
    public static NpyData Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 10 && bytes[0] == 0x93 && Encoding.ASCII.GetString(bytes, 1, 5) == "NUMPY", path);
        var headerLength = bytes[8] | (bytes[9] << 8);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLength);
        Assert.Contains("'fortran_order': False", header, StringComparison.Ordinal);
        var descr = header.Contains("'<f4'", StringComparison.Ordinal) ? "f4" : header.Contains("'<i4'", StringComparison.Ordinal) ? "i4" : string.Empty;
        Assert.NotEqual(string.Empty, descr);
        var shapeText = header[(header.IndexOf("'shape': (", StringComparison.Ordinal) + 10)..];
        shapeText = shapeText[..shapeText.IndexOf(')', StringComparison.Ordinal)];
        var shape = shapeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var count = shape.Aggregate(1, (a, b) => a * b);
        var offset = 10 + headerLength;
        var floats = new float[descr == "f4" ? count : 0];
        var ints = new int[descr == "i4" ? count : 0];
        for (var i = 0; i < count; i++)
        {
            var bits = BitConverter.ToInt32(bytes, offset + (4 * i));
            if (descr == "f4")
            {
                floats[i] = BitConverter.Int32BitsToSingle(bits);
            }
            else
            {
                ints[i] = bits;
            }
        }

        return new NpyData(shape, floats, ints);
    }
}

public static class EvalOnly
{
    public const string SkipReason =
        "testdata/eval-only/ is absent. It holds evaluation-only NVIDIA-derived data and is never committed; see testdata/eval-only/README.md.";

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "SA.Inbetween.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null ? string.Empty : System.IO.Path.Combine(directory.FullName, "testdata", "eval-only");
    }

    public static string RequireManifest()
    {
        var manifest = System.IO.Path.Combine(Root(), "manifest.sha256");
        if (!File.Exists(manifest))
        {
            Assert.Skip(SkipReason);
        }

        return manifest;
    }
}

[Trait("Packet", "P11")]
public class MissingModelSpec
{
    [Fact]
    public void Load_missing_file_gives_BackendUnavailable_0_0()
    {
        var result = OnnxNetwork.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa-inbetween-no-such-model.onnx"));
        Assert.False(result.IsOk);
        result.TryGetFault(out var fault);
        Assert.Equal(Faults.Of(FaultCode.BackendUnavailable, 0, 0), fault);
    }

    [Fact]
    public void Load_sha_mismatch_gives_BackendUnavailable_0_1()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa-inbetween-p11-sha-mismatch.onnx");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            var result = OnnxNetwork.Load(path);
            Assert.False(result.IsOk);
            result.TryGetFault(out var fault);
            Assert.Equal(Faults.Of(FaultCode.BackendUnavailable, 0, 1), fault);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[Trait("Packet", "P11")]
public class RealModelSpec
{
    [Fact]
    public async Task Identity_runs_finite_tokens_and_deterministic()
    {
        var modelPath = ModelLocation.Path();
        if (!File.Exists(modelPath))
        {
            Assert.Skip("model absent at " + modelPath);
        }

        var load = OnnxNetwork.Load(modelPath);
        Assert.True(load.IsOk);
        load.TryGetValue(out var network);
        using (network)
        {
            var inputs = IdentityInputs.Create();
            var first = await network.RunAsync(inputs, CancellationToken.None);
            Assert.True(first.IsOk);
            first.TryGetValue(out var outputs);
            Assert.Equal(NetworkShapes.OutputFrames * NetworkShapes.OutputWidth, outputs.PredGlobalMotions.Length);
            Assert.InRange(outputs.PredNumTokens, 6, 16);
            var bad = 0;
            foreach (var value in outputs.PredGlobalMotions.Span)
            {
                if (!float.IsFinite(value))
                {
                    bad++;
                }
            }

            Assert.Equal(0, bad);
            var second = await network.RunAsync(inputs, CancellationToken.None);
            Assert.True(second.IsOk);
            second.TryGetValue(out var again);
            Assert.Equal(outputs.PredNumTokens, again.PredNumTokens);
            Assert.True(Blocks.BitwiseEqual(outputs.PredGlobalMotions, again.PredGlobalMotions));
        }
    }

    [Fact]
    public async Task Cancelled_token_gives_Cancelled()
    {
        var modelPath = ModelLocation.Path();
        if (!File.Exists(modelPath))
        {
            Assert.Skip("model absent at " + modelPath);
        }

        var load = OnnxNetwork.Load(modelPath);
        Assert.True(load.IsOk);
        load.TryGetValue(out var network);
        using (network)
        {
            using var source = new CancellationTokenSource();
            await source.CancelAsync();
            var result = await network.RunAsync(IdentityInputs.Create(), source.Token);
            Assert.False(result.IsOk);
            result.TryGetFault(out var fault);
            Assert.Equal(Faults.Of(FaultCode.Cancelled, 0, 0), fault);
        }
    }
}

[Trait("Packet", "P11")]
public class CorpusParitySpec
{
    [Fact]
    public async Task Raw_outputs_within_5e3_of_corpus()
    {
        EvalOnly.RequireManifest();
        var goldens = System.IO.Path.Combine(EvalOnly.Root(), "goldens");
        var first = System.IO.Path.Combine(goldens, "req00", "net_pred_global_motions.npy");
        if (!File.Exists(first))
        {
            Assert.Skip("eval-only goldens not present");
        }

        var modelPath = ModelLocation.Path();
        if (!File.Exists(modelPath))
        {
            Assert.Skip("model absent at " + modelPath);
        }

        var dir = System.IO.Path.Combine(goldens, "req00");
        var globalRoot = NpyFiles.Read(System.IO.Path.Combine(dir, "net_global_root_values.npy")).Floats;
        var hasGlobal = NpyFiles.Read(System.IO.Path.Combine(dir, "net_has_global_root_values.npy")).Ints;
        var localRoot = NpyFiles.Read(System.IO.Path.Combine(dir, "net_local_root_values.npy")).Floats;
        var hasLocalRoot = NpyFiles.Read(System.IO.Path.Combine(dir, "net_has_local_root_values.npy")).Ints;
        var localPoses = NpyFiles.Read(System.IO.Path.Combine(dir, "net_local_poses.npy")).Floats;
        var hasLocalPoses = NpyFiles.Read(System.IO.Path.Combine(dir, "net_has_local_poses.npy")).Ints;
        var allowed = NpyFiles.Read(System.IO.Path.Combine(dir, "net_allowed_pred_num_tokens.npy")).Ints;
        var expectedMotions = NpyFiles.Read(System.IO.Path.Combine(dir, "net_pred_global_motions.npy")).Floats;
        var expectedTokens = NpyFiles.Read(System.IO.Path.Combine(dir, "net_pred_num_tokens.npy")).Ints;
        var inputs = new NetworkInputs(
            Blocks.Copy(globalRoot),
            Blocks.Copy(hasGlobal),
            Blocks.Copy(localRoot),
            Blocks.Copy(hasLocalRoot),
            Blocks.Copy(localPoses),
            Blocks.Copy(hasLocalPoses),
            Blocks.Copy(allowed));
        var load = OnnxNetwork.Load(modelPath);
        Assert.True(load.IsOk);
        load.TryGetValue(out var network);
        using (network)
        {
            var result = await network.RunAsync(inputs, CancellationToken.None);
            Assert.True(result.IsOk);
            result.TryGetValue(out var outputs);
            Assert.Equal(expectedTokens[0], outputs.PredNumTokens);
            Assert.Equal(expectedMotions.Length, outputs.PredGlobalMotions.Length);
            var max = 0.0;
            var actual = outputs.PredGlobalMotions.Span;
            for (var i = 0; i < actual.Length; i++)
            {
                var diff = Math.Abs((double)actual[i] - expectedMotions[i]);
                if (diff > max)
                {
                    max = diff;
                }
            }

            Assert.True(max <= 5e-3, "max abs " + max.ToString(CultureInfo.InvariantCulture));
        }
    }
}

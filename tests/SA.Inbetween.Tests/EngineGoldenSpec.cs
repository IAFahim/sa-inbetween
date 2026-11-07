using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SA.Inbetween.Contract;
using SA.Inbetween.Engine;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

public sealed record Npy(int[] Shape, float[] Floats, int[] Ints);

public static class NpyReader
{
    public static Npy Read(string path)
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

        return new Npy(shape, floats, ints);
    }
}

public sealed class Golden
{
    private readonly string directory;

    public Golden(string root, int id)
    {
        directory = Path.Combine(root, string.Create(CultureInfo.InvariantCulture, $"req{id:00}"));
        Id = id;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "request.json")));
        var r = json.RootElement;
        TargetPoses = r.GetProperty("targets").GetInt32();
        TokenCount = r.GetProperty("token_count").GetInt32();
        ValidFrames = r.GetProperty("valid_frames").GetInt32();
        Correct = r.GetProperty("correct").GetBoolean();
        var config = r.GetProperty("config");
        AllowedTokens = config.GetProperty("allowed_token_counts").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var heading = config.GetProperty("heading_alignment").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        HeadingX = heading[0];
        HeadingZ = heading[1];
        ModelToRuntimeKey = r.GetProperty("m2r").GetString() ?? "identity";
        Chain = r.GetProperty("chain").ValueKind == JsonValueKind.Number ? r.GetProperty("chain").GetInt32() : -1;
        Ik = r.GetProperty("ik").GetBoolean();
    }

    public int Id { get; }

    public int TargetPoses { get; }

    public int TokenCount { get; }

    public int ValidFrames { get; }

    public bool Correct { get; }

    public int[] AllowedTokens { get; }

    public double HeadingX { get; }

    public double HeadingZ { get; }

    public string ModelToRuntimeKey { get; }

    public int Chain { get; }

    public bool Ik { get; }

    public int AllowedBits => AllowedTokens.Aggregate(0, (bits, c) => bits | (1 << (c - 6)));

    public bool Has(string name) => File.Exists(Path.Combine(directory, name + ".npy"));

    public Npy Load(string name) => NpyReader.Read(Path.Combine(directory, name + ".npy"));

    public float[] F(string name) => Load(name).Floats;

    public int[] I(string name) => Load(name).Ints;
}

public static class Goldens
{
    public const string Missing = "eval-only goldens not present";

    public static readonly int[] Ids = Enumerable.Range(0, 12).ToArray();

    public static string Root
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("SA_GOLDENS_DIR");
            return string.IsNullOrEmpty(fromEnvironment) ? Path.Combine(Corpus.Root, "goldens") : fromEnvironment;
        }
    }

    public static string Require()
    {
        var root = Root;
        if (!File.Exists(Path.Combine(root, "req00", "final_frames.npy")))
        {
            Assert.Skip(Missing);
        }

        return root;
    }

    public static IEnumerable<Golden> All()
    {
        var root = Require();
        return Ids.Select(i => new Golden(root, i)).ToArray();
    }

    public static Golden One(int id) => new(Require(), id);

    public static float[] Reference() => NpyReader.Read(Path.Combine(Require(), "reference_local.npy")).Floats;

    internal static Xform ModelToRuntime(Golden g)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Require(), "skeleton.json")));
        var v = json.RootElement.GetProperty("model_to_runtime").GetProperty(g.ModelToRuntimeKey).EnumerateArray().Select(e => e.GetDouble()).ToArray();
        return new Xform(new Vec(v[0], v[1], v[2]), new Quat(v[3], v[4], v[5], v[6]));
    }
}

public static class Metrics
{
    public static double MaxAbs(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        Assert.Equal(a.Length, b.Length);
        var max = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            max = Math.Max(max, Math.Abs((double)a[i] - b[i]));
        }

        return max;
    }

    public static double MaxAbsColumns(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int width, int from, int to)
    {
        var max = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var c = i % width;
            if (c >= from && c < to)
            {
                max = Math.Max(max, Math.Abs((double)a[i] - b[i]));
            }
        }

        return max;
    }

    public static (double Millimetres, double Degrees) Transforms(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        Assert.Equal(a.Length, b.Length);
        double mm = 0.0, deg = 0.0;
        for (var o = 0; o < a.Length; o += 7)
        {
            var dx = (double)a[o] - b[o];
            var dy = (double)a[o + 1] - b[o + 1];
            var dz = (double)a[o + 2] - b[o + 2];
            mm = Math.Max(mm, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) * 1000.0);
            deg = Math.Max(deg, Rotation(a.Slice(o + 3, 4), b.Slice(o + 3, 4)));
        }

        return (mm, deg);
    }

    public static double Rotations(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var deg = 0.0;
        for (var o = 0; o < a.Length; o += 4)
        {
            deg = Math.Max(deg, Rotation(a.Slice(o, 4), b.Slice(o, 4)));
        }

        return deg;
    }

    private static double Rotation(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double na = 0, nb = 0, dot = 0;
        for (var i = 0; i < 4; i++)
        {
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
            dot += a[i] * (double)b[i];
        }

        var d = Math.Min(1.0, Math.Max(0.0, Math.Abs(dot / Math.Sqrt(na * nb))));
        return 2.0 * Math.Acos(d) * 180.0 / Math.PI;
    }
}

public sealed class Worst
{
    private readonly string name;
    private double value;
    private int id = -1;

    public Worst(string name)
    {
        this.name = name;
    }

    public void Add(int request, double v)
    {
        if (v > value || id < 0)
        {
            value = v;
            id = request;
        }
    }

    public void AtMost(double limit)
    {
        var sink = Environment.GetEnvironmentVariable("SA_ENGINE_METRICS");
        if (!string.IsNullOrEmpty(sink))
        {
            File.AppendAllText(sink, string.Create(CultureInfo.InvariantCulture, $"{name}: max {value:E3} at req{id:00} (limit {limit:E1}){Environment.NewLine}"));
        }
        Assert.True(value <= limit, string.Create(CultureInfo.InvariantCulture, $"{name}: {value:E3} at req{id:00} exceeds {limit:E1}"));
    }
}

internal static class EngineRun
{
    public static float[] TargetToken(Golden g, out float[] targetLocal)
    {
        targetLocal = g.F("target_local");
        var token = new float[4 * EngineKernels.PoseFloats];
        EngineKernels.PadToken(targetLocal, g.TargetPoses, token);
        return token;
    }

    public static (Xform Alignment, float[] World, float[] Grv, float[] Lrv, float[] Lp) Encode(Golden g)
    {
        var world = new float[8 * EngineKernels.PoseFloats];
        var grv = new float[40];
        var lrv = new float[32];
        var lp = new float[8 * 267];
        var contextToken = new float[4 * EngineKernels.PoseFloats];
        EngineKernels.PadToken(g.F("context_local"), 4, contextToken);
        var alignment = EngineKernels.Encode(contextToken, TargetToken(g, out _), RotationMath.TInv(Goldens.ModelToRuntime(g)), g.HeadingX, g.HeadingZ, world, grv, lrv, lp);
        return (alignment, world, grv, lrv, lp);
    }

    public static float[] AlignmentFloats(Xform a)
    {
        var v = new float[7];
        RotationMath.Write(v, 0, a);
        return v;
    }

    public static (float[] Frames, float[] Contacts, int N) Decode(Golden g, Xform alignment)
    {
        var n = EngineKernels.ValidFrames(g.I("net_pred_num_tokens")[0]);
        var frames = new float[n * EngineKernels.PoseFloats];
        var contacts = new float[n * 4];
        EngineKernels.Decode(g.F("net_pred_global_motions"), n, alignment, Goldens.ModelToRuntime(g), Goldens.Reference(), frames, contacts);
        return (frames, contacts, n);
    }

    public static Xform GoldenAlignment(Golden g) => RotationMath.Read(g.F("alignment"), 0);
}

[Trait(Packets.Name, Packets.P09)]
[Trait("Category", "EvalOnly")]
public class EngineEncodeGoldenSpec
{
    [Fact]
    public void T1_pad_token_is_exact()
    {
        foreach (var g in Goldens.All())
        {
            var token = EngineRun.TargetToken(g, out _);
            Assert.True(Blocks.BitwiseEqual(Blocks.Of(g.F("target_token")), Blocks.Of(token)), $"req{g.Id}");
            var context = new float[4 * EngineKernels.PoseFloats];
            EngineKernels.PadToken(g.F("context_local"), 4, context);
            Assert.True(Blocks.BitwiseEqual(Blocks.Of(g.F("context_token")), Blocks.Of(context)), $"req{g.Id}");
        }
    }

    [Fact]
    public void T2_forward_kinematics_within_0_01_mm_and_deg()
    {
        var mm = new Worst("T2 FK mm");
        var deg = new Worst("T2 FK deg");
        foreach (var g in Goldens.All())
        {
            var e = EngineRun.Encode(g);
            var (p, r) = Metrics.Transforms(e.World, g.F("guide_world"));
            mm.Add(g.Id, p);
            deg.Add(g.Id, r);
        }

        mm.AtMost(0.01);
        deg.AtMost(0.01);
    }

    [Fact]
    public void T3_alignment_within_1e_5()
    {
        var worst = new Worst("T3 alignment abs");
        foreach (var g in Goldens.All())
        {
            var guide = g.F("guide_world");
            var a = EngineKernels.Alignment(guide.AsSpan(3 * EngineKernels.PoseFloats, EngineKernels.PoseFloats), g.HeadingX, g.HeadingZ);
            worst.Add(g.Id, Metrics.MaxAbs(EngineRun.AlignmentFloats(a), g.F("alignment")));
        }

        worst.AtMost(1e-5);
    }

    [Fact]
    public void T4_encode_from_golden_world_within_1e_5()
    {
        var grv = new Worst("T4 global_root_values");
        var lp = new Worst("T4 local_poses");
        var lrv3 = new Worst("T4b local_root_values col 3");
        var lrv = new Worst("T4b local_root_values cols 0-2");
        foreach (var g in Goldens.All())
        {
            var gr = new float[40];
            var lr = new float[32];
            var l = new float[8 * 267];
            EngineKernels.EncodeRows(g.F("guide_world"), EngineRun.GoldenAlignment(g), gr, lr, l);
            grv.Add(g.Id, Metrics.MaxAbs(gr, g.F("net_global_root_values")));
            lp.Add(g.Id, Metrics.MaxAbs(l, g.F("net_local_poses")));
            lrv3.Add(g.Id, Metrics.MaxAbsColumns(lr, g.F("net_local_root_values"), 4, 3, 4));
            lrv.Add(g.Id, Metrics.MaxAbsColumns(lr, g.F("net_local_root_values"), 4, 0, 3));
        }

        grv.AtMost(1e-5);
        lp.AtMost(1e-5);
        lrv3.AtMost(1e-5);
        lrv.AtMost(1e-4);
    }

    [Fact]
    public void T5_conditions_are_exact()
    {
        foreach (var g in Goldens.All())
        {
            var hg = new int[8];
            var hl = new int[8];
            var hp = new int[8];
            var allowed = new int[11];
            EngineKernels.Conditions(g.TargetPoses, g.AllowedBits, hg, hl, hp, allowed);
            Assert.Equal(g.I("net_has_global_root_values"), hg);
            Assert.Equal(g.I("net_has_local_root_values"), hl);
            Assert.Equal(g.I("net_has_local_poses"), hp);
            Assert.Equal(g.I("net_allowed_pred_num_tokens"), allowed);
        }
    }

    [Fact]
    public void T10a_encode_end_to_end_from_local_poses()
    {
        var align = new Worst("T10 alignment");
        var grv = new Worst("T10 global_root_values");
        var lp = new Worst("T10 local_poses");
        var lrv3 = new Worst("T10 local_root_values col 3");
        var lrv = new Worst("T10 local_root_values cols 0-2");
        foreach (var g in Goldens.All())
        {
            var e = EngineRun.Encode(g);
            align.Add(g.Id, Metrics.MaxAbs(EngineRun.AlignmentFloats(e.Alignment), g.F("alignment")));
            grv.Add(g.Id, Metrics.MaxAbs(e.Grv, g.F("net_global_root_values")));
            lp.Add(g.Id, Metrics.MaxAbs(e.Lp, g.F("net_local_poses")));
            lrv3.Add(g.Id, Metrics.MaxAbsColumns(e.Lrv, g.F("net_local_root_values"), 4, 3, 4));
            lrv.Add(g.Id, Metrics.MaxAbsColumns(e.Lrv, g.F("net_local_root_values"), 4, 0, 3));
        }

        align.AtMost(1e-5);
        grv.AtMost(1e-5);
        lp.AtMost(1e-5);
        lrv3.AtMost(1e-5);
        lrv.AtMost(1e-4);
    }

    [Fact]
    public void Public_encode_matches_goldens_for_identity_space_requests()
    {
        var worst = new Worst("public Encode all tensors (cols 0-2 of local_root excluded)");
        var lrv = new Worst("public Encode local_root cols 0-2");
        foreach (var g in Goldens.All().Where(g => g.ModelToRuntimeKey == "identity"))
        {
            var (request, context) = PublicRequest.From(g);
            var inputs = Ok.Value(EngineEncoder.Encode(request, 0, context));
            worst.Add(g.Id, Metrics.MaxAbs(inputs.GlobalRootValues.Span, g.F("net_global_root_values")));
            worst.Add(g.Id, Metrics.MaxAbs(inputs.LocalPoses.Span, g.F("net_local_poses")));
            worst.Add(g.Id, Metrics.MaxAbsColumns(inputs.LocalRootValues.Span, g.F("net_local_root_values"), 4, 3, 4));
            lrv.Add(g.Id, Metrics.MaxAbsColumns(inputs.LocalRootValues.Span, g.F("net_local_root_values"), 4, 0, 3));
            Assert.Equal(g.I("net_has_global_root_values"), inputs.HasGlobalRootValues.Span.ToArray());
            Assert.Equal(g.I("net_has_local_root_values"), inputs.HasLocalRootValues.Span.ToArray());
            Assert.Equal(g.I("net_has_local_poses"), inputs.HasLocalPoses.Span.ToArray());
            Assert.Equal(g.I("net_allowed_pred_num_tokens"), inputs.AllowedPredNumTokens.Span.ToArray());
        }

        worst.AtMost(1e-5);
        lrv.AtMost(1e-4);
    }
}

internal static class PublicRequest
{
    public static (ValidRequest Request, Block<Pose> Context) From(Golden g)
    {
        var context = Split(g.F("context_local"), 4);
        var targets = Split(g.F("target_local"), g.TargetPoses);
        var min = g.AllowedTokens.Min();
        var max = g.AllowedTokens.Max();
        var contiguous = max - min + 1 == g.AllowedTokens.Length;
        Assert.True(contiguous, $"req{g.Id} allowed set is not a range");
        var config = new GenerationConfig(min, max, g.Correct, false, (float)g.HeadingX, (float)g.HeadingZ);
        var target = new TargetSpec(Blocks.Of(targets), g.TargetPoses, 0, 0, 0);
        var request = Ok.Value(Requests.Parse(new GenerationRequest(Blocks.Of(context), Blocks.Of(target), config)));
        return (request, Blocks.Of(context));
    }

    private static Pose[] Split(float[] values, int count) =>
        Enumerable.Range(0, count).Select(i => Ok.Value(Poses.Parse(values.AsSpan(i * EngineKernels.PoseFloats, EngineKernels.PoseFloats)))).ToArray();
}

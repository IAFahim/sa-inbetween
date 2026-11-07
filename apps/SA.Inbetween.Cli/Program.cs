using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SA.Inbetween.Contract;
using SA.Inbetween.Generators;
using SA.Inbetween.Onnx;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Cli;

public static class Program
{
    private const int PoseFloats = 539;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var options = Options.Parse(args);
        switch (args[0])
        {
            case "inspect":
                return Inspect(options);
            case "pose":
                return ExtractPose(options);
            case "generate":
                return await Generate(options).ConfigureAwait(false);
            default:
                return Fail($"unknown command '{args[0]}'");
        }
    }

    private const string Usage = """
        sa-inbetween: generate SOMA motion between poses with MotionBricks (ONNX, CPU)

          inspect <file.saib>
              Print frame count, model hash, recipe hash and contact summary.

          pose <file.saib> --frame N --out <file.pose>
              Write one frame as a raw pose (77 joints x 7 float32, little endian, 2156 bytes).

          generate --model <mmm_v6_soma_fp32.onnx> --context <file.saib|file.pose x4> --target <file.pose|file.saib:FRAME> ... --out <file.saib>
              --context-end N     last context frame inside a .saib (default: last frame)
              --target a[,b,c,d]  up to 4 poses per target; repeat the option for each chained target
              --tokens-min N      allowed tokens, 6..16 (default 8)
              --tokens-max N      allowed tokens, 6..16 (default 14)
              --override N        force a token count (0 = none)
              --no-correct        disable endpoint correction
              --skip-verify       do not check the model sha256 pin
        """;

    private static int Inspect(Options options)
    {
        if (options.Positional.Count != 1)
        {
            return Fail("inspect takes one file");
        }

        var bytes = File.ReadAllBytes(options.Positional[0]);
        var read = MotionFiles.Read(bytes);
        if (read.TryGetFault(out var fault))
        {
            return Fail($"cannot read motion file: {fault.Code} index {fault.Index} value {fault.Value}");
        }

        read.TryGetValue(out var file);
        var inv = CultureInfo.InvariantCulture;
        Console.Out.WriteLine($"frames        {file.Frames.Length}");
        Console.Out.WriteLine($"fps           {file.FramesPerSecond.ToString(inv)}");
        Console.Out.WriteLine($"axes / units  {file.Axes} / {file.Units}");
        Console.Out.WriteLine($"backend       {file.Recipe.Backend}, {file.Recipe.Precision}");
        Console.Out.WriteLine($"model sha256  {Sha256s.ToHex(file.Recipe.Model)}");
        Console.Out.WriteLine($"recipe hash   {Sha256s.ToHex(file.RecipeHash)}");
        var first = file.RootWorld.Span[0].Translation;
        var last = file.RootWorld.Span[file.RootWorld.Length - 1].Translation;
        var travel = MathF.Sqrt(((last.X - first.X) * (last.X - first.X)) + ((last.Z - first.Z) * (last.Z - first.Z)));
        Console.Out.WriteLine($"root travel   {travel.ToString("F3", inv)} m (ground plane)");
        var planted = new int[4];
        foreach (var c in file.Contacts.Span)
        {
            planted[0] += c.LeftHeel > 0.5f ? 1 : 0;
            planted[1] += c.LeftToe > 0.5f ? 1 : 0;
            planted[2] += c.RightHeel > 0.5f ? 1 : 0;
            planted[3] += c.RightToe > 0.5f ? 1 : 0;
        }

        Console.Out.WriteLine($"contact frames  LH {planted[0]}  LT {planted[1]}  RH {planted[2]}  RT {planted[3]}");
        return 0;
    }

    private static int ExtractPose(Options options)
    {
        if (options.Positional.Count != 1 || !options.TryInt("frame", 0, out var frame) || !options.Values.TryGetValue("out", out var outPath))
        {
            return Fail("usage: pose <file.saib> --frame N --out <file.pose>");
        }

        var loaded = LoadPose(options.Positional[0] + ":" + frame.ToString(CultureInfo.InvariantCulture));
        if (loaded.TryGetFault(out var fault))
        {
            return Fail($"cannot extract pose: {fault.Code} index {fault.Index} value {fault.Value}");
        }

        loaded.TryGetValue(out var pose);
        File.WriteAllBytes(outPath, PoseBytes(pose));
        return 0;
    }

    private static async Task<int> Generate(Options options)
    {
        if (!options.Values.TryGetValue("model", out var modelPath) || !options.Values.TryGetValue("out", out var outPath))
        {
            return Fail("generate needs --model and --out");
        }

        var contextArgs = options.Lists.GetValueOrDefault("context") ?? new List<string>();
        var targetArgs = options.Lists.GetValueOrDefault("target") ?? new List<string>();
        if (contextArgs.Count == 0 || targetArgs.Count == 0)
        {
            return Fail("generate needs --context and at least one --target");
        }

        if (!options.TryInt("tokens-min", 8, out var tokensMin) || !options.TryInt("tokens-max", 14, out var tokensMax) ||
            !options.TryInt("override", 0, out var tokenOverride) || !options.TryInt("context-end", -1, out var contextEnd))
        {
            return Fail("numeric option is not an integer");
        }

        var context = LoadContext(contextArgs, contextEnd);
        if (context.TryGetFault(out var contextFault))
        {
            return Fail($"bad context: {contextFault.Code} index {contextFault.Index} value {contextFault.Value}");
        }

        context.TryGetValue(out var contextPoses);
        var targets = new TargetSpec[targetArgs.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            var specs = targetArgs[i].Split(',');
            var poses = new Pose[specs.Length];
            for (var k = 0; k < specs.Length; k++)
            {
                var pose = LoadPose(specs[k]);
                if (pose.TryGetFault(out var poseFault))
                {
                    return Fail($"bad target {i} pose {k}: {poseFault.Code} index {poseFault.Index} value {poseFault.Value}");
                }

                pose.TryGetValue(out poses[k]);
            }

            targets[i] = new TargetSpec(Blocks.Of(poses), poses.Length, 0, tokenOverride, 0);
        }

        var config = new GenerationConfig(tokensMin, tokensMax, !options.Flags.Contains("no-correct"), false, 1f, 0f);
        var raw = new GenerationRequest(Blocks.Copy(contextPoses), Blocks.Of(targets), config);
        var parsed = Requests.Parse(raw);
        if (parsed.TryGetFault(out var requestFault))
        {
            return Fail($"invalid request: {requestFault.Code} index {requestFault.Index} value {requestFault.Value}");
        }

        parsed.TryGetValue(out var valid);

        var modelHash = options.Flags.Contains("skip-verify") ? default : HashFile(modelPath);
        if (!options.Flags.Contains("skip-verify") && Sha256s.ToHex(modelHash) != ModelPins.SomaFp32OnnxSha256)
        {
            return Fail($"model sha256 {Sha256s.ToHex(modelHash)} does not match the pin {ModelPins.SomaFp32OnnxSha256}; use --skip-verify to override");
        }

        var loadedNetwork = OnnxNetwork.Load(modelPath);
        if (loadedNetwork.TryGetFault(out var loadFault))
        {
            return Fail($"cannot load model: {loadFault.Code} value {loadFault.Value}");
        }

        loadedNetwork.TryGetValue(out var network);
        using (network)
        {
            var generator = new PortedGenerator(network);
            var generated = await generator.GenerateAsync(valid, CancellationToken.None).ConfigureAwait(false);
            if (generated.TryGetFault(out var generateFault))
            {
                return Fail($"generation failed: {generateFault.Code} index {generateFault.Index} value {generateFault.Value}");
            }

            generated.TryGetValue(out var results);
            var merged = Merge(results);
            if (merged.TryGetFault(out var mergeFault))
            {
                return Fail($"cannot merge results: {mergeFault.Code} index {mergeFault.Index} value {mergeFault.Value}");
            }

            merged.TryGetValue(out var whole);
            var recipe = new Recipe(Backend.Ported, Precision.Fp32, Sha256s.ParseHex(ModelPins.SomaFp32OnnxSha256).Unwrap(), raw);
            var file = MotionFiles.From(whole, recipe);
            var buffer = new byte[MotionFiles.SizeOf(file)];
            var written = MotionFiles.Write(file, buffer);
            if (written.TryGetFault(out var writeFault))
            {
                return Fail($"cannot write motion: {writeFault.Code} index {writeFault.Index} value {writeFault.Value}");
            }

            File.WriteAllBytes(outPath, buffer);
            Console.Out.WriteLine($"wrote {outPath}: {whole.Frames.Length} frames in {results.Length} segment(s)");
            return 0;
        }
    }

    private static Result<GenerationResult, Fault> Merge(Block<GenerationResult> results)
    {
        if (results.Length == 1)
        {
            return Result.Ok<GenerationResult, Fault>(results.Span[0]);
        }

        var frames = new List<Pose>();
        var contacts = new List<FootContacts>();
        var tokens = 0;
        foreach (var r in results.Span)
        {
            frames.AddRange(r.Frames.Span.ToArray());
            contacts.AddRange(r.Contacts.Span.ToArray());
            tokens += r.Timing.TokensChosen;
        }

        return GenerationResults.Parse(Blocks.Of(frames.ToArray()), Blocks.Of(contacts.ToArray()), new Timing(tokens, 0f, 0f));
    }

    private static Result<Pose[], Fault> LoadContext(List<string> args, int contextEnd)
    {
        if (args.Count == 1 && args[0].EndsWith(".saib", StringComparison.OrdinalIgnoreCase))
        {
            var read = MotionFiles.Read(File.ReadAllBytes(args[0]));
            if (read.TryGetFault(out var fault))
            {
                return Result.Error<Pose[], Fault>(fault);
            }

            read.TryGetValue(out var file);
            var end = contextEnd < 0 ? file.Frames.Length - 1 : contextEnd;
            if (end < 3 || end >= file.Frames.Length)
            {
                return Result.Error<Pose[], Fault>(Faults.Of(FaultCode.ContextPoseCount, 0, end));
            }

            return Result.Ok<Pose[], Fault>(new[] { file.Frames.Span[end - 3], file.Frames.Span[end - 2], file.Frames.Span[end - 1], file.Frames.Span[end] });
        }

        if (args.Count != 4)
        {
            return Result.Error<Pose[], Fault>(Faults.Of(FaultCode.ContextPoseCount, 0, args.Count));
        }

        var poses = new Pose[4];
        for (var i = 0; i < 4; i++)
        {
            var loaded = LoadPose(args[i]);
            if (loaded.TryGetFault(out var fault))
            {
                return Result.Error<Pose[], Fault>(fault);
            }

            loaded.TryGetValue(out poses[i]);
        }

        return Result.Ok<Pose[], Fault>(poses);
    }

    private static Result<Pose, Fault> LoadPose(string spec)
    {
        var colon = spec.LastIndexOf(':');
        var isFrameSpec = colon > 1 && int.TryParse(spec.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _);
        var path = isFrameSpec ? spec[..colon] : spec;
        var bytes = File.ReadAllBytes(path);
        if (path.EndsWith(".saib", StringComparison.OrdinalIgnoreCase))
        {
            var frame = isFrameSpec ? int.Parse(spec.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture) : 0;
            var read = MotionFiles.Read(bytes);
            if (read.TryGetFault(out var fault))
            {
                return Result.Error<Pose, Fault>(fault);
            }

            read.TryGetValue(out var file);
            return frame < file.Frames.Length
                ? Result.Ok<Pose, Fault>(file.Frames.Span[frame])
                : Result.Error<Pose, Fault>(Faults.Of(FaultCode.TargetPoseCount, 0, frame));
        }

        if (bytes.Length != PoseFloats * sizeof(float))
        {
            return Result.Error<Pose, Fault>(Faults.Of(FaultCode.PoseLength, 0, bytes.Length / sizeof(float)));
        }

        var values = new float[PoseFloats];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return Poses.Parse(values);
    }

    private static byte[] PoseBytes(Pose pose)
    {
        var bytes = new byte[PoseFloats * sizeof(float)];
        var values = pose.Values.Span.ToArray();
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static Sha256 HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Sha256s.FromBytes(sha.ComputeHash(stream)).Unwrap();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }
}

internal static class ResultExtensions
{
    public static T Unwrap<T>(this Result<T, Fault> result)
    {
        result.TryGetValue(out var value);
        return value;
    }
}

internal sealed class Options
{
    public List<string> Positional { get; } = new();

    public Dictionary<string, string> Values { get; } = new();

    public Dictionary<string, List<string>> Lists { get; } = new();

    public HashSet<string> Flags { get; } = new();

    private static readonly HashSet<string> FlagNames = new() { "no-correct", "skip-verify" };

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                o.Positional.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            if (FlagNames.Contains(name))
            {
                o.Flags.Add(name);
                continue;
            }

            var value = i + 1 < args.Length ? args[++i] : string.Empty;
            o.Values[name] = value;
            if (!o.Lists.TryGetValue(name, out var list))
            {
                list = new List<string>();
                o.Lists[name] = list;
            }

            list.Add(value);
        }

        return o;
    }

    public bool TryInt(string name, int fallback, out int value)
    {
        value = fallback;
        return !Values.TryGetValue(name, out var text) || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

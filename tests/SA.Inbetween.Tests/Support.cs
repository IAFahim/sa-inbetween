using System;
using System.IO;
using System.Linq;
using CsCheck;
using SA.Inbetween.Contract;
using SA.Inbetween.Contract.Wire;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

public static class Packets
{
    public const string Name = "Packet";
    public const string P02 = "P02";
    public const string P04 = "P04";
    public const string P06 = "P06";
    public const string P07 = "P07";
    public const string P09 = "P09";
    public const string P10 = "P10";
    public const string P11 = "P11";
    public const string P12 = "P12";
}

public static class Props
{
    public const long Iterations = 200;

    public static void Check<T>(Gen<T> gen, Action<T> assert) =>
        gen.Sample(assert, iter: Iterations, threads: 1);
}

public static class Ok
{
    public static T Value<T>(Result<T, Fault> result)
    {
        if (result.TryGetFault(out var fault))
        {
            Assert.Fail($"expected a value, got {fault}");
        }

        result.TryGetValue(out var value);
        return value;
    }

    public static Fault Fault<T>(Result<T, Fault> result)
    {
        Assert.False(result.IsOk, "expected a fault, got a value");
        result.TryGetFault(out var fault);
        return fault;
    }
}

public static class Near
{
    public static void Equal(float expected, float actual, float tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} +- {tolerance}, got {actual}");

    public static void Equal<TSpace>(Vector3<TSpace> expected, Vector3<TSpace> actual, float tolerance)
        where TSpace : struct, ISpace
    {
        Equal(expected.X, actual.X, tolerance);
        Equal(expected.Y, actual.Y, tolerance);
        Equal(expected.Z, actual.Z, tolerance);
    }

    public static void SameRotation<TSpace>(Quaternion<TSpace> expected, Quaternion<TSpace> actual, float tolerance)
        where TSpace : struct, ISpace
    {
        var dot = (expected.X * actual.X) + (expected.Y * actual.Y) + (expected.Z * actual.Z) + (expected.W * actual.W);
        Assert.True(Math.Abs(Math.Abs(dot) - 1f) <= tolerance, $"expected {expected}, got {actual}");
    }

    public static void Equal<TSpace>(Quaternion<TSpace> expected, Quaternion<TSpace> actual, float tolerance)
        where TSpace : struct, ISpace
    {
        Equal(expected.X, actual.X, tolerance);
        Equal(expected.Y, actual.Y, tolerance);
        Equal(expected.Z, actual.Z, tolerance);
        Equal(expected.W, actual.W, tolerance);
    }
}

public static class Fixtures
{
    public static float[] IdentityFloats(int joints)
    {
        var values = new float[joints * Poses.FloatsPerJoint];
        for (var j = 0; j < joints; j++)
        {
            values[(j * 7) + 6] = 1f;
        }

        return values;
    }

    public static Pose IdentityPose() => Ok.Value(Poses.Parse(IdentityFloats(SkeletonJoints.Count)));

    public static ModelPose IdentityModelPose() => Ok.Value(Poses.ParseModel(IdentityFloats(SomaJoints.Count)));

    public static Pose NumberedPose(float offset)
    {
        var values = IdentityFloats(SkeletonJoints.Count);
        for (var j = 0; j < SkeletonJoints.Count; j++)
        {
            values[j * 7] = offset + j;
            values[(j * 7) + 1] = -j;
            values[(j * 7) + 2] = j * 0.5f;
        }

        return Ok.Value(Poses.Parse(values));
    }

    public static TargetSpec Target(int poses = 1) =>
        new(Blocks.Of(Enumerable.Repeat(IdentityPose(), poses).ToArray()), poses, 0, 0, 0);

    public static GenerationRequest Request(int targets = 1) =>
        new(
            Blocks.Of(IdentityPose(), IdentityPose(), IdentityPose(), IdentityPose()),
            Blocks.Of(Enumerable.Repeat(Target(), targets).ToArray()),
            GenerationConfigs.Default);

    public static ValidRequest ValidRequest(int targets = 1) => Ok.Value(Requests.Parse(Request(targets)));

    public static Sha256 ModelHash() => Ok.Value(Sha256s.FromBytes(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));

    public static Recipe Recipe() => new(Backend.Sidecar, Precision.Fp32, ModelHash(), Request());

    public static GenerationResult Result(int frames = Limits.MinFrames) =>
        Ok.Value(GenerationResults.Parse(
            Blocks.Of(Enumerable.Range(0, frames).Select(i => NumberedPose(i)).ToArray()),
            Blocks.Of(Enumerable.Range(0, frames).Select(i => new FootContacts(0f, 0.25f, 0.5f, 1f)).ToArray()),
            new Timing(8, 1.5f, 2.5f)));
}

public static class Gens
{
    public static readonly Gen<float> Finite = Gen.Float[-1000f, 1000f];

    public static readonly Gen<float> Unit = Gen.Float[0f, 1f];

    public static Gen<Vector3<TSpace>> Vector<TSpace>()
        where TSpace : struct, ISpace =>
        Gen.Select(Finite, Finite, Finite, (x, y, z) => new Vector3<TSpace>(x, y, z));

    public static Gen<Quaternion<TSpace>> Rotation<TSpace>()
        where TSpace : struct, ISpace =>
        Gen.Select(Gen.Float[-1f, 1f], Gen.Float[-1f, 1f], Gen.Float[-1f, 1f], Gen.Float[-1f, 1f])
            .Where(q => (q.Item1 * q.Item1) + (q.Item2 * q.Item2) + (q.Item3 * q.Item3) + (q.Item4 * q.Item4) > 0.01f)
            .Select(q =>
            {
                var n = MathF.Sqrt((q.Item1 * q.Item1) + (q.Item2 * q.Item2) + (q.Item3 * q.Item3) + (q.Item4 * q.Item4));
                return new Quaternion<TSpace>(q.Item1 / n, q.Item2 / n, q.Item3 / n, q.Item4 / n);
            });

    public static Gen<float[]> PoseFloats(int joints) =>
        Gen.Select(Vector<SomaModel>(), Rotation<SomaModel>(), (v, q) => new[] { v.X, v.Y, v.Z, q.X, q.Y, q.Z, q.W })
            .Array[joints]
            .Select(parts => parts.SelectMany(p => p).ToArray());

    public static readonly Gen<Pose> Pose = PoseFloats(SkeletonJoints.Count).Select(v => Ok.Value(Poses.Parse(v)));

    public static readonly Gen<ModelPose> ModelPose = PoseFloats(SomaJoints.Count).Select(v => Ok.Value(Poses.ParseModel(v)));

    public static readonly Gen<RigidTransform<SomaModel>> Transform =
        Gen.Select(Vector<SomaModel>(), Rotation<SomaModel>(), (v, q) => new RigidTransform<SomaModel>(v, q));

    public static readonly Gen<FootContacts> Contacts =
        Gen.Select(Unit, Unit, Unit, Unit, (a, b, c, d) => new FootContacts(a, b, c, d));

    public static readonly Gen<TargetSpec> Target =
        Gen.Int[1, 4].SelectMany(n => Gen.Select(
            Pose.Array[n],
            Gen.Int[1, n],
            Gen.Int[0, Limits.MaxTransitionEarlyByFrames],
            Gen.OneOf(Gen.Const(0), Gen.Int[Limits.MinTokenCount, Limits.MaxTokenCount]),
            Gen.Int[0, Limits.IkPinMaskBits],
            (poses, use, early, tokens, mask) => new TargetSpec(Blocks.Of(poses), use, early, tokens, mask)));

    public static readonly Gen<GenerationConfig> Config =
        Gen.Int[Limits.MinTokenCount, Limits.MaxTokenCount].SelectMany(min => Gen.Select(
            Gen.Int[min, Limits.MaxTokenCount],
            Gen.Bool,
            Gen.Bool,
            Gen.Float[0.1f, 1f],
            Gen.Float[-1f, 1f],
            (max, correct, timing, x, z) => new GenerationConfig(min, max, correct, timing, x, z)));

    public static readonly Gen<GenerationRequest> ValidShapedRequest =
        Gen.Select(Pose.Array[Limits.ContextPoseCount], Target.Array[1, Limits.MaxTargets], Config,
            (context, targets, config) => new GenerationRequest(Blocks.Of(context), Blocks.Of(targets), config));

    public static readonly Gen<GenerationRequest> AnyRequest =
        Gen.Select(
            Gen.Int[0, 6].SelectMany(n => Gen.OneOf(Gen.Const(default(Pose)), Pose).Array[n]),
            Gen.Int[0, 6].SelectMany(n => Gen.Select(
                Gen.Int[0, 5].SelectMany(k => Gen.OneOf(Gen.Const(default(Pose)), Pose).Array[k]),
                Gen.Int[-2, 6], Gen.Int[-5, 70], Gen.Int[-3, 20], Gen.Int[-1, 70000],
                (p, u, e, t, m) => new TargetSpec(Blocks.Of(p), u, e, t, m)).Array[n]),
            Gen.Select(Gen.Int[-2, 20], Gen.Int[-2, 20], Gen.Bool, Gen.Bool, Gen.Float, Gen.Float,
                (a, b, c, d, x, z) => new GenerationConfig(a, b, c, d, x, z)),
            (context, targets, config) => new GenerationRequest(Blocks.Of(context), Blocks.Of(targets), config));

    public static readonly Gen<Recipe> Recipe =
        Gen.Select(
            Gen.Enum<Backend>(),
            Gen.Enum<Precision>(),
            Gen.Byte.Array[Sha256s.ByteCount].Select(b => Ok.Value(Sha256s.FromBytes(b))),
            ValidShapedRequest,
            (b, p, h, r) => new Recipe(b, p, h, r));

    public static readonly Gen<Hello> Hello =
        Gen.Select(Gen.UShort, Gen.Enum<Rig>(), Gen.Enum<WirePrecision>(), Gen.UInt,
            (v, r, p, f) => new Hello(v, r, p, f));

    public static readonly Gen<HelloAck> HelloAck =
        Gen.Select(Gen.UShort, Gen.Enum<Rig>(), Gen.Byte, Gen.Byte, Gen.Enum<WireSpace>(), Gen.Bool, Gen.Byte, Gen.Byte)
            .Select(t => new HelloAck(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8));

    public static readonly Gen<WireConfig> WireConfig =
        Gen.Select(Gen.Byte, Gen.Byte, Gen.Bool, Gen.Int[0, 1].Select(i => (byte)i), Finite, Finite,
            (a, b, c, f, x, z) => new WireConfig(a, b, c, f, x, z));

    public static readonly Gen<WireTarget> WireTarget =
        Gen.Select(Gen.Byte, Gen.UShort, Gen.Byte, Gen.UShort, ModelPose.Array[0, 4],
            (u, e, t, m, p) => new WireTarget(u, e, t, m, Blocks.Of(p)));

    public static readonly Gen<Generate> Generate =
        Gen.Select(WireConfig, ModelPose.Array[0, 5], WireTarget.Array[0, 4], Gen.Enum<ChainMode>(),
            (c, ctx, t, m) => new Generate(c, Blocks.Of(ctx), Blocks.Of(t), m));

    public static readonly Gen<WireFrame> WireFrame =
        Gen.Select(ModelPose, Transform, Contacts, (p, r, c) => new WireFrame(p, r, c));

    public static readonly Gen<GenerateResult> GenerateResult =
        Gen.Select(Gen.Byte, Gen.Bool, Gen.Byte, Finite, Finite, WireFrame.Array[0, 6],
            (s, l, t, g, a, f) => new GenerateResult(s, l, t, g, a, Blocks.Of(f)));

    public static readonly Gen<ErrorMessage> ErrorMessage =
        Gen.Select(Gen.Enum<WireErrorCode>(), Gen.Byte.Array[0, 64], (c, m) => new ErrorMessage(c, Blocks.Of(m)));

    public static readonly Gen<byte[]> Bytes = Gen.Byte.Array[0, 2048];
}

public static class Corpus
{
    public const string SkipReason =
        "testdata/eval-only/ is absent. It holds evaluation-only NVIDIA-derived data and is never committed; see testdata/eval-only/README.md.";

    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SA.Inbetween.sln")))
            {
                directory = directory.Parent;
            }

            return directory is null ? string.Empty : Path.Combine(directory.FullName, "testdata", "eval-only");
        }
    }

    public static string RequireManifest()
    {
        var manifest = Path.Combine(Root, "manifest.sha256");
        if (!File.Exists(manifest))
        {
            Assert.Skip(SkipReason);
        }

        return manifest;
    }
}

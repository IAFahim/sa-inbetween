using System;
using System.Linq;
using System.Text;
using CsCheck;
using SA.Inbetween.Contract;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

[Trait(Packets.Name, Packets.P02)]
public class RequestParseSpec
{
    [Fact]
    public void Limits_match_the_plan()
    {
        Assert.Equal((4, 1, 4, 1, 4), (Limits.ContextPoseCount, Limits.MinTargets, Limits.MaxTargets, Limits.MinTargetPoses, Limits.MaxTargetPoses));
        Assert.Equal((6, 16, 18, 58, 30), (Limits.MinTokenCount, Limits.MaxTokenCount, Limits.MinFrames, Limits.MaxFrames, Limits.FramesPerSecond));
        Assert.Equal((58, 0x0F), (Limits.MaxTransitionEarlyByFrames, Limits.IkPinMaskBits));
    }

    [Fact]
    public void Default_config_matches_L5() =>
        Assert.Equal(new GenerationConfig(8, 14, true, false, 1f, 0f), GenerationConfigs.Default);

    [Fact]
    public void Valid_request_parses_and_keeps_its_data()
    {
        var request = Fixtures.Request(4);
        Assert.Equal(request, Ok.Value(Requests.Parse(request)).Request);
    }

    [Fact]
    public void Every_valid_shape_parses() =>
        Props.Check(Gens.ValidShapedRequest, r => Assert.Equal(r, Ok.Value(Requests.Parse(r)).Request));

    [Fact]
    public void Parse_is_total_and_deterministic() =>
        Props.Check(Gens.AnyRequest, r => Assert.Equal(Requests.Parse(r), Requests.Parse(r)));

    public static TheoryData<string, Fault> InvalidShapes() => new()
    {
        { "token min 5", Faults.Of(FaultCode.AllowedTokenMinOutOfRange, 0, 5) },
        { "token min 17", Faults.Of(FaultCode.AllowedTokenMinOutOfRange, 0, 17) },
        { "token max below min", Faults.Of(FaultCode.AllowedTokenMaxOutOfRange, 0, 7) },
        { "token max 17", Faults.Of(FaultCode.AllowedTokenMaxOutOfRange, 0, 17) },
        { "heading zero", Faults.Of(FaultCode.HeadingAlignmentInvalid, 0, 0) },
        { "heading nan", Faults.Of(FaultCode.HeadingAlignmentInvalid, 0, 0) },
        { "context 3", Faults.Of(FaultCode.ContextPoseCount, 0, 3) },
        { "context 5", Faults.Of(FaultCode.ContextPoseCount, 0, 5) },
        { "context pose 2 default", Faults.Of(FaultCode.ContextPoseLength, 2, 0) },
        { "targets 0", Faults.Of(FaultCode.TargetCount, 0, 0) },
        { "targets 5", Faults.Of(FaultCode.TargetCount, 0, 5) },
        { "target 1 has 0 poses", Faults.Of(FaultCode.TargetPoseCount, 1, 0) },
        { "target 1 has 5 poses", Faults.Of(FaultCode.TargetPoseCount, 1, 5) },
        { "target 1 pose 0 default", Faults.Of(FaultCode.TargetPoseLength, 1, 0) },
        { "target 1 uses 0", Faults.Of(FaultCode.PosesToUseOutOfRange, 1, 0) },
        { "target 1 uses 2 of 1", Faults.Of(FaultCode.PosesToUseOutOfRange, 1, 2) },
        { "target 1 early -1", Faults.Of(FaultCode.TransitionEarlyOutOfRange, 1, -1) },
        { "target 1 early 59", Faults.Of(FaultCode.TransitionEarlyOutOfRange, 1, 59) },
        { "target 1 tokens 5", Faults.Of(FaultCode.TokenCountOverrideOutOfRange, 1, 5) },
        { "target 1 tokens 17", Faults.Of(FaultCode.TokenCountOverrideOutOfRange, 1, 17) },
        { "target 1 mask 16", Faults.Of(FaultCode.IkPinMaskInvalid, 1, 16) },
        { "config checked before context", Faults.Of(FaultCode.AllowedTokenMinOutOfRange, 0, 0) },
    };

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void Parse_rejects_each_invalid_shape_with_the_exact_fault(string shape, Fault expected) =>
        Assert.Equal(expected, Ok.Fault(Requests.Parse(Build(shape))));

    private static GenerationRequest Build(string shape)
    {
        var r = Fixtures.Request(2);
        var pose = Fixtures.IdentityPose();
        var t = Fixtures.Target();
        GenerationRequest WithTarget(TargetSpec target) => r with { Targets = Blocks.Of(Fixtures.Target(), target) };
        return shape switch
        {
            "token min 5" => r with { Config = r.Config with { AllowedTokenMin = 5 } },
            "token min 17" => r with { Config = r.Config with { AllowedTokenMin = 17, AllowedTokenMax = 17 } },
            "token max below min" => r with { Config = r.Config with { AllowedTokenMin = 8, AllowedTokenMax = 7 } },
            "token max 17" => r with { Config = r.Config with { AllowedTokenMax = 17 } },
            "heading zero" => r with { Config = r.Config with { HeadingAlignmentX = 0f, HeadingAlignmentZ = 0f } },
            "heading nan" => r with { Config = r.Config with { HeadingAlignmentX = float.NaN } },
            "context 3" => r with { Context = Blocks.Of(pose, pose, pose) },
            "context 5" => r with { Context = Blocks.Of(pose, pose, pose, pose, pose) },
            "context pose 2 default" => r with { Context = Blocks.Of(pose, pose, default, pose) },
            "targets 0" => r with { Targets = Blocks.Empty<TargetSpec>() },
            "targets 5" => r with { Targets = Blocks.Of(t, t, t, t, t) },
            "target 1 has 0 poses" => WithTarget(t with { Poses = Blocks.Empty<Pose>() }),
            "target 1 has 5 poses" => WithTarget(t with { Poses = Blocks.Of(pose, pose, pose, pose, pose) }),
            "target 1 pose 0 default" => WithTarget(t with { Poses = Blocks.Of(default(Pose)) }),
            "target 1 uses 0" => WithTarget(t with { PosesToUse = 0 }),
            "target 1 uses 2 of 1" => WithTarget(t with { PosesToUse = 2 }),
            "target 1 early -1" => WithTarget(t with { TransitionEarlyByFrames = -1 }),
            "target 1 early 59" => WithTarget(t with { TransitionEarlyByFrames = 59 }),
            "target 1 tokens 5" => WithTarget(t with { TokenCountOverride = 5 }),
            "target 1 tokens 17" => WithTarget(t with { TokenCountOverride = 17 }),
            "target 1 mask 16" => WithTarget(t with { IkPinMask = 16 }),
            "config checked before context" => new GenerationRequest(default, default, default),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}

[Trait(Packets.Name, Packets.P02)]
public class GenerationResultParseSpec
{
    [Fact]
    public void Valid_result_keeps_its_data()
    {
        var r = Fixtures.Result(Limits.MaxFrames);
        Assert.Equal(58, r.Frames.Length);
        Assert.Equal(58, r.Contacts.Length);
        Assert.Equal(new Timing(8, 1.5f, 2.5f), r.Timing);
        Assert.Equal(new FootContacts(0f, 0.25f, 0.5f, 1f), r.Contacts.Span[0]);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(59)]
    public void Frame_count_outside_18_to_58_is_rejected(int frames) =>
        Assert.Equal(
            Faults.Of(FaultCode.FrameCountOutOfRange, 0, frames),
            Ok.Fault(GenerationResults.Parse(Poses(frames), Contacts(frames, 0f), default)));

    [Fact]
    public void Contacts_must_match_frames_and_lie_in_unit_range()
    {
        Assert.Equal(Faults.Of(FaultCode.ContactCountMismatch, 0, 19), Ok.Fault(GenerationResults.Parse(Poses(18), Contacts(19, 0f), default)));
        var bad = Contacts(18, 0.5f).Span.ToArray();
        bad[4] = new FootContacts(0f, 0f, 1.01f, 0f);
        Assert.Equal(Faults.Of(FaultCode.ContactOutOfRange, 4, 2), Ok.Fault(GenerationResults.Parse(Poses(18), Blocks.Of(bad), default)));
        bad[4] = new FootContacts(float.NaN, 0f, 0f, 0f);
        Assert.Equal(Faults.Of(FaultCode.ContactOutOfRange, 4, 0), Ok.Fault(GenerationResults.Parse(Poses(18), Blocks.Of(bad), default)));
    }

    [Fact]
    public void Default_frame_is_rejected()
    {
        var frames = Poses(18).Span.ToArray();
        frames[7] = default;
        Assert.Equal(Faults.Of(FaultCode.FramePoseLength, 7, 0), Ok.Fault(GenerationResults.Parse(Blocks.Of(frames), Contacts(18, 0f), default)));
    }

    private static Block<Pose> Poses(int n) => Blocks.Of(Enumerable.Repeat(Fixtures.IdentityPose(), n).ToArray());

    private static Block<FootContacts> Contacts(int n, float v) => Blocks.Of(Enumerable.Repeat(new FootContacts(v, v, v, v), n).ToArray());
}

[Trait(Packets.Name, Packets.P04)]
public class Sha256Spec
{
    private const string AbcHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void FromBytes_requires_32_bytes()
    {
        Assert.Equal(32, Sha256s.ByteCount);
        Assert.Equal(32, Fixtures.ModelHash().Bytes.Length);
        Assert.Equal(Faults.Of(FaultCode.Sha256Invalid, 0, 31), Ok.Fault(Sha256s.FromBytes(new byte[31])));
    }

    [Fact]
    public void Of_hashes_known_vector() =>
        Assert.Equal(AbcHex, Sha256s.ToHex(Sha256s.Of(Encoding.ASCII.GetBytes("abc"))));

    [Fact]
    public void Hex_round_trip_is_lowercase() =>
        Props.Check(Gen.Byte.Array[32], bytes =>
        {
            var hash = Ok.Value(Sha256s.FromBytes(bytes));
            var hex = Sha256s.ToHex(hash);
            Assert.Equal(hex.ToLowerInvariant(), hex);
            Assert.Equal(hash, Ok.Value(Sha256s.ParseHex(hex)));
            Assert.Equal(hash, Ok.Value(Sha256s.ParseHex(hex.ToUpperInvariant())));
        });

    [Fact]
    public void ParseHex_rejects_bad_text()
    {
        Assert.Equal(Faults.Of(FaultCode.Sha256Invalid, 0, 3), Ok.Fault(Sha256s.ParseHex("abc")));
        Assert.Equal(Faults.Of(FaultCode.Sha256Invalid, 1, 5), Ok.Fault(Sha256s.ParseHex(AbcHex.Substring(0, 5) + "g" + AbcHex.Substring(6))));
    }

    [Fact]
    public void Model_pins_are_well_formed()
    {
        Assert.True(Sha256s.ParseHex(ModelPins.SomaFp32OnnxSha256).IsOk);
        Assert.True(Sha256s.ParseHex(ModelPins.SomaN1JsonSha256).IsOk);
        Assert.Equal(757864273L, ModelPins.SomaFp32OnnxBytes);
        Assert.Equal(40, ModelPins.MotionBricksCommit.Length);
    }
}

[Trait(Packets.Name, Packets.P04)]
public class RecipeSpec
{
    [Fact]
    public void Canonical_round_trip() =>
        Props.Check(Gens.Recipe, recipe =>
        {
            var buffer = new byte[Recipes.CanonicalSize(recipe)];
            Assert.Equal(buffer.Length, Ok.Value(Recipes.WriteCanonical(recipe, buffer)));
            Assert.Equal(recipe, Ok.Value(Recipes.ReadCanonical(buffer)));
        });

    [Fact]
    public void Canonical_size_of_minimal_recipe()
    {
        var expected = 2 + 32 + 12 + 1 + (4 * 539 * 4) + 1 + 7 + (539 * 4);
        Assert.Equal(expected, Recipes.CanonicalSize(Fixtures.Recipe()));
    }

    [Fact]
    public void Hash_is_sha256_of_canonical_bytes()
    {
        var recipe = Fixtures.Recipe();
        var buffer = new byte[Recipes.CanonicalSize(recipe)];
        Ok.Value(Recipes.WriteCanonical(recipe, buffer));
        Assert.Equal(Sha256s.Of(buffer), Recipes.Hash(recipe));
        Assert.Equal(32, Recipes.Hash(recipe).Bytes.Length);
    }

    [Fact]
    public void Hash_changes_with_backend_and_precision()
    {
        var r = Fixtures.Recipe();
        Assert.NotEqual(Recipes.Hash(r), Recipes.Hash(r with { Backend = Backend.Ported }));
        Assert.NotEqual(Recipes.Hash(r), Recipes.Hash(r with { Precision = Precision.Fp16Mixed }));
    }

    [Fact]
    public void WriteCanonical_reports_required_size_when_buffer_is_small()
    {
        var r = Fixtures.Recipe();
        Assert.Equal(Faults.Of(FaultCode.WireBufferTooSmall, 0, Recipes.CanonicalSize(r)), Ok.Fault(Recipes.WriteCanonical(r, new byte[10])));
    }

    [Fact]
    public void ReadCanonical_is_total() =>
        Props.Check(Gens.Bytes, bytes => Recipes.ReadCanonical(bytes));
}

[Trait(Packets.Name, Packets.P04)]
public class MotionFileSpec
{
    [Fact]
    public void Format_constants()
    {
        Assert.Equal(0x42494153u, MotionFiles.Magic);
        Assert.Equal(1, MotionFiles.Version);
        Assert.Equal(48, MotionFiles.HeaderSize);
    }

    [Fact]
    public void From_copies_frames_contacts_root_and_recipe()
    {
        var result = Fixtures.Result(20);
        var recipe = Fixtures.Recipe();
        var file = MotionFiles.From(result, recipe);
        Assert.Equal(30, file.FramesPerSecond);
        Assert.Equal(AxisConvention.SomaModelYUpRightHanded, file.Axes);
        Assert.Equal(LengthUnit.Meters, file.Units);
        Assert.Equal(result.Frames, file.Frames);
        Assert.Equal(result.Contacts, file.Contacts);
        Assert.Equal(recipe, file.Recipe);
        Assert.Equal(Recipes.Hash(recipe), file.RecipeHash);
        Assert.Equal(
            result.Frames.Span.ToArray().Select(f => Poses.Joint(f, SkeletonJoints.Root)),
            file.RootWorld.Span.ToArray());
    }

    [Fact]
    public void Round_trip() =>
        Props.Check(Gen.Select(Gens.Pose.Array[18, 20], Gens.Contacts, Gens.Recipe), t =>
        {
            var frames = Blocks.Of(t.Item1);
            var contacts = Blocks.Of(Enumerable.Repeat(t.Item2, t.Item1.Length).ToArray());
            var result = Ok.Value(GenerationResults.Parse(frames, contacts, default));
            var file = MotionFiles.From(result, t.Item3);
            var buffer = new byte[MotionFiles.SizeOf(file)];
            Assert.Equal(buffer.Length, Ok.Value(MotionFiles.Write(file, buffer)));
            Assert.Equal(file, Ok.Value(MotionFiles.Read(buffer)));
        });

    [Fact]
    public void Read_rejects_bad_headers()
    {
        var file = MotionFiles.From(Fixtures.Result(), Fixtures.Recipe());
        var buffer = new byte[MotionFiles.SizeOf(file)];
        Ok.Value(MotionFiles.Write(file, buffer));
        Assert.Equal(Faults.Of(FaultCode.MotionFileTruncated, 0, 10), Ok.Fault(MotionFiles.Read(buffer.AsSpan(0, 10))));
        Assert.Equal(Faults.Of(FaultCode.MotionFileTruncated, 0, buffer.Length - 1), Ok.Fault(MotionFiles.Read(buffer.AsSpan(0, buffer.Length - 1))));
        var badMagic = (byte[])buffer.Clone();
        badMagic[0] = 0;
        Assert.Equal(Faults.Of(FaultCode.MotionFileBadMagic, 0, 0x42494100), Ok.Fault(MotionFiles.Read(badMagic)));
        var badVersion = (byte[])buffer.Clone();
        badVersion[4] = 2;
        Assert.Equal(Faults.Of(FaultCode.MotionFileUnsupportedVersion, 4, 2), Ok.Fault(MotionFiles.Read(badVersion)));
        var badHash = (byte[])buffer.Clone();
        badHash[16] ^= 1;
        Assert.Equal(Faults.Of(FaultCode.RecipeHashMismatch, 16, 0), Ok.Fault(MotionFiles.Read(badHash)));
    }

    [Fact]
    public void Write_is_deterministic_and_little_endian()
    {
        var file = MotionFiles.From(Fixtures.Result(), Fixtures.Recipe());
        var a = new byte[MotionFiles.SizeOf(file)];
        var b = new byte[MotionFiles.SizeOf(file)];
        Ok.Value(MotionFiles.Write(file, a));
        Ok.Value(MotionFiles.Write(file, b));
        Assert.Equal(a, b);
        Assert.Equal(new byte[] { 0x53, 0x41, 0x49, 0x42, 1, 0, 0, 0, 30, 0, 18, 0, 77, 0, 0, 0 }, a.Take(16).ToArray());
        Assert.Equal(Faults.Of(FaultCode.WireBufferTooSmall, 0, a.Length), Ok.Fault(MotionFiles.Write(file, new byte[a.Length - 1])));
    }

    [Fact]
    public void Read_is_total() =>
        Props.Check(Gens.Bytes, bytes => MotionFiles.Read(bytes));
}

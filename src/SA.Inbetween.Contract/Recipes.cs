using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public enum Backend
{
    Sidecar = 0,
    Ported = 1,
}

public enum Precision
{
    Fp32 = 0,
    Fp16Mixed = 1,
}

public readonly record struct Sha256
{
    internal Sha256(Block<byte> bytes)
    {
        Bytes = bytes;
    }

    public Block<byte> Bytes { get; }
}

public readonly record struct Recipe(Backend Backend, Precision Precision, Sha256 Model, GenerationRequest Inputs);

public static class ModelPins
{
    public const string SomaFp32OnnxSha256 = "856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb";
    public const long SomaFp32OnnxBytes = 757864273;
    public const string SomaN1JsonSha256 = "ed41ab5bf481383fa46bdf0322b0c1b96426973e5038b731bc31276121051aaa";
    public const string MotionBricksCommit = "54fc563aeaaf819a753d72cc9fdc6713989cdd43";
}

public static class Sha256s
{
    public const int ByteCount = 32;

    public static Result<Sha256, Fault> FromBytes(System.ReadOnlySpan<byte> bytes) =>
        bytes.Length == ByteCount
            ? Result.Ok<Sha256, Fault>(new Sha256(Blocks.Copy(bytes)))
            : Result.Error<Sha256, Fault>(Faults.Of(FaultCode.Sha256Invalid, 0, bytes.Length));

    public static Result<Sha256, Fault> ParseHex(string text) => Result.Error<Sha256, Fault>(Faults.NotImplemented);

    public static string ToHex(Sha256 hash) => string.Empty;

    public static Sha256 Of(System.ReadOnlySpan<byte> data) => default;
}

public static class Recipes
{
    public static int CanonicalSize(Recipe recipe) => 0;

    public static Result<int, Fault> WriteCanonical(Recipe recipe, System.Span<byte> destination) =>
        Result.Error<int, Fault>(Faults.NotImplemented);

    public static Result<Recipe, Fault> ReadCanonical(System.ReadOnlySpan<byte> source) =>
        Result.Error<Recipe, Fault>(Faults.NotImplemented);

    public static Sha256 Hash(Recipe recipe) => default;
}

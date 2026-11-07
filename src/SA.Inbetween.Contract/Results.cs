using SA.Inbetween.Soma;

namespace SA.Inbetween.Contract;

public readonly record struct FootContacts(float LeftHeel, float LeftToe, float RightHeel, float RightToe);

public readonly record struct Timing(int TokensChosen, float GenerationMilliseconds, float TotalMilliseconds);

public readonly record struct GenerationResult
{
    internal GenerationResult(Block<Pose> frames, Block<FootContacts> contacts, Timing timing)
    {
        Frames = frames;
        Contacts = contacts;
        Timing = timing;
    }

    public Block<Pose> Frames { get; }

    public Block<FootContacts> Contacts { get; }

    public Timing Timing { get; }
}

public static class GenerationResults
{
    public static Result<GenerationResult, Fault> Parse(Block<Pose> frames, Block<FootContacts> contacts, Timing timing)
    {
        if (frames.Length < Limits.MinFrames || frames.Length > Limits.MaxFrames)
        {
            return Fail(FaultCode.FrameCountOutOfRange, 0, frames.Length);
        }

        var poses = frames.Span;
        for (var i = 0; i < poses.Length; i++)
        {
            if (poses[i].Values.Length != Poses.SkeletonFloatCount)
            {
                return Fail(FaultCode.FramePoseLength, i, poses[i].Values.Length);
            }
        }

        if (contacts.Length != frames.Length)
        {
            return Fail(FaultCode.ContactCountMismatch, 0, contacts.Length);
        }

        var values = contacts.Span;
        for (var i = 0; i < values.Length; i++)
        {
            var c = values[i];
            var slot = !InUnitRange(c.LeftHeel) ? 0
                : !InUnitRange(c.LeftToe) ? 1
                : !InUnitRange(c.RightHeel) ? 2
                : !InUnitRange(c.RightToe) ? 3
                : -1;
            if (slot >= 0)
            {
                return Fail(FaultCode.ContactOutOfRange, i, slot);
            }
        }

        return Result.Ok<GenerationResult, Fault>(new GenerationResult(frames, contacts, timing));
    }

    private static bool InUnitRange(float value) => value >= 0f && value <= 1f;

    private static Result<GenerationResult, Fault> Fail(FaultCode code, int index, int value) =>
        Result.Error<GenerationResult, Fault>(Faults.Of(code, index, value));
}

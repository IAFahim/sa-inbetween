using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class SourceStep : GenStep<SourceClip>
    {
        public override void Plan(GenFrame frame, SourceClip clip, GenPlan plan)
        {
            var payload = clip.Payload;
            if (!clip.HasSource)
                plan.Fault(frame, payload.Kind == SourceKind.UnityClip ? "choose the Unity clip it plays (an FBX take or .anim) in the Inspector."
                    : payload.Kind == SourceKind.SaClip ? "choose the .saclip it plays in the Inspector."
                    : "choose the pose in the Inspector.");
            else if (payload.Kind == SourceKind.UnityClip && payload.Motion.legacy)
                plan.Fault(frame, "'" + payload.Motion.name + "' is a Legacy clip. Select its FBX, set Rig > Animation Type to Humanoid (or Generic) and press Apply.");
            else if (payload.Kind == SourceKind.Pose && !payload.Pose.Valid)
                plan.Fault(frame, "the pose '" + payload.Pose.name + "' cannot be read: " + payload.Pose.Problem);
            plan.Sources.Add(new GenSource { Frame = frame, Asset = clip, Length = clip.Length, Loops = clip.Loops, Rate = clip.FrameRate, Name = clip.SourceName });
        }
    }
}

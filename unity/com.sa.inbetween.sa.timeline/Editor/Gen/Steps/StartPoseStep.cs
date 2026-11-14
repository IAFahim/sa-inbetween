using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class StartPoseStep : GenStep<StartPoseClip>
    {
        public override void Plan(GenFrame frame, StartPoseClip clip, GenPlan plan) =>
            plan.Poses.Add(new GenPose { Frame = frame, IsStart = true, Payload = clip.Payload });
    }
}

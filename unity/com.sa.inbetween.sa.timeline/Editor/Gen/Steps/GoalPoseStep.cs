using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class GoalPoseStep : GenStep<GoalPoseClip>
    {
        public override void Plan(GenFrame frame, GoalPoseClip clip, GenPlan plan) =>
            plan.Poses.Add(new GenPose { Frame = frame, IsStart = false, Payload = clip.Payload });
    }
}

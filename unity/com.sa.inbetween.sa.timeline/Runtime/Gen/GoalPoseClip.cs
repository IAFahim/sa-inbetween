using System.ComponentModel;

namespace SA.Inbetween.Timeline.Gen
{
    [DisplayName("Goal Pose")]
    public sealed class GoalPoseClip : GenClip<PosePayload>
    {
        public const double DefaultSeconds = 0.2;

        public override int Stage => GenStage.Pose;

        public override string DefaultName => "Goal Pose";

        public override double duration => DefaultSeconds;
    }
}

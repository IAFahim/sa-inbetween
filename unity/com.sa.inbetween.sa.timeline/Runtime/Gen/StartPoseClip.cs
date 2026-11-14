using System.ComponentModel;

namespace SA.Inbetween.Timeline.Gen
{
    [DisplayName("Start Pose")]
    public sealed class StartPoseClip : GenClip<PosePayload>
    {
        public const double DefaultSeconds = 0.2;

        public override int Stage => GenStage.Pose;

        public override string DefaultName => "Start Pose";

        public override double duration => DefaultSeconds;
    }
}

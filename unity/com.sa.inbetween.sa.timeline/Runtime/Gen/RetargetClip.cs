using System;
using System.ComponentModel;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    public enum GenRig
    {
        Auto,
        Humanoid,
        Generic,
    }

    [Serializable]
    public struct RetargetPayload
    {
        [Tooltip("The model or prefab the motion is baked onto. Empty: the character bound to this track.")]
        public GameObject Character;

        [Tooltip("Which clip goes on the timeline. Auto: the Humanoid clip for a Humanoid avatar, else the Generic one.")]
        public GenRig Rig;

        [Tooltip("Foot IK on the placed clip. Off by default: a generated clip has no IK goal curves, so foot IK would pull the feet to empty goals.")]
        public bool FootIK;
    }

    [DisplayName("Retarget / Bake")]
    public sealed class RetargetClip : GenClip<RetargetPayload>
    {
        public const double DefaultSeconds = 2.0;

        public override int Stage => GenStage.Retarget;

        public override string DefaultName => "Retarget / Bake";

        public override double duration => DefaultSeconds;
    }
}

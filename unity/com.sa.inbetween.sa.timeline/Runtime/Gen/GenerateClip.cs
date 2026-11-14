using System;
using System.ComponentModel;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    public enum GenVariant
    {
        AsSet,
        Shorter,
        Longer,
        OtherLanding,
    }

    [Serializable]
    public struct GeneratePayload
    {
        [Tooltip("Keep the end pose exactly on the goal. Off gives a looser landing.")]
        public bool LandExactly;

        [Tooltip("The version that is exported and placed on the timeline.")]
        public GenVariant Keep;

        [Range(1, 4), Tooltip("How many versions to make: the kept one, then the others in this order: As Set, Shorter, Longer, Other Landing. The others stay next to the kept one as alternates, so switching Keep to one of them needs no new generation.")]
        public int Variations;
    }

    [DisplayName("Generate")]
    public sealed class GenerateClip : GenClip<GeneratePayload>
    {
        public const double DefaultSeconds = 1.0;

        public override int Stage => GenStage.Generate;

        public override string DefaultName => "Generate";

        protected override GeneratePayload DefaultPayload => new GeneratePayload { LandExactly = true, Keep = GenVariant.AsSet, Variations = 1 };

        public override double duration => DefaultSeconds;
    }
}

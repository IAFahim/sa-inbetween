using System;
using System.ComponentModel;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    [Serializable]
    public struct LabelPayload
    {
        [Tooltip("An asset label put on every file the Generate clips under this clip write. Find them with l:<label> in the Project window search.")]
        public string Label;
    }

    [DisplayName("Label")]
    public sealed class LabelClip : GenClip<LabelPayload>
    {
        public const double DefaultSeconds = 2.0;

        public override int Stage => GenStage.Label;

        public override string DefaultName => "Label";

        protected override LabelPayload DefaultPayload => new LabelPayload { Label = "Inbetween" };

        public override double duration => DefaultSeconds;
    }
}

using System;
using System.ComponentModel;
using UnityEngine;

namespace SA.Inbetween.Timeline.Gen
{
    [Serializable]
    public struct ExportPayload
    {
        [Tooltip("Write <name>.anim, a Generic clip for the character. The .saclip is always written.")]
        public bool Anim;

        [Tooltip("Write <name>_Humanoid.anim with muscle curves. Humanoid characters only.")]
        public bool HumanoidAnim;

        [Tooltip("Write <name>.fbx, the character with the clip as a take. Needs the FBX Exporter package.")]
        public bool Fbx;

        [Tooltip("Put the result on the timeline's Generated track, at the Generate clip's time.")]
        public bool PlaceOnTimeline;

        [Tooltip("Also put the Unity clips it starts from and lands on around it on the Generated track, with the hips matched at each seam, so the Generated track plays from start to end.")]
        public bool PlaceSources;

        public static ExportPayload Default => new ExportPayload { Anim = true, HumanoidAnim = true, Fbx = false, PlaceOnTimeline = true, PlaceSources = true };
    }

    [DisplayName("Export")]
    public sealed class ExportClip : GenClip<ExportPayload>
    {
        public const double DefaultSeconds = 2.0;

        public override int Stage => GenStage.Export;

        public override string DefaultName => "Export";

        protected override ExportPayload DefaultPayload => ExportPayload.Default;

        public override double duration => DefaultSeconds;
    }
}

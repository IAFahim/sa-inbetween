using System;
using System.ComponentModel;
using SA.Inbetween.Clips;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Gen
{
    [Serializable]
    public struct SourcePayload
    {
        [Tooltip("What the character does here: a Unity clip (an FBX take or .anim), a .saclip, or one pose.")]
        public SourceKind Kind;

        [Tooltip("The Unity clip. Used when Kind is Unity Clip.")]
        public AnimationClip Motion;

        [Tooltip("The .saclip. Used when Kind is Sa Clip.")]
        public SaClipAsset Clip;

        [Tooltip("The pose. Used when Kind is Pose.")]
        public SaPoseAsset Pose;

        [Tooltip("The character the Unity clip plays on. Empty: the Retarget / Bake character, else the character bound to the track.")]
        public GameObject Character;
    }

    [DisplayName("Source")]
    public sealed class SourceClip : GenClip<SourcePayload>
    {
        public const double PoseSeconds = 0.5;
        public const double UnknownSeconds = 1.0;

        byte[] measured;
        double measuredLength;

        public override int Stage => GenStage.Source;

        public override string DefaultName => "Source";

        public override ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        protected override SourcePayload DefaultPayload => new SourcePayload { Kind = SourceKind.UnityClip };

        public override double duration => Length > 0 ? Length : Payload.Kind == SourceKind.Pose ? PoseSeconds : UnknownSeconds;

        public bool Loops => Payload.Kind == SourceKind.UnityClip && Payload.Motion != null && Payload.Motion.isLooping;

        public float FrameRate => Payload.Kind == SourceKind.UnityClip && Payload.Motion != null && Payload.Motion.frameRate > 0f ? Payload.Motion.frameRate : ClipFrame.ClipFrameRate;

        public bool HasSource => Payload.Kind == SourceKind.UnityClip ? Payload.Motion != null : Payload.Kind == SourceKind.Pose ? Payload.Pose != null : Payload.Clip != null;

        public string SourceName => Payload.Kind == SourceKind.UnityClip ? NameOf(Payload.Motion) : Payload.Kind == SourceKind.Pose ? NameOf(Payload.Pose) : NameOf(Payload.Clip);

        public double Length
        {
            get
            {
                if (Payload.Kind == SourceKind.UnityClip) return Payload.Motion != null ? Payload.Motion.length : 0;
                if (Payload.Kind == SourceKind.Pose) return 0;
                var bytes = Payload.Clip != null ? Payload.Clip.Bytes : null;
                if (bytes == null || bytes.Length == 0) return 0;
                if (!ReferenceEquals(bytes, measured))
                {
                    measured = bytes;
                    measuredLength = SaClipPose.TryLoad(bytes, out var pose, out _) ? pose.Duration : 0;
                }

                return measuredLength;
            }
        }

        static string NameOf(UnityEngine.Object asset) => asset != null ? asset.name : "";
    }
}

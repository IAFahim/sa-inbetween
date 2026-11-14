using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Gen
{
    public static class GenStage
    {
        public const int Source = 0;
        public const int Pose = 10;
        public const int Generate = 20;
        public const int Retarget = 30;
        public const int Export = 40;
        public const int Label = 50;
    }

    public abstract class GenClip : PlayableAsset, ITimelineClipAsset
    {
        public abstract int Stage { get; }

        public abstract string DefaultName { get; }

        public virtual ClipCaps clipCaps => ClipCaps.None;

        public abstract void ResetPayload();

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
    }

    public abstract class GenClip<TPayload> : GenClip where TPayload : struct
    {
        public TPayload Payload;

        protected virtual TPayload DefaultPayload => default;

        public override void ResetPayload() => Payload = DefaultPayload;
    }
}

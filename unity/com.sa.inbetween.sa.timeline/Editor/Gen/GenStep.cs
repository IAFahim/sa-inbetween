using System;
using SA.Inbetween.Timeline.Gen;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public readonly struct GenFrame
    {
        public readonly TimelineClip Clip;
        public readonly GenTrack Track;
        public readonly int TrackIndex;
        public readonly int Order;
        public readonly GenSpan Span;

        public GenFrame(TimelineClip clip, GenTrack track, int trackIndex, int order)
        {
            Clip = clip;
            Track = track;
            TrackIndex = trackIndex;
            Order = order;
            Span = GenRules.Window(clip.start, clip.end);
        }

        public GenClip Asset => Clip.asset as GenClip;

        public double StartSeconds => Clip.start;

        public double EndSeconds => Clip.end;

        public string Where => "'" + Clip.displayName + "' (track '" + (Track != null ? Track.name : "") + "', " + GenRules.Text(Clip.start) + " s)";
    }

    public interface IGenStep
    {
        Type ClipType { get; }

        void Plan(GenFrame frame, GenPlan plan);

        bool Finish(GenFrame frame, GenBakeContext context, out string error);
    }

    public abstract class GenStep<TClip> : IGenStep where TClip : GenClip
    {
        public Type ClipType => typeof(TClip);

        public virtual void Plan(GenFrame frame, TClip clip, GenPlan plan)
        {
        }

        public virtual bool Finish(GenFrame frame, TClip clip, GenBakeContext context, out string error)
        {
            error = "";
            return true;
        }

        void IGenStep.Plan(GenFrame frame, GenPlan plan) => Plan(frame, (TClip)frame.Clip.asset, plan);

        bool IGenStep.Finish(GenFrame frame, GenBakeContext context, out string error) => Finish(frame, (TClip)frame.Clip.asset, context, out error);
    }
}

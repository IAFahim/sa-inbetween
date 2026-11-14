using System;
using System.Globalization;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenPoses
    {
        public static ClipFrame FromSource(GenSource source, GenEdge edge, bool isStart, double at)
        {
            var payload = source.Payload;
            if (payload.Kind == SourceKind.Pose)
                return new ClipFrame { Kind = SourceKind.Pose, Pose = payload.Pose, Where = isStart ? FrameWhere.End : FrameWhere.Start };
            var endEdge = edge == GenEdge.EndedBefore || (edge == GenEdge.Wrapped && isStart);
            var startEdge = edge == GenEdge.FirstTick || edge == GenEdge.StartsAfter || (edge == GenEdge.Wrapped && !isStart);
            var local = endEdge
                ? BridgePlan.LocalEnd(source.ClipIn, source.Duration, source.TimeScale, source.Length, source.Loops)
                : startEdge
                    ? BridgePlan.LocalStart(source.ClipIn, source.Length, source.Loops)
                    : GenRules.LocalSeconds(source.Start, source.ClipIn, source.TimeScale, source.Length, source.Loops, at);
            var anchor = BridgePlan.Anchor(local, source.Length, source.Rate);
            var where = endEdge
                ? anchor == BridgeAnchor.End ? FrameWhere.End : FrameWhere.Time
                : startEdge
                    ? anchor == BridgeAnchor.Start ? FrameWhere.Start : FrameWhere.Time
                    : anchor == BridgeAnchor.Start ? FrameWhere.Start : anchor == BridgeAnchor.End ? FrameWhere.End : FrameWhere.Time;
            if (payload.Kind == SourceKind.UnityClip)
                return TimelineBridge.UnityFrame(payload.Motion, payload.Character != null ? TimelineBridge.CharacterAsset(payload.Character) : null, where, local);
            return new ClipFrame { Kind = SourceKind.SaClip, Clip = payload.Clip, Where = where, Seconds = (float)local, Frame = (int)Math.Round(local * ClipFrame.ClipFrameRate) };
        }

        public static bool Override(GenPlan plan, GenPose pose, GenPick pick, bool isStart, double at, out ClipFrame frame, out GenSource source, out string problem)
        {
            frame = default;
            source = pick.Found ? plan.Sources[pick.Index] : null;
            problem = "";
            switch (pose.Payload.From)
            {
                case GenPoseFrom.PoseAsset:
                    source = null;
                    if (pose.Payload.Pose == null) problem = "pick the pose asset, or set From to Source.";
                    else if (!pose.Payload.Pose.Valid) problem = "the pose '" + pose.Payload.Pose.name + "' cannot be read: " + pose.Payload.Pose.Problem;
                    else frame = new ClipFrame { Kind = SourceKind.Pose, Pose = pose.Payload.Pose, Where = isStart ? FrameWhere.End : FrameWhere.Start };
                    return problem == "";
                case GenPoseFrom.SourceFrame:
                    if (source == null)
                    {
                        problem = "there is no Source clip at " + GenRules.Text(at) + " s to take frame " + pose.Payload.Frame + " from. Set From to Pose Asset, or put a Source clip there.";
                        return false;
                    }

                    if (source.Payload.Kind == SourceKind.Pose)
                    {
                        problem = "the Source clip here is a single pose, so it has no frame " + pose.Payload.Frame + ". Set From to Source.";
                        return false;
                    }

                    var seconds = pose.Payload.Frame / (double)source.Rate;
                    frame = source.Payload.Kind == SourceKind.UnityClip
                        ? TimelineBridge.UnityFrame(source.Payload.Motion, source.Payload.Character != null ? TimelineBridge.CharacterAsset(source.Payload.Character) : null, FrameWhere.Frame, seconds)
                        : new ClipFrame { Kind = SourceKind.SaClip, Clip = source.Payload.Clip, Where = FrameWhere.Frame, Seconds = (float)seconds, Frame = pose.Payload.Frame };
                    frame.Frame = pose.Payload.Frame;
                    return true;
                default:
                    if (source == null)
                    {
                        problem = "there is no Source clip at " + GenRules.Text(at) + " s to take the pose from. Put a Source clip there, or set From to Pose Asset.";
                        return false;
                    }

                    frame = FromSource(source, pick.Edge, isStart, at);
                    return true;
            }
        }

        public static string Describe(ClipFrame frame)
        {
            var name = frame.Kind == SourceKind.UnityClip ? NameOf(frame.Motion) : frame.Kind == SourceKind.SaClip ? NameOf(frame.Clip) : NameOf(frame.Pose);
            switch (frame.Kind == SourceKind.Pose ? FrameWhere.Start : frame.Where)
            {
                case FrameWhere.End: return "'" + name + "' at its end";
                case FrameWhere.Start: return frame.Kind == SourceKind.Pose ? "pose '" + name + "'" : "'" + name + "' at its first frame";
                case FrameWhere.Frame: return "'" + name + "' at frame " + frame.Frame.ToString(CultureInfo.InvariantCulture);
                default: return "'" + name + "' at " + frame.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s (frame " + frame.Frame.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        static string NameOf(UnityEngine.Object asset) => asset != null ? asset.name : "nothing";
    }
}

using SA.Inbetween.Timeline.Editor;
using Tl.Unity;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.TlGlue
{
    public sealed class TlTimelineEnd : IGenTimelineEnd
    {
        public bool TryRead(TimelineAsset timeline, out GenEnd end, out TimelineAsset next)
        {
            end = GenEnd.Stop;
            next = null;
            var settings = Settings(timeline);
            if (settings == null) return false;
            end = settings.End == TlEnd.Loop ? GenEnd.Loop : settings.End == TlEnd.Next ? GenEnd.Next : GenEnd.Stop;
            next = end == GenEnd.Next ? settings.Next : null;
            return true;
        }

        public static TlTimelineSettings Settings(TimelineAsset timeline)
        {
            if (timeline == null || timeline.markerTrack == null) return null;
            foreach (var marker in timeline.markerTrack.GetMarkers())
                if (marker is TlTimelineSettings settings)
                    return settings;
            return null;
        }
    }
}

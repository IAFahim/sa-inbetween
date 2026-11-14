using System.Linq;
using SA.Inbetween.Timeline.Gen;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    [CustomTimelineEditor(typeof(GenClip))]
    public sealed class GenClipEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            var options = base.GetClipOptions(clip);
            var timeline = clip.GetParentTrack() != null ? clip.GetParentTrack().timelineAsset : null;
            var plan = GenPlanCache.For(timeline, GenPlanCache.DirectorOf(timeline));
            var fault = plan != null ? plan.FaultsFor(clip).FirstOrDefault() : null;
            if (!string.IsNullOrEmpty(fault)) options.errorText = fault;
            var job = plan?.Jobs.FirstOrDefault(j => j.Frame.Clip == clip);
            if (job != null && string.IsNullOrEmpty(fault))
                options.tooltip = job.Name + ": from " + job.FromText + " to " + string.Join(", then ", job.GoalTexts) + ", " + GenRules.Text(job.WindowSeconds) + " s.";
            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip.asset is SourceClip source && clip.displayName == source.DefaultName && source.SourceName != "")
            {
                clip.displayName = source.SourceName;
                clip.duration = source.duration;
            }

            GenPlanCache.Forget();
        }
    }
}

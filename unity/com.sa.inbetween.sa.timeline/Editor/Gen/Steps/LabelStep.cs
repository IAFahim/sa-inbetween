using System.Linq;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class LabelStep : GenStep<LabelClip>
    {
        public override void Plan(GenFrame frame, LabelClip clip, GenPlan plan)
        {
            var label = (clip.Payload.Label ?? "").Trim();
            if (label == "")
            {
                plan.Fault(frame, "type the label to put on the files in the Inspector.");
                return;
            }

            var jobs = plan.JobsOverlapping(frame.Span);
            if (jobs.Count == 0) plan.Fault(frame, "it covers no Generate clip, so it is not used. Stretch it over the Generate clips it is for.");
            foreach (var job in jobs)
                if (!job.Labels.Contains(label))
                    job.Labels.Add(label);
        }

        public override bool Finish(GenFrame frame, LabelClip clip, GenBakeContext context, out string error)
        {
            error = "";
            var label = clip.Payload.Label.Trim();
            foreach (var path in context.Files)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) continue;
                var labels = AssetDatabase.GetLabels(asset);
                if (labels.Contains(label)) continue;
                AssetDatabase.SetLabels(asset, labels.Concat(new[] { label }).ToArray());
            }

            return true;
        }
    }
}

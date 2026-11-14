using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class ExportStep : GenStep<ExportClip>
    {
        public override void Plan(GenFrame frame, ExportClip clip, GenPlan plan)
        {
            var jobs = plan.JobsOverlapping(frame.Span);
            if (jobs.Count == 0)
            {
                plan.Fault(frame, "it covers no Generate clip, so it is not used. Stretch it over the Generate clips it is for.");
                return;
            }

            foreach (var job in jobs)
            {
                if (job.HasExport)
                {
                    plan.Fault(frame, "it and another Export clip both cover the Generate clip " + job.Frame.Where + ". Keep one.");
                    continue;
                }

                job.HasExport = true;
                job.Export = clip.Payload;
            }
        }

        public override bool Finish(GenFrame frame, ExportClip clip, GenBakeContext context, out string error) =>
            GenOutputs.Export(context, clip.Payload, out error);
    }
}

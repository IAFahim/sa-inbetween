using System;
using System.Linq;
using SA.Inbetween.Timeline.Gen;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class GenerateStep : GenStep<GenerateClip>
    {
        public override void Plan(GenFrame frame, GenerateClip clip, GenPlan plan)
        {
            var job = new GenJob { Frame = frame, Payload = clip.Payload };
            var spans = plan.SourceSpans();
            var ok = Start(frame, plan, spans, job);
            ok &= Goals(frame, plan, spans, job);
            job.Moves = Math.Max(1, job.Goals.Count);
            job.MoveSeconds = GenRules.MoveSeconds(job.WindowSeconds, job.Moves);
            var length = GenRules.LengthProblem(job.WindowSeconds, job.Moves);
            if (length != "") plan.Fault(frame, length);
            if (ok && length == "") Variants(frame, plan, job);
            plan.Jobs.Add(job);
        }

        static bool Start(GenFrame frame, GenPlan plan, System.Collections.Generic.List<GenSpan> spans, GenJob job)
        {
            var poses = plan.Poses.Where(p => p.IsStart && p.Frame.Span.Touches(frame.Span.Start)).ToList();
            foreach (var p in poses) p.Used = true;
            if (poses.Count > 1)
            {
                plan.Fault(frame, "two Start Pose clips touch its start (" + poses[0].Frame.Where + " and " + poses[1].Frame.Where + "). Keep one.");
                return false;
            }

            var pick = GenRules.StartAt(spans, frame.Span.Start, plan.Loop, plan.Duration);
            if (pick.Ambiguous)
            {
                plan.Fault(frame, "two Source clips meet its start: " + plan.Sources[pick.Index].Frame.Where + " and " + plan.Sources[pick.Other].Frame.Where + ". Move one of them so only one ends at or covers " + GenRules.At(frame.Span.Start) + ".");
                return false;
            }

            if (poses.Count == 1)
            {
                if (!GenPoses.Override(plan, poses[0], pick, true, frame.StartSeconds, out job.From, out job.StartSource, out var problem))
                {
                    plan.Fault(poses[0].Frame, problem);
                    return false;
                }
            }
            else if (!pick.Found)
            {
                plan.Fault(frame, "there is nothing to start from. Put a Source clip that ends at or covers " + GenRules.At(frame.Span.Start) + " on a Generation Track, or a Start Pose over its start.");
                return false;
            }
            else
            {
                job.StartSource = plan.Sources[pick.Index];
                job.From = GenPoses.FromSource(job.StartSource, pick.Edge, true, frame.StartSeconds);
            }

            job.FromText = GenPoses.Describe(job.From);
            return true;
        }

        static bool Goals(GenFrame frame, GenPlan plan, System.Collections.Generic.List<GenSpan> spans, GenJob job)
        {
            var ok = true;
            var inside = plan.Poses.Where(p => !p.IsStart && p.Frame.Span.Inside(frame.Span)).OrderBy(p => p.Frame.Span.Start).ThenBy(p => p.Frame.Order).ToList();
            foreach (var pose in inside)
            {
                pose.Used = true;
                var at = pose.Frame.Span.Start;
                var pick = GenRules.GoalAt(spans, at, plan.Loop, plan.Duration);
                if (pick.Ambiguous)
                {
                    plan.Fault(pose.Frame, "two Source clips cover " + GenRules.At(at) + ": " + plan.Sources[pick.Index].Frame.Where + " and " + plan.Sources[pick.Other].Frame.Where + ". Keep one.");
                    ok = false;
                    continue;
                }

                if (!GenPoses.Override(plan, pose, pick, false, pose.Frame.StartSeconds, out var goal, out _, out var problem))
                {
                    plan.Fault(pose.Frame, problem);
                    ok = false;
                    continue;
                }

                job.Goals.Add(goal);
                job.GoalTexts.Add(GenPoses.Describe(goal) + " (on the way)");
            }

            var finals = plan.Poses.Where(p => !p.IsStart && !inside.Contains(p) && p.Frame.Span.Touches(frame.Span.End)).ToList();
            foreach (var p in finals) p.Used = true;
            if (finals.Count > 1)
            {
                plan.Fault(frame, "two Goal Pose clips touch its end (" + finals[0].Frame.Where + " and " + finals[1].Frame.Where + "). Keep one.");
                return false;
            }

            var last = GenRules.GoalAt(spans, frame.Span.End, plan.Loop, plan.Duration);
            if (last.Ambiguous)
            {
                plan.Fault(frame, "two Source clips meet its end: " + plan.Sources[last.Index].Frame.Where + " and " + plan.Sources[last.Other].Frame.Where + ". Move one of them so only one starts at or covers " + GenRules.At(frame.Span.End) + ".");
                return false;
            }

            SA.Inbetween.Clips.ClipFrame final;
            if (finals.Count == 1)
            {
                if (!GenPoses.Override(plan, finals[0], last, false, frame.EndSeconds, out final, out job.GoalSource, out var problem))
                {
                    plan.Fault(finals[0].Frame, problem);
                    return false;
                }
            }
            else if (!last.Found)
            {
                plan.Fault(frame, "there is nothing to land on. Put a Source clip that starts at or covers " + GenRules.At(frame.Span.End) + " on a Generation Track, or a Goal Pose over its end" + (plan.Loop ? "." : ", or make the timeline loop."));
                return false;
            }
            else
            {
                job.GoalSource = plan.Sources[last.Index];
                final = GenPoses.FromSource(job.GoalSource, last.Edge, false, frame.EndSeconds);
            }

            job.Goals.Add(final);
            job.GoalTexts.Add(GenPoses.Describe(final));
            if (inside.Count > 0)
            {
                var each = GenRules.MoveSeconds(job.WindowSeconds, inside.Count + 1);
                for (var i = 0; i < inside.Count; i++)
                {
                    var reached = frame.StartSeconds + each * (i + 1);
                    if (Math.Abs(reached - inside[i].Frame.StartSeconds) > 0.5 / GenRules.TicksPerSecond)
                        plan.Note(inside[i].Frame, "every move takes the same time (" + GenRules.Text(each) + " s), so this goal is reached at " + GenRules.Text(reached) + " s, not " + GenRules.Text(inside[i].Frame.StartSeconds) + " s.");
                }
            }

            return ok;
        }

        static void Variants(GenFrame frame, GenPlan plan, GenJob job)
        {
            foreach (var variant in GenRecipes.Variants(job.Payload))
            {
                if (GenRecipes.Settings(job, variant, out _, out _, out var problem))
                {
                    job.Variants.Add(variant);
                    continue;
                }

                if (variant == job.Payload.Keep) plan.Fault(frame, problem);
                else plan.Note(frame, problem + " That version is skipped.");
            }
        }
    }
}

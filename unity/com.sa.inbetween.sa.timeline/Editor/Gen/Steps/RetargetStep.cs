using SA.Inbetween.Timeline.Gen;
using UnityEngine;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class RetargetStep : GenStep<RetargetClip>
    {
        public override void Plan(GenFrame frame, RetargetClip clip, GenPlan plan)
        {
            var jobs = plan.JobsOverlapping(frame.Span);
            if (jobs.Count == 0)
            {
                plan.Fault(frame, "it covers no Generate clip, so it is not used. Stretch it over the Generate clips it is for.");
                return;
            }

            var picked = clip.Payload.Character != null ? TimelineBridge.CharacterAsset(clip.Payload.Character) : null;
            if (clip.Payload.Character != null && picked == null)
                plan.Fault(frame, "'" + clip.Payload.Character.name + "' is not a model or prefab in the Project. Drag the character's model or prefab from the Project window.");
            else if (picked != null && picked.GetComponentInChildren<Animator>(true) == null)
                plan.Fault(frame, "'" + picked.name + "' has no Animator. Pick the character's model or prefab.");
            var character = picked != null ? picked : plan.BoundCharacter(frame.Track);
            foreach (var job in jobs)
            {
                if (job.HasRetarget)
                {
                    plan.Fault(frame, "it and another Retarget / Bake clip both cover the Generate clip " + job.Frame.Where + ". Keep one.");
                    continue;
                }

                job.HasRetarget = true;
                if (character != null) job.Character = character;
                job.Rig = clip.Payload.Rig;
                job.FootIK = clip.Payload.FootIK;
            }
        }
    }
}

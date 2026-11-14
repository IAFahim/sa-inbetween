using System;
using System.Collections.Generic;
using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public enum GenEnd
    {
        Stop,
        Loop,
        Next,
    }

    public interface IGenTimelineEnd
    {
        bool TryRead(TimelineAsset timeline, out GenEnd end, out TimelineAsset next);
    }

    public static class GenPlanner
    {
        static Dictionary<Type, IGenStep> steps;
        static List<string> stepProblems;
        static List<IGenTimelineEnd> ends;

        public static IReadOnlyDictionary<Type, IGenStep> Steps
        {
            get
            {
                Discover();
                return steps;
            }
        }

        public static IReadOnlyList<string> StepProblems
        {
            get
            {
                Discover();
                return stepProblems;
            }
        }

        static void Discover()
        {
            if (steps != null) return;
            steps = new Dictionary<Type, IGenStep>();
            stepProblems = new List<string>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGenStep>().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) == null) continue;
                var step = (IGenStep)Activator.CreateInstance(type);
                if (steps.TryGetValue(step.ClipType, out var other))
                {
                    stepProblems.Add("Two steps handle " + step.ClipType.Name + " clips: " + other.GetType().FullName + " and " + type.FullName + ". Delete one of them.");
                    continue;
                }

                steps.Add(step.ClipType, step);
            }

            ends = new List<IGenTimelineEnd>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGenTimelineEnd>().OrderBy(t => t.FullName, StringComparer.Ordinal))
                if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    ends.Add((IGenTimelineEnd)Activator.CreateInstance(type));
        }

        public static bool HasGenTracks(TimelineAsset timeline) => timeline != null && timeline.GetOutputTracks().Any(t => t is GenTrack);

        public static GenPlan Plan(TimelineAsset timeline, PlayableDirector director)
        {
            Discover();
            var plan = new GenPlan { Timeline = timeline, Director = director != null && director.playableAsset == timeline ? director : null };
            if (timeline == null)
            {
                plan.Fault("There is no timeline to plan.");
                return plan;
            }

            foreach (var problem in stepProblems) plan.Fault(problem);
            foreach (var end in ends)
            {
                if (!end.TryRead(timeline, out var how, out var next)) continue;
                plan.Loop = how == GenEnd.Loop;
                plan.Next = how == GenEnd.Next ? next : null;
                break;
            }

            var order = 0;
            var trackIndex = 0;
            foreach (var track in timeline.GetOutputTracks())
            {
                var index = trackIndex++;
                if (!(track is GenTrack gen) || gen.mutedInHierarchy) continue;
                foreach (var clip in gen.GetClips().OrderBy(c => c.start).ThenBy(c => c.end))
                    plan.Clips.Add(new GenFrame(clip, gen, index, order++));
            }

            plan.Duration = plan.Clips.Count == 0 ? 0 : plan.Clips.Max(c => c.Span.End);
            foreach (var frame in plan.Clips.Where(f => f.Asset == null))
                plan.Fault(frame, "this clip's type is missing (its script was renamed, deleted or does not compile). Fix the script or delete the clip.");

            foreach (var stage in plan.Clips.Where(f => f.Asset != null).GroupBy(f => f.Asset.Stage).OrderBy(g => g.Key))
                foreach (var frame in stage)
                {
                    if (!steps.TryGetValue(frame.Asset.GetType(), out var step))
                    {
                        plan.Fault(frame, "no step handles " + frame.Asset.GetType().Name + " clips (its step class is missing or does not compile).");
                        continue;
                    }

                    try
                    {
                        step.Plan(frame, plan);
                    }
                    catch (Exception e)
                    {
                        plan.Fault(frame, "its step failed while planning (" + e.GetType().Name + ": " + e.Message + "). Report this with Copy diagnostics.");
                    }
                }

            Seal(plan);
            return plan;
        }

        static void Seal(GenPlan plan)
        {
            foreach (var job in plan.Jobs)
            {
                if (job.Character == null) job.Character = plan.BoundCharacter(job.Frame.Track);
                if (job.Character == null && job.StartSource != null && job.StartSource.Payload.Character != null) job.Character = TimelineBridge.CharacterAsset(job.StartSource.Payload.Character);
                if (job.Character == null && job.GoalSource != null && job.GoalSource.Payload.Character != null) job.Character = TimelineBridge.CharacterAsset(job.GoalSource.Payload.Character);
                job.From = WithCharacter(job.From, job.Character);
                for (var i = 0; i < job.Goals.Count; i++) job.Goals[i] = WithCharacter(job.Goals[i], job.Character);
                var unity = job.From.Kind == SourceKind.UnityClip || job.Goals.Any(g => g.Kind == SourceKind.UnityClip);
                var needs = job.Export.Anim || job.Export.HumanoidAnim || job.Export.Fbx || job.Export.PlaceOnTimeline;
                if (job.Character == null && (unity || needs))
                    plan.Fault(job.Frame, unity
                        ? "it starts from or lands on a Unity clip, but no character is set to play it on. Bind this track to your character, or set Character on a Retarget / Bake clip over it."
                        : "no character is set to export the result for. Bind this track to your character, or set Character on a Retarget / Bake clip over it.");
                job.Name = TimelineBridge.SafeName(IsDefaultName(job.Frame.Clip.displayName) ? DerivedName(job) : job.Frame.Clip.displayName);
            }

            foreach (var group in plan.Jobs.GroupBy(j => j.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var job in group)
                    plan.Fault(job.Frame, "another Generate clip also writes '" + job.Name + "'. Rename one of them in the Timeline window; a Generate clip's name names its files.");

            for (var i = 0; i < plan.Jobs.Count; i++)
                for (var j = i + 1; j < plan.Jobs.Count; j++)
                    if (plan.Jobs[i].Span.Overlaps(plan.Jobs[j].Span))
                    {
                        plan.Fault(plan.Jobs[i].Frame, "it overlaps the Generate clip " + plan.Jobs[j].Frame.Where + ". Two generations cannot fill the same time; move one.");
                        plan.Fault(plan.Jobs[j].Frame, "it overlaps the Generate clip " + plan.Jobs[i].Frame.Where + ". Two generations cannot fill the same time; move one.");
                    }

            foreach (var pose in plan.Poses.Where(p => !p.Used))
                plan.Fault(pose.Frame, pose.IsStart
                    ? "this Start Pose touches the start of no Generate clip, so it is not used. Move it over the start of the Generate clip it is for."
                    : "this Goal Pose touches the end of no Generate clip and is not inside one, so it is not used. Move it to the end of a Generate clip, or inside one to pass through it.");
        }

        static ClipFrame WithCharacter(ClipFrame frame, GameObject character)
        {
            if (frame.Kind == SourceKind.UnityClip && frame.Character == null) frame.Character = character;
            return frame;
        }

        public static bool IsDefaultName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "Generate") return true;
            if (!name.StartsWith("Generate ", StringComparison.Ordinal)) return false;
            return name.Substring("Generate ".Length).All(char.IsDigit);
        }

        static string DerivedName(GenJob job)
        {
            var from = AssetName(job.From);
            var to = job.Goals.Count > 0 ? AssetName(job.Goals[job.Goals.Count - 1]) : "";
            if (from == "" && to == "") return "Generate";
            if (to == "") return from + "_out";
            if (from == "") return "to_" + to;
            return from == to ? from + "_loop" : from + "_to_" + to;
        }

        static string AssetName(ClipFrame frame)
        {
            UnityEngine.Object asset = frame.Kind == SourceKind.UnityClip ? frame.Motion : frame.Kind == SourceKind.SaClip ? (UnityEngine.Object)frame.Clip : frame.Pose;
            return asset != null ? asset.name : "";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenOutputs
    {
        public const string TrackName = "Generated";

        public static bool PlacesHumanoid(GenJob job) =>
            job.Rig == GenRig.Humanoid || (job.Rig == GenRig.Auto && TimelineBridge.IsHumanoid(job.Character));

        public static bool Export(GenBakeContext context, ExportPayload payload, out string error)
        {
            error = "";
            var recipe = context.Recipe;
            if (recipe == null || recipe.Output == null)
            {
                error = "there is no generated clip to export.";
                return false;
            }

            context.Wrote(AssetDatabase.GetAssetPath(recipe.Output));
            var character = SaClipExport.CharacterFor(recipe);
            if (character == null) return true;
            var humanoid = TimelineBridge.IsHumanoid(character);
            var placeHumanoid = payload.PlaceOnTimeline && PlacesHumanoid(context.Job);
            if (placeHumanoid && !humanoid)
            {
                error = "Rig is Humanoid but '" + character.name + "' has no Humanoid avatar. Set Rig to Generic or Auto on the Retarget / Bake clip.";
                return false;
            }

            AnimationClip generic = null;
            AnimationClip humanoidClip = null;
            if (payload.Anim || (payload.PlaceOnTimeline && !placeHumanoid))
            {
                if ((recipe.SavedClip == null || SaClipExport.SavedClipStale(recipe)) && !SaClipExport.TrySave(recipe, out error)) return false;
                generic = recipe.SavedClip;
                if (generic != null) context.Wrote(AssetDatabase.GetAssetPath(generic));
            }

            if (payload.HumanoidAnim || placeHumanoid)
            {
                if (!humanoid) context.Note("'" + context.Job.Name + "': no Humanoid .anim, because '" + character.name + "' has no Humanoid avatar.");
                else if (!SaClipHumanoidExport.TryExport(recipe, out humanoidClip, out error)) return false;
                else context.Wrote(AssetDatabase.GetAssetPath(humanoidClip));
            }

            if (payload.PlaceOnTimeline)
            {
                var placed = placeHumanoid ? humanoidClip : generic;
                if (placed == null)
                {
                    error = "the clip to put on the timeline was not written.";
                    return false;
                }

                Place(context, placed);
            }

            if (payload.Fbx)
            {
                if (!SaClipFbxExport.TryExport(recipe, out var fbx, out error))
                {
                    error = "the Unity clips were written" + (payload.PlaceOnTimeline ? " and placed on the timeline" : "") + ", but the FBX was not: " + error;
                    return false;
                }

                context.Wrote(fbx);
            }

            return true;
        }

        public const string SourceSuffix = " (source)";
        const double Seam = 1e-3;
        const double ShortestPiece = 1.0 / 30.0;

        public static bool IsSourceCopy(TimelineClip clip) => clip.displayName != null && clip.displayName.EndsWith(SourceSuffix, StringComparison.Ordinal);

        public static string PlaceSources(GenBakeRun run)
        {
            var timeline = run.Timeline;
            var track = Track(timeline, false);
            var jobs = run.Jobs.Where(j => j.Error == "" && j.Job.Export.PlaceOnTimeline && j.Job.Export.PlaceSources).ToList();
            if (track == null || jobs.Count == 0) return "";
            Undo.RegisterCompleteObjectUndo(track, GenBake.UndoName);
            var generated = track.GetClips().Where(c => !IsSourceCopy(c)).OrderBy(c => c.start).ToList();
            var unused = track.GetClips().Where(IsSourceCopy).ToList();
            var sources = new List<GenSource>();
            foreach (var job in jobs)
                foreach (var source in new[] { job.Job.StartSource, job.Job.GoalSource })
                    if (source != null && source.Payload.Kind == SA.Inbetween.Clips.SourceKind.UnityClip && source.Payload.Motion != null && !sources.Contains(source))
                        sources.Add(source);
            var copies = 0;
            foreach (var source in sources.OrderBy(s => s.Start))
            {
                var start = source.Start;
                var end = source.Start + source.Duration;
                foreach (var clip in generated)
                {
                    if (clip.start <= start + Seam && clip.end > start) start = clip.end;
                    else if (clip.start > start + Seam && clip.start < end) end = clip.start;
                }

                if (end - start < ShortestPiece) continue;
                var name = source.Frame.Clip.displayName + SourceSuffix;
                var copy = unused.FirstOrDefault(c => c.displayName == name);
                if (copy != null)
                {
                    unused.Remove(copy);
                    if (copy.asset != null) Undo.RecordObject(copy.asset, GenBake.UndoName);
                }
                else
                {
                    copy = track.CreateClip(source.Payload.Motion);
                    if (copy.asset != null) Undo.RegisterCreatedObjectUndo(copy.asset, GenBake.UndoName);
                }

                var asset = (AnimationPlayableAsset)copy.asset;
                asset.clip = source.Payload.Motion;
                asset.ResetOffsets();
                copy.start = start;
                copy.timeScale = source.TimeScale;
                copy.clipIn = source.ClipIn + (start - source.Start) * source.TimeScale;
                copy.duration = end - start;
                copy.easeInDuration = 0;
                copy.easeOutDuration = 0;
                copy.displayName = name;
                copies++;
            }

            foreach (var leftover in unused)
                timeline.DeleteClip(leftover);
            RecalculateHolds(track);
            var matched = TimelineBridge.MatchChain(timeline, track, run.Director, out var why);
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            if (TimelineEditor.inspectedAsset == timeline) TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            if (copies == 0) return "";
            return "Generated track: " + copies + (copies == 1 ? " source clip" : " source clips") + " around the generated motion, so it plays from start to end; " +
                   (why == "" ? "hips matched at " + matched + (matched == 1 ? " seam." : " seams.") : "hips not matched (" + why + "); bake with the timeline open in the Timeline window on its director to match them.");
        }

        static readonly System.Reflection.MethodInfo CalculateHolds =
            typeof(TimelineClip).Assembly.GetType("UnityEngine.Timeline.Extrapolation")?.GetMethod("CalculateExtrapolationTimes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        public static void RecalculateHolds(TrackAsset track) => CalculateHolds?.Invoke(null, new object[] { track });

        public static AnimationTrack Track(TimelineAsset timeline, bool create)
        {
            var track = timeline.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == TrackName);
            if (track != null || !create) return track;
            Undo.RegisterCompleteObjectUndo(timeline, "Bake Generation Timeline");
            return timeline.CreateTrack<AnimationTrack>(null, TrackName);
        }

        public static TimelineClip Place(GenBakeContext context, AnimationClip clip)
        {
            var timeline = context.Run.Timeline;
            var job = context.Job;
            var track = Track(timeline, true);
            Undo.RegisterCompleteObjectUndo(track, "Bake Generation Timeline");
            var placed = track.GetClips().FirstOrDefault(c => c.displayName == job.Name);
            if (placed == null)
            {
                placed = track.CreateClip(clip);
                if (placed.asset != null) Undo.RegisterCreatedObjectUndo(placed.asset, "Bake Generation Timeline");
            }
            else if (placed.asset != null) Undo.RecordObject(placed.asset, "Bake Generation Timeline");

            var asset = (AnimationPlayableAsset)placed.asset;
            asset.clip = clip;
            asset.loop = AnimationPlayableAsset.LoopMode.Off;
            asset.applyFootIK = job.FootIK;
            asset.ResetOffsets();
            placed.start = job.StartSeconds;
            placed.clipIn = 0;
            placed.timeScale = 1;
            placed.duration = clip.length;
            placed.easeInDuration = 0;
            placed.easeOutDuration = 0;
            placed.displayName = job.Name;
            RecalculateHolds(track);

            var director = context.Run.Director;
            if (director != null && director.playableAsset == timeline && director.GetGenericBinding(track) == null)
            {
                var bound = director.GetGenericBinding(job.Frame.Track);
                if (bound != null)
                {
                    Undo.RecordObject(director, "Bake Generation Timeline");
                    director.SetGenericBinding(track, bound);
                }
            }

            if (Math.Abs(clip.length - job.WindowSeconds) > 0.5 / GenRules.TicksPerSecond)
                context.Note("'" + job.Name + "' is " + GenRules.Text(clip.length) + " s on the timeline; its Generate clip is " + GenRules.Text(job.WindowSeconds) + " s (the generator works in steps of 4 frames).");
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            if (TimelineEditor.inspectedAsset == timeline) TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            return placed;
        }
    }
}

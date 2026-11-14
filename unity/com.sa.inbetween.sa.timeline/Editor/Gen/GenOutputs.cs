using System;
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

            if (payload.Fbx)
            {
                if (!SaClipFbxExport.TryExport(recipe, out var fbx, out error)) return false;
                context.Wrote(fbx);
            }

            if (!payload.PlaceOnTimeline) return true;
            var placed = placeHumanoid ? humanoidClip : generic;
            if (placed == null)
            {
                error = "the clip to put on the timeline was not written.";
                return false;
            }

            Place(context, placed);
            return true;
        }

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

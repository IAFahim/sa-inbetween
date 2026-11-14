using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenPlanCache
    {
        const double Fresh = 0.25;

        static TimelineAsset timeline;
        static PlayableDirector director;
        static GenPlan plan;
        static double at;

        public static GenPlan For(TimelineAsset asset, PlayableDirector inspected)
        {
            if (asset == null) return null;
            var now = EditorApplication.timeSinceStartup;
            if (plan == null || asset != timeline || inspected != director || now - at > Fresh)
            {
                timeline = asset;
                director = inspected;
                plan = GenPlanner.Plan(asset, inspected);
                at = now;
            }

            return plan;
        }

        public static void Forget() => plan = null;

        public static TimelineAsset TimelineOf(Object asset)
        {
            if (asset == null) return null;
            if (asset is TimelineAsset timeline) return timeline;
            var inspected = TimelineEditor.inspectedAsset;
            if (inspected != null && Contains(inspected, asset)) return inspected;
            var path = AssetDatabase.GetAssetPath(asset);
            return path == "" ? null : AssetDatabase.LoadMainAssetAtPath(path) as TimelineAsset;
        }

        public static PlayableDirector DirectorOf(TimelineAsset timeline)
        {
            if (timeline == null) return null;
            var director = TimelineEditor.inspectedDirector;
            if (director != null && director.playableAsset == timeline) return director;
            PlayableDirector only = null;
            foreach (var candidate in Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include))
            {
                if (candidate.playableAsset != timeline || EditorUtility.IsPersistent(candidate)) continue;
                if (only != null) return null;
                only = candidate;
            }

            return only;
        }

        static bool Contains(TimelineAsset timeline, Object asset)
        {
            foreach (var track in timeline.GetOutputTracks())
            {
                if (track == asset) return true;
                foreach (var clip in track.GetClips())
                    if (clip.asset == asset) return true;
            }

            return false;
        }
    }
}

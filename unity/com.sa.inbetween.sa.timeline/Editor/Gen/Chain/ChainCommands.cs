using System.Collections.Generic;
using System.IO;
using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class ChainCommands
    {
        public const string Title = "SA Inbetween";

        public static TimelineAsset TimelineOf(TrackAsset track) => track != null ? track.timelineAsset : null;

        public static TimelineAsset TimelineOf(TimelineClip clip) => clip?.GetParentTrack() != null ? clip.GetParentTrack().timelineAsset : null;

        public static bool IsGap(TimelineClip clip) => clip != null && clip.asset is GenerateClip && clip.GetParentTrack() is GenTrack;

        public static GenBakeRun BakeAll(TimelineAsset timeline, bool force, out string error)
        {
            error = "";
            if (timeline == null)
            {
                error = "Select a timeline with an SA Inbetween Chain or Generation Track.";
                return null;
            }

            return GenBake.Start(timeline, GenPlanCache.DirectorOf(timeline), new GenBakeOptions { Force = force }, out error);
        }

        public static GenBakeRun BakeGaps(IEnumerable<TimelineClip> gaps, bool force, out string error)
        {
            error = "";
            var list = gaps.Where(IsGap).ToList();
            var timeline = list.Select(TimelineOf).FirstOrDefault(t => t != null);
            if (timeline == null)
            {
                error = "Select an in-between (a Generate clip) on an SA Inbetween track.";
                return null;
            }

            var options = new GenBakeOptions { Force = force };
            foreach (var gap in list) options.Only.Add(gap);
            return GenBake.Start(timeline, GenPlanCache.DirectorOf(timeline), options, out error);
        }

        public static List<string> Outputs(TimelineAsset timeline, IEnumerable<TimelineClip> only)
        {
            var paths = new List<string>();
            if (timeline == null) return paths;
            var filter = only != null ? new HashSet<TimelineClip>(only) : null;
            var plan = GenPlanner.Plan(timeline, GenPlanCache.DirectorOf(timeline));
            foreach (var job in plan.Jobs)
            {
                if (filter != null && filter.Count > 0 && !filter.Contains(job.Frame.Clip)) continue;
                foreach (var variant in job.Variants.Count > 0 ? job.Variants : new List<GenVariant> { job.Payload.Keep })
                {
                    var recipe = GenRecipes.RecipePath(timeline, job, variant);
                    if (recipe == "") continue;
                    var stem = recipe.Substring(0, recipe.Length - ".asset".Length);
                    foreach (var path in new[] { recipe, stem + ".saclip", stem + ".anim", stem + "_Humanoid.anim", stem + ".fbx" })
                        if (File.Exists(path) && !paths.Contains(path))
                            paths.Add(path);
                }
            }

            if (filter == null || filter.Count == 0)
            {
                foreach (var track in ChainSync.Tracks(timeline))
                {
                    var stem = GenChain.Stem(timeline, track);
                    foreach (var path in new[] { stem + ".asset", stem + ".saclip", stem + ".anim", stem + "_Humanoid.anim", stem + ".fbx" })
                        if (File.Exists(path))
                            paths.Add(path);
                }

                var plan2 = GenRecipes.PlanPath(timeline);
                if (plan2 != "" && File.Exists(plan2)) paths.Add(plan2);
            }

            return paths;
        }

        public static Object[] Reveal(TimelineAsset timeline, IEnumerable<TimelineClip> only)
        {
            var assets = Outputs(timeline, only).Select(AssetDatabase.LoadMainAssetAtPath).Where(a => a != null).ToArray();
            if (assets.Length == 0)
            {
                var folder = GenRecipes.Folder(timeline);
                var asset = folder != "" ? AssetDatabase.LoadMainAssetAtPath(folder) : null;
                assets = asset != null ? new[] { asset } : new Object[0];
            }

            if (assets.Length == 0) return assets;
            Selection.objects = assets;
            EditorGUIUtility.PingObject(assets[0]);
            if (!Application.isBatchMode) EditorUtility.FocusProjectWindow();
            return assets;
        }

        static void Say(string error)
        {
            if (!string.IsNullOrEmpty(error)) EditorUtility.DisplayDialog(Title, error, "OK");
        }

        [MenuItem("SA/Inbetween/Chain/Bake All", false, 30)]
        static void BakeAllMenu()
        {
            BakeAll(Inspected(), false, out var error);
            Say(error);
        }

        [MenuItem("SA/Inbetween/Chain/Regenerate All", false, 31)]
        static void RegenerateAllMenu()
        {
            BakeAll(Inspected(), true, out var error);
            Say(error);
        }

        [MenuItem("SA/Inbetween/Chain/Reveal Outputs", false, 32)]
        static void RevealMenu() => Reveal(Inspected(), null);

        [MenuItem("SA/Inbetween/Chain/Cancel Bake", false, 33)]
        static void CancelMenu() => GenBake.Cancel(GenBake.Running(Inspected()));

        [MenuItem("SA/Inbetween/Chain/Bake All", true)]
        [MenuItem("SA/Inbetween/Chain/Regenerate All", true)]
        [MenuItem("SA/Inbetween/Chain/Reveal Outputs", true)]
        static bool HasInspected() => GenPlanner.HasGenTracks(Inspected());

        [MenuItem("SA/Inbetween/Chain/Cancel Bake", true)]
        static bool CanCancel() => GenBake.Running(Inspected()) != null;

        public static TimelineAsset Inspected()
        {
            if (TimelineEditor.inspectedAsset != null) return TimelineEditor.inspectedAsset;
            if (Selection.activeObject is TimelineAsset selected) return selected;
            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent<PlayableDirector>(out var director)) return director.playableAsset as TimelineAsset;
            return null;
        }
    }

    [MenuEntry("SA Inbetween/Bake all", MenuPriority.CustomTrackActionSection.start)]
    sealed class BakeTrackAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) => tracks.Any(t => t is GenTrack) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var run = ChainCommands.BakeAll(ChainCommands.TimelineOf(tracks.First(t => t is GenTrack)), false, out var error);
            if (run == null) EditorUtility.DisplayDialog(ChainCommands.Title, error, "OK");
            return run != null;
        }
    }

    [MenuEntry("SA Inbetween/Regenerate all", MenuPriority.CustomTrackActionSection.start + 1)]
    sealed class RegenerateTrackAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) => tracks.Any(t => t is GenTrack) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            var run = ChainCommands.BakeAll(ChainCommands.TimelineOf(tracks.First(t => t is GenTrack)), true, out var error);
            if (run == null) EditorUtility.DisplayDialog(ChainCommands.Title, error, "OK");
            return run != null;
        }
    }

    [MenuEntry("SA Inbetween/Reveal outputs", MenuPriority.CustomTrackActionSection.start + 2)]
    sealed class RevealTrackAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) => tracks.Any(t => t is GenTrack) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TrackAsset> tracks) => ChainCommands.Reveal(ChainCommands.TimelineOf(tracks.First(t => t is GenTrack)), null).Length > 0;
    }

    [MenuEntry("SA Inbetween/Bake this gap", MenuPriority.CustomClipActionSection.start)]
    sealed class BakeGapAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips) => clips.Any(ChainCommands.IsGap) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            var run = ChainCommands.BakeGaps(clips, false, out var error);
            if (run == null) EditorUtility.DisplayDialog(ChainCommands.Title, error, "OK");
            return run != null;
        }
    }

    [MenuEntry("SA Inbetween/Regenerate this gap", MenuPriority.CustomClipActionSection.start + 1)]
    sealed class RegenerateGapAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips) => clips.Any(ChainCommands.IsGap) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            var run = ChainCommands.BakeGaps(clips, true, out var error);
            if (run == null) EditorUtility.DisplayDialog(ChainCommands.Title, error, "OK");
            return run != null;
        }
    }

    [MenuEntry("SA Inbetween/Reveal outputs", MenuPriority.CustomClipActionSection.start + 2)]
    sealed class RevealGapAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips) => clips.Any(ChainCommands.IsGap) ? ActionValidity.Valid : ActionValidity.NotApplicable;

        public override bool Execute(IEnumerable<TimelineClip> clips) =>
            ChainCommands.Reveal(ChainCommands.TimelineOf(clips.First(ChainCommands.IsGap)), clips.Where(ChainCommands.IsGap)).Length > 0;
    }
}

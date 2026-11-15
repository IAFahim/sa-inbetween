using System;
using System.Collections.Generic;
using System.Linq;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public readonly struct ChainGap
    {
        public readonly TimelineClip Before;
        public readonly TimelineClip After;
        public readonly double Start;
        public readonly double End;

        public ChainGap(TimelineClip before, TimelineClip after)
        {
            Before = before;
            After = after;
            Start = before.end;
            End = after.start;
        }

        public double Seconds => End - Start;
    }

    public static class ChainSync
    {
        public const string UndoName = "Chain in-betweens";
        public const double MinGap = 1.0 / GenRules.TicksPerSecond / 2.0;

        public static IEnumerable<ChainTrack> Tracks(TimelineAsset timeline) =>
            timeline == null ? Enumerable.Empty<ChainTrack>() : timeline.GetOutputTracks().OfType<ChainTrack>();

        public static bool HasChains(TimelineAsset timeline) => Tracks(timeline).Any();

        public static List<TimelineClip> Blocks(ChainTrack track) =>
            track.GetClips().Where(c => c.asset is SourceClip).OrderBy(c => c.start).ThenBy(c => c.end).ToList();

        public static List<ChainGap> Gaps(ChainTrack track)
        {
            var blocks = Blocks(track);
            var gaps = new List<ChainGap>();
            for (var i = 0; i + 1 < blocks.Count; i++)
                if (blocks[i + 1].start - blocks[i].end > MinGap)
                    gaps.Add(new ChainGap(blocks[i], blocks[i + 1]));
            return gaps;
        }

        public static string Signature(TimelineAsset timeline)
        {
            var parts = new List<string>();
            foreach (var track in Tracks(timeline))
                foreach (var clip in track.GetClips().OrderBy(c => c.start))
                {
                    var asset = clip.asset as GenClip;
                    parts.Add(track.name + ":" + clip.start.ToString("R") + ":" + clip.duration.ToString("R") + ":" + clip.clipIn.ToString("R") + ":" + clip.timeScale.ToString("R") + ":" +
                              (asset != null ? UnityEngine.JsonUtility.ToJson(asset) : "-") + ":" + (track.mutedInHierarchy ? "m" : "") + ":" + UnityEngine.JsonUtility.ToJson(track.Chain));
                }

            return string.Join("|", parts);
        }

        public static bool Sync(TimelineAsset timeline)
        {
            var changed = false;
            foreach (var track in Tracks(timeline).ToList())
                changed |= Sync(track);
            if (changed)
            {
                EditorUtility.SetDirty(timeline);
                AssetDatabase.SaveAssetIfDirty(timeline);
            }

            return changed;
        }

        public static bool Sync(ChainTrack track)
        {
            var wanted = Gaps(track);
            var gapClips = track.GetClips().Where(c => c.asset is GenerateClip).ToList();
            var keep = new HashSet<TimelineClip>();
            var plan = new List<(ChainGap Gap, TimelineClip Clip)>();
            foreach (var gap in wanted)
            {
                TimelineClip best = null;
                var bestOverlap = 0.0;
                foreach (var clip in gapClips)
                {
                    if (keep.Contains(clip)) continue;
                    var overlap = Math.Min(clip.end, gap.End) - Math.Max(clip.start, gap.Start);
                    if (overlap <= bestOverlap) continue;
                    best = clip;
                    bestOverlap = overlap;
                }

                if (best != null) keep.Add(best);
                plan.Add((gap, best));
            }

            var stale = gapClips.Where(c => !keep.Contains(c)).ToList();
            var moves = plan.Any(p => p.Clip == null || Math.Abs(p.Clip.start - p.Gap.Start) > 1e-6 || Math.Abs(p.Clip.end - p.Gap.End) > 1e-6);
            if (!moves && stale.Count == 0) return false;
            Undo.RegisterCompleteObjectUndo(track, UndoName);
            foreach (var (gap, existing) in plan)
            {
                var clip = existing;
                if (clip == null)
                {
                    clip = track.CreateClip<GenerateClip>();
                    if (clip.asset != null) Undo.RegisterCreatedObjectUndo(clip.asset, UndoName);
                    var generate = (GenerateClip)clip.asset;
                    var payload = generate.Payload;
                    payload.LandExactly = track.Chain.LandExactly;
                    generate.Payload = payload;
                    clip.displayName = ChainTrack.GapName;
                }

                clip.start = gap.Start;
                clip.duration = gap.Seconds;
            }

            foreach (var clip in stale) track.timelineAsset.DeleteClip(clip);
            EditorUtility.SetDirty(track);
            return true;
        }
    }
}

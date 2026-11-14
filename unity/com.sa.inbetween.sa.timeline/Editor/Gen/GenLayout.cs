using System.Collections.Generic;
using System.IO;
using SA.Inbetween.Clips;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenLayout
    {
        public const string StepsTrack = "Steps";
        public const string BakeTrack = "Bake";
        public const double GenerateSeconds = 1.0;

        public static TimelineAsset Create(string path, IReadOnlyList<ClipFrame> sources, GameObject character, double generateSeconds, int variations, double firstSeconds = 0, bool withFbx = false)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(timeline, path);
            Fill(timeline, sources, character, generateSeconds, variations, firstSeconds, withFbx);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        public static void Fill(TimelineAsset timeline, IReadOnlyList<ClipFrame> sources, GameObject character, double generateSeconds, int variations, double firstSeconds = 0, bool withFbx = false)
        {
            var steps = timeline.CreateTrack<GenTrack>(null, StepsTrack);
            var bake = timeline.CreateTrack<GenTrack>(null, BakeTrack);
            var time = 0.0;
            for (var i = 0; i < sources.Count; i++)
            {
                var clip = steps.CreateClip<SourceClip>();
                var source = (SourceClip)clip.asset;
                source.Payload = new SourcePayload { Kind = sources[i].Kind, Motion = sources[i].Motion, Clip = sources[i].Clip, Pose = sources[i].Pose, Character = sources[i].Character };
                clip.start = time;
                clip.duration = i == 0 && firstSeconds > 0 ? firstSeconds : source.duration;
                clip.displayName = source.SourceName != "" ? source.SourceName : source.DefaultName;
                time = clip.end;
                if (i == sources.Count - 1) break;
                var generate = steps.CreateClip<GenerateClip>();
                generate.start = time;
                generate.duration = generateSeconds;
                var payload = ((GenerateClip)generate.asset).Payload;
                payload.Variations = Mathf.Clamp(variations, 1, GenRecipes.Order.Length);
                ((GenerateClip)generate.asset).Payload = payload;
                time = generate.end;
            }

            var retarget = bake.CreateClip<RetargetClip>();
            retarget.start = 0;
            retarget.duration = time;
            ((RetargetClip)retarget.asset).Payload = new RetargetPayload { Character = character, Rig = GenRig.Auto };
            var export = bake.CreateClip<ExportClip>();
            export.start = 0;
            export.duration = time;
            var exportPayload = ((ExportClip)export.asset).Payload;
            exportPayload.Fbx = withFbx;
            ((ExportClip)export.asset).Payload = exportPayload;
            GenOutputs.Track(timeline, true);
        }
    }
}

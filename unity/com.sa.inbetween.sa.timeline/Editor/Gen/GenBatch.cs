using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public static class GenBatch
    {
        public const string Argument = "-genTimeline";

        static readonly Queue<TimelineAsset> Pending = new Queue<TimelineAsset>();
        static GenBakeRun current;
        static int failures;

        public static void Bake()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != Argument) continue;
                var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(args[i + 1]);
                if (timeline == null)
                {
                    Debug.LogError("SA Inbetween: no timeline at '" + args[i + 1] + "'.");
                    failures++;
                    continue;
                }

                Pending.Enqueue(timeline);
            }

            if (Pending.Count == 0 && failures == 0)
            {
                Debug.LogError("SA Inbetween: pass " + Argument + " Assets/<path>.playable (one or more).");
                failures++;
            }

            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (current != null && !current.Done) return;
            if (current != null && !current.Ok) failures++;
            current = current?.Then;
            if (current != null) return;
            while (Pending.Count > 0 && current == null)
            {
                var timeline = Pending.Dequeue();
                current = GenBake.Start(timeline, null, out var error);
                if (current != null) continue;
                Debug.LogError("SA Inbetween: " + timeline.name + ": " + error);
                failures++;
            }

            if (current != null) return;
            EditorApplication.update -= Tick;
            Debug.Log("SA Inbetween: batch bake finished with " + failures + (failures == 1 ? " failure." : " failures."));
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}

using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    [InitializeOnLoad]
    public static class ChainAuto
    {
        public const double Every = 0.5;
        public const double Settle = 1.0;

        static TimelineAsset watched;
        static string signature = "";
        static string failed = "";
        static double changedAt;
        static double checkedAt;

        static ChainAuto()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - checkedAt < Every) return;
            checkedAt = now;
            var timeline = TimelineEditor.inspectedAsset;
            if (timeline == null || !ChainSync.HasChains(timeline)) return;
            Check(timeline, GenPlanCache.DirectorOf(timeline), now);
        }

        public static GenBakeRun Check(TimelineAsset timeline, PlayableDirector director, double now)
        {
            if (timeline == null || Application.isPlaying || EditorApplication.isCompiling || GenBake.Running(timeline) != null) return null;
            if (!ChainSync.Tracks(timeline).Any(t => t.Chain.AutoBake && !t.mutedInHierarchy)) return null;
            var current = ChainSync.Signature(timeline);
            if (timeline != watched || current != signature)
            {
                if (ChainSync.Sync(timeline)) GenPlanCache.Forget();
                watched = timeline;
                signature = ChainSync.Signature(timeline);
                changedAt = now;
                return null;
            }

            if (now - changedAt < Settle || signature == failed) return null;
            if (!InbetweenSettings.Tools().Complete) return null;
            var plan = GenPlanner.Plan(timeline, director);
            if (!plan.Ready || !Stale(plan)) return null;
            var run = GenBake.Start(timeline, director, new GenBakeOptions { Auto = true }, out var error);
            if (run == null)
            {
                failed = signature;
                if (error != "") Debug.LogWarning("SA Inbetween: the chain was not baked on its own: " + error, timeline);
                return null;
            }

            var started = signature;
            GenBake.Finished += Done;

            void Done(GenBakeRun finished)
            {
                if (finished != run) return;
                GenBake.Finished -= Done;
                if (!finished.Ok) failed = started;
                signature = ChainSync.Signature(timeline);
            }

            return run;
        }

        public static void Forget()
        {
            watched = null;
            signature = "";
            failed = "";
        }

        public static bool Stale(GenPlan plan)
        {
            var track = GenOutputs.Track(plan.Timeline, false);
            foreach (var job in plan.Jobs)
            {
                if (job.Variants.Count == 0) return true;
                var probe = ScriptableObject.CreateInstance<InbetweenRecipe>();
                try
                {
                    GenRecipes.Configure(probe, job, job.Variants[0]);
                    var existing = AssetDatabase.LoadAssetAtPath<InbetweenRecipe>(GenRecipes.RecipePath(plan.Timeline, job, job.Variants[0]));
                    if (existing == null || existing.Output == null || existing.BuiltFrom != RecipeRunner.Hash(probe)) return true;
                }
                finally
                {
                    Object.DestroyImmediate(probe);
                }

                if (job.Export.PlaceOnTimeline && (track == null || !track.GetClips().Any(c => c.displayName == job.Name))) return true;
            }

            foreach (var chain in ChainSync.Tracks(plan.Timeline))
                if (chain.Chain.WholeChain && AssetDatabase.LoadAssetAtPath<SaChainAsset>(GenChain.Stem(plan.Timeline, chain) + ".asset") == null)
                    return true;
            return false;
        }
    }
}

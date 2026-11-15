using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SA.Inbetween.Clips;
using SA.Inbetween.Clips.Editor;
using SA.Inbetween.Timeline.Gen;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SA.Inbetween.Timeline.Editor
{
    public sealed class GenBakeJob
    {
        public GenJob Job;
        public readonly List<GenVariant> Variants = new List<GenVariant>();
        public readonly List<InbetweenRecipe> Recipes = new List<InbetweenRecipe>();
        public readonly List<string> Files = new List<string>();
        public string Error = "";
        public bool Skipped;
        internal int Next;
        internal bool Waiting;

        public InbetweenRecipe Kept => Recipes.Count > 0 ? Recipes[0] : null;
    }

    public sealed class GenBakeRun
    {
        public TimelineAsset Timeline;
        public PlayableDirector Director;
        public GenPlan Plan;
        public readonly List<GenBakeJob> Jobs = new List<GenBakeJob>();
        public readonly List<string> Report = new List<string>();
        public string Stage = "";
        public string Error = "";
        public bool Done;
        public int Generated;
        public int Reused;
        public string PlanPath = "";
        public string Digest = "";
        public GenBakeRun Then;
        public GenBakeOptions Options = new GenBakeOptions();
        public readonly List<TimelineSeam> Seams = new List<TimelineSeam>();
        public readonly List<GenPiece> Pieces = new List<GenPiece>();
        public readonly List<SaChainAsset> Chains = new List<SaChainAsset>();
        internal readonly List<TimelineAsset> Chain = new List<TimelineAsset>();
        internal int Current;
        internal int ProgressId = -1;

        public bool Ok => Done && Error == "";

        public int Total => Jobs.Sum(j => j.Recipes.Count);
    }

    public sealed class GenBakeOptions
    {
        public readonly HashSet<TimelineClip> Only = new HashSet<TimelineClip>();
        public bool Force;
        public bool Auto;

        public bool Includes(GenJob job) => Only.Count == 0 || Only.Contains(job.Frame.Clip);
    }

    public sealed class GenBakeContext
    {
        public GenBakeRun Run;
        public GenBakeJob BakeJob;

        public GenJob Job => BakeJob.Job;

        public InbetweenRecipe Recipe => BakeJob.Kept;

        public IReadOnlyList<string> Files => BakeJob.Files;

        public IEnumerable<InbetweenRecipe> Alternates => BakeJob.Recipes.Skip(1);

        public void Wrote(string path)
        {
            if (!string.IsNullOrEmpty(path) && !BakeJob.Files.Contains(path)) BakeJob.Files.Add(path);
        }

        public void Note(string text) => Run.Report.Add(text);
    }

    public static class GenBake
    {
        public const string UndoName = "Bake Generation Timeline";

        static readonly List<GenBakeRun> Runs = new List<GenBakeRun>();
        static readonly Dictionary<TimelineAsset, GenBakeRun> Last = new Dictionary<TimelineAsset, GenBakeRun>();
        static bool hooked;

        public static event Action<GenBakeRun> Finished;

        public static GenBakeRun Running(TimelineAsset timeline) => Runs.FirstOrDefault(r => r.Timeline == timeline && !r.Done);

        public static GenBakeRun LastRun(TimelineAsset timeline) => timeline != null && Last.TryGetValue(timeline, out var run) ? run : null;

        public static bool AnyRunning => Runs.Any(r => !r.Done);

        public static string Problem(GenPlan plan) => Problem(plan, InbetweenSettings.Tools());

        public static string Problem(GenPlan plan, StudioTools tools)
        {
            if (plan.Faults.Count > 0)
                return "This timeline cannot be baked yet: " + plan.Faults[0].Message + (plan.Faults.Count > 1 ? " (and " + (plan.Faults.Count - 1) + " more; see the Inspector)" : "");
            if (plan.Jobs.Count == 0) return "Nothing to bake: add a Generate clip between two Source clips on an SA Inbetween Generation Track.";
            if (AssetDatabase.GetAssetPath(plan.Timeline) == "") return "Save the timeline as an asset first.";
            if (!tools.Complete) return "Inbetween needs its tools before it can bake. " + InbetweenSettings.SetupAdvice(tools);
            if (Application.isPlaying) return "Leave Play mode before baking.";
            if (EditorApplication.isCompiling) return "Unity is compiling scripts; bake again when it finishes.";
            if (Running(plan.Timeline) != null) return "This timeline is already baking.";
            return "";
        }

        public static GenBakeRun Start(TimelineAsset timeline, PlayableDirector director, out string error) => Start(timeline, director, null, null, out error);

        public static GenBakeRun Start(TimelineAsset timeline, PlayableDirector director, GenBakeOptions options, out string error) => Start(timeline, director, null, options, out error);

        static GenBakeRun Start(TimelineAsset timeline, PlayableDirector director, GenBakeRun before, GenBakeOptions options, out string error)
        {
            if (timeline != null && Running(timeline) == null && ChainSync.HasChains(timeline)) ChainSync.Sync(timeline);
            var plan = GenPlanner.Plan(timeline, director);
            error = Problem(plan);
            if (error != "") return null;
            var run = new GenBakeRun { Timeline = timeline, Director = plan.Director, Plan = plan, Options = options ?? new GenBakeOptions() };
            if (before != null) run.Chain.AddRange(before.Chain);
            run.Chain.Add(timeline);
            foreach (var job in plan.Jobs)
            {
                var bakeJob = new GenBakeJob { Job = job };
                foreach (var variant in job.Variants)
                {
                    var recipe = GenRecipes.Ensure(timeline, job, variant, out error);
                    if (recipe == null) return null;
                    var problem = RecipeRunner.Problem(recipe);
                    if (problem != "")
                    {
                        error = "The Generate clip " + job.Frame.Where + " cannot be generated: " + problem;
                        return null;
                    }

                    bakeJob.Variants.Add(variant);
                    bakeJob.Recipes.Add(recipe);
                }

                run.Jobs.Add(bakeJob);
            }

            Runs.Add(run);
            Last[timeline] = run;
            run.ProgressId = Progress.Start("Bake '" + timeline.name + "'", "SA Inbetween generation", Progress.Options.None);
            Progress.RegisterCancelCallback(run.ProgressId, () =>
            {
                Cancel(run);
                return true;
            });
            if (!hooked)
            {
                EditorApplication.update += Tick;
                AssemblyReloadEvents.beforeAssemblyReload += Interrupt;
                hooked = true;
            }

            Tick();
            return run;
        }

        public static void Cancel(GenBakeRun run)
        {
            if (run == null || run.Done) return;
            foreach (var job in run.Jobs)
                foreach (var recipe in job.Recipes)
                    if (RecipeRunner.IsRunning(recipe))
                        RecipeRunner.Cancel(recipe);
            Complete(run, "Cancelled. Generations that finished are kept; the others are unchanged.");
        }

        static void Interrupt()
        {
            foreach (var run in Runs.Where(r => !r.Done).ToList())
                Complete(run, RecipeRunner.InterruptedMessage);
        }

        static void Tick()
        {
            foreach (var run in Runs.ToList())
                if (!run.Done)
                    Step(run);
            Runs.RemoveAll(r => r.Done);
            if (Runs.Count == 0 && hooked)
            {
                EditorApplication.update -= Tick;
                AssemblyReloadEvents.beforeAssemblyReload -= Interrupt;
                hooked = false;
            }
        }

        static void Step(GenBakeRun run)
        {
            while (run.Current < run.Jobs.Count)
            {
                var job = run.Jobs[run.Current];
                if (job.Waiting)
                {
                    var recipe = job.Recipes[job.Next];
                    if (RecipeRunner.IsRunning(recipe)) return;
                    job.Waiting = false;
                    var result = RecipeRunner.Get(recipe);
                    if (result != null && result.Error != "")
                    {
                        Fail(run, job, "Generating '" + recipe.name + "' failed: " + result.Error);
                        continue;
                    }

                    run.Generated++;
                    job.Next++;
                }

                if (job.Error != "")
                {
                    run.Current++;
                    continue;
                }

                if (job.Next < job.Recipes.Count)
                {
                    var recipe = job.Recipes[job.Next];
                    if (recipe == null)
                    {
                        Fail(run, job, "A recipe of '" + job.Job.Name + "' was deleted while baking. Bake again.");
                        continue;
                    }

                    var included = run.Options.Includes(job.Job);
                    if (!GenRecipes.NeedsGeneration(recipe) && !(run.Options.Force && included))
                    {
                        run.Reused++;
                        job.Next++;
                        continue;
                    }

                    if (!included)
                    {
                        job.Skipped = true;
                        job.Next = job.Recipes.Count;
                        run.Report.Add("'" + job.Job.Name + "' is out of date and was not generated (only the chosen in-between was baked).");
                        continue;
                    }

                    run.Stage = "Generating '" + recipe.name + "' (" + (Done(run) + 1) + " of " + run.Total + ")";
                    Report(run);
                    if (!RecipeRunner.Start(recipe, out var error))
                    {
                        Fail(run, job, "Generating '" + recipe.name + "' could not start: " + error);
                        continue;
                    }

                    job.Waiting = true;
                    return;
                }

                if (job.Skipped)
                {
                    run.Current++;
                    continue;
                }

                run.Stage = "Exporting '" + job.Job.Name + "'";
                Report(run);
                Finish(run, job);
                run.Current++;
            }

            string sources;
            try
            {
                sources = GenPieces.Place(run, run.Pieces);
            }
            catch (Exception e)
            {
                sources = "Generated track: the source clips around the generated motion were not placed (" + e.GetType().Name + ": " + e.Message + "). The generated clips are placed.";
            }

            if (sources != "") run.Report.Add(sources);
            try
            {
                foreach (var line in GenChain.Write(run)) run.Report.Add(line);
            }
            catch (Exception e)
            {
                run.Report.Add("The whole chain was not written (" + e.GetType().Name + ": " + e.Message + ").");
            }

            Complete(run, run.Jobs.Any(j => j.Error != "") ? string.Join(" ", run.Jobs.Where(j => j.Error != "").Select(j => j.Error)) : "");
        }

        static int Done(GenBakeRun run) => run.Jobs.Sum(j => Math.Min(j.Next, j.Recipes.Count));

        static void Report(GenBakeRun run)
        {
            if (run.ProgressId < 0 || !Progress.Exists(run.ProgressId)) return;
            var total = Math.Max(1, run.Total);
            Progress.Report(run.ProgressId, Math.Min(1f, Done(run) / (float)total), run.Stage);
        }

        static void Fail(GenBakeRun run, GenBakeJob job, string error)
        {
            job.Error = error;
            job.Waiting = false;
            run.Report.Add(error);
        }

        static void Finish(GenBakeRun run, GenBakeJob job)
        {
            var context = new GenBakeContext { Run = run, BakeJob = job };
            foreach (var recipe in job.Recipes.Skip(1))
            {
                if (recipe.Output != null) context.Wrote(AssetDatabase.GetAssetPath(recipe.Output));
                if (recipe.SaveAnimationClip && (recipe.SavedClip == null || SaClipExport.SavedClipStale(recipe)) && SaClipExport.TrySave(recipe, out _) && recipe.SavedClip != null)
                    context.Wrote(AssetDatabase.GetAssetPath(recipe.SavedClip));
            }

            var hooks = run.Plan.Clips
                .Where(f => f.Asset != null && f.Span.Overlaps(job.Job.Span))
                .OrderBy(f => f.Asset.Stage).ThenBy(f => f.Order)
                .ToList();
            var exported = false;
            foreach (var frame in hooks)
            {
                if (!exported && frame.Asset.Stage >= GenStage.Export && !job.Job.HasExport)
                {
                    exported = true;
                    if (!Default(context, job, run)) return;
                }

                if (!GenPlanner.Steps.TryGetValue(frame.Asset.GetType(), out var step)) continue;
                string error;
                try
                {
                    if (step.Finish(frame, context, out error)) continue;
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + ": " + e.Message;
                }

                Fail(run, job, frame.Where + ": " + error);
                return;
            }

            if (!exported && !job.Job.HasExport) Default(context, job, run);
        }

        static bool Default(GenBakeContext context, GenBakeJob job, GenBakeRun run)
        {
            if (GenOutputs.Export(context, ExportPayload.Default, out var error)) return true;
            Fail(run, job, "'" + job.Job.Name + "': " + error);
            return false;
        }

        static void Complete(GenBakeRun run, string error)
        {
            if (run.Done) return;
            run.Error = error;
            run.Done = true;
            run.Stage = error == "" ? "Done" : "Stopped";
            if (run.ProgressId >= 0 && Progress.Exists(run.ProgressId))
                Progress.Finish(run.ProgressId, error == "" ? Progress.Status.Succeeded : error.StartsWith("Cancelled", StringComparison.Ordinal) ? Progress.Status.Canceled : Progress.Status.Failed);
            run.ProgressId = -1;
            try
            {
                WritePlan(run);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                run.Report.Add("The plan file could not be written: " + e.Message);
            }

            run.Report.Insert(0, Summary(run));
            Last[run.Timeline] = run;
            foreach (var line in run.Report) Debug.Log("SA Inbetween: " + line, run.Timeline);
            Chain(run);
            Finished?.Invoke(run);
        }

        static void Chain(GenBakeRun run)
        {
            var next = run.Plan.Next;
            if (run.Error != "" || next == null || run.Chain.Contains(next) || !GenPlanner.HasGenTracks(next)) return;
            run.Then = Start(next, null, run, null, out var error);
            run.Report.Add(run.Then != null ? "Next: baking '" + next.name + "' (the timeline's Tl Settings say Next)." : "Next: '" + next.name + "' was not baked: " + error);
        }

        static string Summary(GenBakeRun run)
        {
            var files = run.Jobs.Sum(j => j.Files.Count);
            var head = "Baked '" + run.Timeline.name + "': " + run.Jobs.Count + (run.Jobs.Count == 1 ? " generation" : " generations") + ", " + run.Generated + " generated, " + run.Reused + " already up to date, " + files + (files == 1 ? " file" : " files") + ".";
            return run.Error == "" ? head : head + " Problems: " + run.Error;
        }

        static void WritePlan(GenBakeRun run)
        {
            var path = GenRecipes.PlanPath(run.Timeline);
            if (path == "" || !AssetDatabase.IsValidFolder(GenRecipes.Folder(run.Timeline))) return;
            var fresh = GenPlanner.Plan(run.Timeline, run.Director);
            var text = GenPlanJson.Write(fresh, true, out var digest);
            run.Digest = digest;
            run.PlanPath = path;
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && File.ReadAllText(full) == text) return;
            File.WriteAllText(full, text, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public enum RecipeState
    {
        Incomplete,
        NeedsSetup,
        NotGenerated,
        UpToDate,
        OutOfDate,
        Running,
        Failed,
    }

    public sealed class RecipeRun
    {
        internal struct Step
        {
            public string Label;
            public StudioCommand Command;
            public Action After;
        }

        internal readonly Queue<Step> Steps = new Queue<Step>();
        internal StudioProcess Job;
        internal Step Current;
        internal double StartedAt;
        public int Total;
        public int Finished;
        public string Stage = "";
        public string Error = "";
        public string Log = "";
        public bool Done;
    }

    public static class RecipeRunner
    {
        static readonly Dictionary<string, RecipeRun> Runs = new Dictionary<string, RecipeRun>();
        static bool hooked;

        public static RecipeRun Get(InbetweenRecipe recipe) => Runs.TryGetValue(Key(recipe), out var run) ? run : null;

        static string Key(InbetweenRecipe recipe) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe));

        public static bool IsRunning(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            return run != null && !run.Done;
        }

        public static string OutputPath(InbetweenRecipe recipe)
        {
            var path = AssetDatabase.GetAssetPath(recipe);
            return string.IsNullOrEmpty(path) ? "" : Path.Combine(Path.GetDirectoryName(path) ?? "Assets", recipe.name + ".saclip").Replace('\\', '/');
        }

        public static string Problem(InbetweenRecipe recipe)
        {
            if (recipe.Start.Clip == null) return "Choose the clip the character starts from.";
            if (recipe.Goals.Count == 0) return "Add a goal to move toward.";
            for (var i = 0; i < recipe.Goals.Count; i++)
                if (recipe.Goals[i].Clip == null) return (recipe.Goals.Count == 1 ? "The goal" : "Goal " + (i + 1)) + " has no clip.";
            var output = OutputPath(recipe);
            if (output == "") return "Save the recipe as an asset first.";
            if (SamePath(recipe.Start.Clip, output)) return "This recipe is named like its start clip, so the result would overwrite it. Rename the recipe.";
            foreach (var g in recipe.Goals)
                if (SamePath(g.Clip, output)) return "This recipe is named like one of its goal clips, so the result would overwrite it. Rename the recipe.";
            return "";
        }

        static bool SamePath(SaClipAsset clip, string path) => string.Equals(AssetDatabase.GetAssetPath(clip), path, StringComparison.OrdinalIgnoreCase);

        public static RecipeState StateOf(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            if (run != null && !run.Done) return RecipeState.Running;
            if (run != null && run.Error != "") return RecipeState.Failed;
            if (Problem(recipe) != "") return RecipeState.Incomplete;
            if (!InbetweenSettings.Tools().Complete) return RecipeState.NeedsSetup;
            if (recipe.Output == null) return RecipeState.NotGenerated;
            return recipe.BuiltFrom == Hash(recipe) ? RecipeState.UpToDate : RecipeState.OutOfDate;
        }

        public static string Hash(InbetweenRecipe recipe)
        {
            var sb = new StringBuilder();
            Add(sb, recipe.Start);
            foreach (var g in recipe.Goals) Add(sb, g);
            sb.Append(recipe.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(recipe.LandExactly ? "L" : "l");
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        static void Add(StringBuilder sb, ClipFrame f)
        {
            sb.Append('|').Append(f.Where).Append(f.Seconds.ToString("R", CultureInfo.InvariantCulture));
            if (f.Clip == null) return;
            using var sha = SHA1.Create();
            sb.Append(BitConverter.ToString(sha.ComputeHash(f.Clip.Bytes)));
        }

        public static bool Start(InbetweenRecipe recipe, out string error)
        {
            error = Problem(recipe);
            if (error != "") return false;
            if (IsRunning(recipe)) { error = "Already generating."; return false; }
            var tools = InbetweenSettings.Tools();
            if (!tools.Complete) { error = "Setup needed: " + string.Join(", ", tools.Missing()); return false; }
            if (Application.isPlaying) { error = "Leave Play mode before generating."; return false; }

            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe));
            var work = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "SaInbetween", guid);
            Directory.CreateDirectory(work);
            var outputPath = OutputPath(recipe);
            var s = new StudioSettings
            {
                CliPath = tools.Cli,
                BakePath = tools.Bake,
                ModelPath = tools.Model,
                FamilyPath = tools.Family,
                RestPath = tools.Rest,
                OutputFolder = Path.GetDirectoryName(outputPath),
                WorkFolder = work,
                ClipName = recipe.name,
                TokenOverride = StudioCommands.TokensForSeconds(recipe.DurationSeconds),
                Correct = recipe.LandExactly,
                VerifyModel = InbetweenSettings.VerifyModel,
            };

            var run = new RecipeRun();
            var contexts = new List<string>();
            var targets = new List<string>();
            var steps = new List<RecipeRun.Step>();
            if (!AddPoses(s, run, steps, "Reading the start", recipe.Start, true, Path.Combine(work, "start_{}.pose"), contexts, out error)) return false;
            for (var i = 0; i < recipe.Goals.Count; i++)
            {
                var files = new List<string>();
                if (!AddPoses(s, run, steps, recipe.Goals.Count == 1 ? "Reading the goal" : "Reading goal " + (i + 1), recipe.Goals[i], false, Path.Combine(work, "goal" + i + "_{}.pose"), files, out error)) return false;
                targets.Add(string.Join(",", files));
            }

            s.Contexts = contexts;
            s.Targets = targets;
            if (!StudioCommands.TryGenerate(s, out var generate, out error) || !StudioCommands.TryBake(s, out var bake, out error)) return false;
            steps.Add(new RecipeRun.Step { Label = "Generating the motion", Command = generate });
            var hash = Hash(recipe);
            steps.Add(new RecipeRun.Step
            {
                Label = "Baking the clip",
                Command = bake,
                After = () => Finish(recipe, run, outputPath, hash),
            });
            foreach (var step in steps) run.Steps.Enqueue(step);
            run.Total = steps.Count;
            Runs[Key(recipe)] = run;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return true;
        }

        public static string WorkFolder(InbetweenRecipe recipe) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "SaInbetween", Key(recipe));

        internal static bool PrepareSources(InbetweenRecipe recipe, List<RecipeRun.Step> steps, out StudioSettings s, out string error)
        {
            s = null;
            error = Problem(recipe);
            if (error != "") return false;
            if (IsRunning(recipe)) { error = "Already generating."; return false; }
            var tools = InbetweenSettings.Tools();
            if (!tools.Complete) { error = "Setup needed: " + string.Join(", ", tools.Missing()); return false; }
            if (Application.isPlaying) { error = "Leave Play mode before generating."; return false; }

            var work = WorkFolder(recipe);
            Directory.CreateDirectory(work);
            s = new StudioSettings
            {
                CliPath = tools.Cli,
                BakePath = tools.Bake,
                ModelPath = tools.Model,
                FamilyPath = tools.Family,
                RestPath = tools.Rest,
                OutputFolder = work,
                WorkFolder = work,
                ClipName = recipe.name,
                TokenOverride = StudioCommands.TokensForSeconds(recipe.DurationSeconds),
                Correct = recipe.LandExactly,
                VerifyModel = InbetweenSettings.VerifyModel,
            };

            var run = new RecipeRun();
            var contexts = new List<string>();
            var targets = new List<string>();
            if (!AddPoses(s, run, steps, "Reading the start", recipe.Start, true, Path.Combine(work, "start_{}.pose"), contexts, out error)) return false;
            for (var i = 0; i < recipe.Goals.Count; i++)
            {
                var files = new List<string>();
                if (!AddPoses(s, run, steps, recipe.Goals.Count == 1 ? "Reading the goal" : "Reading goal " + (i + 1), recipe.Goals[i], false, Path.Combine(work, "goal" + i + "_{}.pose"), files, out error)) return false;
                targets.Add(string.Join(",", files));
            }

            s.Contexts = contexts;
            s.Targets = targets;
            return true;
        }

        static bool AddPoses(StudioSettings s, RecipeRun run, List<RecipeRun.Step> steps, string label, ClipFrame frame, bool isStart, string pattern, List<string> files, out string error)
        {
            var clipFile = Path.GetFullPath(AssetDatabase.GetAssetPath(frame.Clip));
            if (!StudioCommands.TryPoses(s, clipFile, Times(frame, isStart), pattern, out var command, out error)) return false;
            steps.Add(new RecipeRun.Step { Label = label, Command = command });
            for (var i = 0; i < 4; i++) files.Add(pattern.Replace("{}", i.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        public static string Times(ClipFrame f, bool isStart)
        {
            switch (f.Where)
            {
                case FrameWhere.End: return "e-3f,e-2f,e-1f,e";
                case FrameWhere.Start: return isStart ? "s,s,s,s" : "s,s+1f,s+2f,s+3f";
                default:
                    var t = Mathf.Max(0f, f.Seconds);
                    var step = 1f / StudioCommands.FrameRate;
                    var from = isStart ? -3 : 0;
                    var sb = new StringBuilder();
                    for (var i = 0; i < 4; i++)
                        sb.Append(i == 0 ? "" : ",").Append(Mathf.Max(0f, t + (from + i) * step).ToString("0.####", CultureInfo.InvariantCulture));
                    return sb.ToString();
            }
        }

        static void Finish(InbetweenRecipe recipe, RecipeRun run, string outputPath, string hash)
        {
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            var clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(outputPath);
            if (clip == null)
            {
                run.Error = "The clip was baked but Unity did not import " + outputPath;
                return;
            }

            Undo.RecordObject(recipe, "Generate inbetween");
            recipe.Output = clip;
            recipe.BuiltFrom = hash;
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
        }

        public static void Cancel(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            if (run == null || run.Done) return;
            run.Steps.Clear();
            run.Job?.Kill();
            run.Error = "Cancelled.";
            run.Done = true;
        }

        static void Tick()
        {
            var any = false;
            foreach (var run in Runs.Values)
            {
                if (run.Done) continue;
                any = true;
                if (run.Job == null)
                {
                    if (run.Steps.Count == 0) { run.Done = true; continue; }
                    run.Current = run.Steps.Dequeue();
                    run.Stage = run.Current.Label;
                    run.StartedAt = EditorApplication.timeSinceStartup;
                    run.Job = new StudioProcess();
                    if (!run.Job.Start(run.Current.Command, out var error)) Fail(run, error);
                    continue;
                }

                if (run.Job.Running) continue;
                if (run.Job.ExitCode == int.MinValue || run.Job.Handled) continue;
                run.Job.MarkHandled();
                run.Log = run.Job.Log;
                if (run.Job.ExitCode != 0)
                {
                    Fail(run, run.Current.Label + " failed (exit " + run.Job.ExitCode + ").");
                    continue;
                }

                run.Finished++;
                run.Current.After?.Invoke();
                run.Job = null;
                if (run.Error != "") run.Done = true;
                else if (run.Steps.Count == 0) run.Done = true;
            }

            InternalEditorUtility.RepaintAllViews();
            if (!any && Runs.Count > 0)
            {
                EditorApplication.update -= Tick;
                hooked = false;
            }
        }

        static void Fail(RecipeRun run, string message)
        {
            run.Error = message;
            run.Steps.Clear();
            run.Done = true;
            run.Job = null;
        }
    }
}

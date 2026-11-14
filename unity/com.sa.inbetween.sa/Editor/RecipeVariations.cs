using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    [Serializable]
    public struct VariationSpec
    {
        public string Kind;
        public int TokenOverride;
        public int TokensMin;
        public int TokensMax;
        public bool Correct;
    }

    public sealed class VariationCandidate
    {
        public VariationSpec Spec;
        public string Folder = "";
        public string ClipFile = "";
        public string Error = "";
        public bool Ready;
    }

    public sealed class VariationRun
    {
        internal struct Job
        {
            public string Label;
            public StudioCommand Command;
            public int Candidate;
            public bool Bake;
        }

        internal readonly Queue<Job> Jobs = new Queue<Job>();
        internal readonly Queue<VariationSpec> Spares = new Queue<VariationSpec>();
        internal StudioSettings Settings;
        internal StudioProcess Process;
        internal Job Current;
        public readonly List<VariationCandidate> Candidates = new List<VariationCandidate>();
        public int Total;
        public int Finished;
        public string Stage = "";
        public string Error = "";
        public string Log = "";
        public bool Done;
    }

    public static class RecipeVariations
    {
        public const int MaxCandidates = 4;
        public const string SpecFile = "variation.json";
        public const string KeepUndoName = "Keep variation";

        static readonly Dictionary<string, VariationRun> Runs = new Dictionary<string, VariationRun>();
        static bool hooked;

        public static List<VariationSpec> All(int tokens, bool correct)
        {
            tokens = Mathf.Clamp(tokens, StudioCommands.MinTokens, StudioCommands.MaxTokens);
            var all = new List<VariationSpec> { Fixed("As set", tokens, correct) };
            if (tokens - 1 >= StudioCommands.MinTokens) all.Add(Fixed("Shorter", tokens - 1, correct));
            if (tokens + 1 <= StudioCommands.MaxTokens) all.Add(Fixed("Longer", tokens + 1, correct));
            all.Add(new VariationSpec
            {
                Kind = "Model picks",
                TokensMin = Mathf.Max(StudioCommands.MinTokens, tokens - 1),
                TokensMax = Mathf.Min(StudioCommands.MaxTokens, tokens + 1),
                Correct = correct,
            });
            all.Add(Fixed("Other landing", tokens, !correct));
            if (tokens - 1 >= StudioCommands.MinTokens) all.Add(Fixed("Shorter", tokens - 1, !correct));
            if (tokens + 1 <= StudioCommands.MaxTokens) all.Add(Fixed("Longer", tokens + 1, !correct));
            return all;
        }

        public static List<VariationSpec> Plan(int tokens, bool correct)
        {
            var all = All(tokens, correct);
            return all.GetRange(0, Mathf.Min(MaxCandidates, all.Count));
        }

        public static List<VariationSpec> Spares(int tokens, bool correct)
        {
            var all = All(tokens, correct);
            return all.GetRange(Mathf.Min(MaxCandidates, all.Count), all.Count - Mathf.Min(MaxCandidates, all.Count));
        }

        public static int SameAs(IReadOnlyList<byte[]> earlier, byte[] bytes)
        {
            for (var i = 0; i < earlier.Count; i++)
                if (earlier[i] != null && ClipFileUndo.Same(earlier[i], bytes))
                    return i;
            return -1;
        }

        static VariationSpec Fixed(string kind, int tokens, bool correct) => new VariationSpec
        {
            Kind = kind,
            TokenOverride = tokens,
            TokensMin = Mathf.Min(8, tokens),
            TokensMax = Mathf.Max(14, tokens),
            Correct = correct,
        };

        public static string Describe(VariationSpec spec)
        {
            var length = spec.TokenOverride != 0
                ? StudioCommands.SecondsForTokens(spec.TokenOverride).ToString("0.00") + " s"
                : StudioCommands.SecondsForTokens(spec.TokensMin).ToString("0.00") + "-" + StudioCommands.SecondsForTokens(spec.TokensMax).ToString("0.00") + " s";
            return spec.Kind + ", " + length + (spec.Correct ? "" : ", loose");
        }

        public static int TokensForFrames(int frames, int moves)
        {
            var perMove = (float)frames / Mathf.Max(1, moves);
            return Mathf.Clamp(Mathf.RoundToInt((perMove + 6f) / 4f), StudioCommands.MinTokens, StudioCommands.MaxTokens);
        }

        public static float KeptSeconds(VariationSpec spec, int frames, int moves) =>
            StudioCommands.SecondsForTokens(spec.TokenOverride != 0 ? spec.TokenOverride : TokensForFrames(frames, moves));

        public static StudioSettings Apply(StudioSettings source, VariationSpec spec, string folder) => new StudioSettings
        {
            CliPath = source.CliPath,
            BakePath = source.BakePath,
            ModelPath = source.ModelPath,
            FamilyPath = source.FamilyPath,
            RestPath = source.RestPath,
            OutputFolder = folder,
            WorkFolder = folder,
            Contexts = source.Contexts,
            ContextEnd = source.ContextEnd,
            Targets = source.Targets,
            TokensMin = spec.TokensMin,
            TokensMax = spec.TokensMax,
            TokenOverride = spec.TokenOverride,
            Correct = spec.Correct,
            VerifyModel = source.VerifyModel,
            ClipName = source.ClipName,
        };

        static string Key(InbetweenRecipe recipe) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe));

        public static string Folder(InbetweenRecipe recipe) => Path.Combine(RecipeRunner.WorkFolder(recipe), "variations");

        public static VariationRun Get(InbetweenRecipe recipe)
        {
            var key = Key(recipe);
            if (string.IsNullOrEmpty(key))
                return null;
            if (Runs.TryGetValue(key, out var run))
                return run;
            run = Restore(recipe);
            if (run != null)
                Runs[key] = run;
            return run;
        }

        public static bool IsRunning(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            return run != null && !run.Done;
        }

        public static bool AnyRunning()
        {
            foreach (var run in Runs.Values)
                if (!run.Done) return true;
            return false;
        }

        internal static List<string> Interrupt()
        {
            var stopped = new List<string>();
            foreach (var pair in Runs)
            {
                if (pair.Value.Done) continue;
                pair.Value.Jobs.Clear();
                pair.Value.Process?.Kill();
                pair.Value.Process = null;
                pair.Value.Error = RecipeRunner.InterruptedMessage;
                pair.Value.Done = true;
                stopped.Add("variations:" + pair.Key);
            }

            return stopped;
        }

        static VariationRun Restore(InbetweenRecipe recipe)
        {
            var folder = Folder(recipe);
            if (!Directory.Exists(folder))
                return null;
            var run = new VariationRun { Done = true };
            for (var i = 0; i < MaxCandidates; i++)
            {
                var dir = Path.Combine(folder, i.ToString());
                var spec = Path.Combine(dir, SpecFile);
                var clip = Path.Combine(dir, recipe.name + ".saclip");
                if (!File.Exists(spec) || !File.Exists(clip))
                    continue;
                run.Candidates.Add(new VariationCandidate { Spec = JsonUtility.FromJson<VariationSpec>(File.ReadAllText(spec)), Folder = dir, ClipFile = clip, Ready = true });
            }

            return run.Candidates.Count == 0 ? null : run;
        }

        public static bool Start(InbetweenRecipe recipe, out string error)
        {
            if (IsRunning(recipe)) { error = "Already making variations."; return false; }
            var steps = new List<RecipeRun.Step>();
            if (!RecipeRunner.PrepareSources(recipe, steps, out var settings, out error)) return false;

            var folder = Folder(recipe);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var run = new VariationRun();
            foreach (var step in steps)
                run.Jobs.Enqueue(new VariationRun.Job { Label = step.Label, Command = step.Command, Candidate = -1 });
            run.Settings = settings;
            var tokens = StudioCommands.TokensForSeconds(recipe.DurationSeconds);
            foreach (var spare in Spares(tokens, recipe.LandExactly)) run.Spares.Enqueue(spare);
            var specs = Plan(tokens, recipe.LandExactly);
            for (var i = 0; i < specs.Count; i++)
            {
                run.Candidates.Add(new VariationCandidate { Folder = Path.Combine(folder, i.ToString()) });
                if (!Enqueue(run, i, specs[i], out error)) return false;
            }

            run.Total = run.Jobs.Count;
            Runs[Key(recipe)] = run;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return true;
        }

        static bool Enqueue(VariationRun run, int index, VariationSpec spec, out string error)
        {
            var candidate = run.Candidates[index];
            Directory.CreateDirectory(candidate.Folder);
            var s = Apply(run.Settings, spec, candidate.Folder);
            if (!StudioCommands.TryGenerate(s, out var generate, out error) || !StudioCommands.TryBake(s, out var bake, out error)) return false;
            File.WriteAllText(Path.Combine(candidate.Folder, SpecFile), JsonUtility.ToJson(spec));
            candidate.Spec = spec;
            candidate.ClipFile = StudioCommands.ClipPath(s);
            candidate.Ready = false;
            candidate.Error = "";
            var name = "variation " + (index + 1) + " (" + spec.Kind + ")";
            run.Jobs.Enqueue(new VariationRun.Job { Label = "Generating " + name, Command = generate, Candidate = index });
            run.Jobs.Enqueue(new VariationRun.Job { Label = "Baking " + name, Command = bake, Candidate = index, Bake = true });
            return true;
        }

        static void Baked(VariationRun run, int index)
        {
            var candidate = run.Candidates[index];
            if (!File.Exists(candidate.ClipFile))
            {
                candidate.Error = "The baker wrote no clip.";
                return;
            }

            var bytes = File.ReadAllBytes(candidate.ClipFile);
            var earlier = new List<byte[]>();
            for (var i = 0; i < index; i++)
                earlier.Add(run.Candidates[i].Ready ? File.ReadAllBytes(run.Candidates[i].ClipFile) : null);
            var same = SameAs(earlier, bytes);
            if (same < 0)
            {
                candidate.Ready = true;
                return;
            }

            while (run.Spares.Count > 0)
            {
                if (Enqueue(run, index, run.Spares.Dequeue(), out _))
                {
                    run.Total += 2;
                    return;
                }
            }

            candidate.Error = "Same result as variation " + (same + 1) + ".";
        }

        public static void Cancel(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            if (run == null || run.Done) return;
            run.Jobs.Clear();
            run.Process?.Kill();
            run.Process = null;
            run.Error = "Cancelled.";
            run.Done = true;
        }

        public static void Discard(InbetweenRecipe recipe)
        {
            Cancel(recipe);
            Runs.Remove(Key(recipe));
            var folder = Folder(recipe);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        public static bool Keep(InbetweenRecipe recipe, int index, out string error)
        {
            error = "";
            var run = Get(recipe);
            if (run == null || index < 0 || index >= run.Candidates.Count || !run.Candidates[index].Ready || !File.Exists(run.Candidates[index].ClipFile))
            {
                error = "That variation is not ready.";
                return false;
            }

            if (RecipeRunner.IsRunning(recipe) || !run.Done)
            {
                error = "Wait until generating has finished.";
                return false;
            }

            var candidate = run.Candidates[index];
            var bytes = File.ReadAllBytes(candidate.ClipFile);
            if (!SaClipPose.TryLoad(bytes, out var pose, out error))
                return false;
            var output = RecipeRunner.OutputPath(recipe);
            if (output == "")
            {
                error = "Save the recipe as an asset first.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(KeepUndoName);
            var group = Undo.GetCurrentGroup();
            var record = ClipFileUndo.For(output, recipe);
            Undo.RecordObject(record, KeepUndoName);
            record.Commit(bytes);
            var animPath = recipe.SaveAnimationClip && SaClipExport.CharacterFor(recipe) != null ? SaClipExport.ExportPath(recipe) : "";
            var animRecord = animPath == "" ? null : ClipFileUndo.For(animPath, null);
            if (animRecord != null) Undo.RecordObject(animRecord, KeepUndoName);
            Undo.RecordObject(recipe, KeepUndoName);
            File.WriteAllBytes(output, bytes);
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceUpdate);
            var clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(output);
            if (clip == null)
            {
                Undo.RevertAllDownToGroup(group);
                error = "Unity did not import " + output;
                return false;
            }

            recipe.Output = clip;
            recipe.DurationSeconds = KeptSeconds(candidate.Spec, pose.SampleCount, recipe.Goals.Count);
            recipe.LandExactly = candidate.Spec.Correct;
            recipe.BuiltFrom = RecipeRunner.Hash(recipe);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            SaClipExport.SaveAfterGenerate(recipe);
            if (animRecord != null) animRecord.Commit(File.Exists(animPath) ? File.ReadAllBytes(animPath) : Array.Empty<byte>());
            Undo.CollapseUndoOperations(group);
            Runs.Remove(Key(recipe));
            var folder = Folder(recipe);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            return true;
        }

        static void Tick()
        {
            var any = false;
            foreach (var run in Runs.Values)
            {
                if (run.Done) continue;
                any = true;
                if (run.Process == null)
                {
                    if (!Next(run)) { Finish(run); continue; }
                    run.Stage = run.Current.Label;
                    run.Process = new StudioProcess();
                    if (!run.Process.Start(run.Current.Command, out var error)) Failed(run, error);
                    continue;
                }

                if (run.Process.Running) continue;
                if (run.Process.ExitCode == int.MinValue || run.Process.Handled) continue;
                run.Process.MarkHandled();
                run.Log = run.Process.Log;
                var code = run.Process.ExitCode;
                run.Process = null;
                run.Finished++;
                if (code != 0)
                {
                    Failed(run, run.Current.Label + " failed (exit " + code + ")." + StudioProcess.ExitHint(code));
                    continue;
                }

                if (run.Current.Bake)
                    Baked(run, run.Current.Candidate);
            }

            InternalEditorUtility.RepaintAllViews();
            if (!any)
            {
                EditorApplication.update -= Tick;
                hooked = false;
            }
        }

        static bool Next(VariationRun run)
        {
            while (run.Jobs.Count > 0)
            {
                var job = run.Jobs.Dequeue();
                if (job.Candidate >= 0 && run.Candidates[job.Candidate].Error != "")
                {
                    run.Finished++;
                    continue;
                }

                run.Current = job;
                return true;
            }

            return false;
        }

        static void Failed(VariationRun run, string message)
        {
            run.Process = null;
            if (run.Current.Candidate >= 0)
            {
                run.Candidates[run.Current.Candidate].Error = message;
                return;
            }

            run.Error = message;
            run.Jobs.Clear();
            run.Done = true;
        }

        static void Finish(VariationRun run)
        {
            run.Done = true;
            run.Stage = "";
            var ready = 0;
            foreach (var c in run.Candidates)
                if (c.Ready) ready++;
            if (ready == 0 && run.Error == "")
                run.Error = run.Candidates.Count > 0 && run.Candidates[0].Error != "" ? run.Candidates[0].Error : "No variation could be made.";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using SA.Editor;

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

    [InitializeOnLoad]
    public static class RecipeRunner
    {
        public const string GenerateUndoName = "Generate inbetween";
        public const string InterruptedKey = "SA.Inbetween.interrupted";
        public const string InterruptedMessage = "Generating stopped because Unity reloaded its scripts (a script changed, or Play mode started). Nothing was written to the project. Press Generate again.";

        static readonly Dictionary<string, RecipeRun> Runs = new Dictionary<string, RecipeRun>();
        static bool hooked;

        static RecipeRunner()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Interrupt;
            AssemblyReloadEvents.beforeAssemblyReload += Interrupt;
            EditorApplication.playModeStateChanged -= HoldPlayMode;
            EditorApplication.playModeStateChanged += HoldPlayMode;
            RestoreInterrupted();
        }

        internal static void ForgetAll() => Runs.Clear();

        public static bool AnyRunning()
        {
            foreach (var run in Runs.Values)
                if (!run.Done) return true;
            return RecipeVariations.AnyRunning();
        }

        static void HoldPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || !AnyRunning()) return;
            EditorApplication.isPlaying = false;
            Debug.LogWarning("SA Inbetween: Play mode was not started because a bridge is still generating. Wait until the recipe says Up to date (a few seconds), or press Cancel, then press Play.");
            foreach (SceneView view in SceneView.sceneViews)
                view.ShowNotification(new GUIContent("Still generating: Play mode waits until it finishes"), 4.0);
        }

        public static void Interrupt()
        {
            var stopped = new List<string>();
            foreach (var pair in Runs)
            {
                if (pair.Value.Done) continue;
                pair.Value.Steps.Clear();
                pair.Value.Job?.Kill();
                pair.Value.Job = null;
                pair.Value.Error = InterruptedMessage;
                pair.Value.Done = true;
                stopped.Add(pair.Key);
            }

            stopped.AddRange(RecipeVariations.Interrupt());
            if (stopped.Count == 0) return;
            var earlier = SessionState.GetString(InterruptedKey, "");
            SessionState.SetString(InterruptedKey, (earlier == "" ? "" : earlier + "\n") + string.Join("\n", stopped));
        }

        public static void RestoreInterrupted()
        {
            var keys = SessionState.GetString(InterruptedKey, "");
            SessionState.EraseString(InterruptedKey);
            if (keys == "") return;
            foreach (var key in keys.Split('\n'))
                if (key != "" && !key.StartsWith("variations:", StringComparison.Ordinal) && !Runs.ContainsKey(key))
                    Runs[key] = new RecipeRun { Done = true, Error = InterruptedMessage, Stage = "Interrupted" };
        }

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
            var start = SourceProblem(recipe.Start, "the character starts from", "The start");
            if (start != "") return start;
            if (recipe.Goals.Count == 0) return "Add a goal to move toward.";
            for (var i = 0; i < recipe.Goals.Count; i++)
            {
                var name = recipe.Goals.Count == 1 ? "The goal" : "Goal " + (i + 1);
                if (recipe.Goals[i].Kind == SourceKind.SaClip && recipe.Goals[i].Clip == null) return name + " has no clip.";
                var goal = SourceProblem(recipe.Goals[i], "for " + name.ToLowerInvariant(), name);
                if (goal != "") return goal;
            }
            var output = OutputPath(recipe);
            if (output == "") return "Save the recipe as an asset first.";
            if (SamePath(recipe.Start, output)) return "This recipe is named like its start clip, so the result would overwrite it. Rename the recipe.";
            foreach (var g in recipe.Goals)
                if (SamePath(g, output)) return "This recipe is named like one of its goal clips, so the result would overwrite it. Rename the recipe.";
            return Overwrites(recipe);
        }

        public static string Overwrites(InbetweenRecipe recipe)
        {
            var written = new[] { SaClipExport.ExportPath(recipe), SaClipHumanoidExport.ExportPath(recipe), SaClipFbxExport.ExportPath(recipe) };
            var sources = new List<UnityEngine.Object> { recipe.Character, recipe.Start.Motion, recipe.Start.Character };
            foreach (var g in recipe.Goals) { sources.Add(g.Motion); sources.Add(g.Character); }
            foreach (var source in sources)
            {
                if (source == null) continue;
                var path = AssetDatabase.GetAssetPath(source);
                foreach (var target in written)
                    if (target != "" && string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
                        return "This recipe is named like '" + Path.GetFileName(path) + "', so saving its result would overwrite that file. Rename the recipe (for example " + recipe.name + "_bridge).";
            }

            return "";
        }

        static string SourceProblem(ClipFrame f, string role, string name)
        {
            if (f.Kind == SourceKind.SaClip) return f.Clip == null ? "Choose the clip " + role + "." : "";
            if (f.Kind == SourceKind.Pose) return f.Pose == null ? "Choose the pose " + role + "." : f.Pose.Valid ? "" : name + " pose cannot be read: " + f.Pose.Problem;
            if (f.Motion == null) return "Choose the Unity clip " + role + ".";
            if (f.Character == null) return name + " needs a character to play its Unity clip on.";
            if (f.Motion.legacy) return name + ": '" + f.Motion.name + "' is a Legacy clip. Select its FBX, set Rig > Animation Type to Humanoid (or Generic) and press Apply.";
            return PastEnd(f, name);
        }

        public static string PastEnd(ClipFrame f, string name)
        {
            if (f.Kind != SourceKind.UnityClip || f.Motion == null || f.Motion.length <= 0f) return "";
            if (f.Where != FrameWhere.Frame && f.Where != FrameWhere.Time) return "";
            var rate = f.FrameRate;
            var last = Mathf.Max(0, Mathf.RoundToInt(f.Motion.length * rate));
            if (f.AnchorSeconds <= f.Motion.length + 0.5f / rate) return "";
            return f.Where == FrameWhere.Frame
                ? name + " is at frame " + f.Frame + ", past the end of '" + f.Motion.name + "' (frames 0 to " + last + ", " + f.Motion.length.ToString("0.00", CultureInfo.InvariantCulture) + " s). Choose a frame up to " + last + "."
                : name + " is at " + f.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s, past the end of '" + f.Motion.name + "' (" + f.Motion.length.ToString("0.00", CultureInfo.InvariantCulture) + " s). Choose a time up to " + f.Motion.length.ToString("0.00", CultureInfo.InvariantCulture) + " s.";
        }

        static bool SamePath(ClipFrame f, string path) => f.Kind == SourceKind.SaClip && string.Equals(AssetDatabase.GetAssetPath(f.Clip), path, StringComparison.OrdinalIgnoreCase);

        public static RecipeState StateOf(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            if (run != null && !run.Done) return RecipeState.Running;
            if (run != null && run.Error != "") return RecipeState.Failed;
            if (Problem(recipe) != "") return RecipeState.Incomplete;
            if (!InbetweenSettings.Tools().Complete) return RecipeState.NeedsSetup;
            if (recipe.Output == null) return RecipeState.NotGenerated;
            if (recipe.BuiltFrom != Hash(recipe)) return RecipeState.OutOfDate;
            return SaClipExport.SavedClipStale(recipe) ? RecipeState.OutOfDate : RecipeState.UpToDate;
        }

        public static string Hash(InbetweenRecipe recipe)
        {
            var sb = new StringBuilder();
            Add(sb, recipe.Start);
            foreach (var g in recipe.Goals) Add(sb, g);
            sb.Append(recipe.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(recipe.LandExactly ? "L" : "l");
            if (recipe.ModelPicksLength) sb.Append("|pick");
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        static void Add(StringBuilder sb, ClipFrame f)
        {
            sb.Append('|').Append(f.Where).Append(f.AnchorSeconds.ToString("R", CultureInfo.InvariantCulture));
            if (f.Kind == SourceKind.Pose)
            {
                sb.Append("|pose|");
                if (f.Pose == null || f.Pose.Values == null) return;
                var raw = new byte[f.Pose.Values.Length * sizeof(float)];
                Buffer.BlockCopy(f.Pose.Values, 0, raw, 0, raw.Length);
                using var poseSha = SHA1.Create();
                sb.Append(BitConverter.ToString(poseSha.ComputeHash(raw)));
                return;
            }
            if (f.Kind == SourceKind.UnityClip)
            {
                sb.Append("|unity|").Append(f.Frame.ToString(CultureInfo.InvariantCulture)).Append('|').Append(SourceKey(f));
                return;
            }
            if (f.Clip == null) return;
            using var sha = SHA1.Create();
            sb.Append(BitConverter.ToString(sha.ComputeHash(f.Clip.Bytes)));
        }

        public const float SourceRate = 30f;
        const string SourceSampling = "fixed2";

        public static string SourceKey(ClipFrame f)
        {
            var sb = new StringBuilder();
            sb.Append(SourceRate.ToString("R", CultureInfo.InvariantCulture)).Append(SourceSampling).Append('|').Append(FamilyVersion());
            AppendAsset(sb, f.Motion);
            AppendAsset(sb, f.Character);
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        static void AppendAsset(StringBuilder sb, UnityEngine.Object asset)
        {
            sb.Append('|');
            if (asset == null) { sb.Append("none"); return; }
            var path = AssetDatabase.GetAssetPath(asset);
            if (path == "") { sb.Append(asset.name).Append('|').Append(GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString()); return; }
            sb.Append(GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString()).Append('|').Append(ContentHash(path));
        }

        static readonly Dictionary<string, (long, long, string)> Contents = new Dictionary<string, (long, long, string)>();

        public static string ContentHash(string path)
        {
            var full = Path.GetFullPath(path);
            var info = new FileInfo(full);
            if (!info.Exists) return "missing";
            var meta = new FileInfo(full + ".meta");
            var stamp = info.Length ^ (meta.Exists ? meta.Length * 31 : 0);
            var ticks = info.LastWriteTimeUtc.Ticks ^ (meta.Exists ? meta.LastWriteTimeUtc.Ticks : 0);
            if (Contents.TryGetValue(full, out var known) && known.Item1 == stamp && known.Item2 == ticks) return known.Item3;
            using var sha = SHA1.Create();
            var sb = new StringBuilder();
            if (IsYaml(full)) sb.Append(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(WithoutLines(File.ReadAllText(full), "m_Name:")))));
            else using (var stream = File.OpenRead(full)) sb.Append(BitConverter.ToString(sha.ComputeHash(stream)));
            if (meta.Exists) sb.Append('|').Append(BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(WithoutLines(File.ReadAllText(meta.FullName), "timeCreated:")))));
            var hash = sb.ToString().Replace("-", "").ToLowerInvariant();
            Contents[full] = (stamp, ticks, hash);
            return hash;
        }

        static bool IsYaml(string path)
        {
            var head = new byte[5];
            using (var stream = File.OpenRead(path))
                if (stream.Read(head, 0, head.Length) < head.Length) return false;
            return Encoding.ASCII.GetString(head) == "%YAML";
        }

        static string WithoutLines(string text, string key)
        {
            var sb = new StringBuilder();
            foreach (var line in text.Split('\n'))
                if (!line.TrimStart().StartsWith(key, StringComparison.Ordinal))
                    sb.Append(line).Append('\n');
            return sb.ToString();
        }

        static readonly Lazy<string> Family = new Lazy<string>(() =>
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FamilyClips).Assembly);
            if (info == null) return "unknown";
            return info.version + (info.git != null ? "@" + info.git.hash : "");
        });

        static string FamilyVersion() => Family.Value;

        public static string SourceFile(ClipFrame f, string work) => Path.Combine(work, "src_" + SourceKey(f).Substring(0, 16) + ".saclip");

        public static bool TryBakeSource(ClipFrame f, string work, out string file, out string error)
        {
            file = "";
            error = SourceProblem(f, "to bake", "The source");
            if (error != "") return false;
            if (f.Kind == SourceKind.SaClip)
            {
                file = Path.GetFullPath(AssetDatabase.GetAssetPath(f.Clip));
                return true;
            }
            if (f.Kind == SourceKind.Pose)
            {
                error = "A pose source has no clip to bake.";
                return false;
            }
            file = SourceFile(f, work);
            if (File.Exists(file)) return true;
            if (!SaClipConvert.TryConvert(f.Character, f.Motion, out var bytes, out error))
            {
                error = FailureAdvice.ForConversion(f.Motion.name, f.Character.name, error);
                return false;
            }
            Directory.CreateDirectory(work);
            var temporary = file + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temporary, file);
            return true;
        }

        public static byte[] PreviewBytes(InbetweenRecipe recipe, ClipFrame f)
        {
            if (f.Kind == SourceKind.SaClip) return f.Clip == null ? null : f.Clip.Bytes;
            if (f.Kind == SourceKind.Pose) return null;
            if (f.Motion == null || f.Character == null) return null;
            var file = SourceFile(f, WorkFolder(recipe));
            if (!File.Exists(file)) return null;
            if (Baked.TryGetValue(file, out var cached)) return cached;
            var bytes = File.ReadAllBytes(file);
            Baked[file] = bytes;
            return bytes;
        }

        static readonly Dictionary<string, byte[]> Baked = new Dictionary<string, byte[]>();

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
                OutputFolder = work,
                WorkFolder = work,
                ClipName = recipe.name,
                Correct = recipe.LandExactly,
                VerifyModel = InbetweenSettings.VerifyModel,
            };
            ApplyLength(s, recipe);

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
            var baked = StudioCommands.ClipPath(s);
            if (File.Exists(baked)) File.Delete(baked);
            steps.Add(new RecipeRun.Step
            {
                Label = "Baking the clip",
                Command = bake,
                After = () => Finish(recipe, run, baked, outputPath, hash),
            });
            foreach (var step in steps) run.Steps.Enqueue(step);
            run.Total = steps.Count;
            Runs[Key(recipe)] = run;
            if (!hooked) { EditorApplication.update += Tick; hooked = true; }
            return true;
        }

        public static void ApplyLength(StudioSettings s, InbetweenRecipe recipe)
        {
            var tokens = StudioCommands.TokensForSeconds(recipe.DurationSeconds);
            if (!recipe.ModelPicksLength)
            {
                s.TokenOverride = tokens;
                return;
            }

            s.TokenOverride = 0;
            s.TokensMin = Mathf.Max(StudioCommands.MinTokens, tokens - 1);
            s.TokensMax = Mathf.Min(StudioCommands.MaxTokens, tokens + 1);
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
                Correct = recipe.LandExactly,
                VerifyModel = InbetweenSettings.VerifyModel,
            };
            ApplyLength(s, recipe);

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
            if (frame.Kind == SourceKind.Pose)
                return CopyPose(frame, pattern, files, out error);
            if (!TryBakeSource(frame, s.WorkFolder, out var clipFile, out error)) return false;
            var rootLast = isStart && frame.Kind == SourceKind.UnityClip;
            if (!StudioCommands.TryPoses(s, clipFile, Times(frame, isStart), pattern, rootLast, out var command, out error)) return false;
            steps.Add(new RecipeRun.Step { Label = label, Command = command });
            for (var i = 0; i < 4; i++) files.Add(pattern.Replace("{}", i.ToString(CultureInfo.InvariantCulture)));
            return true;
        }

        static bool CopyPose(ClipFrame frame, string pattern, List<string> files, out string error)
        {
            error = SourceProblem(frame, "to use", "The source");
            if (error != "") return false;
            var raw = new byte[frame.Pose.Values.Length * sizeof(float)];
            Buffer.BlockCopy(frame.Pose.Values, 0, raw, 0, raw.Length);
            for (var i = 0; i < 4; i++)
            {
                var file = pattern.Replace("{}", i.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
                File.WriteAllBytes(file, raw);
                files.Add(file);
            }

            return true;
        }

        public static string Times(ClipFrame f, bool isStart)
        {
            switch (f.Where)
            {
                case FrameWhere.End: return "e-3f,e-2f,e-1f,e";
                case FrameWhere.Start: return isStart ? "s,s,s,s" : "s,s+1f,s+2f,s+3f";
                default:
                    var t = Mathf.Max(0f, f.AnchorSeconds);
                    var step = 1f / StudioCommands.FrameRate;
                    var from = isStart ? -3 : 0;
                    var sb = new StringBuilder();
                    for (var i = 0; i < 4; i++)
                        sb.Append(i == 0 ? "" : ",").Append(Mathf.Max(0f, t + (from + i) * step).ToString("0.####", CultureInfo.InvariantCulture));
                    return sb.ToString();
            }
        }

        internal static void Finish(InbetweenRecipe recipe, RecipeRun run, string baked, string outputPath, string hash)
        {
            if (!File.Exists(baked) || new FileInfo(baked).Length == 0)
            {
                run.Error = "Baking finished but wrote no clip. Press Generate again; if it happens again, press Copy diagnostics and report it.";
                return;
            }

            if (recipe == null)
            {
                run.Error = "The recipe was deleted while it was generating.";
                return;
            }

            if (!SaClipPose.TryLoad(File.ReadAllBytes(baked), out _, out var unreadable))
            {
                run.Error = "The baked clip cannot be read (" + unreadable + "). Press Generate again; if it happens again, press Copy diagnostics and report it.";
                return;
            }

            outputPath = OutputPath(recipe) == "" ? outputPath : OutputPath(recipe);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(GenerateUndoName);
            var group = Undo.GetCurrentGroup();
            var clipRecord = ClipFileUndo.For(outputPath, recipe);
            Undo.RecordObject(clipRecord, GenerateUndoName);
            var animPath = recipe.SaveAnimationClip && SaClipExport.CharacterFor(recipe) != null ? SaClipExport.ExportPath(recipe) : "";
            var animRecord = animPath == "" ? null : ClipFileUndo.For(animPath, null);
            if (animRecord != null) Undo.RecordObject(animRecord, GenerateUndoName);
            Undo.RecordObject(recipe, GenerateUndoName);
            Place(baked, outputPath);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            var clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(outputPath);
            if (clip == null)
            {
                Undo.RevertAllDownToGroup(group);
                run.Error = "The clip was baked but Unity did not import " + outputPath + ". Check that the folder is writable, then press Generate again.";
                return;
            }

            recipe.Output = clip;
            recipe.BuiltFrom = hash;
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssetIfDirty(recipe);
            SaClipExport.SaveAfterGenerate(recipe);
            clipRecord.Commit(File.ReadAllBytes(outputPath));
            if (animRecord != null) animRecord.Commit(File.Exists(animPath) ? File.ReadAllBytes(animPath) : Array.Empty<byte>());
            Undo.CollapseUndoOperations(group);
        }

        static void Place(string from, string to)
        {
            var full = Path.GetFullPath(to);
            var folder = Path.GetDirectoryName(full) ?? ".";
            Directory.CreateDirectory(folder);
            var partial = Path.Combine(folder, "." + Path.GetFileName(full) + ".partial");
            File.Copy(from, partial, true);
            if (File.Exists(full)) File.Delete(full);
            File.Move(partial, full);
        }

        public static void Cancel(InbetweenRecipe recipe)
        {
            var run = Get(recipe);
            if (run == null || run.Done) return;
            run.Steps.Clear();
            run.Job?.Kill();
            run.Job = null;
            run.Error = "Cancelled. Nothing was written to the project; the previous result, if any, is unchanged.";
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
                    if (!run.Job.Start(run.Current.Command, out var error)) Fail(run, FailureAdvice.ForStart(error));
                    continue;
                }

                if (run.Job.Running)
                {
                    var limit = InbetweenSettings.StepLimitSeconds;
                    if (EditorApplication.timeSinceStartup - run.StartedAt <= limit) continue;
                    run.Job.Kill();
                    run.Log = run.Job.Log;
                    Fail(run, FailureAdvice.TookTooLong(run.Current.Label, limit));
                    continue;
                }

                if (run.Job.ExitCode == int.MinValue || run.Job.Handled) continue;
                run.Job.MarkHandled();
                run.Log = run.Job.Log;
                if (run.Job.ExitCode != 0)
                {
                    Fail(run, FailureAdvice.ForExit(run.Current.Label, run.Job.ExitCode, run.Log));
                    continue;
                }

                run.Finished++;
                try
                {
                    run.Current.After?.Invoke();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    run.Error = "The result could not be written into the project: " + e.Message + ". Check that the folder is writable and the file is not open in another program, then press Generate again.";
                }

                run.Job = null;
                if (run.Error != "") Remember(run);
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
            Remember(run);
        }

        public const string LastFailureKey = "SA.Inbetween.lastFailure";

        public static string LastFailure => SessionState.GetString(LastFailureKey, "");

        static void Remember(RecipeRun run) =>
            SessionState.SetString(LastFailureKey, run.Stage + ": " + run.Error + "\n--- tool log (last lines) ---\n" + FailureAdvice.Tail(run.Log, 40));
    }
}

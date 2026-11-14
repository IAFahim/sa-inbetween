using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public static class InbetweenDiagnostics
    {
        public const string ModelSha256 = "856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb";
        public const long ModelBytes = 757864273;
        public const string IssuesUrl = "https://github.com/IAFahim/sa-inbetween/issues/new?template=bug-report.md";
        public const string GuideUrl = "https://github.com/IAFahim/sa-inbetween/blob/main/docs/designer-guide.md";
        public const string InstallUrl = "https://github.com/IAFahim/sa-inbetween/blob/main/docs/install.md";

        static readonly string[] Packages =
        {
            "com.sa.inbetween.sa", "com.sa.inbetween.sa.timeline", "com.sa.unity", "com.sa.motion.unity",
            "com.unity.formats.fbx", "com.unity.entities", "com.unity.timeline",
        };

        static readonly Dictionary<string, (long, long, string)> Hashes = new Dictionary<string, (long, long, string)>();

        public static string Build(InbetweenRecipe recipe)
        {
            var sb = new StringBuilder();
            sb.Append("SA Inbetween diagnostics\n");
            Section(sb, "System", () => SystemLines(sb));
            Section(sb, "Packages", () => PackageLines(sb));
            Section(sb, "Tools", () => ToolLines(sb));
            Section(sb, "FBX export", () => FbxLines(sb));
            if (recipe != null) Section(sb, "Recipe", () => RecipeLines(sb, recipe));
            Section(sb, "Last error", () => ErrorLines(sb, recipe));
            Section(sb, "Editor log (SA Inbetween lines)", () => LogLines(sb));
            return sb.ToString();
        }

        public static string Copy(InbetweenRecipe recipe)
        {
            var text = Build(recipe);
            EditorGUIUtility.systemCopyBuffer = text;
            return text;
        }

        public static void ReportBug(InbetweenRecipe recipe)
        {
            Copy(recipe);
            if (!Application.isBatchMode) Application.OpenURL(IssuesUrl);
            Debug.Log("SA Inbetween: diagnostics copied to the clipboard. Paste them into the bug report under \"Diagnostics\".");
        }

        static void Section(StringBuilder sb, string title, Action body)
        {
            sb.Append("\n## ").Append(title).Append('\n');
            try
            {
                body();
            }
            catch (Exception e)
            {
                sb.Append("(unavailable: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
            }
        }

        static void Line(StringBuilder sb, string key, string value) => sb.Append(key).Append(": ").Append(value).Append('\n');

        static void SystemLines(StringBuilder sb)
        {
            Line(sb, "Unity", Application.unityVersion);
            Line(sb, "OS", SystemInfo.operatingSystem);
            Line(sb, "CPU", SystemInfo.processorType + ", " + SystemInfo.processorCount.ToString(CultureInfo.InvariantCulture) + " threads");
            Line(sb, "Memory", SystemInfo.systemMemorySize.ToString(CultureInfo.InvariantCulture) + " MB");
            Line(sb, "GPU", SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ")");
            Line(sb, "Batch mode", Application.isBatchMode ? "yes" : "no");
            Line(sb, "Project", Anonymous(Directory.GetParent(Application.dataPath).FullName));
        }

        static void PackageLines(StringBuilder sb)
        {
            var found = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
            foreach (var info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                found[info.name] = info;
            foreach (var name in Packages)
            {
                if (!found.TryGetValue(name, out var info)) { Line(sb, name, "not installed"); continue; }
                var source = info.source.ToString();
                if (info.git != null && !string.IsNullOrEmpty(info.git.hash)) source += " " + info.git.hash.Substring(0, Math.Min(10, info.git.hash.Length));
                if (info.source == UnityEditor.PackageManager.PackageSource.Local) source += " " + Anonymous(info.resolvedPath);
                Line(sb, name, info.version + " (" + source + ")");
            }
        }

        static void ToolLines(StringBuilder sb)
        {
            Line(sb, "Tools folder setting", InbetweenSettings.ToolsFolder == "" ? "(none, using <project>/SaInbetween)" : Anonymous(InbetweenSettings.ToolsFolder));
            Line(sb, "Project tools folder", Directory.Exists(InbetweenSettings.ProjectFolder) ? "present" : "missing");
            var t = InbetweenSettings.Tools();
            Line(sb, "Complete", t.Complete ? "yes" : "no, missing " + string.Join(", ", t.Missing()));
            Line(sb, "Check model file", InbetweenSettings.VerifyModel ? "on" : "off");
            FileLine(sb, "Generator", t.Cli);
            if (t.Cli != "") FileLine(sb, "Generator dll", Path.Combine(Path.GetDirectoryName(t.Cli) ?? "", "sa-inbetween.dll"));
            FileLine(sb, "Baker", t.Bake);
            if (t.Bake != "") FileLine(sb, "Baker dll", Path.Combine(Path.GetDirectoryName(t.Bake) ?? "", "sa-soma-bake.dll"));
            FileLine(sb, "Character family", t.Family);
            FileLine(sb, "Rest pose", t.Rest);
            FileLine(sb, "Model", t.Model);
            if (t.Model != "")
            {
                var hash = Sha256(t.Model);
                Line(sb, "Model matches the pin", hash == ModelSha256 ? "yes" : "NO (expected " + ModelSha256 + ", " + ModelBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes)");
            }
        }

        static void FileLine(StringBuilder sb, string label, string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) { Line(sb, label, "missing"); return; }
            var info = new FileInfo(path);
            Line(sb, label, Anonymous(path) + ", " + info.Length.ToString(CultureInfo.InvariantCulture) + " bytes, sha256 " + Sha256(path));
        }

        public static string Sha256(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "missing";
            var key = info.FullName;
            if (Hashes.TryGetValue(key, out var known) && known.Item1 == info.Length && known.Item2 == info.LastWriteTimeUtc.Ticks) return known.Item3;
            using var sha = SHA256.Create();
            using var stream = info.OpenRead();
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            Hashes[key] = (info.Length, info.LastWriteTimeUtc.Ticks, hash);
            return hash;
        }

        static void FbxLines(StringBuilder sb)
        {
            Line(sb, "FBX Exporter package", SaClipFbxExport.Available ? "installed" : "not installed");
#if SA_FBX
            Line(sb, "FBX SDK native library", SaClipFbxExport.NativeReady(out var error) ? "loads" : error);
#endif
            if (Application.platform == RuntimePlatform.LinuxEditor)
                Line(sb, "libxml2.so.2", SaClipFbxExport.LinuxLibraryMissing("libxml2.so.2") ? "missing (see docs/install.md, FBX export on Linux)" : "found");
        }

        static void RecipeLines(StringBuilder sb, InbetweenRecipe recipe)
        {
            Line(sb, "Asset", AssetDatabase.GetAssetPath(recipe));
            Line(sb, "State", RecipeRunner.StateOf(recipe).ToString());
            var problem = RecipeRunner.Problem(recipe);
            if (problem != "") Line(sb, "Problem", problem);
            Line(sb, "Start", Describe(recipe.Start));
            for (var i = 0; i < recipe.Goals.Count; i++) Line(sb, "Goal " + (i + 1), Describe(recipe.Goals[i]));
            Line(sb, "Duration", recipe.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s, land exactly " + (recipe.LandExactly ? "on" : "off"));
            var character = SaClipExport.CharacterFor(recipe);
            Line(sb, "Character", character == null ? "none" : AssetDatabase.GetAssetPath(character) + (IsHumanoid(character) ? " (Humanoid)" : " (no Humanoid avatar)"));
            Line(sb, "Result", recipe.Output == null ? "none" : AssetDatabase.GetAssetPath(recipe.Output) + ", " + recipe.Output.Bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes");
            Line(sb, "Unity clip", recipe.SavedClip == null ? (recipe.SaveAnimationClip ? "not saved yet" : "off") : AssetDatabase.GetAssetPath(recipe.SavedClip));
            Line(sb, "Settings match the result", recipe.Output == null ? "no result" : recipe.BuiltFrom == RecipeRunner.Hash(recipe) ? "yes" : "no");
        }

        static string Describe(ClipFrame f)
        {
            var where = f.Where == FrameWhere.Frame ? "frame " + f.Frame.ToString(CultureInfo.InvariantCulture)
                : f.Where == FrameWhere.Time ? f.Seconds.ToString("0.###", CultureInfo.InvariantCulture) + " s"
                : f.Where == FrameWhere.End ? "end" : "start";
            switch (f.Kind)
            {
                case SourceKind.UnityClip:
                    return "Unity clip " + AssetPath(f.Motion) + (f.Motion == null ? "" : " '" + f.Motion.name + "', " + f.Motion.length.ToString("0.###", CultureInfo.InvariantCulture) + " s at " + f.Motion.frameRate.ToString("0.##", CultureInfo.InvariantCulture) + " fps" + (f.Motion.legacy ? ", legacy" : "") + (f.Motion.isHumanMotion ? ", humanoid" : ", generic")) + ", " + where + ", on " + AssetPath(f.Character) + (f.Character != null && IsHumanoid(f.Character) ? " (Humanoid)" : "");
                case SourceKind.Pose:
                    return "pose " + AssetPath(f.Pose);
                default:
                    return ".saclip " + AssetPath(f.Clip) + ", " + where;
            }
        }

        static string AssetPath(UnityEngine.Object asset) => asset == null ? "(none)" : AssetDatabase.GetAssetPath(asset);

        public static bool IsHumanoid(GameObject character) =>
            character != null && character.GetComponentInChildren<Animator>(true) is Animator animator && animator.avatar != null && animator.avatar.isHuman;

        static void ErrorLines(StringBuilder sb, InbetweenRecipe recipe)
        {
            var run = recipe == null ? null : RecipeRunner.Get(recipe);
            if (run != null && run.Error != "")
            {
                Line(sb, "This recipe", run.Stage + ": " + run.Error);
                sb.Append("--- tool log (last lines) ---\n").Append(Anonymous(FailureAdvice.Tail(run.Log, 40))).Append('\n');
                return;
            }

            var last = RecipeRunner.LastFailure;
            sb.Append(last == "" ? "(none this session)" : Anonymous(last)).Append('\n');
        }

        static void LogLines(StringBuilder sb)
        {
            var path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) { sb.Append("(no log file)\n"); return; }
            var kept = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (line.IndexOf("SA Inbetween", StringComparison.Ordinal) >= 0 || line.IndexOf("SA.Inbetween", StringComparison.Ordinal) >= 0 && line.IndexOf("Exception", StringComparison.Ordinal) >= 0)
                    {
                        kept.Add(line);
                        if (kept.Count > 15) kept.RemoveAt(0);
                    }
            }

            sb.Append(kept.Count == 0 ? "(none)" : Anonymous(string.Join("\n", kept))).Append('\n');
        }

        public static string Anonymous(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return text;
            return text.Replace(home.Replace('\\', '/'), "~").Replace(home, "~");
        }
    }

    public static class InbetweenMenu
    {
        [MenuItem("SA/Inbetween/Copy Diagnostics", false, 40)]
        static void CopyDiagnostics()
        {
            InbetweenDiagnostics.Copy(Selection.activeObject as InbetweenRecipe);
            EditorUtility.DisplayDialog("SA Inbetween", "Diagnostics are on the clipboard: versions, tool files and their SHA-256, the last error and its log. Paste them into a bug report or a message. Home folder paths are shortened to ~.", "OK");
        }

        [MenuItem("SA/Inbetween/Report a Bug...", false, 41)]
        static void ReportBug()
        {
            InbetweenDiagnostics.ReportBug(Selection.activeObject as InbetweenRecipe);
            EditorUtility.DisplayDialog("SA Inbetween", "Diagnostics are on the clipboard. The bug report page opens in your browser: describe what you did, then paste the diagnostics where it says Diagnostics.", "OK");
        }

        [MenuItem("SA/Inbetween/Project Settings", false, 60)]
        static void OpenSettings() => SettingsService.OpenProjectSettings("Project/SA Inbetween");

        [MenuItem("SA/Inbetween/Designer Guide", false, 61)]
        static void OpenGuide() => Application.OpenURL(InbetweenDiagnostics.GuideUrl);
    }
}

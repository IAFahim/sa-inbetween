using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SA.Inbetween.Clips.Editor
{
    public struct StudioCommand
    {
        public string File;
        public List<string> Args;
    }

    public sealed class StudioSettings
    {
        public string CliPath = "";
        public string BakePath = "";
        public string ModelPath = "";
        public string FamilyPath = "";
        public string OutputFolder = "Assets/Generated";
        public List<string> Contexts = new List<string>();
        public int ContextEnd = -1;
        public List<string> Targets = new List<string> { "" };
        public int TokensMin = 8;
        public int TokensMax = 14;
        public int TokenOverride;
        public bool Correct = true;
        public bool VerifyModel = true;
        public string ClipName = "motion";
    }

    public static class StudioCommands
    {
        public const int MinTokens = 6;
        public const int MaxTokens = 16;
        public const float FrameRate = 30f;

        public static int FramesForTokens(int tokens) => 4 * tokens - 6;

        public static float SecondsForTokens(int tokens) => FramesForTokens(tokens) / FrameRate;

        public static int TokensForSeconds(float seconds)
        {
            var tokens = Mathf.RoundToInt((seconds * FrameRate + 6f) / 4f);
            return Mathf.Clamp(tokens, MinTokens, MaxTokens);
        }

        public static string SaibPath(StudioSettings s) => Path.Combine(s.OutputFolder, s.ClipName + ".saib");

        public static string ClipPath(StudioSettings s) => Path.Combine(s.OutputFolder, s.ClipName + ".saclip");

        public static bool TryGenerate(StudioSettings s, out StudioCommand command, out string error)
        {
            command = default;
            if (!CheckName(s, out error) || !Require(s.CliPath, "sa-inbetween executable", out error) ||
                !Require(s.ModelPath, "model (.onnx)", out error) || !RequireContext(s, out error))
                return false;
            if (s.TokenOverride != 0 && (s.TokenOverride < MinTokens || s.TokenOverride > MaxTokens))
            {
                error = "Duration must be between " + SecondsForTokens(MinTokens).ToString("0.0") + " and " + SecondsForTokens(MaxTokens).ToString("0.0") + " seconds";
                return false;
            }

            if (s.TokensMin < 6 || s.TokensMax > 16 || s.TokensMin > s.TokensMax)
            {
                error = "Tokens must satisfy 6 <= min <= max <= 16";
                return false;
            }

            var targets = new List<string>();
            foreach (var t in s.Targets)
                if (!string.IsNullOrWhiteSpace(t))
                    targets.Add(t.Trim());
            if (targets.Count == 0)
            {
                error = "Add at least one target pose";
                return false;
            }

            var args = new List<string>();
            Launcher(s.CliPath, args, out var file);
            args.Add("generate");
            args.Add("--model"); args.Add(s.ModelPath);
            foreach (var c in s.Contexts) { args.Add("--context"); args.Add(c); }
            if (s.ContextEnd >= 0) { args.Add("--context-end"); args.Add(s.ContextEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            foreach (var t in targets) { args.Add("--target"); args.Add(t); }
            args.Add("--tokens-min"); args.Add(s.TokensMin.ToString(System.Globalization.CultureInfo.InvariantCulture));
            args.Add("--tokens-max"); args.Add(s.TokensMax.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (s.TokenOverride != 0) { args.Add("--override"); args.Add(s.TokenOverride.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            if (!s.Correct) args.Add("--no-correct");
            if (!s.VerifyModel) args.Add("--skip-verify");
            args.Add("--out"); args.Add(SaibPath(s));
            command = new StudioCommand { File = file, Args = args };
            error = "";
            return true;
        }

        public static bool TryBake(StudioSettings s, out StudioCommand command, out string error)
        {
            command = default;
            if (!CheckName(s, out error) || !Require(s.BakePath, "sa-soma-bake executable", out error) ||
                !Require(s.FamilyPath, "family (.safamily)", out error))
                return false;
            var args = new List<string>();
            Launcher(s.BakePath, args, out var file);
            args.Add(s.FamilyPath);
            args.Add(SaibPath(s));
            args.Add(ClipPath(s));
            command = new StudioCommand { File = file, Args = args };
            error = "";
            return true;
        }

        static void Launcher(string path, List<string> args, out string file)
        {
            if (path.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase))
            {
                file = "dotnet";
                args.Add(path);
            }
            else
                file = path;
        }

        static bool CheckName(StudioSettings s, out string error)
        {
            if (string.IsNullOrWhiteSpace(s.ClipName) || s.ClipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "Clip name is empty or has characters a file name cannot hold";
                return false;
            }

            error = "";
            return true;
        }

        static bool RequireContext(StudioSettings s, out string error)
        {
            if (s.Contexts == null || s.Contexts.Count == 0)
            {
                error = "Choose a start pose";
                return false;
            }

            error = "";
            return true;
        }

        static bool Require(string value, string label, out string error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Set the " + label;
                return false;
            }

            error = "";
            return true;
        }
    }
}

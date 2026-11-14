using System;
using System.Collections.Generic;

namespace SA.Inbetween.Clips.Editor
{
    public static class FailureAdvice
    {
        public const string ReportHint = " If it happens again, press Copy diagnostics and attach the text to a bug report (SA > Inbetween > Report a Bug).";
        public const string ModelMismatch = "The model file is not the one Inbetween needs: a different version or an unfinished download. Download mmm_v6_soma_fp32.onnx again (757,864,273 bytes), put it in SaInbetween/model, and press Generate again. Copy diagnostics shows the file's SHA-256.";
        public const string ModelUnreadable = "The model file could not be opened: it is damaged or not an ONNX model. Download mmm_v6_soma_fp32.onnx again (757,864,273 bytes), put it in SaInbetween/model, and press Generate again.";
        public const string OutOfMemory = "The computer ran out of memory while generating. Close other programs and press Generate again.";
        public const string RestPose = "The rest pose file is damaged (soma_rest.pose must be 2,156 bytes). Make it again as the designer guide's \"Tools folder\" section says, put it in SaInbetween, and press Generate again.";
        public const string FamilyFile = "The character family file (humanoid-v1.safamily) cannot be read. Unpack the tools archive again so SaInbetween/humanoid-v1.safamily is the one from the release, and press Generate again.";
        public const string MissingFile = "A tool file is missing or was moved while generating. Open Edit > Project Settings > SA Inbetween: every line must say found. Then press Generate again.";

        public static string ForExit(string stage, int exitCode, string log)
        {
            var hint = StudioProcess.ExitHint(exitCode);
            if (hint != "") return stage + " could not start: Windows is missing a library the tool needs." + hint;
            var text = log ?? "";
            if (Has(text, "does not match the pin")) return ModelMismatch;
            if (Has(text, "cannot load model") || Has(text, "onnxruntime") && Has(text, "error")) return ModelUnreadable;
            if (Has(text, "OutOfMemory") || Has(text, "bad_alloc") || Has(text, "Cannot allocate memory")) return OutOfMemory;
            if (Has(text, "rest pose must be")) return RestPose;
            if (Has(text, "family file") || Has(text, "family identity")) return FamilyFile;
            if (Has(text, "FileNotFound") || Has(text, "DirectoryNotFound") || Has(text, "No such file")) return MissingFile;
            var line = LastError(text);
            return stage + " failed (exit code " + exitCode + ")" + (line == "" ? "." : ": " + line + ".") + " Press Generate again." + ReportHint;
        }

        public static string ForStart(string error)
        {
            if (string.IsNullOrEmpty(error)) return "";
            if (Has(error, "Make it executable")) return error + " Then press Generate again.";
            if (Has(error, "Could not start")) return error + ". Open Edit > Project Settings > SA Inbetween and check that every line says found." + ReportHint;
            return error;
        }

        public static string ForConversion(string sourceName, string characterName, string error)
        {
            var e = error ?? "";
            var plain = "Cannot read '" + sourceName + "' on '" + characterName + "'";
            if (Has(e, "required bones") || Has(e, "bone assignment") || Has(e, "avatar") || Has(e, "identity"))
                return plain + ": Inbetween cannot find a humanoid skeleton on it (" + e.TrimEnd('.') + "). Select the character's FBX, set Inspector > Rig > Animation Type to Humanoid, press Apply, and press Generate again. Non-humanoid rigs (animals, props) are not supported.";
            if (Has(e, "hips height"))
                return plain + ": the character does not stand upright on the ground (" + e.TrimEnd('.') + "). Use the model as imported, with its feet at Y = 0 and +Y up.";
            if (Has(e, "non-unit scale"))
                return plain + ": " + e.TrimEnd('.') + ". In the FBX import settings, keep Scale Factor and Convert Units as they are, and remove scaled bones from the rig in your modelling tool.";
            if (Has(e, "no longer exists") || Has(e, "is missing on"))
                return plain + ": the character changed while it was read (" + e.TrimEnd('.') + "). Press Generate again.";
            return plain + ": " + e.TrimEnd('.') + ". Check that the clip was made for this character (same skeleton), or set both FBX files to Humanoid.";
        }

        public static string LastError(string log)
        {
            var lines = Lines(log);
            for (var i = lines.Count - 1; i >= 0; i--)
                if (lines[i].StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                    return lines[i].Substring("error:".Length).Trim();
            for (var i = lines.Count - 1; i >= 0; i--)
                if (Has(lines[i], "exception") || Has(lines[i], "error"))
                    return lines[i].Trim();
            return "";
        }

        public static string Tail(string log, int count)
        {
            var lines = Lines(log);
            var from = Math.Max(0, lines.Count - count);
            return string.Join("\n", lines.GetRange(from, lines.Count - from));
        }

        static List<string> Lines(string log)
        {
            var lines = new List<string>();
            foreach (var line in (log ?? "").Replace("\r", "").Split('\n'))
                if (line.Trim() != "")
                    lines.Add(line);
            return lines;
        }

        static bool Has(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Text;
using SA.Motion.Numerics;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class SeamJoin
    {
        public string Name = "";
        public double HeightBeforeCm;
        public double HeightAfterCm;
        public double PopBeforeDegrees;
        public double PopAfterDegrees;
    }

    public sealed class SeamReport
    {
        public SeamJoin Start;
        public SeamJoin Goal;
        public FootReport Feet;

        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var join in new[] { Start, Goal })
                {
                    if (join == null) continue;
                    sb.Append(join.Name).Append(": height step ").Append(Cm(join.HeightBeforeCm)).Append(" -> ").Append(Cm(join.HeightAfterCm))
                        .Append(", rotation pop ").Append(join.PopBeforeDegrees.ToString("0.00", CultureInfo.InvariantCulture)).Append(" -> ")
                        .Append(join.PopAfterDegrees.ToString("0.00", CultureInfo.InvariantCulture)).Append(" deg. ");
                }

                if (Feet != null)
                    sb.Append("Feet: slide ").Append(Cm(Feet.SlideBefore * 100.0)).Append(" -> ").Append(Cm(Feet.SlideAfter * 100.0))
                        .Append(", ").Append(Feet.LockedFrames).Append(" locked, ").Append(Feet.ClampedFrames).Append(" clamped frames.");
                return sb.ToString().Trim();
            }
        }

        static string Cm(double cm) => cm.ToString("0.00", CultureInfo.InvariantCulture) + " cm";
    }

    public static class SeamBake
    {
        public const string RawExtension = ".raw.saclip";
        public const string ReportExtension = ".seams.txt";

        public static string RawFile(string clipFile) => Path.ChangeExtension(clipFile, null) + RawExtension;

        public static SeamSide Side(ClipFrame frame, string work, bool isStart, out string error)
        {
            error = "";
            byte[] bytes;
            if (frame.Kind == SourceKind.Pose) return null;
            if (frame.Kind == SourceKind.SaClip)
                bytes = frame.Clip != null ? frame.Clip.Bytes : null;
            else
            {
                var file = RecipeRunner.SourceFile(frame, work);
                bytes = File.Exists(file) ? File.ReadAllBytes(file) : null;
            }

            if (bytes == null || !CanonicalClip.TryLoad(bytes, out var clip, out error)) return null;
            var seconds = frame.Where == FrameWhere.End ? clip.Duration : frame.Where == FrameWhere.Start ? 0.0 : frame.AnchorSeconds;
            seconds = Math.Max(0.0, Math.Min(clip.Duration, seconds));
            var step = 1.0 / SaClipExport.Rate;
            return new SeamSide(CanonicalClip.Sample(clip, seconds), CanonicalClip.Sample(clip, isStart ? seconds - step : seconds + step));
        }

        public static CanonicalFrames Fix(CanonicalFrames raw, SeamSide start, SeamSide goal, SeamSettings settings, out SeamReport report)
        {
            report = new SeamReport();
            var hz = raw.Rate;
            var poses = SeamFix.Blend(raw.Poses, raw.Count, hz, start, goal, settings);
            if (settings.Feet) poses = FootFix.Apply(poses, raw.Count, raw.Root, CanonicalClip.Parents, raw.Contacts, hz, out report.Feet);
            if (start != null) report.Start = Join("start", start.At, SeamFix.Frame(raw.Poses, 0), SeamFix.Frame(poses, 0), true);
            if (goal != null) report.Goal = Join("goal", goal.At, SeamFix.Frame(raw.Poses, raw.Count - 1), SeamFix.Frame(poses, raw.Count - 1), false);
            return new CanonicalFrames { Poses = poses, Root = raw.Root, Contacts = raw.Contacts, Count = raw.Count, Duration = raw.Duration, Loop = raw.Loop, RootPolicy = raw.RootPolicy };
        }

        static SeamJoin Join(string name, Pose[] side, Pose[] before, Pose[] after, bool sideFirst)
        {
            var join = new SeamJoin { Name = name };
            double h0, h1;
            if (sideFirst)
            {
                SeamFix.Join(side, before, out h0, out join.PopBeforeDegrees, out _);
                SeamFix.Join(side, after, out h1, out join.PopAfterDegrees, out _);
            }
            else
            {
                SeamFix.Join(before, side, out h0, out join.PopBeforeDegrees, out _);
                SeamFix.Join(after, side, out h1, out join.PopAfterDegrees, out _);
            }

            join.HeightBeforeCm = h0 * 100.0;
            join.HeightAfterCm = h1 * 100.0;
            return join;
        }

        public static bool TryFixFile(InbetweenRecipe recipe, string work, string clipFile, out SeamReport report, out string error)
        {
            report = null;
            error = "";
            if (recipe == null || !recipe.Seam.Active) return true;
            var raw = File.ReadAllBytes(clipFile);
            File.WriteAllBytes(RawFile(clipFile), raw);
            if (!CanonicalClip.TryLoad(raw, out var clip, out error)) return false;
            var start = Side(recipe.Start, work, true, out _);
            var goal = recipe.Goals.Count > 0 ? Side(recipe.Goals[recipe.Goals.Count - 1], work, false, out _) : null;
            var fixedFrames = Fix(CanonicalClip.Frames(clip), start, goal, recipe.Seam, out report);
            if (!CanonicalClip.TryEncode(fixedFrames, out var bytes, out error)) return false;
            File.WriteAllBytes(clipFile, bytes);
            File.WriteAllText(Path.ChangeExtension(clipFile, null) + ReportExtension, report.Text + "\n");
            return true;
        }
    }
}

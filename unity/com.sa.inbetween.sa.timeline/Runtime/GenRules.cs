using System;
using System.Collections.Generic;

namespace SA.Inbetween.Timeline
{
    public readonly struct GenSpan
    {
        public readonly int Start;
        public readonly int End;

        public GenSpan(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Length => End - Start;

        public bool Covers(int tick) => Start <= tick && tick < End;

        public bool Overlaps(GenSpan other) => Start < other.End && other.Start < End;

        public bool Touches(int tick) => Start <= tick && tick <= End;

        public bool Inside(GenSpan outer) => outer.Start < Start && End < outer.End;
    }

    public enum GenEdge
    {
        None,
        Covered,
        FirstTick,
        EndedBefore,
        StartsAfter,
        Wrapped,
    }

    public readonly struct GenPick
    {
        public readonly int Index;
        public readonly GenEdge Edge;
        public readonly int Other;

        public GenPick(int index, GenEdge edge, int other)
        {
            Index = index;
            Edge = edge;
            Other = other;
        }

        public bool Found => Index >= 0;

        public bool Ambiguous => Other >= 0;

        public static GenPick None => new GenPick(-1, GenEdge.None, -1);
    }

    public static class GenRules
    {
        public const int TicksPerSecond = 30;
        public const int MaxMoves = 4;

        public static int Ticks(double seconds) => seconds <= 0 ? 0 : (int)Math.Round(seconds * TicksPerSecond);

        public static double Seconds(int ticks) => ticks / (double)TicksPerSecond;

        public static GenSpan Window(double startSeconds, double endSeconds)
        {
            var start = Ticks(startSeconds);
            return new GenSpan(start, Math.Max(start + 1, Ticks(endSeconds)));
        }

        public static GenPick StartAt(IReadOnlyList<GenSpan> sources, int tick, bool loop, int duration)
        {
            var covered = Covering(sources, tick);
            if (covered.Found) return covered;
            var before = Last(sources, s => s.End <= tick);
            if (before.Found) return new GenPick(before.Index, GenEdge.EndedBefore, before.Other);
            if (!loop) return GenPick.None;
            var wrapped = Last(sources, s => s.End <= duration);
            return wrapped.Found ? new GenPick(wrapped.Index, GenEdge.Wrapped, wrapped.Other) : GenPick.None;
        }

        public static GenPick GoalAt(IReadOnlyList<GenSpan> sources, int tick, bool loop, int duration)
        {
            var covered = Covering(sources, tick);
            if (covered.Found) return covered;
            var after = First(sources, s => s.Start >= tick);
            if (after.Found) return new GenPick(after.Index, GenEdge.StartsAfter, after.Other);
            if (!loop || tick < duration) return GenPick.None;
            var wrapped = First(sources, s => s.Start >= 0);
            return wrapped.Found ? new GenPick(wrapped.Index, GenEdge.Wrapped, wrapped.Other) : GenPick.None;
        }

        static GenPick Covering(IReadOnlyList<GenSpan> sources, int tick)
        {
            var found = -1;
            var other = -1;
            for (var i = 0; i < sources.Count; i++)
            {
                if (!sources[i].Covers(tick)) continue;
                if (found < 0) found = i;
                else if (other < 0) other = i;
            }

            if (found < 0) return GenPick.None;
            return new GenPick(found, sources[found].Start == tick ? GenEdge.FirstTick : GenEdge.Covered, other);
        }

        static GenPick Last(IReadOnlyList<GenSpan> sources, Func<GenSpan, bool> where)
        {
            var found = -1;
            var other = -1;
            for (var i = 0; i < sources.Count; i++)
            {
                if (!where(sources[i])) continue;
                if (found < 0 || sources[i].End > sources[found].End)
                {
                    found = i;
                    other = -1;
                }
                else if (sources[i].End == sources[found].End && other < 0) other = i;
            }

            return found < 0 ? GenPick.None : new GenPick(found, GenEdge.EndedBefore, other);
        }

        static GenPick First(IReadOnlyList<GenSpan> sources, Func<GenSpan, bool> where)
        {
            var found = -1;
            var other = -1;
            for (var i = 0; i < sources.Count; i++)
            {
                if (!where(sources[i])) continue;
                if (found < 0 || sources[i].Start < sources[found].Start)
                {
                    found = i;
                    other = -1;
                }
                else if (sources[i].Start == sources[found].Start && other < 0) other = i;
            }

            return found < 0 ? GenPick.None : new GenPick(found, GenEdge.StartsAfter, other);
        }

        public static double LocalSeconds(double sourceStart, double clipIn, double timeScale, double length, bool loop, double at)
        {
            var local = Math.Max(0, clipIn) + Math.Max(0, at - sourceStart) * (timeScale > 0 ? timeScale : 1.0);
            if (length <= 0) return local;
            if (loop && local > length + BridgePlan.Epsilon)
            {
                local %= length;
                if (local < BridgePlan.Epsilon) local = length;
            }

            return Math.Min(local, length);
        }

        public static int Moves(int intermediateGoals) => intermediateGoals + 1;

        public static double MoveSeconds(double windowSeconds, int moves) => moves <= 0 ? windowSeconds : windowSeconds / moves;

        public static string LengthProblem(double windowSeconds, int moves)
        {
            var each = MoveSeconds(windowSeconds, moves);
            if (moves > MaxMoves) return "it has " + moves + " moves; the generator chains at most " + MaxMoves + ". Remove a Goal Pose inside it.";
            if (each < BridgePlan.MinSeconds - BridgePlan.Epsilon)
                return moves == 1
                    ? "it is " + Text(windowSeconds) + " s long; one move takes at least " + Text(BridgePlan.MinSeconds) + " s. Make it longer."
                    : "its " + moves + " moves get " + Text(each) + " s each; a move takes at least " + Text(BridgePlan.MinSeconds) + " s. Make it longer or remove a Goal Pose.";
            if (each > BridgePlan.MaxSeconds + BridgePlan.Epsilon)
                return moves == 1
                    ? "it is " + Text(windowSeconds) + " s long; one move takes at most " + Text(BridgePlan.MaxSeconds) + " s. Make it shorter, or put a Goal Pose inside it to chain two moves."
                    : "its " + moves + " moves get " + Text(each) + " s each; a move takes at most " + Text(BridgePlan.MaxSeconds) + " s. Make it shorter or add a Goal Pose inside it.";
            return "";
        }

        public static string Text(double seconds) => seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        public static string At(int tick) => Text(Seconds(tick)) + " s";
    }
}

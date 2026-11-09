using System;

namespace SA.Inbetween.Timeline
{
    public enum BridgeAnchor
    {
        Start,
        End,
        Time,
    }

    /// <summary>Where the bridge goes on the track and how far the clips from B on must move so it fits exactly.</summary>
    public readonly struct BridgePlacement
    {
        public readonly double Start;
        public readonly double Duration;
        public readonly double Shift;

        public BridgePlacement(double start, double duration, double shift)
        {
            Start = start;
            Duration = duration;
            Shift = shift;
        }

        public double End => Start + Duration;
    }

    /// <summary>Pure timing rules for a Timeline bridge. Seconds throughout; no Unity types.</summary>
    public static class BridgePlan
    {
        /// <summary>Shortest bridge the generator makes (InbetweenRecipe.MinSeconds).</summary>
        public const double MinSeconds = 0.6;

        /// <summary>Longest bridge the generator makes (InbetweenRecipe.MaxSeconds).</summary>
        public const double MaxSeconds = 1.93;

        /// <summary>Bridge length when A and B touch or overlap, so there is no gap to read a length from.</summary>
        public const double DefaultSeconds = 1.0;

        /// <summary>Gaps and shifts smaller than this are treated as zero (well below one frame at 240 Hz).</summary>
        public const double Epsilon = 1e-4;

        public static double Gap(double aEnd, double bStart) => bStart - aEnd;

        /// <summary>The bridge length to ask for: the gap clamped to what the generator can make, or the default when there is no gap.</summary>
        public static double DurationFor(double gap)
        {
            if (double.IsNaN(gap) || gap <= Epsilon) return DefaultSeconds;
            return Math.Min(MaxSeconds, Math.Max(MinSeconds, gap));
        }

        /// <summary>
        /// Places a bridge of <paramref name="length"/> seconds right after A. B and every later clip move by
        /// <see cref="BridgePlacement.Shift"/> so B starts where the bridge ends: positive opens room (no gap, or too short a gap),
        /// negative closes a gap longer than the bridge.
        /// </summary>
        public static BridgePlacement Place(double aEnd, double bStart, double length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "A bridge needs a positive length.");
            var shift = aEnd + length - bStart;
            if (Math.Abs(shift) < Epsilon) shift = 0;
            return new BridgePlacement(aEnd, length, shift);
        }

        /// <summary>True for a clip that ripples with B: it starts at or after B's start (before the move).</summary>
        public static bool Ripples(double clipStart, double bStart) => clipStart >= bStart - Epsilon;

        /// <summary>Applies the ripple to a list of clip start times; clips before B stay put.</summary>
        public static double[] Ripple(double[] starts, double bStart, double shift)
        {
            var moved = new double[starts.Length];
            for (var i = 0; i < starts.Length; i++) moved[i] = Ripples(starts[i], bStart) ? starts[i] + shift : starts[i];
            return moved;
        }

        /// <summary>Time inside the source clip at a Timeline clip's last frame (clip-in plus scaled duration), wrapped when the clip loops.</summary>
        public static double LocalEnd(double clipIn, double duration, double timeScale, double length, bool loop)
        {
            var local = clipIn + duration * (timeScale > 0 ? timeScale : 1.0);
            if (length <= 0) return local;
            if (loop)
            {
                if (local > length + Epsilon)
                {
                    local %= length;
                    if (local < Epsilon) local = length;
                }

                return Math.Min(local, length);
            }

            return Math.Min(local, length);
        }

        /// <summary>Time inside the source clip at a Timeline clip's first frame, wrapped when the clip loops.</summary>
        public static double LocalStart(double clipIn, double length, bool loop)
        {
            var local = Math.Max(0, clipIn);
            if (length <= 0) return local;
            if (loop && local >= length - Epsilon) local %= length;
            return Math.Min(local, length);
        }

        /// <summary>Snaps a source-clip time to Start or End when it is within half a frame of either, else keeps the exact time.</summary>
        public static BridgeAnchor Anchor(double local, double length, double frameRate)
        {
            var half = 0.5 / (frameRate > 0 ? frameRate : 30.0);
            if (local <= half) return BridgeAnchor.Start;
            if (length > 0 && local >= length - half) return BridgeAnchor.End;
            return BridgeAnchor.Time;
        }
    }
}

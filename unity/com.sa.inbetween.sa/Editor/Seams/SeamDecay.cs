using System;
using SA.Motion.Numerics;

namespace SA.Inbetween.Clips.Editor
{
    public static class SeamDecay
    {
        const double Ln2 = 0.69314718055994530942;

        public static double Rate(float halfLife) => 2.0 * Ln2 / Math.Max(halfLife, 1e-5f);

        public static double Weight(SeamCurve curve, float seconds, double time)
        {
            if (time <= 0.0) return 1.0;
            if (curve == SeamCurve.Quintic)
            {
                if (time >= seconds) return 0.0;
                var u = time / seconds;
                var inverse = 1.0 - u;
                return inverse * inverse * inverse * (1.0 + 3.0 * u + 6.0 * u * u);
            }

            var y = Rate(seconds);
            return Math.Exp(-y * time) * (1.0 + y * time);
        }

        public static double VelocityWeight(SeamCurve curve, float seconds, double time)
        {
            if (time <= 0.0) return 0.0;
            if (curve == SeamCurve.Quintic)
            {
                if (time >= seconds) return 0.0;
                var u = time / seconds;
                var inverse = 1.0 - u;
                return u * inverse * inverse * inverse * (1.0 + 3.0 * u) * seconds;
            }

            return Math.Exp(-Rate(seconds) * time) * time;
        }

        public static float Offset(SeamCurve curve, float seconds, float offset, float velocity, double time) =>
            (float)(offset * Weight(curve, seconds, time) + velocity * VelocityWeight(curve, seconds, time));

        public static Float3 Offset(SeamCurve curve, float seconds, Float3 offset, Float3 velocity, double time)
        {
            var a = (float)Weight(curve, seconds, time);
            var b = (float)VelocityWeight(curve, seconds, time);
            return offset * a + velocity * b;
        }
    }
}

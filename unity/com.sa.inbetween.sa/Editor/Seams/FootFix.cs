using System;
using System.Collections.Generic;
using SA.Animation;
using SA.Motion.Numerics;

namespace SA.Inbetween.Clips.Editor
{
    public readonly struct Leg
    {
        public readonly int Upper;
        public readonly int Lower;
        public readonly int Foot;
        public readonly int Channel;

        public Leg(int upper, int lower, int foot, int channel)
        {
            Upper = upper;
            Lower = lower;
            Foot = foot;
            Channel = channel;
        }

        public static readonly Leg Left = new Leg(48, 49, 50, 0);
        public static readonly Leg Right = new Leg(52, 53, 54, 1);
        public static readonly Leg[] Both = { Left, Right };
    }

    public sealed class FootReport
    {
        public int LockedFrames;
        public int ClampedFrames;
        public double SlideBefore;
        public double SlideAfter;
        public float Ground;
    }

    public static class FootFix
    {
        public static readonly Float3 Sole = new Float3(0f, -0.1f, 0.08f);
        public const float ContactHeight = 0.05f;
        public const float ContactSpeed = 0.6f;
        public const float ContactWeight = 0.5f;
        public const int Ramp = 3;

        public static RigidPose[] Model(Pose[] frames, int frame, RigidPose root, int[] parents)
        {
            var placed = new Pose[SeamFix.Bones];
            Array.Copy(frames, frame * SeamFix.Bones, placed, 0, SeamFix.Bones);
            RigidMath.TryCompose(root, new RigidPose(placed[SeamFix.Hips].Position, Quaternion.Normalize(placed[SeamFix.Hips].Rotation)), out var hips);
            placed[SeamFix.Hips] = new Pose { Position = hips.Position, Rotation = hips.Rotation, Scale = Float3.One };
            var model = new RigidPose[SeamFix.Bones];
            RigidMath.Model(parents, placed, model);
            return model;
        }

        public static Float3 SoleOf(RigidPose foot) => foot.Position + Quaternion.Rotate(Quaternion.Normalize(foot.Rotation), Sole);

        public static float Ground(Pose[] frames, int count, RigidPose[] root, int[] parents)
        {
            var ground = 0f;
            foreach (var f in new[] { 0, count - 1 })
            {
                var model = Model(frames, f, root[f], parents);
                foreach (var leg in Leg.Both) ground = Math.Min(ground, SoleOf(model[leg.Foot]).Y);
            }

            return ground;
        }

        public static bool[] Contacts(Pose[] frames, int count, RigidPose[] root, int[] parents, float[] weights, float hz, Leg leg, float ground)
        {
            var soles = new Float3[count];
            for (var f = 0; f < count; f++) soles[f] = SoleOf(Model(frames, f, root[f], parents)[leg.Foot]);
            var channels = weights != null && weights.Length >= count ? weights.Length / count : 0;
            var contact = new bool[count];
            for (var f = 0; f < count; f++)
            {
                var neighbour = f > 0 ? soles[f - 1] : f + 1 < count ? soles[f + 1] : soles[f];
                var travel = soles[f] - neighbour;
                var speed = (float)Math.Sqrt(travel.X * travel.X + travel.Z * travel.Z) * hz;
                var low = soles[f].Y <= ground + ContactHeight;
                var model = channels == 0 || weights[f * channels + leg.Channel] >= ContactWeight;
                contact[f] = low && model && speed <= ContactSpeed;
            }

            return contact;
        }

        public static double Slide(Pose[] frames, int count, RigidPose[] root, int[] parents, bool[] contact, Leg leg)
        {
            var total = 0.0;
            Float3 previous = default;
            for (var f = 0; f < count; f++)
            {
                var sole = SoleOf(Model(frames, f, root[f], parents)[leg.Foot]);
                if (f > 0 && contact[f] && contact[f - 1])
                {
                    var d = sole - previous;
                    total += Math.Sqrt((double)d.X * d.X + (double)d.Z * d.Z);
                }

                previous = sole;
            }

            return total;
        }

        public static Pose[] Apply(Pose[] frames, int count, RigidPose[] root, int[] parents, float[] weights, float hz, out FootReport report)
        {
            report = new FootReport();
            var result = (Pose[])frames.Clone();
            if (count < 3) return result;
            var ground = Ground(frames, count, root, parents);
            report.Ground = ground;
            foreach (var leg in Leg.Both)
            {
                var contact = Contacts(frames, count, root, parents, weights, hz, leg, ground);
                report.SlideBefore += Slide(frames, count, root, parents, contact, leg);
                var soles = new Float3[count];
                for (var f = 0; f < count; f++) soles[f] = SoleOf(Model(frames, f, root[f], parents)[leg.Foot]);
                var targets = (Float3[])soles.Clone();
                var weight = new float[count];
                foreach (var (from, to) in Intervals(contact))
                {
                    for (var f = from; f <= to; f++)
                    {
                        targets[f] = Anchor(soles, from, to, count, f);
                        weight[f] = 1f;
                    }

                    for (var k = 1; k <= Ramp; k++)
                    {
                        var w = Smooth(1f - k / (float)(Ramp + 1));
                        var before = from - k;
                        if (before > 0 && !contact[before] && w > weight[before])
                        {
                            weight[before] = w;
                            targets[before] = Lerp(soles[before], Anchor(soles, from, to, count, from), w);
                        }

                        var after = to + k;
                        if (after < count - 1 && !contact[after] && w > weight[after])
                        {
                            weight[after] = w;
                            targets[after] = Lerp(soles[after], Anchor(soles, from, to, count, to), w);
                        }
                    }
                }

                for (var f = 1; f < count - 1; f++)
                {
                    if (targets[f].Y < ground)
                    {
                        targets[f] = new Float3(targets[f].X, ground, targets[f].Z);
                        report.ClampedFrames++;
                    }

                    var move = targets[f] - soles[f];
                    if (move.LengthSquared < 1e-10f) continue;
                    if (weight[f] >= 1f) report.LockedFrames++;
                    Solve(result, f, root[f], parents, leg, move);
                }

                report.SlideAfter += Slide(result, count, root, parents, contact, leg);
            }

            return result;
        }

        static Float3 Anchor(Float3[] soles, int from, int to, int count, int f)
        {
            if (from == 0 && to == count - 1)
            {
                var span = Math.Max(1, to - from);
                return Lerp(soles[from], soles[to], (f - from) / (float)span);
            }

            return to == count - 1 ? soles[to] : soles[from];
        }

        static IEnumerable<(int, int)> Intervals(bool[] contact)
        {
            var f = 0;
            while (f < contact.Length)
            {
                if (!contact[f])
                {
                    f++;
                    continue;
                }

                var start = f;
                while (f + 1 < contact.Length && contact[f + 1]) f++;
                if (f > start) yield return (start, f);
                f++;
            }
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        static Float3 Lerp(Float3 a, Float3 b, float t) => a + (b - a) * t;

        public static void Solve(Pose[] frames, int frame, RigidPose root, int[] parents, Leg leg, Float3 move)
        {
            var model = Model(frames, frame, root, parents);
            var a = model[leg.Upper].Position;
            var b = model[leg.Lower].Position;
            var c = model[leg.Foot].Position;
            var t = c + move;
            var parent = model[parents[leg.Upper]].Rotation;
            var aRotation = model[leg.Upper].Rotation;
            var bRotation = model[leg.Lower].Rotation;
            var footRotation = model[leg.Foot].Rotation;
            TwoBone(a, b, c, t, parent, ref aRotation, ref bRotation);
            var at = frame * SeamFix.Bones;
            var upper = frames[at + leg.Upper];
            upper.Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(Quaternion.Normalize(parent)), aRotation));
            frames[at + leg.Upper] = upper;
            var lower = frames[at + leg.Lower];
            lower.Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(aRotation), bRotation));
            frames[at + leg.Lower] = lower;
            var foot = frames[at + leg.Foot];
            foot.Rotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.InverseUnit(bRotation), Quaternion.Normalize(footRotation)));
            frames[at + leg.Foot] = foot;
        }

        public static void TwoBone(Float3 a, Float3 b, Float3 c, Float3 target, Quaternion parent, ref Quaternion aRotation, ref Quaternion bRotation)
        {
            const float eps = 1e-4f;
            var lab = (b - a).Length;
            var lcb = (b - c).Length;
            var lat = Clamp((target - a).Length, eps, lab + lcb - eps);
            var acAb0 = Angle(c - a, b - a);
            var baBc0 = Angle(a - b, c - b);
            var acAb1 = (float)Math.Acos(Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            var baBc1 = (float)Math.Acos(Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            var axis0 = Float3.Cross(c - a, b - a);
            if (axis0.LengthSquared < 1e-12f) axis0 = Quaternion.Rotate(Quaternion.Normalize(parent), new Float3(1f, 0f, 0f));
            axis0 = axis0 / axis0.Length;
            var r0 = Quaternion.AxisAngle(axis0, acAb1 - acAb0);
            var r1 = Quaternion.AxisAngle(axis0, baBc1 - baBc0);
            var bent = Quaternion.Multiply(r0, r1);
            var knee = a + Quaternion.Rotate(r0, b - a);
            var ankle = knee + Quaternion.Rotate(bent, c - b);
            var swing = Between(ankle - a, target - a);
            aRotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(swing, r0), Quaternion.Normalize(aRotation)));
            bRotation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(swing, bent), Quaternion.Normalize(bRotation)));
        }

        static Quaternion Between(Float3 from, Float3 to)
        {
            var lf = from.Length;
            var lt = to.Length;
            if (lf < 1e-9f || lt < 1e-9f) return Quaternion.Identity;
            var u = from / lf;
            var v = to / lt;
            var axis = Float3.Cross(u, v);
            var sine = axis.Length;
            if (sine < 1e-9f) return Quaternion.Identity;
            return Quaternion.AxisAngle(axis / sine, (float)Math.Atan2(sine, Float3.Dot(u, v)));
        }

        static float Angle(Float3 u, Float3 v)
        {
            var lu = u.Length;
            var lv = v.Length;
            if (lu < 1e-9f || lv < 1e-9f) return 0f;
            return (float)Math.Acos(Clamp(Float3.Dot(u, v) / (lu * lv), -1f, 1f));
        }

        static float Clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;
    }
}

using System;
using SA.Motion.Numerics;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class SeamSide
    {
        public readonly Pose[] At;
        public readonly Pose[] Beyond;

        public SeamSide(Pose[] at, Pose[] beyond)
        {
            At = at;
            Beyond = beyond ?? at;
        }
    }

    public static class SeamFix
    {
        public const int Bones = 56;
        public const int Root = 0;
        public const int Hips = 1;

        public static Pose[] Frame(Pose[] frames, int frame)
        {
            var pose = new Pose[Bones];
            Array.Copy(frames, frame * Bones, pose, 0, Bones);
            return pose;
        }

        public static Pose[] Blend(Pose[] frames, int count, float hz, SeamSide start, SeamSide goal, SeamSettings settings)
        {
            var result = (Pose[])frames.Clone();
            if (count < 2 || !settings.Blends || (start == null && goal == null)) return result;
            var length = (count - 1) / (double)hz;
            var first = Frame(frames, 0);
            var second = Frame(frames, 1);
            var last = Frame(frames, count - 1);
            var beforeLast = Frame(frames, count - 2);
            var startOffsets = start != null ? Offsets(start.At, first, Velocities(start.Beyond, start.At, hz), Velocities(first, second, hz), false) : null;
            var goalOffsets = goal != null ? Offsets(goal.At, last, Velocities(goal.At, goal.Beyond, hz), Velocities(beforeLast, last, hz), true) : null;
            for (var f = 0; f < count; f++)
            {
                var t = f / (double)hz;
                var along = length > 0 ? t / length : 0;
                for (var b = Hips; b < Bones; b++)
                {
                    var height = 0f;
                    var turn = Float3.Zero;
                    if (startOffsets != null)
                    {
                        height += Tail(settings, startOffsets.Height[b], startOffsets.HeightVelocity[b], t, length, along);
                        turn = turn + Tail(settings, startOffsets.Turn[b], startOffsets.TurnVelocity[b], t, length, along);
                    }

                    if (goalOffsets != null)
                    {
                        height += Tail(settings, goalOffsets.Height[b], goalOffsets.HeightVelocity[b], length - t, length, 1 - along);
                        turn = turn + Tail(settings, goalOffsets.Turn[b], goalOffsets.TurnVelocity[b], length - t, length, 1 - along);
                    }

                    var at = f * Bones + b;
                    var pose = result[at];
                    if (b == Hips) pose.Position = new Float3(pose.Position.X, pose.Position.Y + height, pose.Position.Z);
                    pose.Rotation = Quaternion.Normalize(Quaternion.Multiply(RotationVector.Exp(turn), Quaternion.Normalize(pose.Rotation)));
                    result[at] = pose;
                }
            }

            return result;
        }

        static float Tail(SeamSettings settings, float offset, float velocity, double time, double length, double along) =>
            SeamDecay.Offset(settings.Curve, settings.Seconds, offset, velocity, time) - (float)(SeamDecay.Offset(settings.Curve, settings.Seconds, offset, velocity, length) * along);

        static Float3 Tail(SeamSettings settings, Float3 offset, Float3 velocity, double time, double length, double along) =>
            SeamDecay.Offset(settings.Curve, settings.Seconds, offset, velocity, time) - SeamDecay.Offset(settings.Curve, settings.Seconds, offset, velocity, length) * (float)along;

        sealed class SideOffsets
        {
            public readonly float[] Height = new float[Bones];
            public readonly float[] HeightVelocity = new float[Bones];
            public readonly Float3[] Turn = new Float3[Bones];
            public readonly Float3[] TurnVelocity = new Float3[Bones];
        }

        sealed class PoseVelocities
        {
            public readonly float[] Height = new float[Bones];
            public readonly Float3[] Angular = new Float3[Bones];
        }

        static PoseVelocities Velocities(Pose[] earlier, Pose[] later, float hz)
        {
            var v = new PoseVelocities();
            for (var b = Hips; b < Bones; b++)
            {
                v.Height[b] = (later[b].Position.Y - earlier[b].Position.Y) * hz;
                v.Angular[b] = RotationVector.Log(Quaternion.Multiply(Quaternion.Normalize(later[b].Rotation), Quaternion.InverseUnit(Quaternion.Normalize(earlier[b].Rotation)))) * hz;
            }

            return v;
        }

        static SideOffsets Offsets(Pose[] side, Pose[] generated, PoseVelocities sideVelocity, PoseVelocities generatedVelocity, bool backwards)
        {
            var o = new SideOffsets();
            var sign = backwards ? -1f : 1f;
            for (var b = Hips; b < Bones; b++)
            {
                var error = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Normalize(side[b].Rotation), Quaternion.InverseUnit(Quaternion.Normalize(generated[b].Rotation))));
                if (b == Hips) error = Swing(error);
                var rotation = RotationVector.Log(error);
                var angular = sideVelocity.Angular[b] - Quaternion.Rotate(error, generatedVelocity.Angular[b]);
                if (b == Hips) angular = new Float3(angular.X, 0f, angular.Z);
                if (!RotationVector.TryLeftJacobianInverse(rotation, angular, out var rotationVelocity)) rotationVelocity = angular;
                o.Turn[b] = rotation;
                o.TurnVelocity[b] = rotationVelocity * sign;
                if (b != Hips) continue;
                o.Height[b] = side[b].Position.Y - generated[b].Position.Y;
                o.HeightVelocity[b] = (sideVelocity.Height[b] - generatedVelocity.Height[b]) * sign;
            }

            return o;
        }

        public static Quaternion Swing(Quaternion rotation)
        {
            var q = Quaternion.Canonical(Quaternion.Normalize(rotation));
            var twistLength = (float)Math.Sqrt(q.Y * q.Y + q.W * q.W);
            if (twistLength < 1e-6f) return q;
            var twist = new Quaternion(0f, q.Y / twistLength, 0f, q.W / twistLength);
            return Quaternion.Normalize(Quaternion.Multiply(q, Quaternion.InverseUnit(twist)));
        }

        public static double Degrees(Quaternion a, Quaternion b)
        {
            var d = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2.0 * Math.Acos(Math.Min(1.0, d)) * 180.0 / Math.PI;
        }

        public static void Join(Pose[] before, Pose[] after, out double heightStep, out double rotationPop, out int worstBone)
        {
            heightStep = Math.Abs(after[Hips].Position.Y - before[Hips].Position.Y);
            rotationPop = 0;
            worstBone = -1;
            for (var b = Hips; b < Bones; b++)
            {
                var degrees = b == Hips
                    ? Degrees(Swing(Quaternion.Multiply(Quaternion.Normalize(after[b].Rotation), Quaternion.InverseUnit(Quaternion.Normalize(before[b].Rotation)))), Quaternion.Identity)
                    : Degrees(after[b].Rotation, before[b].Rotation);
                if (degrees <= rotationPop) continue;
                rotationPop = degrees;
                worstBone = b;
            }
        }
    }
}

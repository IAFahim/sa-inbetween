using System;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Engine;

internal static class SomaSkeleton
{
    public const int Joints = 77;
    public const int ModelJoints = 30;
    public const int RightLeg = 72;
    public const int LeftLeg = 67;

    public static readonly int[] Parents =
    {
        -1, 0, 1, 2, 3, 4, 5, 6, 6, 6, 6, 3, 11, 12, 13, 14, 15, 16, 17, 14, 19, 20, 21, 22, 14, 24, 25, 26, 27, 14,
        29, 30, 31, 32, 14, 34, 35, 36, 37, 3, 39, 40, 41, 42, 43, 44, 45, 42, 47, 48, 49, 50, 42, 52, 53, 54, 55, 42,
        57, 58, 59, 60, 42, 62, 63, 64, 65, 0, 67, 68, 69, 70, 0, 72, 73, 74, 75,
    };

    public static readonly int[] ModelToPose =
    {
        0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 18, 28, 39, 40, 41, 42, 46, 56, 67, 68, 69, 70, 72, 73, 74, 75,
    };

    public static readonly int[] PoseToModel = BuildPoseToModel();

    private static int[] BuildPoseToModel()
    {
        var map = new int[Joints];
        for (var j = 0; j < Joints; j++)
        {
            map[j] = -1;
        }

        for (var m = 0; m < ModelJoints; m++)
        {
            var p = ModelToPose[m];
            var parent = Parents[p];
            if (parent < 0 || map[parent] >= 0)
            {
                map[p] = m;
            }
        }

        return map;
    }
}

internal static class EngineKernels
{
    public const int PoseFloats = SomaSkeleton.Joints * 7;
    public const int TokenPoses = 4;
    public const int Rows = 8;
    public const int Boundary = 3;
    public const int Features = 366;
    public const int OutputRows = 64;
    public const int RotationOffsetIn = 87;
    public const int RotationOffsetOut = 92;
    public const int ContactOffset = 362;
    public const int Contacts = 4;
    public const double SampleRate = 30.0;

    private static readonly Vec Up = new(0.0, 1.0, 0.0);
    private static readonly Vec AxisX = new(1.0, 0.0, 0.0);

    public static void PadToken(ReadOnlySpan<float> poses, int count, Span<float> token)
    {
        for (var r = 0; r < TokenPoses; r++)
        {
            poses.Slice(Math.Min(r, count - 1) * PoseFloats, PoseFloats).CopyTo(token.Slice(r * PoseFloats, PoseFloats));
        }
    }

    public static void ForwardKinematics(ReadOnlySpan<float> local, Xform root, Span<float> world)
    {
        RotationMath.Write(world, 0, RotationMath.TMul(root, RotationMath.Read(local, 0)));
        var parents = SomaSkeleton.Parents;
        for (var j = 1; j < SomaSkeleton.Joints; j++)
        {
            RotationMath.Write(world, j, RotationMath.TMul(RotationMath.Read(world, parents[j]), RotationMath.Read(local, j)));
        }
    }

    public static double Heading(Vec right, Vec left)
    {
        var across = new Vec(right.X - left.X, 0.0, right.Z - left.Z);
        var n = RotationMath.Length(across);
        if (n <= 1.0e-6)
        {
            return 0.0;
        }

        var forward = RotationMath.Cross(Up, across * (1.0 / n));
        return Math.Atan2(forward.X, forward.Z);
    }

    public static Xform Alignment(ReadOnlySpan<float> lastContextWorld, double headingX, double headingZ)
    {
        var hips = RotationMath.Read(lastContextWorld, 0).P;
        var current = Heading(RotationMath.Read(lastContextWorld, SomaSkeleton.RightLeg).P, RotationMath.Read(lastContextWorld, SomaSkeleton.LeftLeg).P);
        var desired = Math.Atan2(headingX, headingZ);
        return new Xform(new Vec(hips.X, 0.0, hips.Z), RotationMath.AxisAngle(Up, current - desired));
    }

    public static Vec AlignedPosition(Xform alignment, Vec p) => RotationMath.RotateInverse(alignment.Q, p - alignment.P);

    public static void EncodeRows(ReadOnlySpan<float> world8, Xform alignment, Span<float> globalRoot, Span<float> localRoot, Span<float> localPoses)
    {
        var inverse = RotationMath.Conj(alignment.Q);
        for (var f = 0; f < Rows; f++)
        {
            var nf = f == TokenPoses - 1 ? f : Math.Min(f + 1, Rows - 1);
            var pose = world8.Slice(f * PoseFloats, PoseFloats);
            var next = world8.Slice(nf * PoseFloats, PoseFloats);
            var ch = AlignedPosition(alignment, RotationMath.Read(pose, 0).P);
            var nh = AlignedPosition(alignment, RotationMath.Read(next, 0).P);
            var hc = AlignedHeading(pose, alignment);
            var hn = AlignedHeading(next, alignment);
            var dh = Math.Atan2(Math.Sin(hn - hc), Math.Cos(hn - hc));
            var g = globalRoot.Slice(f * 5, 5);
            g[0] = (float)ch.X;
            g[1] = (float)ch.Y;
            g[2] = (float)ch.Z;
            g[3] = (float)Math.Cos(hc);
            g[4] = (float)Math.Sin(hc);
            var l = localRoot.Slice(f * 4, 4);
            l[0] = (float)(dh * SampleRate);
            l[1] = (float)((nh.X - ch.X) * SampleRate);
            l[2] = (float)((nh.Z - ch.Z) * SampleRate);
            l[3] = (float)ch.Y;
            var row = localPoses.Slice(f * 267, 267);
            var ground = new Vec(ch.X, 0.0, ch.Z);
            for (var m = 0; m < SomaSkeleton.ModelJoints; m++)
            {
                var t = RotationMath.Read(pose, SomaSkeleton.ModelToPose[m]);
                if (m > 0)
                {
                    var p = AlignedPosition(alignment, t.P) - ground;
                    var po = (m - 1) * 3;
                    row[po] = (float)p.X;
                    row[po + 1] = (float)p.Y;
                    row[po + 2] = (float)p.Z;
                }

                var r = RotationMath.Mul(inverse, t.Q);
                var a = RotationMath.Rotate(r, AxisX);
                var b = RotationMath.Rotate(r, Up);
                var o = RotationOffsetIn + (6 * m);
                row[o] = (float)a.X;
                row[o + 1] = (float)a.Y;
                row[o + 2] = (float)a.Z;
                row[o + 3] = (float)b.X;
                row[o + 4] = (float)b.Y;
                row[o + 5] = (float)b.Z;
            }
        }
    }

    public static void Conditions(int targetPoses, int allowedBits, Span<int> hasGlobal, Span<int> hasLocalRoot, Span<int> hasLocalPoses, Span<int> allowed)
    {
        for (var i = 0; i < Rows; i++)
        {
            var v = i < TokenPoses + targetPoses ? 1 : 0;
            hasGlobal[i] = v;
            hasLocalPoses[i] = v;
            hasLocalRoot[i] = i == TokenPoses - 1 || i == TokenPoses + targetPoses - 1 ? 0 : v;
        }

        for (var i = 0; i < allowed.Length; i++)
        {
            allowed[i] = (allowedBits >> i) & 1;
        }
    }

    public static Xform Encode(
        ReadOnlySpan<float> contextToken,
        ReadOnlySpan<float> targetToken,
        Xform runtimeToModel,
        double headingX,
        double headingZ,
        Span<float> world8,
        Span<float> globalRoot,
        Span<float> localRoot,
        Span<float> localPoses)
    {
        for (var r = 0; r < TokenPoses; r++)
        {
            ForwardKinematics(contextToken.Slice(r * PoseFloats, PoseFloats), runtimeToModel, world8.Slice(r * PoseFloats, PoseFloats));
            ForwardKinematics(targetToken.Slice(r * PoseFloats, PoseFloats), runtimeToModel, world8.Slice((r + TokenPoses) * PoseFloats, PoseFloats));
        }

        var alignment = Alignment(world8.Slice((TokenPoses - 1) * PoseFloats, PoseFloats), headingX, headingZ);
        EncodeRows(world8, alignment, globalRoot, localRoot, localPoses);
        return alignment;
    }

    public static int ValidFrames(int tokens) => Math.Max(0, Math.Min(Math.Max(tokens * TokenPoses, 0), OutputRows) - (2 * Boundary));

    public static Quat ReadRotation(ReadOnlySpan<float> row, int modelJoint)
    {
        var o = RotationOffsetOut + (6 * modelJoint);
        return RotationMath.FromRotation6D(new Vec(row[o], row[o + 1], row[o + 2]), new Vec(row[o + 3], row[o + 4], row[o + 5]));
    }

    public static void Decode(
        ReadOnlySpan<float> pred,
        int frames,
        Xform alignment,
        Xform modelToRuntime,
        ReadOnlySpan<float> reference,
        Span<float> output,
        Span<float> contacts)
    {
        Span<double> global = stackalloc double[SomaSkeleton.ModelJoints * 4];
        var parents = SomaSkeleton.Parents;
        var poseToModel = SomaSkeleton.PoseToModel;
        for (var f = 0; f < frames; f++)
        {
            var row = pred.Slice((Boundary + f) * Features, Features);
            for (var c = 0; c < Contacts; c++)
            {
                contacts[(f * Contacts) + c] = Math.Min(Math.Max(row[ContactOffset + c], 0f), 1f);
            }

            for (var m = 0; m < SomaSkeleton.ModelJoints; m++)
            {
                var q = ReadRotation(row, m);
                global[m * 4] = q.X;
                global[(m * 4) + 1] = q.Y;
                global[(m * 4) + 2] = q.Z;
                global[(m * 4) + 3] = q.W;
            }

            var pose = output.Slice(f * PoseFloats, PoseFloats);
            var hips = new Xform(new Vec(row[0], row[1], row[2]), Global(global, 0));
            RotationMath.Write(pose, 0, RotationMath.TMul(modelToRuntime, RotationMath.TMul(alignment, hips)));
            for (var j = 1; j < SomaSkeleton.Joints; j++)
            {
                var m = poseToModel[j];
                var bind = RotationMath.Read(reference, j);
                RotationMath.Write(
                    pose,
                    j,
                    m < 0 ? bind : new Xform(bind.P, RotationMath.Normalize(RotationMath.Mul(RotationMath.Conj(Global(global, poseToModel[parents[j]])), Global(global, m)))));
            }
        }
    }

    public static void EndpointDeltas(ReadOnlySpan<float> output, int frames, ReadOnlySpan<float> contextLast, ReadOnlySpan<float> targetFirst, Span<float> start, Span<float> end)
    {
        var first = output.Slice(0, PoseFloats);
        var last = output.Slice((frames - 1) * PoseFloats, PoseFloats);
        RotationMath.Write(start, 0, RootDelta(RotationMath.Read(first, 0), RotationMath.Read(contextLast, 0)));
        RotationMath.Write(end, 0, RootDelta(RotationMath.Read(last, 0), RotationMath.Read(targetFirst, 0)));
        for (var j = 1; j < SomaSkeleton.Joints; j++)
        {
            RotationMath.Write(start, j, RotationMath.TMul(RotationMath.TInv(RotationMath.Read(first, j)), RotationMath.Read(contextLast, j)));
            RotationMath.Write(end, j, RotationMath.TMul(RotationMath.TInv(RotationMath.Read(last, j)), RotationMath.Read(targetFirst, j)));
        }
    }

    public static void ApplyEndpointDeltas(Span<float> output, int frames, ReadOnlySpan<float> start, ReadOnlySpan<float> end)
    {
        if (frames < 2)
        {
            return;
        }

        var startRoot = RotationMath.Read(start, 0);
        var endRoot = RotationMath.Read(end, 0);
        for (var f = 0; f < frames; f++)
        {
            var ewf = (float)f / (frames - 1);
            var swf = 1f - ewf;
            double ew = ewf, sw = swf;
            var pose = output.Slice(f * PoseFloats, PoseFloats);
            var root = RotationMath.Read(pose, 0);
            var rot = RotationMath.Mul(RotationMath.Mul(root.Q, RotationMath.Slerp(Quat.Identity, startRoot.Q, sw)), RotationMath.Slerp(Quat.Identity, endRoot.Q, ew));
            RotationMath.Write(pose, 0, new Xform(root.P + (startRoot.P * sw) + (endRoot.P * ew), RotationMath.Normalize(rot)));
            for (var j = 1; j < SomaSkeleton.Joints; j++)
            {
                var c = RotationMath.TMul(RotationMath.Read(pose, j), RotationMath.Blend(Xform.Identity, RotationMath.Read(start, j), sw));
                RotationMath.Write(pose, j, RotationMath.TMul(c, RotationMath.Blend(Xform.Identity, RotationMath.Read(end, j), ew)));
            }
        }
    }

    private static Xform RootDelta(Xform generated, Xform guide) =>
        new(guide.P - generated.P, RotationMath.Normalize(RotationMath.Mul(RotationMath.Conj(RotationMath.Normalize(generated.Q)), RotationMath.Normalize(guide.Q))));

    private static double AlignedHeading(ReadOnlySpan<float> pose, Xform alignment) =>
        Heading(
            AlignedPosition(alignment, RotationMath.Read(pose, SomaSkeleton.RightLeg).P),
            AlignedPosition(alignment, RotationMath.Read(pose, SomaSkeleton.LeftLeg).P));

    private static Quat Global(ReadOnlySpan<double> global, int m) =>
        new(global[m * 4], global[(m * 4) + 1], global[(m * 4) + 2], global[(m * 4) + 3]);
}

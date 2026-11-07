using System;
using SA.Inbetween.Soma;

namespace SA.Inbetween.Engine;

internal static class IkKernels
{
    public const int Chains = 4;
    public const double Epsilon = 1.0e-3;

    private static readonly int[] Pins = { 1, 2, 4, 8 };
    private static readonly int[] Roots = { 12, 40, 67, 72 };
    private static readonly int[] Mids = { 13, 41, 68, 73 };
    private static readonly int[] Tips = { 14, 42, 69, 74 };
    private static readonly double[] HintY = { -0.25, 0.25, -0.25, 0.25 };

    public static void Capture(ReadOnlySpan<float> targetLocal, Span<float> world, Span<float> pinned)
    {
        EngineKernels.ForwardKinematics(targetLocal, Xform.Identity, world);
        for (var k = 0; k < Chains; k++)
        {
            RotationMath.Write(pinned, k, RotationMath.Read(world, Tips[k]));
        }
    }

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Min(Math.Max((x - edge0) / (edge1 - edge0), 0.0), 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    public static double Weight(double timeToTransition, double playbackDuration) =>
        playbackDuration <= 0.0 ? 1.0 : Smoothstep(0.5, 1.0, 1.0 - (timeToTransition / playbackDuration));

    public static void Apply(Span<float> pose, double weight, int pinMask, ReadOnlySpan<float> pinned, Span<float> world)
    {
        if (pinMask == 0 || weight <= 0.0)
        {
            return;
        }

        EngineKernels.ForwardKinematics(pose, Xform.Identity, world);
        var parents = SomaSkeleton.Parents;
        for (var k = 0; k < Chains; k++)
        {
            if ((pinMask & Pins[k]) == 0)
            {
                continue;
            }

            var rootParent = parents[Roots[k]];
            var pa = rootParent >= 0 ? RotationMath.Read(world, rootParent) : Xform.Identity;
            var mid = RotationMath.Read(world, Mids[k]);
            var hint = RotationMath.TMul(mid, new Xform(new Vec(0.0, HintY[k], 0.0), Quat.Identity)).P;
            Solve(weight, pa, RotationMath.Read(world, Roots[k]), mid, RotationMath.Read(world, Tips[k]), RotationMath.Read(pinned, k), hint, out var sa, out var sb, out var sc);
            RotationMath.Write(pose, Roots[k], RotationMath.TMul(RotationMath.TInv(pa), sa));
            RotationMath.Write(pose, Mids[k], RotationMath.TMul(RotationMath.TInv(sa), sb));
            RotationMath.Write(pose, Tips[k], RotationMath.TMul(RotationMath.TInv(sb), sc));
        }
    }

    private static void Solve(double weight, Xform pa, Xform aTx, Xform bTx, Xform cTx, Xform tTx, Vec hint, out Xform aOut, out Xform bOut, out Xform cOut)
    {
        weight = Math.Min(Math.Max(weight, 0.0), 1.0);
        Vec a = aTx.P, b = bTx.P, c = cTx.P;
        var t = c + ((tTx.P - c) * weight);
        var lab = RotationMath.Length(b - a);
        var lcb = RotationMath.Length(b - c);
        var lat = Math.Min(Math.Max(RotationMath.Length(t - a), Epsilon), lab + lcb - Epsilon);
        var axis = RotationMath.Normalize(RotationMath.Cross(c - a, hint - a));
        var acab0 = Math.Atan2(RotationMath.Dot(axis, RotationMath.Cross(c - a, b - a)), RotationMath.Dot(c - a, b - a));
        var babc0 = Math.Atan2(RotationMath.Dot(axis, RotationMath.Cross(a - b, c - b)), RotationMath.Dot(a - b, c - b));
        var acab1 = Math.Acos(Clamp(((lab * lab) + (lat * lat) - (lcb * lcb)) / (2.0 * lab * lat)));
        var babc1 = Math.Acos(Clamp(((lab * lab) + (lcb * lcb) - (lat * lat)) / (2.0 * lab * lcb)));
        var rotA = RotationMath.AxisAngle(RotationMath.RotateInverse(aTx.Q, axis), acab1 - acab0);
        var rotB = RotationMath.AxisAngle(RotationMath.RotateInverse(bTx.Q, axis), babc1 - babc0);
        var zero = new Vec(0.0, 0.0, 0.0);
        var aLocal = RotationMath.TMul(RotationMath.TMul(RotationMath.TInv(pa), aTx), new Xform(zero, rotA));
        var aWorld = RotationMath.TMul(pa, aLocal);
        var acLocal = RotationMath.RotateInverse(aWorld.Q, c - a);
        var atLocal = RotationMath.RotateInverse(aWorld.Q, t - a);
        aLocal = RotationMath.TMul(aLocal, new Xform(zero, Between(acLocal, atLocal)));
        aWorld = RotationMath.TMul(pa, aLocal);
        var bLocal = RotationMath.TMul(RotationMath.TMul(RotationMath.TInv(aTx), bTx), new Xform(zero, rotB));
        var bWorld = RotationMath.TMul(aWorld, bLocal);
        var cWorld = RotationMath.TMul(bWorld, RotationMath.TMul(RotationMath.TInv(bTx), cTx));
        aOut = new Xform(aWorld.P, RotationMath.Normalize(aWorld.Q));
        bOut = new Xform(bWorld.P, RotationMath.Normalize(bWorld.Q));
        cOut = new Xform(cWorld.P, RotationMath.Normalize(RotationMath.Slerp(cWorld.Q, tTx.Q, weight)));
    }

    private static double Clamp(double v) => Math.Min(Math.Max(v, -1.0), 1.0);

    private static Quat Between(Vec a, Vec b)
    {
        a = RotationMath.Normalize(a);
        b = RotationMath.Normalize(b);
        var c = RotationMath.Cross(a, b);
        return RotationMath.Normalize(new Quat(c.X, c.Y, c.Z, 1.0 + RotationMath.Dot(a, b)));
    }
}

using System;

namespace SA.Inbetween.Soma;

internal readonly struct Vec
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec operator -(Vec a) => new(-a.X, -a.Y, -a.Z);

    public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
}

internal readonly struct Quat
{
    public static readonly Quat Identity = new(0.0, 0.0, 0.0, 1.0);

    public readonly double X;
    public readonly double Y;
    public readonly double Z;
    public readonly double W;

    public Quat(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
}

internal readonly struct Xform
{
    public static readonly Xform Identity = new(new Vec(0.0, 0.0, 0.0), Quat.Identity);

    public readonly Vec P;
    public readonly Quat Q;

    public Xform(Vec p, Quat q)
    {
        P = p;
        Q = q;
    }
}

internal static class RotationMath
{
    public static double Dot(Vec a, Vec b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    public static Vec Cross(Vec a, Vec b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    public static double Length(Vec v) => Math.Sqrt(Dot(v, v));

    public static Vec Normalize(Vec v)
    {
        var n = Length(v);
        return n > 0.0 ? v * (1.0 / n) : new Vec(0.0, 0.0, 0.0);
    }

    public static Quat Mul(Quat a, Quat b) => new(
        (a.W * b.X) + (b.W * a.X) + (a.Y * b.Z) - (b.Y * a.Z),
        (a.W * b.Y) + (b.W * a.Y) + (a.Z * b.X) - (b.Z * a.X),
        (a.W * b.Z) + (b.W * a.Z) + (a.X * b.Y) - (b.X * a.Y),
        (a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z));

    public static Quat Conj(Quat q) => new(-q.X, -q.Y, -q.Z, q.W);

    public static Quat Normalize(Quat q)
    {
        var n = Math.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
        return n > 0.0 ? new Quat(q.X / n, q.Y / n, q.Z / n, q.W / n) : Quat.Identity;
    }

    public static Vec Rotate(Quat q, Vec v)
    {
        var c = (2.0 * q.W * q.W) - 1.0;
        var d = 2.0 * ((q.X * v.X) + (q.Y * v.Y) + (q.Z * v.Z));
        return new(
            (v.X * c) + (q.X * d) + (((q.Y * v.Z) - (q.Z * v.Y)) * q.W * 2.0),
            (v.Y * c) + (q.Y * d) + (((q.Z * v.X) - (q.X * v.Z)) * q.W * 2.0),
            (v.Z * c) + (q.Z * d) + (((q.X * v.Y) - (q.Y * v.X)) * q.W * 2.0));
    }

    public static Vec RotateInverse(Quat q, Vec v)
    {
        var c = (2.0 * q.W * q.W) - 1.0;
        var d = 2.0 * ((q.X * v.X) + (q.Y * v.Y) + (q.Z * v.Z));
        return new(
            (v.X * c) + (q.X * d) - (((q.Y * v.Z) - (q.Z * v.Y)) * q.W * 2.0),
            (v.Y * c) + (q.Y * d) - (((q.Z * v.X) - (q.X * v.Z)) * q.W * 2.0),
            (v.Z * c) + (q.Z * d) - (((q.X * v.Y) - (q.Y * v.X)) * q.W * 2.0));
    }

    public static Quat AxisAngle(Vec axis, double angle)
    {
        var h = 0.5 * angle;
        var s = Math.Sin(h);
        return new(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(h));
    }

    public static Quat Slerp(Quat q0, Quat q1, double t)
    {
        var d = Mul(Conj(q0), q1);
        var v = new Vec(d.X, d.Y, d.Z);
        var axis = d.W < 0.0 ? -Normalize(v) : Normalize(v);
        var angle = 2.0 * Math.Atan2(Length(v), Math.Abs(d.W));
        return Mul(q0, AxisAngle(axis, t * angle));
    }

    public static Xform TMul(Xform a, Xform b) => new(a.P + Rotate(a.Q, b.P), Mul(a.Q, b.Q));

    public static Xform TInv(Xform t)
    {
        var qi = Conj(t.Q);
        return new(-Rotate(qi, t.P), qi);
    }

    public static Xform Blend(Xform t0, Xform t1, double w) =>
        new(t0.P + ((t1.P - t0.P) * w), Slerp(t0.Q, t1.Q, w));

    public static Quat FromColumns(Vec x, Vec y, Vec z)
    {
        double m00 = x.X, m10 = x.Y, m20 = x.Z;
        double m01 = y.X, m11 = y.Y, m21 = y.Z;
        double m02 = z.X, m12 = z.Y, m22 = z.Z;
        var tr = m00 + m11 + m22;
        double qx, qy, qz, qw, h;
        if (tr >= 0.0)
        {
            h = Math.Sqrt(tr + 1.0);
            qw = 0.5 * h;
            h = 0.5 / h;
            qx = (m21 - m12) * h;
            qy = (m02 - m20) * h;
            qz = (m10 - m01) * h;
        }
        else
        {
            var d = m11 > m00 ? 1 : 0;
            if (m22 > (d == 0 ? m00 : m11))
            {
                d = 2;
            }

            if (d == 0)
            {
                h = Math.Sqrt(m00 - (m11 + m22) + 1.0);
                qx = 0.5 * h;
                h = 0.5 / h;
                qy = (m01 + m10) * h;
                qz = (m20 + m02) * h;
                qw = (m21 - m12) * h;
            }
            else if (d == 1)
            {
                h = Math.Sqrt(m11 - (m22 + m00) + 1.0);
                qy = 0.5 * h;
                h = 0.5 / h;
                qz = (m12 + m21) * h;
                qx = (m01 + m10) * h;
                qw = (m02 - m20) * h;
            }
            else
            {
                h = Math.Sqrt(m22 - (m00 + m11) + 1.0);
                qz = 0.5 * h;
                h = 0.5 / h;
                qx = (m20 + m02) * h;
                qy = (m12 + m21) * h;
                qw = (m10 - m01) * h;
            }
        }

        return Normalize(new Quat(qx, qy, qz, qw));
    }

    public static Quat FromRotation6D(Vec a, Vec b)
    {
        var x = Normalize(a);
        var z = Normalize(Cross(x, b));
        var y = Cross(z, x);
        return FromColumns(x, y, z);
    }

    public static Xform Read(ReadOnlySpan<float> values, int joint)
    {
        var o = joint * 7;
        return new(new Vec(values[o], values[o + 1], values[o + 2]), new Quat(values[o + 3], values[o + 4], values[o + 5], values[o + 6]));
    }

    public static void Write(Span<float> values, int joint, Xform t)
    {
        var o = joint * 7;
        values[o] = (float)t.P.X;
        values[o + 1] = (float)t.P.Y;
        values[o + 2] = (float)t.P.Z;
        values[o + 3] = (float)t.Q.X;
        values[o + 4] = (float)t.Q.Y;
        values[o + 5] = (float)t.Q.Z;
        values[o + 6] = (float)t.Q.W;
    }
}

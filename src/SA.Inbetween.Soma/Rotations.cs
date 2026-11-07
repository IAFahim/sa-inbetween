using System;

namespace SA.Inbetween.Soma;

public static class Rotations
{
    public static Quaternion<TSpace> Identity<TSpace>()
        where TSpace : struct, ISpace => new(0f, 0f, 0f, 1f);

    public static Quaternion<TSpace> Multiply<TSpace>(Quaternion<TSpace> left, Quaternion<TSpace> right)
        where TSpace : struct, ISpace
    {
        double ax = left.X, ay = left.Y, az = left.Z, aw = left.W;
        double bx = right.X, by = right.Y, bz = right.Z, bw = right.W;
        return new(
            (float)((aw * bx) + (ax * bw) + (ay * bz) - (az * by)),
            (float)((aw * by) - (ax * bz) + (ay * bw) + (az * bx)),
            (float)((aw * bz) + (ax * by) - (ay * bx) + (az * bw)),
            (float)((aw * bw) - (ax * bx) - (ay * by) - (az * bz)));
    }

    public static Quaternion<TSpace> Conjugate<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace => new(-rotation.X, -rotation.Y, -rotation.Z, rotation.W);

    public static Quaternion<TSpace> Normalize<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace
    {
        double x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;
        var norm = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        return norm > 0.0 && !double.IsInfinity(norm) && !double.IsNaN(norm)
            ? new((float)(x / norm), (float)(y / norm), (float)(z / norm), (float)(w / norm))
            : Identity<TSpace>();
    }

    public static Vector3<TSpace> Rotate<TSpace>(Quaternion<TSpace> rotation, Vector3<TSpace> vector)
        where TSpace : struct, ISpace
    {
        double qx = rotation.X, qy = rotation.Y, qz = rotation.Z, qw = rotation.W;
        double vx = vector.X, vy = vector.Y, vz = vector.Z;
        var tx = 2.0 * ((qy * vz) - (qz * vy));
        var ty = 2.0 * ((qz * vx) - (qx * vz));
        var tz = 2.0 * ((qx * vy) - (qy * vx));
        return new(
            (float)(vx + (qw * tx) + ((qy * tz) - (qz * ty))),
            (float)(vy + (qw * ty) + ((qz * tx) - (qx * tz))),
            (float)(vz + (qw * tz) + ((qx * ty) - (qy * tx))));
    }

    public static Quaternion<TSpace> FromAxisAngle<TSpace>(Vector3<TSpace> unitAxis, Radians angle)
        where TSpace : struct, ISpace
    {
        var half = 0.5 * angle.Value;
        var s = Math.Sin(half);
        return new((float)(unitAxis.X * s), (float)(unitAxis.Y * s), (float)(unitAxis.Z * s), (float)Math.Cos(half));
    }

    public static Radians AngleBetween<TSpace>(Quaternion<TSpace> left, Quaternion<TSpace> right)
        where TSpace : struct, ISpace
    {
        var a = Length(left);
        var b = Length(right);
        if (!(a > 0.0) || !(b > 0.0))
        {
            return new(float.NaN);
        }

        var dot = ((left.X * (double)right.X) + (left.Y * (double)right.Y) + (left.Z * (double)right.Z) + (left.W * (double)right.W)) / (a * b);
        return new((float)(2.0 * Math.Acos(Math.Min(1.0, Math.Abs(dot)))));
    }

    public static Rotation6D<TSpace> ToRotation6D<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace =>
        new(Rotate(rotation, new Vector3<TSpace>(1f, 0f, 0f)), Rotate(rotation, new Vector3<TSpace>(0f, 1f, 0f)));

    public static Quaternion<TSpace> FromRotation6D<TSpace>(Rotation6D<TSpace> rotation)
        where TSpace : struct, ISpace
    {
        var q = RotationMath.FromRotation6D(
            new Vec(rotation.XAxis.X, rotation.XAxis.Y, rotation.XAxis.Z),
            new Vec(rotation.YAxis.X, rotation.YAxis.Y, rotation.YAxis.Z));
        var sign = q.W < 0.0 ? -1.0 : 1.0;
        return new((float)(q.X * sign), (float)(q.Y * sign), (float)(q.Z * sign), (float)(q.W * sign));
    }

    private static double Length<TSpace>(Quaternion<TSpace> q)
        where TSpace : struct, ISpace =>
        Math.Sqrt((q.X * (double)q.X) + (q.Y * (double)q.Y) + (q.Z * (double)q.Z) + (q.W * (double)q.W));
}

public static class SpaceConversions
{
    private static readonly float HalfSqrt2 = (float)Math.Sqrt(0.5);

    public static Vector3<SomaRuntimeZUp> ModelToRuntime(Vector3<SomaModel> vector) => new(vector.X, -vector.Z, vector.Y);

    public static Quaternion<SomaRuntimeZUp> ModelToRuntime(Quaternion<SomaModel> rotation) =>
        Rotations.Multiply(new Quaternion<SomaRuntimeZUp>(HalfSqrt2, 0f, 0f, HalfSqrt2), Retag<SomaModel, SomaRuntimeZUp>(rotation));

    public static Vector3<SomaModel> RuntimeToModel(Vector3<SomaRuntimeZUp> vector) => new(vector.X, vector.Z, -vector.Y);

    public static Quaternion<SomaModel> RuntimeToModel(Quaternion<SomaRuntimeZUp> rotation) =>
        Rotations.Multiply(new Quaternion<SomaModel>(-HalfSqrt2, 0f, 0f, HalfSqrt2), Retag<SomaRuntimeZUp, SomaModel>(rotation));

    public static Vector3<UnityWorld> ModelToUnity(Vector3<SomaModel> vector) => new(-vector.X, vector.Y, vector.Z);

    public static Quaternion<UnityWorld> ModelToUnity(Quaternion<SomaModel> rotation) => new(rotation.X, -rotation.Y, -rotation.Z, rotation.W);

    public static Vector3<SomaModel> UnityToModel(Vector3<UnityWorld> vector) => new(-vector.X, vector.Y, vector.Z);

    public static Quaternion<SomaModel> UnityToModel(Quaternion<UnityWorld> rotation) => new(rotation.X, -rotation.Y, -rotation.Z, rotation.W);

    private static Quaternion<TTo> Retag<TFrom, TTo>(Quaternion<TFrom> q)
        where TFrom : struct, ISpace
        where TTo : struct, ISpace => new(q.X, q.Y, q.Z, q.W);
}

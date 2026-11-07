namespace SA.Inbetween.Soma;

public interface ISpace
{
}

public readonly struct SomaModel : ISpace
{
}

public readonly struct SomaRuntimeZUp : ISpace
{
}

public readonly struct UnityWorld : ISpace
{
}

public readonly record struct Vector3<TSpace>(float X, float Y, float Z)
    where TSpace : struct, ISpace;

public readonly record struct Quaternion<TSpace>(float X, float Y, float Z, float W)
    where TSpace : struct, ISpace;

public readonly record struct Rotation6D<TSpace>(Vector3<TSpace> XAxis, Vector3<TSpace> YAxis)
    where TSpace : struct, ISpace;

public readonly record struct RigidTransform<TSpace>(Vector3<TSpace> Translation, Quaternion<TSpace> Rotation)
    where TSpace : struct, ISpace;

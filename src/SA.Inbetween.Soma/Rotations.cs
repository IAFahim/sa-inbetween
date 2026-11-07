namespace SA.Inbetween.Soma;

public static class Rotations
{
    public static Quaternion<TSpace> Identity<TSpace>()
        where TSpace : struct, ISpace => new(0f, 0f, 0f, 1f);

    public static Quaternion<TSpace> Multiply<TSpace>(Quaternion<TSpace> left, Quaternion<TSpace> right)
        where TSpace : struct, ISpace => Poisoned<TSpace>();

    public static Quaternion<TSpace> Conjugate<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace => Poisoned<TSpace>();

    public static Quaternion<TSpace> Normalize<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace => Poisoned<TSpace>();

    public static Vector3<TSpace> Rotate<TSpace>(Quaternion<TSpace> rotation, Vector3<TSpace> vector)
        where TSpace : struct, ISpace => new(float.NaN, float.NaN, float.NaN);

    public static Quaternion<TSpace> FromAxisAngle<TSpace>(Vector3<TSpace> unitAxis, Radians angle)
        where TSpace : struct, ISpace => Poisoned<TSpace>();

    public static Radians AngleBetween<TSpace>(Quaternion<TSpace> left, Quaternion<TSpace> right)
        where TSpace : struct, ISpace => new(float.NaN);

    public static Rotation6D<TSpace> ToRotation6D<TSpace>(Quaternion<TSpace> rotation)
        where TSpace : struct, ISpace => new(new(float.NaN, float.NaN, float.NaN), new(float.NaN, float.NaN, float.NaN));

    public static Quaternion<TSpace> FromRotation6D<TSpace>(Rotation6D<TSpace> rotation)
        where TSpace : struct, ISpace => Poisoned<TSpace>();

    private static Quaternion<TSpace> Poisoned<TSpace>()
        where TSpace : struct, ISpace => new(float.NaN, float.NaN, float.NaN, float.NaN);
}

public static class SpaceConversions
{
    public static Vector3<SomaRuntimeZUp> ModelToRuntime(Vector3<SomaModel> vector) => new(float.NaN, float.NaN, float.NaN);

    public static Quaternion<SomaRuntimeZUp> ModelToRuntime(Quaternion<SomaModel> rotation) => new(float.NaN, float.NaN, float.NaN, float.NaN);

    public static Vector3<SomaModel> RuntimeToModel(Vector3<SomaRuntimeZUp> vector) => new(float.NaN, float.NaN, float.NaN);

    public static Quaternion<SomaModel> RuntimeToModel(Quaternion<SomaRuntimeZUp> rotation) => new(float.NaN, float.NaN, float.NaN, float.NaN);

    public static Vector3<UnityWorld> ModelToUnity(Vector3<SomaModel> vector) => new(float.NaN, float.NaN, float.NaN);

    public static Quaternion<UnityWorld> ModelToUnity(Quaternion<SomaModel> rotation) => new(float.NaN, float.NaN, float.NaN, float.NaN);

    public static Vector3<SomaModel> UnityToModel(Vector3<UnityWorld> vector) => new(float.NaN, float.NaN, float.NaN);

    public static Quaternion<SomaModel> UnityToModel(Quaternion<UnityWorld> rotation) => new(float.NaN, float.NaN, float.NaN, float.NaN);
}

namespace SA.Inbetween.Unity
{
    public static class SomaUnity
    {
        public static UnityEngine.Vector3 ToUnity(SA.Inbetween.Soma.Vector3<SA.Inbetween.Soma.UnityWorld> v) => new UnityEngine.Vector3(v.X, v.Y, v.Z);
    }
}

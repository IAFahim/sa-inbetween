using UnityEngine;

namespace SA.Inbetween.Clips
{
    public sealed class SaPoseAsset : ScriptableObject
    {
        public const int Joints = 77;
        public const int FloatsPerJoint = 7;
        public const int FloatCount = Joints * FloatsPerJoint;

        public float[] Values = System.Array.Empty<float>();
        public string Problem = "";

        public bool Valid => Problem == "" && Values != null && Values.Length == FloatCount;
    }
}

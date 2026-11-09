using SA.Animation;
using SA.Animation.Content;
using UnityEngine;
using MotionPose = SA.Motion.Numerics.Pose;

namespace SA.Inbetween.Clips
{
    public sealed class SaClipPose
    {
        public const int BoneCount = CanonicalHumanoid.BoneCount;
        public const int Hips = CanonicalHumanoid.Hips;
        public const int Head = 6;

        static int[] parents;
        static MotionPose[] reference;
        static readonly Quaternion[] Rotations = new Quaternion[BoneCount];

        readonly SharedClip clip;
        readonly MotionPose[] local = new MotionPose[BoneCount];

        SaClipPose(SharedClip clip)
        {
            this.clip = clip;
        }

        public float Duration => (float)clip.Duration;
        public int SampleCount => clip.SampleCount;

        public static int Parent(int bone)
        {
            Ensure();
            return parents[bone];
        }

        static void Ensure()
        {
            if (parents != null)
                return;
            var p = new int[BoneCount];
            var r = new MotionPose[BoneCount];
            CanonicalHumanoid.WriteReference(p, r);
            reference = r;
            parents = p;
        }

        public static bool TryLoad(byte[] bytes, out SaClipPose pose, out string error)
        {
            pose = null;
            error = string.Empty;
            if (bytes == null || bytes.Length == 0)
            {
                error = "clip has no data";
                return false;
            }

            Ensure();
            var directory = new SacaChunk[SacaContainer.MaxChunks];
            var verify = SacaFile.Verify(bytes, directory, out _, out var header);
            if (verify != AnimationError.None)
            {
                error = "clip rejected: " + verify;
                return false;
            }

            var load = SharedClipReader.Load(bytes, header.Family, reference, out var clip);
            if (load != AnimationError.None || clip == null)
            {
                error = "clip rejected: " + load;
                return false;
            }

            pose = new SaClipPose(clip);
            return true;
        }

        public static void Reference(Vector3[] world)
        {
            Ensure();
            Solve(reference, Pose.identity, world);
        }

        public Pose RootAt(float seconds)
        {
            if (SampleLocator.Locate(clip.Duration, clip.SampleCount, seconds, false, out var window) != AnimationError.None)
                return Pose.identity;
            var a = clip.Root[window.First];
            var b = clip.Root[window.Second];
            return new Pose(
                Vector3.Lerp(ToUnity(a.Position), ToUnity(b.Position), window.Alpha),
                Quaternion.Slerp(ToUnity(a.Rotation), ToUnity(b.Rotation), window.Alpha));
        }

        public bool Sample(float seconds, Vector3[] world) => Sample(seconds, RootAt(seconds), world);

        public bool Sample(float seconds, Pose root, Vector3[] world)
        {
            if (clip.Sample(Mathf.Clamp(seconds, 0f, Duration), reference, local) != AnimationError.None)
                return false;
            Solve(local, root, world);
            return true;
        }

        static void Solve(MotionPose[] poses, Pose root, Vector3[] world)
        {
            var rotations = Rotations;
            for (var i = 0; i < BoneCount; i++)
            {
                var parent = parents[i];
                var position = parent < 0 ? root.position : world[parent];
                var rotation = parent < 0 ? root.rotation : rotations[parent];
                world[i] = position + rotation * ToUnity(poses[i].Position);
                rotations[i] = Quaternion.Normalize(rotation * ToUnity(poses[i].Rotation));
            }
        }

        static Vector3 ToUnity(SA.Motion.Numerics.Float3 v) => new Vector3(v.X, v.Y, v.Z);

        static Quaternion ToUnity(SA.Motion.Numerics.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}

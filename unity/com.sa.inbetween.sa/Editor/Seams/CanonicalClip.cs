using System;
using SA.Animation;
using SA.Animation.Content;
using SA.Editor;
using SA.Motion.Numerics;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class CanonicalFrames
    {
        public Pose[] Poses;
        public RigidPose[] Root;
        public float[] Contacts;
        public int Count;
        public double Duration;
        public bool Loop;
        public RootPolicy RootPolicy = RootPolicy.Planar;

        public float Rate => Count > 1 && Duration > 0 ? (float)((Count - 1) / Duration) : SaClipExport.Rate;
    }

    public static class CanonicalClip
    {
        public static readonly PoseMarker[] FootMarkers =
        {
            new PoseMarker(50, FootFix.Sole),
            new PoseMarker(54, FootFix.Sole),
        };

        static int[] parents;
        static Pose[] reference;

        public static int[] Parents
        {
            get
            {
                if (parents == null) parents = SharedFamilyBake.CanonicalParentsArray();
                return parents;
            }
        }

        public static Pose[] Reference
        {
            get
            {
                if (reference == null) reference = SharedFamilyBake.CanonicalReferencePoses();
                return reference;
            }
        }

        public static AssetId Family
        {
            get
            {
                SharedFamilyBake.FamilyId(out var a, out var b, out var c, out var d);
                return new AssetId(a, b, c, d);
            }
        }

        public static bool TryLoad(byte[] bytes, out SharedClip clip, out string error)
        {
            clip = null;
            error = "";
            if (bytes == null || bytes.Length == 0)
            {
                error = "the clip is empty.";
                return false;
            }

            var load = SharedClipReader.Load(bytes, Family, Reference, out clip);
            if (load == AnimationError.None && clip != null) return true;
            error = "the clip does not load for the humanoid family (" + load + ").";
            return false;
        }

        public static Pose[] Sample(SharedClip clip, double seconds)
        {
            var pose = new Pose[SeamFix.Bones];
            clip.Sample(Math.Max(0.0, Math.Min(clip.Duration, seconds)), Reference, pose);
            return pose;
        }

        public static CanonicalFrames Frames(SharedClip clip)
        {
            var count = clip.SampleCount;
            var frames = new CanonicalFrames
            {
                Count = count,
                Duration = clip.Duration,
                Loop = clip.Loop,
                RootPolicy = clip.RootPolicy,
                Poses = new Pose[count * SeamFix.Bones],
                Root = new RigidPose[count],
                Contacts = clip.ContactWeights != null && clip.ContactWeights.Length > 0 ? (float[])clip.ContactWeights.Clone() : null,
            };
            var probe = new Pose[SeamFix.Bones];
            for (var f = 0; f < count; f++)
            {
                var seconds = count > 1 ? clip.Duration * f / (count - 1) : 0.0;
                clip.Sample(seconds, Reference, probe);
                Array.Copy(probe, 0, frames.Poses, f * SeamFix.Bones, SeamFix.Bones);
                frames.Root[f] = clip.Root != null && clip.Root.Length == count ? clip.Root[f] : RigidPose.Identity;
            }

            return frames;
        }

        public static bool TryEncode(CanonicalFrames frames, out byte[] bytes, out string error)
        {
            error = "";
            var contacts = frames.Contacts != null && frames.Contacts.Length > 0 && frames.Contacts.Length % frames.Count == 0 ? frames.Contacts : Array.Empty<float>();
            var encode = SharedClipEncoder.Encode(Family, Parents, Reference, frames.Poses, frames.Count, frames.Duration, frames.Loop,
                frames.RootPolicy, frames.Root, ReadOnlySpan<Pose>.Empty, contacts, FootMarkers, true, CompressionLimits.Default, out bytes, out _);
            if (encode == AnimationError.None && TryLoad(bytes, out _, out error)) return true;
            if (error == "") error = "the corrected clip could not be encoded (" + encode + ").";
            return false;
        }
    }
}

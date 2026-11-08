using SA.Animation;
using SA.Animation.Content;
using SA.Motion.Numerics;
using Unity.Collections;
using Unity.Entities;

namespace SA.Inbetween.Clips
{
    public static class SaClipRuntime
    {
        public static bool TryAttach(EntityManager manager, Entity entity, byte[] bytes, bool loop, out int clipIndex, out string error)
        {
            clipIndex = -1;
            error = string.Empty;
            if (bytes == null || bytes.Length == 0)
            {
                error = "clip has no data";
                return false;
            }

            if (!manager.HasBuffer<SharedClipAsset>(entity) || !manager.HasBuffer<SharedContribution>(entity))
            {
                error = "entity is not a shared-family character";
                return false;
            }

            var clips = manager.GetBuffer<SharedClipAsset>(entity);
            if (clips.Length == 0 || !clips[0].Value.IsCreated)
            {
                error = "character has no clips to take the family from";
                return false;
            }

            var family = clips[0].Value.Value.Family;
            var parents = new int[CanonicalHumanoid.BoneCount];
            var reference = new Pose[CanonicalHumanoid.BoneCount];
            if (CanonicalHumanoid.WriteReference(parents, reference) != AnimationError.None)
            {
                error = "canonical reference unavailable";
                return false;
            }

            var load = SharedClipReader.Load(bytes, family, reference, out var clip);
            if (load != AnimationError.None || clip == null)
            {
                error = "clip rejected: " + load;
                return false;
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var data = ref builder.ConstructRoot<SharedClipData>();
            data.Family = family;
            data.Artifact = ContentIdentity.Hash(bytes);
            data.Duration = clip.Duration;
            data.SampleCount = clip.SampleCount;
            data.BoneCount = clip.BoneCount;
            data.Loop = (byte)(loop ? 1 : 0);
            var stored = builder.Allocate(ref data.Bytes, bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
                stored[i] = bytes[i];
            var blob = builder.CreateBlobAssetReference<SharedClipData>(Allocator.Persistent);
            builder.Dispose();

            manager.CompleteAllTrackedJobs();
            clips = manager.GetBuffer<SharedClipAsset>(entity);
            clipIndex = clips.Length;
            clips.Add(new SharedClipAsset { Value = blob });
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            var contribution = contributions.Length > 0 ? contributions[0] : new SharedContribution { Mask = ushort.MaxValue, Speed = 1f };
            contribution.Clip = clipIndex;
            contribution.Weight = 1f;
            contribution.Speed = 1f;
            contribution.Offset = 0f;
            contribution.Sample = 0.0;
            contributions.Add(contribution);
            SharedFamilyControl.PlaySingle(ref contributions, clipIndex);
            return true;
        }

        public static void Detach(EntityManager manager, Entity entity, int clipIndex)
        {
            if (clipIndex < 0 || !manager.Exists(entity) || !manager.HasBuffer<SharedClipAsset>(entity) || !manager.HasBuffer<SharedContribution>(entity))
                return;
            manager.CompleteAllTrackedJobs();
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            for (var i = contributions.Length - 1; i >= 0; i--)
                if (contributions[i].Clip == clipIndex)
                    contributions.RemoveAt(i);
            var clips = manager.GetBuffer<SharedClipAsset>(entity);
            if (clipIndex != clips.Length - 1)
                return;
            var blob = clips[clipIndex].Value;
            clips.RemoveAt(clipIndex);
            if (blob.IsCreated)
                blob.Dispose();
        }
    }
}

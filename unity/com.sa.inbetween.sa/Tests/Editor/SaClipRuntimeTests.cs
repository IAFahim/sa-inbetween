using NUnit.Framework;
using SA.Animation;
using SA.Inbetween.Clips;
using Unity.Collections;
using Unity.Entities;

namespace SA.Inbetween.Clips.Tests
{
    public class SaClipRuntimeTests
    {
        World world;
        Entity entity;

        [SetUp]
        public void SetUp()
        {
            world = new World("SaClipRuntimeTests");
            var manager = world.EntityManager;
            entity = manager.CreateEntity();
            manager.AddBuffer<SharedClipAsset>(entity);
            manager.AddBuffer<SharedContribution>(entity);
        }

        [TearDown]
        public void TearDown()
        {
            var manager = world.EntityManager;
            var clips = manager.GetBuffer<SharedClipAsset>(entity);
            for (var i = 0; i < clips.Length; i++)
                if (clips[i].Value.IsCreated)
                    clips[i].Value.Dispose();
            world.Dispose();
        }

        [Test]
        public void RejectsEmptyBytes()
        {
            Assert.IsFalse(SaClipRuntime.TryAttach(world.EntityManager, entity, new byte[0], true, out var index, out var error));
            Assert.AreEqual(-1, index);
            StringAssert.Contains("no data", error);
        }

        [Test]
        public void RejectsAnEntityWithoutClipsToTakeTheFamilyFrom()
        {
            Assert.IsFalse(SaClipRuntime.TryAttach(world.EntityManager, entity, new byte[] { 1, 2, 3 }, true, out _, out var error));
            StringAssert.Contains("no clips", error);
        }

        [Test]
        public void RejectsANonCharacterEntity()
        {
            var bare = world.EntityManager.CreateEntity();
            Assert.IsFalse(SaClipRuntime.TryAttach(world.EntityManager, bare, new byte[] { 1 }, true, out _, out var error));
            StringAssert.Contains("not a shared-family character", error);
        }

        [Test]
        public void RejectsGarbageAsAClipWithoutThrowing()
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SharedClipData>();
            root.Family = new AssetId(1, 2, 3, 4);
            var seed = builder.CreateBlobAssetReference<SharedClipData>(Allocator.Persistent);
            builder.Dispose();
            world.EntityManager.GetBuffer<SharedClipAsset>(entity).Add(new SharedClipAsset { Value = seed });
            Assert.IsFalse(SaClipRuntime.TryAttach(world.EntityManager, entity, new byte[] { 9, 9, 9, 9 }, true, out var index, out var error));
            Assert.AreEqual(-1, index);
            StringAssert.Contains("rejected", error);
            Assert.AreEqual(1, world.EntityManager.GetBuffer<SharedClipAsset>(entity).Length);
        }

        static byte[] EncodeIdleClip(out AssetId family)
        {
            ulong a, b, c, d;
            Assert.IsTrue(SA.Editor.SharedFamilyBake.FamilyId(out a, out b, out c, out d));
            family = new AssetId(a, b, c, d);
            var parents = new int[CanonicalHumanoid.BoneCount];
            var reference = new SA.Motion.Numerics.Pose[CanonicalHumanoid.BoneCount];
            Assert.AreEqual(AnimationError.None, CanonicalHumanoid.WriteReference(parents, reference));
            const int frames = 4;
            var samples = new SA.Motion.Numerics.Pose[frames * CanonicalHumanoid.BoneCount];
            for (var f = 0; f < frames; f++)
                for (var i = 0; i < CanonicalHumanoid.BoneCount; i++)
                    samples[f * CanonicalHumanoid.BoneCount + i] = reference[i];
            var root = new SA.Animation.RigidPose[frames];
            for (var f = 0; f < frames; f++)
                root[f] = SA.Animation.RigidPose.Identity;
            var error = SA.Animation.Content.SharedClipEncoder.Encode(family, parents, reference, samples, frames, 0.1, false,
                SA.Animation.Content.RootPolicy.Planar, root, System.ReadOnlySpan<SA.Motion.Numerics.Pose>.Empty, new float[frames * 4],
                new SA.Animation.PoseMarker[0], true, SA.Animation.Content.CompressionLimits.Default, out var bytes, out _);
            Assert.AreEqual(AnimationError.None, error);
            return bytes;
        }

        [Test]
        public void AcceptsAnEncodedClipAndDetachRestoresTheBuffers()
        {
            var bytes = EncodeIdleClip(out _);
            var seed = SA.Editor.SharedFamilyBake.MakeClipBlob(bytes, 0.1, 4, true);
            var manager = world.EntityManager;
            manager.GetBuffer<SharedClipAsset>(entity).Add(new SharedClipAsset { Value = seed });
            Assert.IsTrue(SaClipRuntime.TryAttach(manager, entity, bytes, true, out var index, out var error), error);
            Assert.AreEqual(1, index);
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            Assert.AreEqual(1, contributions.Length);
            Assert.AreEqual(1f, contributions[0].Weight);
            Assert.AreEqual(1, contributions[0].Clip);
            SaClipRuntime.Detach(manager, entity, index);
            Assert.AreEqual(1, manager.GetBuffer<SharedClipAsset>(entity).Length);
            Assert.AreEqual(0, manager.GetBuffer<SharedContribution>(entity).Length);
        }

        [Test]
        public void AClipFromAnotherFamilyIsRejected()
        {
            var bytes = EncodeIdleClip(out _);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SharedClipData>();
            root.Family = new AssetId(7, 7, 7, 7);
            var seed = builder.CreateBlobAssetReference<SharedClipData>(Allocator.Persistent);
            builder.Dispose();
            world.EntityManager.GetBuffer<SharedClipAsset>(entity).Add(new SharedClipAsset { Value = seed });
            Assert.IsFalse(SaClipRuntime.TryAttach(world.EntityManager, entity, bytes, true, out _, out var error));
            StringAssert.Contains("rejected", error);
        }
    }
}

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

        [Test]
        public void DetachingAnEarlierClipFreesItAndKeepsLaterClipsInPlace()
        {
            var bytes = EncodeIdleClip(out _);
            var seed = SA.Editor.SharedFamilyBake.MakeClipBlob(bytes, 0.1, 4, true);
            var manager = world.EntityManager;
            manager.GetBuffer<SharedClipAsset>(entity).Add(new SharedClipAsset { Value = seed });
            Assert.IsTrue(SaClipRuntime.TryAttach(manager, entity, bytes, true, out var first, out var error), error);
            Assert.IsTrue(SaClipRuntime.TryAttach(manager, entity, bytes, true, out var second, out error), error);
            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            var freed = manager.GetBuffer<SharedClipAsset>(entity)[first].Value;
            SaClipRuntime.Detach(manager, entity, first);
            var clips = manager.GetBuffer<SharedClipAsset>(entity);
            Assert.AreEqual(3, clips.Length);
            Assert.IsFalse(clips[first].Value.IsCreated);
            Assert.IsTrue(clips[second].Value.IsCreated);
            Assert.Throws<System.InvalidOperationException>(() => Touch(freed));
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            Assert.AreEqual(1, contributions.Length);
            Assert.AreEqual(second, contributions[0].Clip);
            Assert.IsTrue(SaClipRuntime.TryAttach(manager, entity, bytes, true, out var reused, out error), error);
            Assert.AreEqual(first, reused);
            SaClipRuntime.Detach(manager, entity, second);
            Assert.AreEqual(2, manager.GetBuffer<SharedClipAsset>(entity).Length);
            SaClipRuntime.Detach(manager, entity, reused);
            SaClipRuntime.Detach(manager, entity, reused);
            Assert.AreEqual(1, manager.GetBuffer<SharedClipAsset>(entity).Length);
            Assert.AreEqual(0, manager.GetBuffer<SharedContribution>(entity).Length);
        }

        static double Touch(BlobAssetReference<SharedClipData> blob) => blob.Value.Duration;

        Entity Character(string name)
        {
            var manager = world.EntityManager;
            var character = manager.CreateEntity();
            manager.AddBuffer<SharedClipAsset>(character);
            manager.AddBuffer<SharedContribution>(character);
            var text = new FixedString64Bytes();
            text.CopyFromTruncated(name);
            manager.AddComponentData(character, new SharedRig { NameHash = SA.Names.Hash(text) });
            return character;
        }

        [Test]
        public void OneCharacterIsFoundWithoutAName()
        {
            Assert.IsTrue(SaCharacterPick.TryFind(world.EntityManager, "", out var found, out var status), status);
            Assert.AreEqual(entity, found);
        }

        [Test]
        public void SeveralCharactersAreToldApartByName()
        {
            var hero = Character("Hero");
            var guard = Character("Guard");
            var manager = world.EntityManager;
            Assert.IsFalse(SaCharacterPick.TryFind(manager, "", out var none, out var status));
            Assert.AreEqual(Entity.Null, none);
            StringAssert.Contains("3 SA characters", status);
            StringAssert.Contains("Character", status);
            Assert.IsTrue(SaCharacterPick.TryFind(manager, "Guard", out var found, out status), status);
            Assert.AreEqual(guard, found);
            Assert.IsTrue(SaCharacterPick.TryFind(manager, "Hero", out found, out status), status);
            Assert.AreEqual(hero, found);
            Assert.IsFalse(SaCharacterPick.TryFind(manager, "Nobody", out found, out status));
            Assert.AreEqual(Entity.Null, found);
            StringAssert.Contains("No SA character named 'Nobody'", status);
            Character("Guard");
            Assert.IsFalse(SaCharacterPick.TryFind(manager, "Guard", out found, out status));
            Assert.AreEqual(Entity.Null, found);
            StringAssert.Contains("2 SA characters named 'Guard'", status);
        }

        [Test]
        public void NoCharacterYetSaysItIsWaiting()
        {
            using var empty = new World("SaClipRuntimeTests.Empty");
            Assert.IsFalse(SaCharacterPick.TryFind(empty.EntityManager, "Hero", out _, out var status));
            StringAssert.Contains("Waiting", status);
        }
    }
}

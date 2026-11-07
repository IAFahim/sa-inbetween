using NUnit.Framework;
using SA.Inbetween.Contract;
using SA.Inbetween.Soma;
using SA.Inbetween.Unity;
using UnityEngine;

namespace SA.Inbetween.Unity.Tests
{
    [TestFixture]
    public sealed class ContractInUnityTests
    {
        [Test]
        public void RequestsParse_IdentityPoses_IsOk()
        {
            var pose = IdentityPose();
            var request = new GenerationRequest(
                Blocks.Of(pose, pose, pose, pose),
                Blocks.Of(new TargetSpec(Blocks.Of(pose), 1, 0, 0, 0)),
                GenerationConfigs.Default);
            Assert.IsTrue(Requests.Parse(request).IsOk);
        }

        [Test]
        public void SomaJoints_NameHips_IsHips()
        {
            Assert.AreEqual("Hips", SomaJoints.Name(SomaJoints.Hips));
        }

        [Test]
        public void SomaUnity_ToUnity_MapsComponents()
        {
            var actual = SomaUnity.ToUnity(new SA.Inbetween.Soma.Vector3<UnityWorld>(1f, 2f, 3f));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), actual);
        }

        [Test]
        public void SomaAssembly_ImageRuntimeVersion_IsNotEmpty()
        {
            var version = typeof(SA.Inbetween.Soma.Pose).Assembly.ImageRuntimeVersion;
            Assert.IsFalse(string.IsNullOrEmpty(version));
        }

        private static SA.Inbetween.Soma.Pose IdentityPose()
        {
            var values = new float[Poses.SkeletonFloatCount];
            for (var j = 0; j < SkeletonJoints.Count; j++)
            {
                values[(j * Poses.FloatsPerJoint) + 6] = 1f;
            }

            Poses.Parse(values).TryGetValue(out var pose);
            return pose;
        }
    }
}

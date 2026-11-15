using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using SA.Animation;
using SA.Animation.Content;
using SA.Editor;
using SA.Inbetween.Clips.Editor;
using SA.Motion.Numerics;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Pose = SA.Motion.Numerics.Pose;
using Quaternion = SA.Motion.Numerics.Quaternion;

namespace SA.Inbetween.Clips.Tests
{
    public class RuntimeStartTests
    {
        const string Folder = "Assets/zz_runtime_start";
        const string SyntyCharacter = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        static readonly string[] Clips = { "Assets/InbetweenDemo/Walk.saclip", "Assets/InbetweenDemo/MidAirJump.saclip" };
        static readonly HumanBodyBones[] Checked =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal, HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal,
        };

        static uint Hash(string name)
        {
            var type = typeof(FamilyClips).Assembly.GetType("SA.Editor.RigBuilder");
            return (uint)type.GetMethod("Hash", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { name });
        }

        static RigidPose[] Model(int[] parents, Pose[] locals)
        {
            var model = new RigidPose[locals.Length];
            Assert.AreEqual(AnimationError.None, RigidMath.Model(parents, locals, model));
            return model;
        }

        static Float3 Relative(RigidPose hips, RigidPose bone)
        {
            RigidMath.TryInverse(hips, out var inverse);
            RigidMath.TryCompose(inverse, bone, out var relative);
            return relative.Position;
        }

        static int IndexOf(IReadOnlyList<Transform> bones, Transform bone)
        {
            for (var i = 0; i < bones.Count; i++)
                if (bones[i] == bone)
                    return i;
            return -1;
        }

        static double Compare(FamilyRigPlan plan, int[] parents, SharedClip shared, List<(string Name, int Plan, uint Hash)> checkedBones, int hipsPlan, uint hipsHash, string when, List<string> report)
        {
            var worst = 0.0;
            string error;
            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            using var query = manager.CreateEntityQuery(typeof(SharedContribution), typeof(Rig), typeof(BoneEntity));
            Assert.AreEqual(1, query.CalculateEntityCount());
            var entity = query.GetSingletonEntity();
            var rigReference = manager.GetComponentData<Rig>(entity).Value;
            ref var rig = ref rigReference.Value;
            var bones = manager.GetBuffer<BoneEntity>(entity);
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            var sample = -1.0;
            for (var i = 0; i < contributions.Length; i++)
                if (contributions[i].Weight >= 1f) sample = contributions[i].Sample;
            Assert.GreaterOrEqual(sample, 0.0, "one clip plays at full weight");
            var count = rig.Bones.Length;
            var rigParents = new int[count];
            var locals = new Pose[count];
            var byHash = new Dictionary<uint, int>();
            for (var b = 0; b < count; b++)
            {
                rigParents[b] = rig.Bones[b].Parent;
                byHash[rig.Bones[b].NameHash] = b;
                var boneEntity = bones[b].Value;
                var local = b == 0 || !manager.HasComponent<LocalTransform>(boneEntity) ? LocalTransform.Identity : manager.GetComponentData<LocalTransform>(boneEntity);
                locals[b] = new Pose { Position = new Float3(local.Position.x, local.Position.y, local.Position.z), Rotation = new Quaternion(local.Rotation.value.x, local.Rotation.value.y, local.Rotation.value.z, local.Rotation.value.w), Scale = Float3.One };
            }

            var played = Model(rigParents, locals);
            var canonical = new Pose[SharedFamilyBake.CanonicalBones];
            shared.Sample(Math.Min(sample, shared.Duration), SharedFamilyBake.CanonicalReferencePoses(), canonical);
            Assert.IsTrue(FamilyClips.TryApply(plan, canonical, new[] { SaClipExport.RootAt(shared, Math.Min(sample, shared.Duration)) }, out var expectedLocals, out error), error);
            expectedLocals[0] = Pose.Identity;
            var expected = Model(parents, expectedLocals);
            Assert.IsTrue(byHash.TryGetValue(hipsHash, out var hipsRig), "hips in the rig");
            foreach (var bone in checkedBones)
            {
                Assert.IsTrue(byHash.TryGetValue(bone.Hash, out var rigIndex), bone.Name + " in the rig");
                var a = Relative(played[hipsRig], played[rigIndex]);
                var b = Relative(expected[hipsPlan], expected[bone.Plan]);
                var d = RealJumpTests.Length(a - b);
                worst = Math.Max(worst, d);
                if (d > 0.02) report.Add(when + ": " + bone.Name + " is " + (d * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + " cm from where the clip puts it (sample " + sample.ToString("0.000", CultureInfo.InvariantCulture) + " s)");
            }

            return worst;
        }

        [UnityTest, Category("RealAssets")]
        public IEnumerator A_baked_clip_starts_with_head_hands_and_feet_where_the_clip_puts_them()
        {
            if (!Application.isBatchMode) Assert.Ignore("replaces the open scene and enters Play mode; run it in batch mode");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var clip = Clips.Select(AssetDatabase.LoadAssetAtPath<SaClipAsset>).FirstOrDefault(c => c != null);
            if (model == null || clip == null) Assert.Ignore("Synty character or a test .saclip missing");
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "zz_runtime_start");
            var copyPath = Folder + "/" + clip.name + ".saclip";
            System.IO.File.WriteAllBytes(copyPath, clip.Bytes);
            AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
            clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(copyPath);
            Assert.IsNotNull(clip, "the copied clip imports");
            var report = new List<string>();
            var instance = UnityEngine.Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = model.name;
            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, Folder + "/Runtime Start.unity"));
                model = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
                clip = AssetDatabase.LoadAssetAtPath<SaClipAsset>(copyPath);
                Assert.IsTrue(SaCharacterSetup.TryCreate(model, clip, scene, Vector3.zero, out _, out var error), error);
                Assert.IsTrue(FamilyClips.TryPlan(instance, out var plan, out error), error);
                Assert.IsTrue(SaClipPose.TryLoad(clip.Bytes, out _, out error), error);
                var shared = RealJumpTests.Load(clip.Bytes);
                var animator = instance.GetComponentInChildren<Animator>(true);
                var parents = new int[plan.BoneCount];
                for (var b = 0; b < plan.BoneCount; b++) parents[b] = plan.Parents[b];
                var checkedBones = new List<(string Name, int Plan, uint Hash)>();
                foreach (var human in Checked)
                {
                    var t = animator.GetBoneTransform(human);
                    if (t == null) continue;
                    var index = IndexOf(plan.Bones, t);
                    if (index > 0) checkedBones.Add((human.ToString(), index, Hash(t.name)));
                }

                var hipsPlan = IndexOf(plan.Bones, animator.GetBoneTransform(HumanBodyBones.Hips));
                var hipsHash = Hash(plan.Bones[hipsPlan].name);
                Assert.Greater(checkedBones.Count, 8, "the Synty character maps head, hands, feet and fingers");

                yield return new EnterPlayMode();
                SaClipPlayer player = null;
                var begun = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - begun < 90f)
                {
                    player = UnityEngine.Object.FindAnyObjectByType<SaClipPlayer>();
                    if (player != null && player.IsPlaying) break;
                    yield return null;
                }

                Assert.IsNotNull(player);
                Assert.IsTrue(player.IsPlaying, player.Status);
                var trace = new List<string>();
                var traced = 0.0;
                for (var frame = 0; frame < 12; frame++)
                {
                    var lines = new List<string>();
                    var e = Compare(plan, parents, shared, checkedBones, hipsPlan, hipsHash, "frame " + frame, lines);
                    traced = Math.Max(traced, e);
                    report.AddRange(lines);
                    trace.Add("frame " + frame + " dt " + Time.deltaTime.ToString("0.000", CultureInfo.InvariantCulture) + " s: " + (e * 100).ToString("0.000", CultureInfo.InvariantCulture) + " cm (" + SharedFamilySystem.LastAction + ")");
                    yield return null;
                }

                TestContext.WriteLine(string.Join("\n", trace));
                var worst = new double[2];
                for (var look = 0; look < 2; look++)
                {
                    yield return null;
                    if (look == 1)
                    {
                        var wait = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - wait < 0.5f) yield return null;
                    }

                    worst[look] = Compare(plan, parents, shared, checkedBones, hipsPlan, hipsHash, look == 0 ? "first frame" : "0.5 s later", report);
                }

                TestContext.WriteLine("head, neck, hands, feet, toes and finger tips vs the clip, relative to the hips: first 12 frames " + (traced * 100.0).ToString("0.000", CultureInfo.InvariantCulture) + " cm, then " + (worst[0] * 100.0).ToString("0.000", CultureInfo.InvariantCulture) + " cm, 0.5 s later " + (worst[1] * 100.0).ToString("0.000", CultureInfo.InvariantCulture) + " cm");
                yield return new ExitPlayMode();
                Assert.That(report, Is.Empty, string.Join("\n", report));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Folder);
            }
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SA.Animation;
using SA.Editor;
using SA.Inbetween.Clips.Editor;
using UnityEditor;
using UnityEngine;

namespace SA.Inbetween.Clips.Tests
{
    public class RealExportTests
    {
        const float Hz = 30f;
        const string SyntyCharacter = "Assets/Synty/AnimationBaseLocomotion/Meshes/PolygonSyntyCharacter.fbx";
        const string Result = "Assets/InbetweenDemo/MidAirJump.saclip";

        [Test, Category("RealAssets")]
        public void HumanoidClipMatchesTheGenericClip()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var source = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Result);
            if (character == null || source == null) Assert.Ignore("Synty character or MidAirJump result missing");
            Assert.IsTrue(SaClipExport.TryBuild(source, character, out var generic, out var error), error);
            Assert.IsTrue(SaClipHumanoidExport.TryBuild(source, character, out var humanoid, out error), error);
            Assert.IsTrue(humanoid.isHumanMotion, "the Humanoid clip has no muscle curves");
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var report = new StringBuilder();
            try
            {
                Assert.IsTrue(FamilyClips.TryPlan(instance, out var plan, out error), error);
                var animator = instance.GetComponentInChildren<Animator>(true);
                var avatar = animator.avatar;
                var frames = Mathf.RoundToInt(generic.length * Hz) + 1;
                var worst = new double[plan.BoneCount];
                var swing = new double[plan.BoneCount];
                double metres = 0;
                for (var f = 0; f < frames; f++)
                {
                    animator.avatar = null;
                    var a = RealJumpTests.TargetModel(plan, RealJumpTests.Direct(plan, instance, generic, f / Hz, true), 0);
                    animator.avatar = avatar;
                    var b = RealJumpTests.TargetModel(plan, RealJumpTests.Direct(plan, instance, humanoid, f / Hz, true), 0);
                    for (var bone = 1; bone < plan.BoneCount; bone++)
                        if (RealJumpTests.Body(plan.CanonicalOfBone[bone]))
                        {
                            worst[bone] = Math.Max(worst[bone], RealJumpTests.Degrees(a[bone].Rotation, b[bone].Rotation));
                            var child = Child(plan.CanonicalOfBone[bone]);
                            var end = child < 0 ? -1 : plan.BoneOfCanonical[child];
                            if (end < 0) continue;
                            var da = a[end].Position - a[bone].Position;
                            var db = b[end].Position - b[bone].Position;
                            var cos = ((double)da.X * db.X + (double)da.Y * db.Y + (double)da.Z * db.Z) / (RealJumpTests.Length(da) * RealJumpTests.Length(db));
                            swing[bone] = Math.Max(swing[bone], Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI);
                        }
                    foreach (var role in new[] { 13, 32, 50, 54 })
                    {
                        var bone = plan.BoneOfCanonical[role];
                        metres = Math.Max(metres, RealJumpTests.Length(a[bone].Position - b[bone].Position));
                    }
                }

                double max = 0, maxSwing = 0;
                var names = SharedFamilyBake.CanonicalNames;
                report.Append("E2 Humanoid vs Generic, ").Append(frames).Append(" frames, per bone: full world rotation / bone direction (deg):\n");
                for (var bone = 1; bone < plan.BoneCount; bone++)
                    if (RealJumpTests.Body(plan.CanonicalOfBone[bone]))
                    {
                        report.Append("  ").Append(names[plan.CanonicalOfBone[bone]]).Append(' ').Append(worst[bone].ToString("0.000", CultureInfo.InvariantCulture))
                            .Append(" / ").Append(swing[bone].ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
                        max = Math.Max(max, worst[bone]);
                        maxSwing = Math.Max(maxSwing, swing[bone]);
                    }

                report.Append("max full rotation ").Append(max.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg (twist redistributed by the avatar), max bone direction ")
                    .Append(maxSwing.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg, hand/foot position ")
                    .Append((metres * 100.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" cm\n");
                animator.avatar = avatar;
                humanoid.SampleAnimation(instance, generic.length * 0.5f);
                var png = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa", "j1", "e2_humanoid.png");
                Directory.CreateDirectory(Path.GetDirectoryName(png));
                File.WriteAllBytes(png, Render(instance, 480, 360).EncodeToPNG());
                report.Append("rendered ").Append(png).Append('\n');
                Assert.IsFalse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, "export dirtied the scene");
                Assert.LessOrEqual(maxSwing, 3.0, report.ToString());
                Assert.LessOrEqual(metres, 0.01, report.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(generic);
                UnityEngine.Object.DestroyImmediate(humanoid);
                RealJumpTests.Report("e2", report.ToString());
            }
        }

        static int Child(int canonical)
        {
            switch (canonical)
            {
                case 1: return 2;
                case 2: return 3;
                case 3: return 4;
                case 4: return 5;
                case 5: return 6;
                case 10: return 11;
                case 11: return 12;
                case 12: return 13;
                case 29: return 30;
                case 30: return 31;
                case 31: return 32;
                case 48: return 49;
                case 49: return 50;
                case 50: return 51;
                case 52: return 53;
                case 53: return 54;
                case 54: return 55;
                default: return -1;
            }
        }

        internal static Texture2D Render(GameObject posed, int width, int height)
        {
            var utility = new PreviewRenderUtility();
            var meshes = new System.Collections.Generic.List<Mesh>();
            try
            {
                var hips = posed.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.Hips).position;
                var focus = new Vector3(hips.x, Mathf.Max(0.9f, hips.y), hips.z);
                utility.camera.fieldOfView = 30f;
                utility.camera.nearClipPlane = 0.05f;
                utility.camera.farClipPlane = 100f;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
                var rotation = Quaternion.Euler(10f, 150f, 0f);
                utility.camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 4.8f, rotation);
                utility.lights[0].intensity = 1.2f;
                utility.lights[0].transform.rotation = Quaternion.Euler(40f, 120f, 0f);
                utility.ambientColor = new Color(0.35f, 0.35f, 0.38f, 1f);
                utility.BeginStaticPreview(new Rect(0, 0, width, height));
                foreach (var skin in posed.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    meshes.Add(mesh);
                    skin.BakeMesh(mesh, true);
                    var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                    var materials = skin.sharedMaterials;
                    for (var m = 0; m < mesh.subMeshCount && m < materials.Length; m++)
                        utility.DrawMesh(mesh, matrix, materials[m], m);
                }

                utility.Render(true);
                return utility.EndStaticPreview();
            }
            finally
            {
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                utility.Cleanup();
            }
        }
    }
}

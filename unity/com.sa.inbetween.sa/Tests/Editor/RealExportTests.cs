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
            Assert.IsFalse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, "the scene was dirty before the test");
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

        [Test, Category("RealAssets")]
        public void Humanoid_clip_keeps_the_jump_height_when_the_animator_has_root_motion_off()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var source = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Result);
            if (character == null || source == null) Assert.Ignore("Synty character or MidAirJump result missing");
            Assert.IsTrue(SaClipHumanoidExport.TryBuild(source, character, out var humanoid, out var error), error);
            var truth = UnityEngine.Object.Instantiate(character);
            truth.hideFlags = HideFlags.HideAndDontSave;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var played = UnityEngine.Object.Instantiate(character);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(played, scene);
            var graph = UnityEngine.Playables.PlayableGraph.Create("humanoid height");
            try
            {
                Assert.IsTrue(FamilyClips.TryPlan(truth, out var plan, out error), error);
                Assert.IsTrue(SaClipExport.TrySampleLocals(plan, source.Bytes, out var locals, out var frames, out error), error);
                var animator = played.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "humanoid", animator);
                var clipPlayable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, humanoid);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, clipPlayable);
                var playedHips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var report = new StringBuilder();
                var heights = new float[2];
                var frameOf = new[] { 0, frames - 1 };
                for (var i = 0; i < 2; i++)
                {
                    SaClipExport.ApplyFrame(plan, locals, frameOf[i]);
                    heights[i] = plan.Bones[plan.Hips].position.y;
                    UnityEngine.Playables.PlayableExtensions.SetTime(clipPlayable, frameOf[i] / Hz);
                    graph.Evaluate(0f);
                    var playedHeight = playedHips.position.y;
                    report.Append("frame ").Append(frameOf[i]).Append(": result hips ").Append(heights[i].ToString("0.000", CultureInfo.InvariantCulture))
                        .Append(" m, Humanoid clip through an Animator with root motion off ").Append(playedHeight.ToString("0.000", CultureInfo.InvariantCulture)).Append(" m\n");
                    Assert.AreEqual(heights[i], playedHeight, 0.05f, report.ToString());
                }

                Assert.Greater(heights[0] - heights[1], 0.2f, "the source clip should start mid-air and land: " + report);
            }
            finally
            {
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(played);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(truth);
                UnityEngine.Object.DestroyImmediate(humanoid);
            }
        }

        [Test, Category("RealAssets")]
        public void FbxReimportMatchesTheClips()
        {
            if (!SaClipFbxExport.Available) Assert.Ignore(SaClipFbxExport.MissingPackage);
            Assert.IsFalse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, "the scene was dirty before the test");
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(SyntyCharacter);
            var source = AssetDatabase.LoadAssetAtPath<SaClipAsset>(Result);
            if (character == null || source == null) Assert.Ignore("Synty character or MidAirJump result missing");
            const string folder = "Assets/zz_w8_test";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "zz_w8_test");
            var path = folder + "/E3.fbx";
            var report = new StringBuilder();
            var instance = UnityEngine.Object.Instantiate(character);
            instance.hideFlags = HideFlags.HideAndDontSave;
            AnimationClip generic = null, humanoid = null;
            try
            {
                var exported = SaClipFbxExport.TryExport(source, character, path, out var error);
                Assert.IsFalse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, "FBX export dirtied the scene");
                if (!exported && error.StartsWith(SaClipFbxExport.NativeProblem))
                {
                    report.Append(error).Append('\n');
                    Assert.Ignore(error);
                }
                Assert.IsTrue(exported, error);
                Assert.IsTrue(SaClipExport.TryBuild(source, character, out generic, out error), error);
                Assert.IsTrue(SaClipHumanoidExport.TryBuild(source, character, out humanoid, out error), error);
                Assert.IsTrue(FamilyClips.TryPlan(instance, out var plan, out error), error);
                var animator = instance.GetComponentInChildren<Animator>(true);
                var avatar = animator.avatar;
                var frames = Mathf.RoundToInt(generic.length * Hz) + 1;
                report.Append("FBX ").Append(path).Append(", ").Append(new FileInfo(path).Length / 1024).Append(" KiB\n");

                var fbxGeneric = FbxClip(path);
                Assert.IsNotNull(fbxGeneric, "no take in the FBX");
                report.Append("generic take '").Append(fbxGeneric.name).Append("' ").Append(fbxGeneric.length.ToString("0.000", CultureInfo.InvariantCulture)).Append(" s\n");
                Measure(plan, instance, animator, null, generic, null, fbxGeneric, frames, out var genericDegrees, out var genericSwing, out var genericMetres);
                report.Append("E3 FBX as Generic vs Generic .anim: rotation ").Append(genericDegrees.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg, hand/foot ")
                    .Append((genericMetres * 1000.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" mm\n");

                AnimationClip fbxHuman = null;
                var humanDegrees = 0.0;
                var crossSwing = 0.0;
                if (fbxHuman == null)
                    report.Append("E3 Humanoid reimport of the exported FBX is not attempted (the exporter adds a root node above the character, so a copied Humanoid avatar reports a hierarchy mismatch); Humanoid retargeting uses the Humanoid .anim export\n");
                else
                {
                    Assert.IsTrue(fbxHuman.isHumanMotion, "the reimported take is not Humanoid");
                    Measure(plan, instance, animator, avatar, humanoid, avatar, fbxHuman, frames, out humanDegrees, out var humanSwing, out var humanMetres);
                    report.Append("E3 FBX as Humanoid (copied avatar) vs Humanoid .anim: rotation ").Append(humanDegrees.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg, bone direction ")
                        .Append(humanSwing.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg, hand/foot ").Append((humanMetres * 1000.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" mm\n");
                    Measure(plan, instance, animator, null, generic, avatar, fbxHuman, frames, out var crossDegrees, out crossSwing, out var crossMetres);
                    report.Append("E3 FBX as Humanoid vs Generic .anim: rotation ").Append(crossDegrees.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg (twist), bone direction ")
                        .Append(crossSwing.ToString("0.000", CultureInfo.InvariantCulture)).Append(" deg, hand/foot ").Append((crossMetres * 1000.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(" mm\n");
                }

                Assert.IsFalse(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty, "export dirtied the scene");
                Assert.LessOrEqual(genericDegrees, 1.5, report.ToString());
                Assert.LessOrEqual(humanDegrees, 0.5, report.ToString());
                Assert.LessOrEqual(crossSwing, 0.5, report.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                if (generic != null) UnityEngine.Object.DestroyImmediate(generic);
                if (humanoid != null) UnityEngine.Object.DestroyImmediate(humanoid);
                RealJumpTests.Report("e3", report.ToString());
                AssetDatabase.DeleteAsset(folder);
            }
        }

        static AnimationClip FbxClip(string path)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            return null;
        }

        static void Measure(FamilyRigPlan plan, GameObject instance, Animator animator, Avatar avatarA, AnimationClip a, Avatar avatarB, AnimationClip b, int frames,
            out double degrees, out double swing, out double metres)
        {
            degrees = swing = metres = 0;
            for (var f = 0; f < frames; f++)
            {
                var t = Mathf.Min(f / Hz, Mathf.Min(a.length, b.length));
                animator.avatar = avatarA;
                var ma = RealJumpTests.TargetModel(plan, RealJumpTests.Direct(plan, instance, a, t, true), 0);
                animator.avatar = avatarB;
                var mb = RealJumpTests.TargetModel(plan, RealJumpTests.Direct(plan, instance, b, t, true), 0);
                for (var bone = 1; bone < plan.BoneCount; bone++)
                {
                    if (!RealJumpTests.Body(plan.CanonicalOfBone[bone])) continue;
                    degrees = Math.Max(degrees, RealJumpTests.Degrees(ma[bone].Rotation, mb[bone].Rotation));
                    var child = Child(plan.CanonicalOfBone[bone]);
                    var end = child < 0 ? -1 : plan.BoneOfCanonical[child];
                    if (end < 0) continue;
                    var da = ma[end].Position - ma[bone].Position;
                    var db = mb[end].Position - mb[bone].Position;
                    var cos = ((double)da.X * db.X + (double)da.Y * db.Y + (double)da.Z * db.Z) / (RealJumpTests.Length(da) * RealJumpTests.Length(db));
                    swing = Math.Max(swing, Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI);
                }

                foreach (var role in new[] { 13, 32, 50, 54 })
                {
                    var bone = plan.BoneOfCanonical[role];
                    metres = Math.Max(metres, RealJumpTests.Length(ma[bone].Position - mb[bone].Position));
                }
            }

            animator.avatar = avatarB ?? avatarA;
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

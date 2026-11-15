using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SA.Inbetween.Install.Tests
{
    public sealed class InstallTests
    {
        static string Scratch => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "sa-install-tests");

        [SetUp]
        public void Clean()
        {
            if (Directory.Exists(Scratch)) Directory.Delete(Scratch, true);
            Directory.CreateDirectory(Scratch);
        }

        [TearDown]
        public void Remove()
        {
            if (Directory.Exists(Scratch)) Directory.Delete(Scratch, true);
        }

        [Test]
        public void The_other_packages_come_from_the_same_repository_and_revision_as_the_installer()
        {
            const string own = "https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.install#v0.10.0";
            Assert.That(InstallPlan.SiblingSource("com.sa.inbetween.install", own, "com.sa.inbetween.sa"), Is.EqualTo("https://github.com/IAFahim/sa-inbetween.git?path=unity/com.sa.inbetween.sa#v0.10.0"));
            Assert.That(InstallPlan.SiblingSource("com.sa.inbetween.install", "https://github.com/IAFahim/sa-inbetween.git?path=/unity/com.sa.inbetween.install", "com.sa.inbetween.sa.timeline"),
                Is.EqualTo("https://github.com/IAFahim/sa-inbetween.git?path=/unity/com.sa.inbetween.sa.timeline"));
            Assert.That(InstallPlan.SiblingSource("com.sa.inbetween.install", "file:/home/me/sa-inbetween/unity/com.sa.inbetween.install", "com.sa.inbetween.sa"), Is.EqualTo("file:/home/me/sa-inbetween/unity/com.sa.inbetween.sa"));
            var needs = InstallPlan.Needs("com.sa.inbetween.install", own);
            Assert.That(needs.Select(n => n.Name), Is.EqualTo(new[] { "com.sa.motion.unity", "com.sa.unity", "com.sa.inbetween.sa", "com.sa.inbetween.sa.timeline", "com.unity.formats.fbx" }));
            Assert.That(needs.Last().Request, Is.EqualTo("com.unity.formats.fbx@5.1.6"));
            Assert.That(needs[0].Request, Does.StartWith("https://github.com/IAFahim/sa-motion.git?path=packages/com.sa.motion.unity#"));
        }

        [Test]
        public void The_tools_archive_and_its_hash_are_named_like_the_release()
        {
            Assert.That(InstallPlan.ToolsArchive("0.10.0", "linux-x64"), Is.EqualTo("SaInbetween-tools-v0.10.0-linux-x64.tar.gz"));
            Assert.That(InstallPlan.ToolsArchive("0.10.0", "win-x64"), Is.EqualTo("SaInbetween-tools-v0.10.0-win-x64.zip"));
            Assert.That(InstallPlan.ToolsArchive("0.10.0", ""), Is.EqualTo(""));
            Assert.That(InstallPlan.ReleaseBase("0.10.0", null), Is.EqualTo("https://github.com/IAFahim/sa-inbetween/releases/download/v0.10.0/"));
            Assert.That(InstallPlan.ReleaseBase("0.10.0", "file:///tmp/dist"), Is.EqualTo("file:///tmp/dist/"));
            const string sums = "aaaa  sa-inbetween-v0.10.0-linux-x64.tar.gz\nBBBB *SaInbetween-tools-v0.10.0-linux-x64.tar.gz\n";
            Assert.That(InstallPlan.HashFor(sums, "SaInbetween-tools-v0.10.0-linux-x64.tar.gz"), Is.EqualTo("bbbb"));
            Assert.That(InstallPlan.HashFor(sums, "missing.zip"), Is.EqualTo(""));
        }

        [Test]
        public void Nothing_from_NVIDIA_is_pinned_to_a_mirror()
        {
            Assert.That(InstallPlan.Model.Url, Does.StartWith("https://media.githubusercontent.com/media/NVlabs/motionbricks/"));
            Assert.That(InstallPlan.RestSource.Url, Does.StartWith("https://media.githubusercontent.com/media/NVlabs/motionbricks/"));
            Assert.That(InstallPlan.Model.Sha256, Is.EqualTo("856419dc72aeb14e591eebf4dd0ea05734a2506ddb00bcd39dd6dac2ad60f4fb"));
            Assert.That(InstallPlan.Model.Bytes, Is.EqualTo(757864273));
        }

        static void Tar(Stream output, string name, byte[] data, int mode)
        {
            var header = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
            Encoding.ASCII.GetBytes(Convert.ToString(mode, 8).PadLeft(7, '0')).CopyTo(header, 100);
            Encoding.ASCII.GetBytes(Convert.ToString(data.Length, 8).PadLeft(11, '0')).CopyTo(header, 124);
            header[156] = (byte)'0';
            output.Write(header, 0, header.Length);
            output.Write(data, 0, data.Length);
            var pad = (512 - data.Length % 512) % 512;
            output.Write(new byte[pad], 0, pad);
        }

        [Test]
        public void A_tar_gz_unpacks_into_its_folder_only()
        {
            var archive = Path.Combine(Scratch, "tools.tar.gz");
            using (var file = File.Create(archive))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                Tar(gzip, "SaInbetween/cli/sa-inbetween", Encoding.UTF8.GetBytes("#!/bin/sh\necho hi\n"), 493);
                Tar(gzip, "SaInbetween/humanoid-v1.safamily", Encoding.UTF8.GetBytes("{}"), 420);
                gzip.Write(new byte[1024], 0, 1024);
            }

            var target = Path.Combine(Scratch, "out");
            var files = ToolArchive.Extract(archive, target);
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(File.ReadAllText(Path.Combine(target, "SaInbetween", "humanoid-v1.safamily")), Is.EqualTo("{}"));

            var evil = Path.Combine(Scratch, "evil.tar.gz");
            using (var file = File.Create(evil))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                Tar(gzip, "../outside.txt", new byte[] { 1 }, 420);
                gzip.Write(new byte[1024], 0, 1024);
            }

            Assert.Throws<IOException>(() => ToolArchive.Extract(evil, Path.Combine(Scratch, "evil")));
            Assert.That(File.Exists(Path.Combine(Scratch, "outside.txt")), Is.False);
        }

        static string Bvh(int frames)
        {
            var sb = new StringBuilder("HIERARCHY\nROOT Root\n{\nOFFSET 0 0 0\nCHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation\n");
            sb.Append("JOINT Hips\n{\nOFFSET 1 90 2\nCHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation\n");
            for (var j = 2; j <= SomaRestPose.Joints; j++) sb.Append("JOINT J").Append(j).Append("\n{\nOFFSET ").Append(j).Append(" 0.5 0\nCHANNELS 3 Zrotation Yrotation Xrotation\n");
            sb.Append("End Site\n{\nOFFSET 0 1 0\n}\n");
            for (var j = 1; j <= SomaRestPose.Joints; j++) sb.Append("}\n");
            sb.Append("}\nMOTION\nFrames: ").Append(frames).Append("\nFrame Time: 0.008333\n");
            for (var f = 0; f < frames; f++)
            {
                sb.Append("0 0 0 0 0 0 1.5 91 2.5 90 0 0");
                for (var j = 2; j <= SomaRestPose.Joints; j++) sb.Append(" 0 0 ").Append((f * 10).ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            return sb.ToString();
        }

        [Test]
        public void The_rest_pose_is_frame_zero_of_the_sample_in_metres_and_quaternions()
        {
            Assert.That(SomaRestPose.TryConvert(Bvh(2), 0, out var pose, out var error), Is.True, error);
            Assert.That(pose.Length, Is.EqualTo(2156));
            var floats = new float[pose.Length / 4];
            Buffer.BlockCopy(pose, 0, floats, 0, pose.Length);
            Assert.That(floats[0], Is.EqualTo(0.015f).Within(1e-6f), "hips position comes from its channels");
            Assert.That(floats[1], Is.EqualTo(0.91f).Within(1e-6f));
            Assert.That(floats[5], Is.EqualTo(Mathf.Sin(Mathf.PI / 4f)).Within(1e-6f), "90 degrees about Z");
            Assert.That(floats[6], Is.EqualTo(Mathf.Cos(Mathf.PI / 4f)).Within(1e-6f));
            Assert.That(floats[7], Is.EqualTo(0.02f).Within(1e-6f), "other joints keep their offset");
            Assert.That(floats[13], Is.EqualTo(1f).Within(1e-6f), "frame 0 has no rotation");
            Assert.That(SomaRestPose.TryConvert(Bvh(2), 1, out var second, out error), Is.True, error);
            Assert.That(second, Is.Not.EqualTo(pose));
            Assert.That(SomaRestPose.TryConvert("HIERARCHY\nROOT Root\n{\n}\nMOTION\n", 0, out _, out error), Is.False);
            Assert.That(error, Does.Contain("SOMA"));
        }

        [Test]
        public void The_real_rest_pose_matches_the_one_the_tools_used_so_far()
        {
            var bvh = Environment.GetEnvironmentVariable("SA_INBETWEEN_REST_BVH");
            var known = InbetweenSetup.RestPath;
            if (string.IsNullOrEmpty(bvh) || !File.Exists(bvh) || !File.Exists(known)) Assert.Ignore("set SA_INBETWEEN_REST_BVH to NVIDIA's " + InstallPlan.RestSourceFile + " and keep SaInbetween/" + InstallPlan.RestFile + " to compare");
            Assert.That(ToolArchive.Sha256(bvh), Is.EqualTo(InstallPlan.RestSource.Sha256));
            Assert.That(SomaRestPose.TryConvert(File.ReadAllText(bvh), 0, out var pose, out var error), Is.True, error);
            var a = new float[539];
            var b = new float[539];
            Buffer.BlockCopy(pose, 0, a, 0, pose.Length);
            Buffer.BlockCopy(File.ReadAllBytes(known), 0, b, 0, 2156);
            var worst = 0.0;
            for (var j = 0; j < 77; j++)
            {
                var sign = a[j * 7 + 3] * b[j * 7 + 3] + a[j * 7 + 4] * b[j * 7 + 4] + a[j * 7 + 5] * b[j * 7 + 5] + a[j * 7 + 6] * b[j * 7 + 6] < 0 ? -1f : 1f;
                for (var k = 0; k < 7; k++) worst = Math.Max(worst, Math.Abs((k >= 3 ? sign : 1f) * a[j * 7 + k] - b[j * 7 + k]));
            }

            Assert.That(worst, Is.LessThan(1e-6), "largest difference " + worst);
        }

        [UnityTest]
        public IEnumerator A_download_is_kept_only_when_its_size_and_SHA_256_match()
        {
            var source = Path.Combine(Scratch, "source.bin");
            var bytes = Encoding.UTF8.GetBytes("motion bricks are not shipped");
            File.WriteAllBytes(source, bytes);
            var good = new Download(new Uri(source).AbsoluteUri, Path.Combine(Scratch, "good.bin"), ToolArchive.Sha256(bytes), bytes.Length);
            good.Start();
            var begun = EditorApplication.timeSinceStartup;
            while (!good.Poll() && EditorApplication.timeSinceStartup - begun < 30) yield return null;
            Assert.That(good.Ok, Is.True, good.Error);
            Assert.That(File.ReadAllBytes(good.File), Is.EqualTo(bytes));
            var bad = new Download(new Uri(source).AbsoluteUri, Path.Combine(Scratch, "bad.bin"), new string('0', 64));
            bad.Start();
            begun = EditorApplication.timeSinceStartup;
            while (!bad.Poll() && EditorApplication.timeSinceStartup - begun < 30) yield return null;
            Assert.That(bad.Ok, Is.False);
            Assert.That(bad.Error, Does.Contain("SHA-256"));
            Assert.That(File.Exists(bad.File) || File.Exists(bad.File + ".partial"), Is.False, "a damaged download leaves nothing behind");
        }

        [Test]
        public void The_setup_lists_every_step_with_what_it_does()
        {
            var steps = InbetweenSetup.Check();
            var names = steps.Select(s => s.Name).ToList();
            Assert.That(names, Does.Contain("Packages"));
            Assert.That(names, Does.Contain("Tools"));
            Assert.That(names, Does.Contain("Motion model (NVIDIA)"));
            Assert.That(names, Does.Contain("Rest pose (NVIDIA sample)"));
            Assert.That(names, Does.Contain("Render pipeline"));
            Assert.That(steps.All(s => s.Detail.Length > 20), Is.True);
            if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline == null && UnityEngine.QualitySettings.GetRenderPipelineAssetAt(UnityEngine.QualitySettings.GetQualityLevel()) != null)
                Assert.That(steps.Single(s => s.Name == "Render pipeline").State, Is.EqualTo(SetupState.Done), "a URP asset on the quality levels counts; the template sets none in Graphics");
            var model = steps.Single(s => s.Name == "Motion model (NVIDIA)");
            if (!InbetweenSetup.HasModel() && !InbetweenSetup.NvidiaAccepted) Assert.That(model.State, Is.EqualTo(SetupState.NeedsConsent), "the model is never downloaded without an answer");
        }
    }
}

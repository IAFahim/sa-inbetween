using NUnit.Framework;
using SA.Inbetween.Clips.Editor;

namespace SA.Inbetween.Clips.Tests
{
    public class StudioCommandsTests
    {
        static StudioSettings Valid() => new StudioSettings
        {
            CliPath = "/t/sa-inbetween",
            BakePath = "/t/bake.dll",
            ModelPath = "/m/model.onnx",
            FamilyPath = "/f/humanoid-v1.safamily",
            OutputFolder = "Assets/Generated",
            Contexts = new System.Collections.Generic.List<string> { "/c/a0.pose", "/c/a1.pose", "/c/a2.pose", "/c/a3.pose" },
            Targets = new System.Collections.Generic.List<string> { "/p/a.pose", " ", "/p/b.pose,/p/c.pose" },
            ClipName = "walk",
        };

        [Test]
        public void Generate_builds_expected_arguments()
        {
            Assert.IsTrue(StudioCommands.TryGenerate(Valid(), out var c, out _));
            Assert.AreEqual("/t/sa-inbetween", c.File);
            Assert.AreEqual("generate", c.Args[0]);
            CollectionAssert.AreEqual(new[] { "--target", "/p/a.pose", "--target", "/p/b.pose,/p/c.pose" }, c.Args.GetRange(c.Args.IndexOf("--target"), 4));
            Assert.AreEqual("Assets/Generated/walk.saib", c.Args[c.Args.IndexOf("--out") + 1].Replace('\\', '/'));
            Assert.IsFalse(c.Args.Contains("--no-correct"));
            Assert.IsFalse(c.Args.Contains("--context-end"));
        }

        [Test]
        public void Generate_flags_and_context_end()
        {
            var s = Valid();
            s.Correct = false;
            s.VerifyModel = false;
            s.ContextEnd = 12;
            Assert.IsTrue(StudioCommands.TryGenerate(s, out var c, out _));
            Assert.IsTrue(c.Args.Contains("--no-correct"));
            Assert.IsTrue(c.Args.Contains("--skip-verify"));
            Assert.AreEqual("12", c.Args[c.Args.IndexOf("--context-end") + 1]);
        }

        [Test]
        public void Generate_rejects_bad_input()
        {
            var s = Valid(); s.ModelPath = ""; Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out var e1)); StringAssert.Contains("model", e1);
            s = Valid(); s.Targets = new System.Collections.Generic.List<string> { "" }; Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out var e2)); StringAssert.Contains("target", e2);
            s = Valid(); s.TokensMin = 4; Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out _));
            s = Valid(); s.TokensMin = 12; s.TokensMax = 8; Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out _));
            s = Valid(); s.ClipName = "a/b"; Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out _));
        }

        [Test]
        public void Bake_runs_dll_through_dotnet()
        {
            Assert.IsTrue(StudioCommands.TryBake(Valid(), out var c, out _));
            Assert.AreEqual("dotnet", c.File);
            Assert.AreEqual("/t/bake.dll", c.Args[0]);
            Assert.AreEqual(3, c.Args.Count - 1);
            StringAssert.EndsWith("walk.saclip", c.Args[3].Replace('\\', '/'));
        }

        [Test]
        public void Bake_needs_family()
        {
            var s = Valid(); s.FamilyPath = " ";
            Assert.IsFalse(StudioCommands.TryBake(s, out _, out var e));
            StringAssert.Contains("family", e);
        }

        [Test]
        public void Process_reports_missing_executable()
        {
            var p = new StudioProcess();
            Assert.IsFalse(p.Start(new StudioCommand { File = "/no/such/tool", Args = new System.Collections.Generic.List<string>() }, out var e));
            StringAssert.Contains("Could not start", e);
            Assert.IsFalse(p.Running);
        }

        [Test]
        public void Generate_passes_every_context_pose_and_needs_one()
        {
            Assert.IsTrue(StudioCommands.TryGenerate(Valid(), out var c, out _));
            var count = 0;
            foreach (var a in c.Args) if (a == "--context") count++;
            Assert.AreEqual(4, count);
            var s = Valid(); s.Contexts = new System.Collections.Generic.List<string>();
            Assert.IsFalse(StudioCommands.TryGenerate(s, out _, out var e));
            StringAssert.Contains("start", e);
        }

        [Test]
        public void Discovery_finds_tools_in_nested_folders()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa_studio_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "cli"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "model"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "cli", "sa-inbetween"), "x");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "cli", "sa-soma-bake.exe"), "x");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "model", "mmm_v6_soma_fp32.onnx"), "x");
                var partial = StudioDiscovery.Find(root);
                Assert.IsFalse(partial.Complete);
                CollectionAssert.AreEquivalent(new[] { "a .safamily file", "library.json" }, partial.Missing());
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "other.safamily"), "x");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "humanoid-v1.safamily"), "x");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "library.json"), "{}");
                var full = StudioDiscovery.Find(root);
                Assert.IsTrue(full.Complete);
                StringAssert.EndsWith("humanoid-v1.safamily", full.Family);
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }

        [Test]
        public void Discovery_of_missing_folder_reports_everything_missing()
        {
            var t = StudioDiscovery.Find("/no/such/folder");
            Assert.IsFalse(t.Complete);
            Assert.AreEqual(5, t.Missing().Count);
        }

        [Test]
        public void Library_loads_complete_entries_only()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa_lib_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(root);
                foreach (var n in new[] { "a_0", "a_1", "a_2", "a_3", "b_0" })
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(root, n + ".pose"), new byte[4]);
                var json = "{\"version\":1,\"entries\":[" +
                    "{\"name\":\"A 1\",\"group\":\"A\",\"time\":0,\"files\":[\"a_0.pose\",\"a_1.pose\",\"a_2.pose\",\"a_3.pose\"]}," +
                    "{\"name\":\"B 1\",\"group\":\"B\",\"time\":0,\"files\":[\"b_0.pose\",\"b_1.pose\",\"b_2.pose\",\"b_3.pose\"]}]}";
                var path = System.IO.Path.Combine(root, "library.json");
                System.IO.File.WriteAllText(path, json);
                Assert.IsTrue(StudioLibrary.TryLoad(path, out var lib, out _));
                Assert.AreEqual(1, lib.Entries.Count);
                Assert.AreEqual("A/A 1", lib.Labels()[0]);
                Assert.AreEqual(4, lib.PathsOf(0).Length);
                Assert.AreEqual(0, lib.IndexOf("A 1"));
                Assert.AreEqual(-1, lib.IndexOf("B 1"));
                System.IO.File.WriteAllText(path, "{\"version\":2,\"entries\":[]}");
                Assert.IsFalse(StudioLibrary.TryLoad(path, out _, out var err));
                StringAssert.Contains("empty", err);
                Assert.IsFalse(StudioLibrary.TryLoad("/no/such.json", out _, out _));
            }
            finally
            {
                System.IO.Directory.Delete(root, true);
            }
        }
    }
}

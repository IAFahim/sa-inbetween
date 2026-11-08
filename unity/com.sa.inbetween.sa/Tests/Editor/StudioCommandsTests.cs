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
            ContextFile = "/c/ctx.saib",
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
    }
}

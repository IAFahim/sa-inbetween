using System;
using System.IO;
using System.Threading.Tasks;
using SA.Inbetween.Cli;
using SA.Inbetween.Contract;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Cli.Tests;

public class CliSpec : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "sa-inbetween-cli-" + Guid.NewGuid().ToString("N"));

    public CliSpec()
    {
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        Directory.Delete(dir, true);
    }

    private static Pose IdentityPose()
    {
        var values = new float[539];
        for (var j = 0; j < 77; j++)
        {
            values[(j * 7) + 6] = 1f;
        }

        Assert.True(Poses.Parse(values).TryGetValue(out var pose));
        return pose;
    }

    private string WriteMotion(int frames)
    {
        var pose = IdentityPose();
        var framePoses = new Pose[frames];
        var contacts = new FootContacts[frames];
        for (var i = 0; i < frames; i++)
        {
            framePoses[i] = pose;
        }

        Assert.True(GenerationResults.Parse(Blocks.Of(framePoses), Blocks.Of(contacts), new Timing(8, 0f, 0f)).TryGetValue(out var result));
        var context = Blocks.Of(pose, pose, pose, pose);
        var target = new TargetSpec(Blocks.Of(pose), 1, 0, 0, 0);
        var request = new GenerationRequest(context, Blocks.Of(target), GenerationConfigs.Default);
        var recipe = new Recipe(Backend.Ported, Precision.Fp32, Sha256s.Of(new byte[] { 1 }), request);
        var file = MotionFiles.From(result, recipe);
        var buffer = new byte[MotionFiles.SizeOf(file)];
        Assert.True(MotionFiles.Write(file, buffer).TryGetValue(out _));
        var path = Path.Combine(dir, "m.saib");
        File.WriteAllBytes(path, buffer);
        return path;
    }

    [Fact]
    public async Task No_arguments_prints_usage_and_fails()
    {
        Assert.Equal(2, await Program.Main(Array.Empty<string>()));
    }

    [Fact]
    public async Task Help_succeeds()
    {
        Assert.Equal(0, await Program.Main(new[] { "--help" }));
    }

    [Fact]
    public async Task Unknown_command_fails()
    {
        Assert.Equal(1, await Program.Main(new[] { "bogus" }));
    }

    [Fact]
    public async Task Missing_file_fails_without_throwing()
    {
        Assert.Equal(1, await Program.Main(new[] { "inspect", Path.Combine(dir, "missing.saib") }));
    }

    [Fact]
    public async Task Garbage_file_is_rejected()
    {
        var path = Path.Combine(dir, "garbage.saib");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Equal(1, await Program.Main(new[] { "inspect", path }));
    }

    [Fact]
    public async Task Valid_motion_inspects()
    {
        Assert.Equal(0, await Program.Main(new[] { "inspect", WriteMotion(18) }));
    }

    [Fact]
    public async Task Pose_extraction_writes_one_pose()
    {
        var motion = WriteMotion(18);
        var output = Path.Combine(dir, "p.pose");
        Assert.Equal(0, await Program.Main(new[] { "pose", motion, "--frame", "3", "--out", output }));
        Assert.Equal(539 * sizeof(float), new FileInfo(output).Length);
    }

    [Fact]
    public async Task Pose_frame_out_of_range_fails()
    {
        var motion = WriteMotion(18);
        Assert.Equal(1, await Program.Main(new[] { "pose", motion, "--frame", "18", "--out", Path.Combine(dir, "p.pose") }));
    }

    [Fact]
    public async Task Generate_without_required_options_fails()
    {
        Assert.Equal(1, await Program.Main(new[] { "generate" }));
    }

    [Fact]
    public async Task Generate_rejects_a_model_that_does_not_match_the_pin()
    {
        var motion = WriteMotion(18);
        var pose = Path.Combine(dir, "a.pose");
        Assert.Equal(0, await Program.Main(new[] { "pose", motion, "--frame", "0", "--out", pose }));
        var model = Path.Combine(dir, "fake.onnx");
        File.WriteAllBytes(model, new byte[] { 0, 1, 2 });
        var exit = await Program.Main(new[] { "generate", "--model", model, "--context", pose, "--context", pose, "--context", pose, "--context", pose, "--target", pose, "--out", Path.Combine(dir, "o.saib") });
        Assert.Equal(1, exit);
        Assert.False(File.Exists(Path.Combine(dir, "o.saib")));
    }
}

using System;
using SA.Inbetween.Engine;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

public static class Allocations
{
    public static long PerCall(Action action)
    {
        for (var i = 0; i < 3; i++)
        {
            action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public static float[] Poses(int count)
    {
        var values = new float[count * EngineKernels.PoseFloats];
        for (var p = 0; p < count; p++)
        {
            for (var j = 0; j < SkeletonJoints.Count; j++)
            {
                var o = (p * EngineKernels.PoseFloats) + (j * 7);
                values[o] = 0.01f * j;
                values[o + 1] = 0.02f * p;
                values[o + 6] = 1f;
            }
        }

        return values;
    }
}

public sealed class KernelBuffers
{
    public float[] Context { get; } = Allocations.Poses(4);

    public float[] Target { get; } = Allocations.Poses(4);

    public float[] World { get; } = new float[8 * EngineKernels.PoseFloats];

    public float[] GlobalRoot { get; } = new float[40];

    public float[] LocalRoot { get; } = new float[32];

    public float[] LocalPoses { get; } = new float[8 * 267];

    public int[] HasGlobal { get; } = new int[8];

    public int[] HasLocalRoot { get; } = new int[8];

    public int[] HasLocalPoses { get; } = new int[8];

    public int[] Allowed { get; } = new int[11];

    public float[] Pred { get; } = Prediction();

    public float[] Frames { get; } = new float[58 * EngineKernels.PoseFloats];

    public float[] Contacts { get; } = new float[58 * 4];

    public float[] Start { get; } = new float[EngineKernels.PoseFloats];

    public float[] End { get; } = new float[EngineKernels.PoseFloats];

    public float[] Pinned { get; } = new float[IkKernels.Chains * 7];

    private static float[] Prediction()
    {
        var pred = new float[64 * 366];
        for (var r = 0; r < 64; r++)
        {
            for (var m = 0; m < 30; m++)
            {
                var o = (r * 366) + 92 + (6 * m);
                pred[o] = 1f;
                pred[o + 4] = 1f;
            }

            pred[(r * 366) + 1] = 1f;
        }

        return pred;
    }
}

[Trait(Packets.Name, Packets.P10)]
public class EngineAllocationSpec
{
    [Fact]
    public void Encode_kernels_allocate_nothing_after_warmup()
    {
        var b = new KernelBuffers();
        Assert.Equal(0, Allocations.PerCall(() =>
        {
            EngineKernels.Encode(b.Context, b.Target, Xform.Identity, 1.0, 0.0, b.World, b.GlobalRoot, b.LocalRoot, b.LocalPoses);
            EngineKernels.Conditions(2, 0x1FC, b.HasGlobal, b.HasLocalRoot, b.HasLocalPoses, b.Allowed);
            EngineKernels.PadToken(b.Target, 2, b.World.AsSpan(0, 4 * EngineKernels.PoseFloats));
        }));
    }

    [Fact]
    public void Decode_and_correction_kernels_allocate_nothing_after_warmup()
    {
        var b = new KernelBuffers();
        var context = b.Context.AsSpan(3 * EngineKernels.PoseFloats, EngineKernels.PoseFloats).ToArray();
        Assert.Equal(0, Allocations.PerCall(() =>
        {
            EngineKernels.Decode(b.Pred, 58, Xform.Identity, Xform.Identity, context, b.Frames, b.Contacts);
            EngineKernels.EndpointDeltas(b.Frames, 58, context, b.Target, b.Start, b.End);
            EngineKernels.ApplyEndpointDeltas(b.Frames, 58, b.Start, b.End);
        }));
        Assert.All(b.Frames, v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void Ik_kernels_allocate_nothing_after_warmup()
    {
        var b = new KernelBuffers();
        var pose = b.Context.AsSpan(0, EngineKernels.PoseFloats).ToArray();
        var world = new float[EngineKernels.PoseFloats];
        Assert.Equal(0, Allocations.PerCall(() =>
        {
            IkKernels.Capture(b.Target.AsSpan(0, EngineKernels.PoseFloats), world, b.Pinned);
            IkKernels.Apply(pose, 0.5, 15, b.Pinned, world);
        }));
    }

    [Fact]
    public void Rotation_functions_allocate_nothing_after_warmup()
    {
        var q = new Quaternion<SomaModel>(0.1f, 0.2f, 0.3f, 0.927f);
        var sink = 0f;
        Assert.Equal(0, Allocations.PerCall(() =>
        {
            var r = Rotations.Multiply(q, Rotations.Conjugate(Rotations.Normalize(q)));
            var six = Rotations.ToRotation6D(r);
            var back = Rotations.FromRotation6D(six);
            var v = Rotations.Rotate(back, new Vector3<SomaModel>(1f, 2f, 3f));
            var u = SpaceConversions.UnityToModel(SpaceConversions.ModelToUnity(SpaceConversions.RuntimeToModel(SpaceConversions.ModelToRuntime(back))));
            sink += v.X + u.W + Rotations.AngleBetween(q, back).Value + Units.ToDegrees(new Radians(1f)).Value;
        }));
        Assert.True(float.IsFinite(sink));
    }
}

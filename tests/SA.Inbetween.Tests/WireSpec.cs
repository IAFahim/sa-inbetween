using System;
using System.Linq;
using CsCheck;
using SA.Inbetween.Contract;
using SA.Inbetween.Contract.Wire;
using SA.Inbetween.Soma;
using Xunit;

namespace SA.Inbetween.Tests;

[Trait(Packets.Name, Packets.P04)]
public class WireSpec
{
    [Fact]
    public void Format_constants_match_L5()
    {
        Assert.Equal((0x4D42, 1, 12), (WireFormat.Magic, WireFormat.Version, WireFormat.HeaderSize));
        Assert.Equal((840, 884), (WireFormat.PoseSize, WireFormat.FrameSize));
        Assert.Equal((8, 9, 12, 13), (WireFormat.HelloBodySize, WireFormat.HelloAckBodySize, WireFormat.WireConfigSize, WireFormat.GenerateResultFixedSize));
        Assert.Equal((1, 2), (WireFormat.WantTimingFlag, WireFormat.F16ResponseFlag));
    }

    [Fact]
    public void Hello_has_exact_little_endian_bytes()
    {
        var buffer = new byte[20];
        Assert.Equal(20, Ok.Value(WireCodec.WriteHello(7, new Hello(1, Rig.Soma, WirePrecision.Fp32, 0x01020304), buffer)));
        Assert.Equal(
            new byte[] { 0x42, 0x4D, 1, 1, 7, 0, 0, 0, 8, 0, 0, 0, 1, 0, 0, 0, 4, 3, 2, 1 },
            buffer);
    }

    [Fact]
    public void Header_round_trip_for_every_message() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.ErrorMessage), t =>
        {
            var buffer = new byte[WireCodec.SizeOf(t.Item2)];
            var written = Ok.Value(WireCodec.WriteError(t.Item1, t.Item2, buffer));
            Assert.Equal(
                new WireHeader(WireFormat.Magic, WireFormat.Version, MessageType.Error, t.Item1, (uint)(written - WireFormat.HeaderSize)),
                Ok.Value(WireCodec.ReadHeader(buffer)));
        });

    [Fact]
    public void ReadHeader_rejects_bad_headers()
    {
        var good = new byte[] { 0x42, 0x4D, 1, 7, 9, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(new WireHeader(0x4D42, 1, MessageType.Ping, 9, 0), Ok.Value(WireCodec.ReadHeader(good)));
        Assert.Equal(Faults.Of(FaultCode.WireTruncated, 0, 11), Ok.Fault(WireCodec.ReadHeader(good.AsSpan(0, 11))));
        Assert.Equal(Faults.Of(FaultCode.WireBadMagic, 0, 0x4D43), Ok.Fault(WireCodec.ReadHeader(With(good, 0, 0x43))));
        Assert.Equal(Faults.Of(FaultCode.WireUnsupportedVersion, 2, 2), Ok.Fault(WireCodec.ReadHeader(With(good, 2, 2))));
        Assert.Equal(Faults.Of(FaultCode.WireUnknownMessageType, 3, 0), Ok.Fault(WireCodec.ReadHeader(With(good, 3, 0))));
        Assert.Equal(Faults.Of(FaultCode.WireUnknownMessageType, 3, 9), Ok.Fault(WireCodec.ReadHeader(With(good, 3, 9))));
    }

    [Fact]
    public void Hello_round_trip() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.Hello), t =>
        {
            var buffer = new byte[WireFormat.HeaderSize + WireFormat.HelloBodySize];
            Assert.Equal(buffer.Length, Ok.Value(WireCodec.WriteHello(t.Item1, t.Item2, buffer)));
            Assert.Equal(t.Item2, Ok.Value(WireCodec.ReadHello(buffer.AsSpan(WireFormat.HeaderSize))));
        });

    [Fact]
    public void HelloAck_round_trip() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.HelloAck), t =>
        {
            var buffer = new byte[WireFormat.HeaderSize + WireFormat.HelloAckBodySize];
            Assert.Equal(buffer.Length, Ok.Value(WireCodec.WriteHelloAck(t.Item1, t.Item2, buffer)));
            Assert.Equal(t.Item2, Ok.Value(WireCodec.ReadHelloAck(buffer.AsSpan(WireFormat.HeaderSize))));
        });

    [Fact]
    public void Generate_round_trip() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.Generate), t =>
        {
            var buffer = new byte[WireCodec.SizeOf(t.Item2)];
            Assert.Equal(buffer.Length, Ok.Value(WireCodec.WriteGenerate(t.Item1, t.Item2, buffer)));
            Assert.Equal(t.Item2, Ok.Value(WireCodec.ReadGenerate(buffer.AsSpan(WireFormat.HeaderSize))));
        });

    [Fact]
    public void Generate_size_follows_L5_layout()
    {
        var pose = Fixtures.IdentityModelPose();
        var target = new WireTarget(1, 0, 0, 0, Blocks.Of(pose));
        var message = new Generate(default, Blocks.Of(pose, pose, pose, pose), Blocks.Of(target), ChainMode.Single);
        Assert.Equal(12 + 12 + 1 + (4 * 840) + 1 + (7 + 840) + 1, WireCodec.SizeOf(message));
    }

    [Fact]
    public void GenerateResult_round_trip() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.GenerateResult), t =>
        {
            var buffer = new byte[WireCodec.SizeOf(t.Item2)];
            Assert.Equal(12 + 13 + (884 * t.Item2.Frames.Length), buffer.Length);
            Assert.Equal(buffer.Length, Ok.Value(WireCodec.WriteGenerateResult(t.Item1, t.Item2, buffer)));
            Assert.Equal(t.Item2, Ok.Value(WireCodec.ReadGenerateResult(buffer.AsSpan(WireFormat.HeaderSize))));
        });

    [Fact]
    public void Error_round_trip() =>
        Props.Check(Gen.Select(Gen.UInt, Gens.ErrorMessage), t =>
        {
            var buffer = new byte[WireCodec.SizeOf(t.Item2)];
            Assert.Equal(12 + 4 + t.Item2.MessageUtf8.Length, buffer.Length);
            Assert.Equal(buffer.Length, Ok.Value(WireCodec.WriteError(t.Item1, t.Item2, buffer)));
            Assert.Equal(t.Item2, Ok.Value(WireCodec.ReadError(buffer.AsSpan(WireFormat.HeaderSize))));
        });

    [Theory]
    [InlineData(MessageType.Reset)]
    [InlineData(MessageType.Ping)]
    [InlineData(MessageType.Cancel)]
    public void Empty_messages_are_header_only(MessageType type)
    {
        var buffer = new byte[12];
        Assert.Equal(12, Ok.Value(WireCodec.WriteEmpty(type, 3, buffer)));
        Assert.Equal(new WireHeader(WireFormat.Magic, 1, type, 3, 0), Ok.Value(WireCodec.ReadHeader(buffer)));
    }

    [Fact]
    public void WriteEmpty_rejects_types_with_bodies() =>
        Assert.Equal(
            Faults.Of(FaultCode.WireUnexpectedMessageType, 0, (int)MessageType.Generate),
            Ok.Fault(WireCodec.WriteEmpty(MessageType.Generate, 1, new byte[12])));

    [Fact]
    public void Writes_report_required_size_when_buffer_is_small()
    {
        Assert.Equal(Faults.Of(FaultCode.WireBufferTooSmall, 0, 20), Ok.Fault(WireCodec.WriteHello(1, default, new byte[19])));
        Assert.Equal(Faults.Of(FaultCode.WireBufferTooSmall, 0, 21), Ok.Fault(WireCodec.WriteHelloAck(1, default, new byte[20])));
        Assert.Equal(Faults.Of(FaultCode.WireBufferTooSmall, 0, 12), Ok.Fault(WireCodec.WriteEmpty(MessageType.Ping, 1, new byte[11])));
    }

    [Fact]
    public void Reads_reject_bad_field_values_with_body_offset()
    {
        Assert.Equal(Faults.Of(FaultCode.WireFieldOutOfRange, 2, 2), Ok.Fault(WireCodec.ReadHello(new byte[] { 1, 0, 2, 0, 0, 0, 0, 0 })));
        Assert.Equal(Faults.Of(FaultCode.WireFieldOutOfRange, 6, 2), Ok.Fault(WireCodec.ReadHelloAck(new byte[] { 1, 0, 0, 30, 30, 0, 2, 18, 58 })));
        Assert.Equal(Faults.Of(FaultCode.WireFieldOutOfRange, 0, 0), Ok.Fault(WireCodec.ReadError(new byte[] { 0, 0, 0, 0 })));
        Assert.Equal(Faults.Of(FaultCode.WireTruncated, 0, 7), Ok.Fault(WireCodec.ReadHello(new byte[7])));
        Assert.Equal(Faults.Of(FaultCode.WireBodyLengthMismatch, 0, 9), Ok.Fault(WireCodec.ReadHello(new byte[9])));
    }

    [Fact]
    public void Generate_rejects_f16_flag_and_non_unit_pose()
    {
        var pose = Fixtures.IdentityModelPose();
        var message = new Generate(new WireConfig(8, 14, true, 0, 1f, 0f), Blocks.Of(pose), Blocks.Empty<WireTarget>(), ChainMode.Single);
        var buffer = new byte[WireCodec.SizeOf(message)];
        Ok.Value(WireCodec.WriteGenerate(1, message, buffer));
        var body = buffer.AsSpan(WireFormat.HeaderSize).ToArray();
        var f16 = (byte[])body.Clone();
        f16[3] = WireFormat.F16ResponseFlag;
        Assert.Equal(Faults.Of(FaultCode.WireFieldOutOfRange, 3, 2), Ok.Fault(WireCodec.ReadGenerate(f16)));
        var badPose = (byte[])body.Clone();
        BitConverter.GetBytes(2f).CopyTo(badPose, 13 + 24);
        Assert.Equal(Faults.Of(FaultCode.WireFieldOutOfRange, 13, (int)FaultCode.PoseQuaternionNotUnit), Ok.Fault(WireCodec.ReadGenerate(badPose)));
    }

    [Fact]
    public void Writing_twice_gives_the_same_bytes() =>
        Props.Check(Gens.GenerateResult, m =>
        {
            var a = new byte[WireCodec.SizeOf(m)];
            var b = new byte[WireCodec.SizeOf(m)];
            Ok.Value(WireCodec.WriteGenerateResult(5, m, a));
            Ok.Value(WireCodec.WriteGenerateResult(5, m, b));
            Assert.Equal(a, b);
        });

    [Fact]
    public void Random_bytes_never_throw() =>
        Props.Check(Gens.Bytes, bytes =>
        {
            WireCodec.ReadHeader(bytes);
            WireCodec.ReadHello(bytes);
            WireCodec.ReadHelloAck(bytes);
            WireCodec.ReadGenerate(bytes);
            WireCodec.ReadGenerateResult(bytes);
            WireCodec.ReadError(bytes);
        });

    [Theory]
    [InlineData(WireErrorCode.BadRequest, FaultCode.RemoteBadRequest)]
    [InlineData(WireErrorCode.NotReady, FaultCode.RemoteNotReady)]
    [InlineData(WireErrorCode.GpuOutOfMemory, FaultCode.RemoteGpuOutOfMemory)]
    [InlineData(WireErrorCode.Internal, FaultCode.RemoteInternal)]
    [InlineData(WireErrorCode.Cancelled, FaultCode.RemoteCancelled)]
    public void ToFault_maps_remote_errors(WireErrorCode code, FaultCode expected) =>
        Assert.Equal(Faults.Of(expected, 0, 0), WireCodec.ToFault(new ErrorMessage(code, Blocks.Empty<byte>())));

    private static byte[] With(byte[] source, int index, byte value)
    {
        var copy = (byte[])source.Clone();
        copy[index] = value;
        return copy;
    }
}

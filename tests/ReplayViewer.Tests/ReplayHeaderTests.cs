using System.Buffers.Binary;

namespace ReplayViewer.Tests;

public class ReplayHeaderTests
{
    [Fact]
    public void Live_flag_is_read_from_a_minimal_header()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);
        WriteUInt32(stream, 0);
        stream.Write(new byte[12]);
        WriteInt32(stream, 0);
        WriteUInt32(stream, 1);

        stream.Position = 0;
        Assert.True(ReplayHeader.TryInspect(stream, out var info));
        Assert.True(info.IsLive);
    }

    [Fact]
    public void Wrong_magic_is_rejected()
    {
        using var stream = new MemoryStream([0, 1, 2, 3, 4, 5, 6, 7]);
        Assert.False(ReplayHeader.TryInspect(stream, out _));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteInt32(Stream stream, int value) => WriteUInt32(stream, unchecked((uint)value));
}

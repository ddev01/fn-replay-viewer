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

    [Fact]
    public void Truncated_header_is_rejected()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);

        stream.Position = 0;
        Assert.False(ReplayHeader.TryInspect(stream, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1025)]
    public void Impossible_custom_version_count_is_rejected(int count)
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);
        WriteUInt32(stream, 7);
        WriteInt32(stream, count);

        stream.Position = 0;
        Assert.False(ReplayHeader.TryInspect(stream, out _));
    }

    [Fact]
    public void Oversized_fstring_is_rejected()
    {
        using var stream = MinimalHeader();
        WriteInt32(stream, 2_000_000);

        stream.Position = 0;
        Assert.False(ReplayHeader.TryInspect(stream, out _));
    }

    [Fact]
    public void Utf16_fstring_is_skipped()
    {
        using var stream = MinimalHeader();
        WriteInt32(stream, -4);
        stream.Write(new byte[8]);
        WriteUInt32(stream, 1);

        stream.Position = 0;
        Assert.True(ReplayHeader.TryInspect(stream, out var info));
        Assert.True(info.IsLive);
    }

    [Fact]
    public void Encryption_key_is_reported()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);
        WriteUInt32(stream, 6);
        stream.Write(new byte[12]);
        WriteInt32(stream, 0);
        WriteUInt32(stream, 0);
        stream.Write(new byte[8]);
        stream.Write(new byte[4]);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 3);
        stream.Write(new byte[3]);

        stream.Position = 0;
        Assert.True(ReplayHeader.TryInspect(stream, out var info));
        Assert.True(info.IsEncrypted);
        Assert.Equal(3, info.EncryptionKeyLength);
    }

    [Fact]
    public void Oversized_encryption_key_is_rejected()
    {
        using var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);
        WriteUInt32(stream, 6);
        stream.Write(new byte[12]);
        WriteInt32(stream, 0);
        WriteUInt32(stream, 1);
        stream.Write(new byte[8]);
        stream.Write(new byte[4]);
        WriteUInt32(stream, 1);
        WriteUInt32(stream, 1025);

        stream.Position = 0;
        Assert.False(ReplayHeader.TryInspect(stream, out _));
    }

    [Fact]
    public void Non_seekable_stream_reads_the_same_header()
    {
        using var buffer = new MemoryStream();
        WriteUInt32(buffer, ReplayHeader.FileMagic);
        WriteUInt32(buffer, 7);
        WriteInt32(buffer, 20);
        buffer.Write(new byte[400]);
        buffer.Write(new byte[12]);
        WriteInt32(buffer, 0);
        WriteUInt32(buffer, 1);
        buffer.Write(new byte[8]);
        buffer.Write(new byte[4]);
        WriteUInt32(buffer, 0);
        WriteUInt32(buffer, 0);
        buffer.Position = 0;

        using var stream = new NonSeekableStream(buffer);
        Assert.True(ReplayHeader.TryInspect(stream, out var info));
        Assert.True(info.IsLive);
    }

    [Fact]
    public void File_that_is_not_a_replay_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "ReplayViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
        try
        {
            Assert.False(ReplayHeader.TryInspect(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream MinimalHeader()
    {
        var stream = new MemoryStream();
        WriteUInt32(stream, ReplayHeader.FileMagic);
        WriteUInt32(stream, 0);
        stream.Write(new byte[12]);
        return stream;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteInt32(Stream stream, int value) => WriteUInt32(stream, unchecked((uint)value));

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

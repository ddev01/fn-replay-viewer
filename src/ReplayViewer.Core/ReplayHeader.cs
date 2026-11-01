using System.Buffers.Binary;

namespace ReplayViewer.Core;

/// <summary>
/// Inspects Unreal replay headers. Completed-only: never mutates the file.
/// </summary>
public static class ReplayHeader
{
    public const uint FileMagic = 0x1CA2E27F;
    private const uint HistoryRecordedTimestamp = 3;
    private const uint HistoryCompression = 2;
    private const uint HistoryEncryption = 6;
    private const uint HistoryCustomVersions = 7;

    public readonly record struct Info(bool IsLive, bool IsEncrypted, int EncryptionKeyLength);

    public static bool TryInspect(string path, out Info info)
    {
        info = default;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        return TryInspect(stream, out info);
    }

    public static bool TryInspect(Stream stream, out Info info)
    {
        info = default;
        var reader = new HeaderReader(stream);
        try
        {
            if (reader.ReadUInt32() != FileMagic)
            {
                return false;
            }

            var fileVersion = reader.ReadUInt32();
            if (fileVersion >= HistoryCustomVersions)
            {
                var customVersionCount = reader.ReadInt32();
                if (customVersionCount < 0 || customVersionCount > 1024)
                {
                    return false;
                }

                reader.Skip(customVersionCount * 20);
            }

            reader.Skip(12);
            if (!reader.TrySkipFString())
            {
                return false;
            }

            var isLive = reader.ReadUInt32() != 0;

            if (fileVersion >= HistoryRecordedTimestamp)
            {
                reader.Skip(8);
            }

            if (fileVersion >= HistoryCompression)
            {
                reader.Skip(4);
            }

            var isEncrypted = false;
            var keyLength = 0;
            if (fileVersion >= HistoryEncryption)
            {
                isEncrypted = reader.ReadUInt32() != 0;
                var size = reader.ReadUInt32();
                if (size > 1024)
                {
                    return false;
                }

                keyLength = (int)size;
                reader.Skip(keyLength);
            }

            info = new Info(isLive, isEncrypted, keyLength);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed class HeaderReader(Stream stream)
    {
        public uint ReadUInt32()
        {
            Span<byte> buf = stackalloc byte[4];
            ReadExactly(buf);
            return BinaryPrimitives.ReadUInt32LittleEndian(buf);
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public void Skip(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (stream.CanSeek)
            {
                stream.Seek(count, SeekOrigin.Current);
                return;
            }

            Span<byte> buf = stackalloc byte[Math.Min(count, 256)];
            var remaining = count;
            while (remaining > 0)
            {
                var n = Math.Min(remaining, buf.Length);
                ReadExactly(buf[..n]);
                remaining -= n;
            }
        }

        public bool TrySkipFString()
        {
            var length = ReadInt32();
            if (length == 0)
            {
                return true;
            }

            var byteCount = length < 0 ? -2 * length : length;
            if (byteCount is < 0 or > 1_000_000)
            {
                return false;
            }

            Skip(byteCount);
            return true;
        }

        private void ReadExactly(Span<byte> buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer[read..]);
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }
        }
    }
}

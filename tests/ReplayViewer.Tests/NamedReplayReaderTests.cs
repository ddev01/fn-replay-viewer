using System.Text;

namespace ReplayViewer.Tests;

public sealed class NamedReplayReaderTests
{
    [Fact]
    public void ReadString_decodes_an_unreal_fstring()
    {
        // length counts the terminator
        byte[] data = [.. BitConverter.GetBytes(4), .. Encoding.UTF8.GetBytes("Bob"), 0];

        Assert.Equal("Bob", NamedReplayReader.ReadString(data));
    }

    [Fact]
    public void ReadString_rejects_a_length_that_overruns_the_data()
    {
        byte[] data = [.. BitConverter.GetBytes(50), 65, 0];

        Assert.Null(NamedReplayReader.ReadString(data));
    }

    [Fact]
    public void ContainsAtAnyBit_finds_an_id_that_is_not_byte_aligned()
    {
        byte[] id = [0x9A, 0x86, 0x9F, 0xE1];
        const int shift = 3;
        // Write id << shift into a zeroed buffer (LSB-first bit stream) with a header byte before it.
        var payload = new byte[id.Length + 2];
        for (var i = 0; i < id.Length; i++)
        {
            var wide = id[i] << shift;
            payload[1 + i] |= (byte)wide;
            payload[2 + i] |= (byte)(wide >> 8);
        }

        Assert.True(NamedReplayReader.ContainsAtAnyBit(payload, id));
        Assert.False(NamedReplayReader.ContainsAtAnyBit(payload, [0x01, 0x02, 0x03, 0x04]));
    }
}

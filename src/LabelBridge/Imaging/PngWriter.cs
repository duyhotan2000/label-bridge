using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace LabelBridge.Imaging;

/// <summary>Minimal 8-bit grayscale PNG encoder for diagnostics.</summary>
public static class PngWriter
{
    static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(GrayImage img, Stream s)
    {
        s.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)img.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)img.Height);
        ihdr[8] = 8; // bit depth; color type 0 (gray), default compression/filter/interlace
        WriteChunk(s, "IHDR", ihdr);

        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < img.Height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(img.Pixels, y * img.Width, img.Width);
            }
        }
        WriteChunk(s, "IDAT", raw.ToArray());
        WriteChunk(s, "IEND", []);
    }

    static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)data.Length);
        s.Write(u32);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(u32, crc);
        s.Write(u32);
    }

    static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}

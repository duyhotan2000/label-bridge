using System.Buffers.Binary;
using LabelBridge.Imaging;

namespace LabelBridge.Tests.Imaging;

public class PngWriterTests
{
    [Fact]
    public void Png_HasSignatureAndIhdr()
    {
        var ms = new MemoryStream();
        PngWriter.Write(new GrayImage(3, 2, 203, [0, 128, 255, 255, 128, 0]), ms);
        var b = ms.ToArray();

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, b[..8]);
        Assert.Equal("IHDR"u8.ToArray(), b[12..16]);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)));
        Assert.Equal(8, b[24]);
        Assert.Equal(0, b[25]);
        Assert.Equal("IEND"u8.ToArray(), b[^8..^4]);
    }
}

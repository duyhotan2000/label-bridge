using System.Buffers.Binary;
using System.Text;
using LabelBridge.Raster;
using LabelBridge.Tests.TestHelpers;

namespace LabelBridge.Tests.Raster;

public class RasterDecoderTests
{
    [Fact]
    public void Urf_TwoGrayPages_DecodedExactly()
    {
        var p1 = RasterEncoders.GrayPixels(40, 30, (x, y) => (byte)((x * 7 + y * 3) % 256));
        var p2 = RasterEncoders.GrayPixels(40, 30, (x, y) => x is >= 5 and < 20 && y is >= 3 and < 25 ? (byte)0 : (byte)255);
        var urf = RasterEncoders.EncodeUrf(new RasterPage(p1, 40, 30, 203), new RasterPage(p2, 40, 30, 203));

        var pages = UrfDecoder.Decode(new MemoryStream(urf)).ToList();

        Assert.Equal(2, pages.Count);
        Assert.Equal(p1, pages[0].Pixels);
        Assert.Equal(p2, pages[1].Pixels);
        Assert.All(pages, p => Assert.Equal(203, p.Dpi));
        Assert.All(pages, p => Assert.Equal((40, 30), (p.Width, p.Height)));
    }

    [Fact]
    public void Urf_Rgb24_ConvertsToGray()
    {
        var rgb = Enumerable.Repeat((byte)255, 10 * 10 * 3).ToArray();
        var i = (4 * 10 + 3) * 3;
        rgb[i] = 255; rgb[i + 1] = 0; rgb[i + 2] = 0;
        var urf = RasterEncoders.EncodeUrf(new RasterPage(rgb, 10, 10, 300, 24));

        var page = UrfDecoder.Decode(new MemoryStream(urf)).Single();

        Assert.Equal(76, page[3, 4]);
        Assert.Equal(99, page.Pixels.Count(p => p == 255));
    }

    [Fact]
    public void Urf_FillCode128_FillsWhite()
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("UNIRAST\0"));
        ms.Write(new byte[] { 0, 0, 0, 1 });
        var h = new byte[32];
        h[0] = 8;
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(12), 5);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(20), 203);
        ms.Write(h);
        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x80 });

        var page = UrfDecoder.Decode(new MemoryStream(ms.ToArray())).Single();

        Assert.Equal(new byte[] { 0, 255, 255, 255, 255 }, page.Pixels);
    }

    [Fact]
    public void Pwg_GrayPage_ReadsResolutionAndSize()
    {
        var px = RasterEncoders.GrayPixels(50, 20, (x, y) => (byte)(x * 5));
        var pwg = RasterEncoders.EncodePwg(new RasterPage(px, 50, 20, 300));

        var page = PwgDecoder.Decode(new MemoryStream(pwg)).Single();

        Assert.Equal((50, 20, 300), (page.Width, page.Height, page.Dpi));
        Assert.Equal(px, page.Pixels);
    }

    [Fact]
    public void Urf_TruncatedStream_ThrowsRasterFormatException()
    {
        var px = RasterEncoders.GrayPixels(40, 30, (x, y) => (byte)((x * 7 + y * 3) % 256));
        var urf = RasterEncoders.EncodeUrf(new RasterPage(px, 40, 30, 203));
        var cut = urf[..^10];

        Assert.Throws<RasterFormatException>(() => UrfDecoder.Decode(new MemoryStream(cut)).ToList());
    }

    [Fact]
    public void RasterReader_UnknownMime_Throws()
    {
        Assert.Throws<RasterFormatException>(() =>
            RasterReader.ReadPages(new MemoryStream(), "application/pdf").ToList());
    }
}

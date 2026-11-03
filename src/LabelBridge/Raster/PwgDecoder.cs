using System.Buffers.Binary;
using System.Text;
using LabelBridge.Imaging;

namespace LabelBridge.Raster;

/// <summary>PWG raster (image/pwg-raster); pages run until end of stream.</summary>
public static class PwgDecoder
{
    const int HeaderSize = 1796;

    public static IEnumerable<GrayImage> Decode(Stream s)
    {
        var sync = new byte[4];
        RasterLine.ReadExactly(s, sync);
        if (Encoding.ASCII.GetString(sync) != "RaS2")
            throw new RasterFormatException("Not a PWG raster stream");

        var header = new byte[HeaderSize];
        while (true)
        {
            var first = s.ReadByte();
            if (first < 0)
                yield break;
            header[0] = (byte)first;
            RasterLine.ReadExactly(s, header.AsSpan(1));
            int U32(int offset) => (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset));
            var dpi = U32(276);
            var width = U32(372);
            var height = U32(376);
            var bitsPerPixel = U32(388);
            UrfDecoder.Validate(width, height, dpi);
            var px = new byte[width * height];
            RasterLine.DecodePage(s, width, height, bitsPerPixel / 8, px);
            yield return new GrayImage(width, height, dpi, px);
        }
    }
}

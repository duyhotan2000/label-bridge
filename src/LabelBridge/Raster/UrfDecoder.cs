using System.Buffers.Binary;
using System.Text;
using LabelBridge.Imaging;

namespace LabelBridge.Raster;

/// <summary>Apple Raster (image/urf).</summary>
public static class UrfDecoder
{
    public static IEnumerable<GrayImage> Decode(Stream s)
    {
        var fileHeader = new byte[12];
        RasterLine.ReadExactly(s, fileHeader);
        if (Encoding.ASCII.GetString(fileHeader, 0, 7) != "UNIRAST")
            throw new RasterFormatException("Not a URF stream");
        var pageCount = BinaryPrimitives.ReadUInt32BigEndian(fileHeader.AsSpan(8));

        var header = new byte[32];
        for (var p = 0; p < pageCount; p++)
        {
            RasterLine.ReadExactly(s, header);
            var bpp = header[0];
            var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
            var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16));
            var dpi = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20));
            Validate(width, height, dpi);
            var px = new byte[width * height];
            RasterLine.DecodePage(s, width, height, bpp / 8, px);
            yield return new GrayImage(width, height, dpi, px);
        }
    }

    internal static void Validate(int width, int height, int dpi)
    {
        if (width is <= 0 or > 20000 || height is <= 0 or > 20000 || dpi is <= 0 or > 2400)
            throw new RasterFormatException($"Implausible page {width}x{height} at {dpi} dpi");
    }
}

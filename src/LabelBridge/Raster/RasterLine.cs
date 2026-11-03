namespace LabelBridge.Raster;

/// <summary>
/// Decodes the compressed page body shared by URF and PWG raster: a line-repeat
/// byte, then pixel runs (0-127 = repeat next pixel n+1 times, 129-255 = 257-n
/// literal pixels, 128 = fill the rest of the line with white).
/// </summary>
internal static class RasterLine
{
    public static void DecodePage(Stream s, int width, int height, int bytesPerPixel, Span<byte> grayOut)
    {
        if (bytesPerPixel is not (1 or 3))
            throw new RasterFormatException($"Unsupported pixel size {bytesPerPixel * 8} bits");
        var line = new byte[width * bytesPerPixel];
        var pixel = new byte[bytesPerPixel];
        var y = 0;
        while (y < height)
        {
            var repeat = ReadByte(s) + 1;
            var x = 0;
            while (x < width)
            {
                var code = ReadByte(s);
                if (code == 128)
                {
                    line.AsSpan(x * bytesPerPixel).Fill(255);
                    x = width;
                }
                else if (code < 128)
                {
                    var count = Math.Min(code + 1, width - x);
                    ReadExactly(s, pixel);
                    for (var i = 0; i < count; i++)
                        pixel.CopyTo(line, (x + i) * bytesPerPixel);
                    x += count;
                }
                else
                {
                    var count = 257 - code;
                    if (x + count > width)
                        throw new RasterFormatException("Literal run past end of line");
                    ReadExactly(s, line.AsSpan(x * bytesPerPixel, count * bytesPerPixel));
                    x += count;
                }
            }

            var gray = grayOut.Slice(y * width, width);
            ToGray(line, bytesPerPixel, gray);
            for (var r = 1; r < repeat && y + r < height; r++)
                gray.CopyTo(grayOut.Slice((y + r) * width, width));
            y += repeat;
        }
    }

    static void ToGray(byte[] line, int bytesPerPixel, Span<byte> gray)
    {
        if (bytesPerPixel == 1)
        {
            line.CopyTo(gray);
            return;
        }
        for (var i = 0; i < gray.Length; i++)
        {
            var o = i * 3;
            gray[i] = (byte)((299 * line[o] + 587 * line[o + 1] + 114 * line[o + 2]) / 1000);
        }
    }

    internal static int ReadByte(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0)
            throw new RasterFormatException("Unexpected end of raster data");
        return b;
    }

    internal static void ReadExactly(Stream s, Span<byte> buf)
    {
        try
        {
            s.ReadExactly(buf);
        }
        catch (EndOfStreamException)
        {
            throw new RasterFormatException("Unexpected end of raster data");
        }
    }
}

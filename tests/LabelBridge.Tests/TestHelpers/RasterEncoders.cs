using System.Buffers.Binary;
using System.Text;

namespace LabelBridge.Tests.TestHelpers;

/// <summary>A page to encode; Pixels has bpp/8 bytes per pixel.</summary>
public sealed record RasterPage(byte[] Pixels, int Width, int Height, int Dpi, int Bpp = 8);

/// <summary>Encoders for Apple URF and PWG raster, used to build test inputs.</summary>
public static class RasterEncoders
{
    public static byte[] EncodeUrf(params RasterPage[] pages)
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("UNIRAST\0"));
        WriteU32(ms, (uint)pages.Length);
        foreach (var p in pages)
        {
            var h = new byte[32];
            h[0] = (byte)p.Bpp;
            h[1] = (byte)(p.Bpp == 8 ? 0 : 1);
            BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(12), (uint)p.Width);
            BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(16), (uint)p.Height);
            BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(20), (uint)p.Dpi);
            ms.Write(h);
            EncodeData(ms, p);
        }
        return ms.ToArray();
    }

    public static byte[] EncodePwg(params RasterPage[] pages)
    {
        var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("RaS2"));
        foreach (var p in pages)
        {
            var h = new byte[1796];
            void U32(int offset, int v) => BinaryPrimitives.WriteUInt32BigEndian(h.AsSpan(offset), (uint)v);
            U32(276, p.Dpi);
            U32(280, p.Dpi);
            U32(372, p.Width);
            U32(376, p.Height);
            U32(384, 8);
            U32(388, p.Bpp);
            U32(392, p.Width * p.Bpp / 8);
            ms.Write(h);
            EncodeData(ms, p);
        }
        return ms.ToArray();
    }

    /// <summary>Line repeats plus PackBits-style pixel runs.</summary>
    static void EncodeData(Stream s, RasterPage p)
    {
        var bpp = p.Bpp / 8;
        var stride = p.Width * bpp;
        var y = 0;
        while (y < p.Height)
        {
            var line = p.Pixels.AsSpan(y * stride, stride);
            var repeat = 1;
            while (y + repeat < p.Height && repeat < 256 &&
                   p.Pixels.AsSpan((y + repeat) * stride, stride).SequenceEqual(line))
                repeat++;
            s.WriteByte((byte)(repeat - 1));
            EncodeLine(s, line.ToArray(), p.Width, bpp);
            y += repeat;
        }
    }

    static void EncodeLine(Stream s, byte[] line, int width, int bpp)
    {
        bool Same(int a, int b) => line.AsSpan(a * bpp, bpp).SequenceEqual(line.AsSpan(b * bpp, bpp));
        var x = 0;
        while (x < width)
        {
            var run = 1;
            while (x + run < width && run < 128 && Same(x, x + run))
                run++;
            if (run > 1)
            {
                s.WriteByte((byte)(run - 1));
                s.Write(line.AsSpan(x * bpp, bpp));
                x += run;
                continue;
            }
            var lit = 1;
            while (x + lit < width && lit < 128 && !(x + lit + 1 < width && Same(x + lit, x + lit + 1)))
                lit++;
            if (lit == 1)
            {
                s.WriteByte(0);
                s.Write(line.AsSpan(x * bpp, bpp));
            }
            else
            {
                s.WriteByte((byte)(257 - lit));
                s.Write(line.AsSpan(x * bpp, lit * bpp));
            }
            x += lit;
        }
    }

    static void WriteU32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        s.Write(b);
    }

    public static byte[] GrayPixels(int w, int h, Func<int, int, byte> f)
    {
        var px = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = f(x, y);
        return px;
    }
}

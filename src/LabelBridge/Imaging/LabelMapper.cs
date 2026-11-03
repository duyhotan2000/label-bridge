using LabelBridge.Config;

namespace LabelBridge.Imaging;

/// <summary>Places a received page (A6 for Shopee AWBs) onto the physical label.</summary>
public static class LabelMapper
{
    public static GrayImage Map(GrayImage page, LabelOptions o)
    {
        int lw = o.WidthDots, lh = o.HeightDots;
        if (page.Dpi != o.Dpi)
        {
            var f = (double)o.Dpi / page.Dpi;
            page = Resize(page, 0, 0, page.Width, page.Height,
                Math.Max(1, (int)Math.Round(page.Width * f)), Math.Max(1, (int)Math.Round(page.Height * f)), o.Dpi);
        }

        switch (o.FitMode)
        {
            case "stretch":
                return Resize(page, 0, 0, page.Width, page.Height, lw, lh, o.Dpi);
            case "crop-center":
                return Place(page, 0, 0, page.Width, page.Height, lw, lh, o.Dpi);
            default:
                var box = ContentBox(page, o.Threshold);
                if (box is null)
                    return GrayImage.White(lw, lh, o.Dpi);
                var (bx, by, bw, bh) = box.Value;
                if (bw <= lw && bh <= lh)
                    return Place(page, bx, by, bw, bh, lw, lh, o.Dpi);
                var scale = Math.Min((double)lw / bw, (double)lh / bh);
                var scaled = Resize(page, bx, by, bw, bh,
                    Math.Min(lw, (int)Math.Round(bw * scale)), Math.Min(lh, (int)Math.Round(bh * scale)), o.Dpi);
                return Place(scaled, 0, 0, scaled.Width, scaled.Height, lw, lh, o.Dpi);
        }
    }

    /// <summary>Bounding box of pixels darker than the threshold, or null for a blank page.</summary>
    static (int X, int Y, int W, int H)? ContentBox(GrayImage img, byte threshold)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (img[x, y] < threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Centers the source region on a white canvas, cropping overflow equally from both sides.</summary>
    static GrayImage Place(GrayImage src, int sx, int sy, int sw, int sh, int w, int h, int dpi)
    {
        var dst = GrayImage.White(w, h, dpi);
        int ox = (w - sw) / 2, oy = (h - sh) / 2;
        for (var y = 0; y < sh; y++)
        {
            var dy = y + oy;
            if (dy < 0 || dy >= h) continue;
            for (var x = 0; x < sw; x++)
            {
                var dx = x + ox;
                if (dx < 0 || dx >= w) continue;
                dst[dx, dy] = src[sx + x, sy + y];
            }
        }
        return dst;
    }

    /// <summary>
    /// Resizes a source region: box-filter average when shrinking along an axis,
    /// nearest neighbor when enlarging.
    /// </summary>
    static GrayImage Resize(GrayImage src, int sx, int sy, int sw, int sh, int w, int h, int dpi)
    {
        var dst = new GrayImage(w, h, dpi, new byte[w * h]);
        double fx = (double)sw / w, fy = (double)sh / h;
        for (var y = 0; y < h; y++)
        {
            var y0 = (int)(y * fy);
            var y1 = Math.Max(y0 + 1, Math.Min(sh, (int)((y + 1) * fy)));
            for (var x = 0; x < w; x++)
            {
                var x0 = (int)(x * fx);
                var x1 = Math.Max(x0 + 1, Math.Min(sw, (int)((x + 1) * fx)));
                int sum = 0, n = 0;
                for (var yy = y0; yy < y1; yy++)
                    for (var xx = x0; xx < x1; xx++)
                    {
                        sum += src[sx + xx, sy + yy];
                        n++;
                    }
                dst[x, y] = (byte)(sum / n);
            }
        }
        return dst;
    }
}

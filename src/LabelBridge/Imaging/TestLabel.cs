using LabelBridge.Config;

namespace LabelBridge.Imaging;

/// <summary>Built-in label for `test-print`: border, corner squares and barcode-like bars.</summary>
public static class TestLabel
{
    public static GrayImage Create(LabelOptions o)
    {
        int w = o.WidthDots, h = o.HeightDots;
        var img = GrayImage.White(w, h, o.Dpi);
        void Fill(int x0, int y0, int fw, int fh)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(h, y0 + fh); y++)
                for (var x = Math.Max(0, x0); x < Math.Min(w, x0 + fw); x++)
                    img[x, y] = 0;
        }

        const int border = 4, corner = 20;
        Fill(0, 0, w, border);
        Fill(0, h - border, w, border);
        Fill(0, 0, border, h);
        Fill(w - border, 0, border, h);

        Fill(0, 0, corner, corner);
        Fill(w - corner, 0, corner, corner);
        Fill(0, h - corner, corner, corner);
        Fill(w - corner, h - corner, corner, corner);

        int[] widths = [2, 4, 6, 8];
        var x = 40;
        for (var i = 0; x + widths[i % 4] < w - 40; i++)
        {
            var bar = widths[i % 4];
            Fill(x, h / 3, bar, h / 3);
            x += bar * 2;
        }
        return img;
    }
}

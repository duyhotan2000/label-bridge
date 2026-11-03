using LabelBridge.Config;
using LabelBridge.Imaging;

namespace LabelBridge.Tests.Imaging;

public class LabelMapperTests
{
    static GrayImage PageWithRect(int w, int h, int dpi, int rx, int ry, int rw, int rh)
    {
        var img = GrayImage.White(w, h, dpi);
        for (var y = ry; y < ry + rh; y++)
            for (var x = rx; x < rx + rw; x++)
                img[x, y] = 0;
        return img;
    }

    static (int X, int Y, int W, int H) BlackBox(GrayImage img)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (img[x, y] < 128)
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    [Fact]
    public void Map_A6PageWithMargins_CopiedOneToOneCentered()
    {
        var page = PageWithRect(840, 1184, 203, 70, 42, 700, 1100);

        var label = LabelMapper.Map(page, new LabelOptions());

        Assert.Equal((800, 1200, 203), (label.Width, label.Height, label.Dpi));
        Assert.Equal((50, 50, 700, 1100), BlackBox(label));
        Assert.Equal(700 * 1100, label.Pixels.Count(p => p == 0));
    }

    [Fact]
    public void Map_ContentWiderThanLabel_ScaledToFit()
    {
        var page = PageWithRect(840, 1184, 203, 0, 0, 840, 1184);

        var label = LabelMapper.Map(page, new LabelOptions());

        var box = BlackBox(label);
        Assert.Equal((0, 800), (box.X, box.W));
        Assert.InRange(box.Y, 35, 37);
        Assert.InRange(box.Y + box.H - 1, 1162, 1164);
        Assert.Equal(255, label[400, 10]);
        Assert.Equal(255, label[400, 1190]);
    }

    [Fact]
    public void Map_300DpiInput_ResampledTo203()
    {
        var page = PageWithRect(1240, 1748, 300, 600, 600, 300, 300);

        var label = LabelMapper.Map(page, new LabelOptions());

        var box = BlackBox(label);
        Assert.InRange(box.W, 201, 205);
        Assert.InRange(box.H, 201, 205);
    }

    [Fact]
    public void Map_BlankPage_ReturnsBlankLabel()
    {
        var label = LabelMapper.Map(GrayImage.White(840, 1184, 203), new LabelOptions());

        Assert.Equal((800, 1200), (label.Width, label.Height));
        Assert.All(label.Pixels, p => Assert.Equal(255, p));
    }

    [Fact]
    public void Map_CropCenter_NoScaling()
    {
        var page = GrayImage.White(840, 1184, 203);
        for (var y = 0; y < 1184; y++)
        {
            page[20, y] = 0;
            page[819, y] = 0;
        }

        var label = LabelMapper.Map(page, new LabelOptions { FitMode = "crop-center" });

        Assert.Equal(0, label[0, 600]);
        Assert.Equal(0, label[799, 600]);
        Assert.Equal(255, label[0, 4]);   // 8 white rows added top and bottom
        Assert.Equal(0, label[0, 8]);
        Assert.Equal(0, label[0, 1191]);
        Assert.Equal(255, label[0, 1192]);
    }

    [Fact]
    public void Map_Stretch_FillsLabel()
    {
        var page = PageWithRect(840, 1184, 203, 0, 0, 840, 1184);

        var label = LabelMapper.Map(page, new LabelOptions { FitMode = "stretch" });

        Assert.All(label.Pixels, p => Assert.Equal(0, p));
    }
}

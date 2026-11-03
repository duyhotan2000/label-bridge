using LabelBridge.Config;
using LabelBridge.Imaging;

namespace LabelBridge.Tests.Imaging;

public class MonochromeTests
{
    [Fact]
    public void Pack_Threshold()
    {
        var img = new GrayImage(4, 1, 203, [0, 159, 160, 255]);

        var mono = Monochrome.Pack(img, 160);

        Assert.Equal(1, mono.WidthBytes);
        Assert.Equal(0b1100_0000, mono.Bits[0]);
        Assert.True(mono.IsBlack(1, 0));
        Assert.False(mono.IsBlack(2, 0));
    }

    [Fact]
    public void Pack_PadsRowsToWholeBytes()
    {
        var img = new GrayImage(9, 2, 203, Enumerable.Repeat((byte)0, 18).ToArray());

        var mono = Monochrome.Pack(img, 128);

        Assert.Equal(2, mono.WidthBytes);
        Assert.Equal(new byte[] { 0xFF, 0x80, 0xFF, 0x80 }, mono.Bits);
    }

    [Fact]
    public void ToGray_RoundTrips()
    {
        var mono = new MonoBitmap(3, 1, [0b1010_0000]);

        var gray = mono.ToGray(203);

        Assert.Equal(new byte[] { 0, 255, 0 }, gray.Pixels);
    }

    [Fact]
    public void TestLabel_HasBorderAndCorners()
    {
        var label = TestLabel.Create(new LabelOptions());

        Assert.Equal((800, 1200), (label.Width, label.Height));
        Assert.Equal(0, label[0, 0]);
        Assert.Equal(0, label[799, 1199]);
        Assert.Equal(0, label[10, 10]);      // corner square
        Assert.Equal(255, label[400, 100]);
    }
}

using System.Text;
using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Output;

namespace LabelBridge.Tests.Output;

public class TsplEncoderTests
{
    static readonly MonoBitmap Bitmap = new(16, 2, [0xFF, 0x00, 0x0F, 0xF0]);

    static byte[] Expected(byte[] data) =>
        Encoding.ASCII.GetBytes("SIZE 100 mm,150 mm\r\nGAP 2 mm,0 mm\r\nDIRECTION 1\r\nDENSITY 8\r\nSPEED 4\r\nCLS\r\nBITMAP 0,0,2,2,0,")
            .Concat(data)
            .Concat(Encoding.ASCII.GetBytes("\r\nPRINT 1,1\r\n"))
            .ToArray();

    [Fact]
    public void Encode_SingleLabel_ExactBytes()
    {
        var bytes = TsplEncoder.Encode([Bitmap], new LabelOptions(), new TsplOptions());

        Assert.Equal(Expected([0x00, 0xFF, 0xF0, 0x0F]), bytes);
    }

    [Fact]
    public void Encode_InvertBitmap_WritesBitsAsIs()
    {
        var bytes = TsplEncoder.Encode([Bitmap], new LabelOptions(), new TsplOptions { InvertBitmap = true });

        Assert.Equal(Expected([0xFF, 0x00, 0x0F, 0xF0]), bytes);
    }

    [Fact]
    public void Encode_ThreeLabels_ThreePrintCommands()
    {
        var text = Encoding.Latin1.GetString(TsplEncoder.Encode([Bitmap, Bitmap, Bitmap], new LabelOptions(), new TsplOptions()));

        Assert.Equal(3, text.Split("PRINT 1,1").Length - 1);
        Assert.Equal(3, text.Split("CLS").Length - 1);
    }

    [Fact]
    public void Encode_FractionalValues_UseInvariantCulture()
    {
        var text = Encoding.Latin1.GetString(TsplEncoder.Encode([Bitmap],
            new LabelOptions { WidthMm = 101.5 }, new TsplOptions { GapMm = 2.5 }));

        Assert.StartsWith("SIZE 101.5 mm,150 mm\r\nGAP 2.5 mm,0 mm\r\n", text);
    }
}

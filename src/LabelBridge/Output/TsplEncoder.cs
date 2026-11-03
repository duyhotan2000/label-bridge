using System.Globalization;
using System.Text;
using LabelBridge.Config;
using LabelBridge.Imaging;

namespace LabelBridge.Output;

/// <summary>Builds TSPL commands (Xprinter / TSC label language) for a batch of labels.</summary>
public static class TsplEncoder
{
    public static byte[] Encode(IReadOnlyList<MonoBitmap> labels, LabelOptions label, TsplOptions t)
    {
        var ms = new MemoryStream();
        void Line(string text) => ms.Write(Encoding.ASCII.GetBytes(text + "\r\n"));

        foreach (var bmp in labels)
        {
            Line($"SIZE {Num(label.WidthMm)} mm,{Num(label.HeightMm)} mm");
            Line($"GAP {Num(t.GapMm)} mm,0 mm");
            Line($"DIRECTION {t.Direction}");
            Line($"DENSITY {t.Density}");
            Line($"SPEED {t.Speed}");
            Line("CLS");
            ms.Write(Encoding.ASCII.GetBytes($"BITMAP 0,0,{bmp.WidthBytes},{bmp.Height},0,"));
            // In TSPL a 0 bit prints a black dot, so our "1 = black" bits are inverted by default.
            foreach (var b in bmp.Bits)
                ms.WriteByte(t.InvertBitmap ? b : (byte)~b);
            Line("");
            Line("PRINT 1,1");
        }
        return ms.ToArray();
    }

    static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}

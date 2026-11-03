using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LabelBridge.Config;
using LabelBridge.Imaging;

namespace LabelBridge.Output;

/// <summary>Prints through the installed Xprinter Windows driver (fallback if raw TSPL doesn't work).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDriverOutput(BridgeOptions o) : ILabelOutput
{
    public Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct) =>
        Task.Run(() => Print(labels, jobName), ct);

    void Print(IReadOnlyList<MonoBitmap> labels, string jobName)
    {
        using var doc = new PrintDocument();
        doc.DocumentName = jobName;
        doc.PrinterSettings.PrinterName = o.WindowsPrinterName;
        if (!doc.PrinterSettings.IsValid)
            throw new InvalidOperationException($"Windows printer '{o.WindowsPrinterName}' is not installed");
        // PaperSize is in hundredths of an inch.
        var paper = new PaperSize("Label", (int)Math.Round(o.Label.WidthMm / 0.254), (int)Math.Round(o.Label.HeightMm / 0.254));
        doc.DefaultPageSettings.PaperSize = paper;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        doc.OriginAtMargins = false;

        var next = 0;
        doc.PrintPage += (_, e) =>
        {
            using var bmp = ToBitmap(labels[next]);
            e.Graphics!.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(bmp, new Rectangle(0, 0, paper.Width, paper.Height));
            next++;
            e.HasMorePages = next < labels.Count;
        };
        doc.Print();
    }

    Bitmap ToBitmap(MonoBitmap m)
    {
        var bmp = new Bitmap(m.Width, m.Height, PixelFormat.Format32bppRgb);
        bmp.SetResolution(o.Label.Dpi, o.Label.Dpi);
        var data = bmp.LockBits(new Rectangle(0, 0, m.Width, m.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            var row = new int[m.Width];
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                    row[x] = m.IsBlack(x, y) ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, m.Width);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}

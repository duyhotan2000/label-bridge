using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LabelBridge;

/// <summary>Tray icons drawn in code: a label printer with a status dot.</summary>
static class Icons
{
    public static readonly Icon Running = Create(Color.FromArgb(46, 160, 67));
    public static readonly Icon Stopped = Create(Color.FromArgb(140, 140, 140));
    public static readonly Icon Pending = Create(Color.FromArgb(230, 162, 30));

    static Icon Create(Color dot)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(40, 44, 52));
            using var paper = new SolidBrush(Color.White);
            using var outline = new Pen(Color.FromArgb(40, 44, 52), 2);
            g.FillRectangle(paper, 8, 2, 14, 10);           // label coming out the top
            g.DrawRectangle(outline, 8, 2, 14, 10);
            g.FillRectangle(body, 2, 11, 26, 13);          // printer body
            g.FillRectangle(paper, 7, 20, 16, 8);          // label coming out the front
            g.DrawRectangle(outline, 7, 20, 16, 8);
            using var dotBrush = new SolidBrush(dot);
            using var ring = new Pen(Color.White, 2);
            g.FillEllipse(dotBrush, 18, 16, 14, 14);
            g.DrawEllipse(ring, 18, 16, 14, 14);
        }
        var handle = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}

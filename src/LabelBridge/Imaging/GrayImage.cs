namespace LabelBridge.Imaging;

/// <summary>8-bit grayscale image, row-major, 0 = black, 255 = white.</summary>
public sealed record GrayImage(int Width, int Height, int Dpi, byte[] Pixels)
{
    public static GrayImage White(int width, int height, int dpi)
    {
        var px = new byte[width * height];
        Array.Fill(px, (byte)255);
        return new GrayImage(width, height, dpi, px);
    }

    public byte this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }
}

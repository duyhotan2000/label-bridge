namespace LabelBridge.Imaging;

/// <summary>1-bit image: bit = 1 means black, MSB = leftmost pixel, rows padded to whole bytes.</summary>
public sealed record MonoBitmap(int Width, int Height, byte[] Bits)
{
    public int WidthBytes => (Width + 7) / 8;

    public bool IsBlack(int x, int y) => (Bits[y * WidthBytes + x / 8] & (0x80 >> (x % 8))) != 0;

    public GrayImage ToGray(int dpi)
    {
        var img = GrayImage.White(Width, Height, dpi);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (IsBlack(x, y))
                    img[x, y] = 0;
        return img;
    }
}

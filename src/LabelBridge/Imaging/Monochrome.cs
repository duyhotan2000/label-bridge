namespace LabelBridge.Imaging;

public static class Monochrome
{
    /// <summary>Pixels darker than <paramref name="threshold"/> become black. No dithering, so barcodes stay crisp.</summary>
    public static MonoBitmap Pack(GrayImage img, byte threshold)
    {
        var widthBytes = (img.Width + 7) / 8;
        var bits = new byte[widthBytes * img.Height];
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (img[x, y] < threshold)
                    bits[y * widthBytes + x / 8] |= (byte)(0x80 >> (x % 8));
        return new MonoBitmap(img.Width, img.Height, bits);
    }
}

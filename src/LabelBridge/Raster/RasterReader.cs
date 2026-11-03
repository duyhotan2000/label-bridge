using LabelBridge.Imaging;

namespace LabelBridge.Raster;

public static class RasterReader
{
    public static IEnumerable<GrayImage> ReadPages(Stream s, string mimeType) => mimeType switch
    {
        "image/urf" => UrfDecoder.Decode(s),
        "image/pwg-raster" => PwgDecoder.Decode(s),
        _ => throw new RasterFormatException($"Unsupported document format {mimeType}"),
    };
}

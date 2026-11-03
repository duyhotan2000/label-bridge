using LabelBridge.Config;
using LabelBridge.Jobs;

namespace LabelBridge.Ipp;

/// <summary>What the printer tells iOS about itself (Get-Printer-Attributes).</summary>
public static class PrinterAttributes
{
    public static readonly string[] DocumentFormats = ["image/urf", "image/pwg-raster"];

    public static readonly short[] Operations =
    [
        IppOp.PrintJob, IppOp.ValidateJob, IppOp.CancelJob,
        IppOp.GetJobAttributes, IppOp.GetJobs, IppOp.GetPrinterAttributes,
    ];

    public static string[] UrfSupported(BridgeOptions o) =>
        ["V1.4", "CP1", "W8", "SRGB24", "RS" + string.Join("-", o.AdvertisedResolutions), "IS1", "MT1"];

    public static IppGroup Build(BridgeOptions o, string printerUri, Guid uuid, JobStore store, TimeSpan uptime)
    {
        var g = new IppGroup(IppTag.PrinterAttributes);
        void Add(string name, IppTag tag, params object[] values) =>
            g.Attributes.Add(new IppAttribute(name, [.. values.Select(v => new IppValue(tag, v))]));

        Add("charset-configured", IppTag.Charset, "utf-8");
        Add("charset-supported", IppTag.Charset, "utf-8");
        Add("natural-language-configured", IppTag.NaturalLanguage, "en");
        Add("generated-natural-language-supported", IppTag.NaturalLanguage, "en");
        Add("ipp-versions-supported", IppTag.Keyword, "1.1", "2.0");
        Add("operations-supported", IppTag.Enum, [.. Operations.Select(op => (object)(int)op)]);

        Add("printer-uri-supported", IppTag.Uri, printerUri);
        Add("uri-security-supported", IppTag.Keyword, "none");
        Add("uri-authentication-supported", IppTag.Keyword, "none");
        Add("printer-name", IppTag.NameWithoutLanguage, o.PrinterName);
        Add("printer-info", IppTag.TextWithoutLanguage, o.PrinterName);
        Add("printer-make-and-model", IppTag.TextWithoutLanguage, "Xprinter XP-470B (LabelBridge)");
        Add("printer-uuid", IppTag.Uri, $"urn:uuid:{uuid}");
        Add("printer-location", IppTag.TextWithoutLanguage, "");
        var web = new UriBuilder(printerUri) { Scheme = "http", Path = "/" };
        Add("printer-more-info", IppTag.Uri, web.Uri.ToString());

        var queued = store.All().Count(j => !j.IsFinished);
        Add("printer-state", IppTag.Enum, store.IsBusy ? 4 : 3);
        Add("printer-state-reasons", IppTag.Keyword, "none");
        Add("printer-is-accepting-jobs", IppTag.Boolean, true);
        Add("queued-job-count", IppTag.Integer, queued);
        Add("printer-up-time", IppTag.Integer, Math.Max(1, (int)uptime.TotalSeconds));

        Add("document-format-supported", IppTag.MimeMediaType, DocumentFormats);
        Add("document-format-default", IppTag.MimeMediaType, "image/urf");
        Add("pdl-override-supported", IppTag.Keyword, "attempted");
        Add("compression-supported", IppTag.Keyword, "none");

        Add("urf-supported", IppTag.Keyword, UrfSupported(o));
        var resolutions = o.AdvertisedResolutions.Select(r => (object)new IppResolution(r, r)).ToArray();
        Add("pwg-raster-document-resolution-supported", IppTag.Resolution, resolutions);
        Add("pwg-raster-document-type-supported", IppTag.Keyword, "sgray_8", "srgb_8");
        Add("printer-resolution-supported", IppTag.Resolution, resolutions);
        Add("printer-resolution-default", IppTag.Resolution, resolutions[0]);

        Add("print-color-mode-supported", IppTag.Keyword, "monochrome");
        Add("print-color-mode-default", IppTag.Keyword, "monochrome");
        Add("color-supported", IppTag.Boolean, false);
        Add("copies-supported", IppTag.RangeOfInteger, new IppRange(1, 1));
        Add("copies-default", IppTag.Integer, 1);
        Add("sides-supported", IppTag.Keyword, "one-sided");
        Add("sides-default", IppTag.Keyword, "one-sided");
        Add("orientation-requested-supported", IppTag.Enum, 3);
        Add("orientation-requested-default", IppTag.Enum, 3);
        Add("print-quality-supported", IppTag.Enum, 4);
        Add("print-quality-default", IppTag.Enum, 4);

        Add("media-supported", IppTag.Keyword, o.Media.Name);
        Add("media-default", IppTag.Keyword, o.Media.Name);
        Add("media-ready", IppTag.Keyword, o.Media.Name);
        Add("media-col-default", IppTag.BegCollection, MediaCol(o));
        Add("media-col-ready", IppTag.BegCollection, MediaCol(o));
        Add("media-col-database", IppTag.BegCollection, MediaCol(o));
        foreach (var side in new[] { "bottom", "left", "right", "top" })
            Add($"media-{side}-margin-supported", IppTag.Integer, 0);
        Add("media-source-supported", IppTag.Keyword, "main");
        Add("media-type-supported", IppTag.Keyword, "labels");

        Add("printer-kind", IppTag.Keyword, "labels");
        Add("multiple-document-jobs-supported", IppTag.Boolean, false);
        return g;
    }

    static IppCollection MediaCol(BridgeOptions o)
    {
        var size = new IppCollection
        {
            new("x-dimension", new IppValue(IppTag.Integer, o.Media.WidthHundredthsMm)),
            new("y-dimension", new IppValue(IppTag.Integer, o.Media.HeightHundredthsMm)),
        };
        var col = new IppCollection { new("media-size", new IppValue(IppTag.BegCollection, size)) };
        foreach (var side in new[] { "bottom", "left", "right", "top" })
            col.Add(new IppAttribute($"media-{side}-margin", new IppValue(IppTag.Integer, 0)));
        col.Add(new IppAttribute("media-source", new IppValue(IppTag.Keyword, "main")));
        col.Add(new IppAttribute("media-type", new IppValue(IppTag.Keyword, "labels")));
        return col;
    }
}

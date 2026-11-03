namespace LabelBridge.Config;

/// <summary>All settings, bound from the "Bridge" section of appsettings.json.</summary>
public sealed class BridgeOptions
{
    /// <summary>Name shown in the iPhone print dialog.</summary>
    public string PrinterName { get; set; } = "XP-470B Label";
    public int IppPort { get; set; } = 631;
    /// <summary>tspl | windows-driver | file</summary>
    public string Output { get; set; } = "tspl";
    public string PrinterHost { get; set; } = "192.168.1.100";
    public int PrinterPort { get; set; } = 9100;
    public int ConnectTimeoutMs { get; set; } = 5000;
    /// <summary>Max time to send one job to the printer before the job is aborted.</summary>
    public int WriteTimeoutMs { get; set; } = 30000;
    public string WindowsPrinterName { get; set; } = "Xprinter XP-470B";
    /// <summary>Relative to the exe directory.</summary>
    public string OutputDirectory { get; set; } = "jobs";
    public bool SaveJobs { get; set; } = false;
    /// <summary>Tests only.</summary>
    public bool DisableBonjour { get; set; } = false;
    public int[] AdvertisedResolutions { get; set; } = [203, 300];
    public LabelOptions Label { get; set; } = new();
    public TsplOptions Tspl { get; set; } = new();
    public MediaOptions Media { get; set; } = new();
}

/// <summary>The physical label.</summary>
public sealed class LabelOptions
{
    public double WidthMm { get; set; } = 100;
    public double HeightMm { get; set; } = 150;
    public int Dpi { get; set; } = 203;
    /// <summary>fit | crop-center | stretch</summary>
    public string FitMode { get; set; } = "fit";
    /// <summary>Gray values below this become black.</summary>
    public byte Threshold { get; set; } = 160;

    /// <summary>Thermal printers sold as "203 dpi" are exactly 8 dots/mm (203.2 dpi).</summary>
    public double DotsPerMm => Math.Round(Dpi / 25.4, 1);
    public int WidthDots => (int)Math.Round(WidthMm * DotsPerMm);
    public int HeightDots => (int)Math.Round(HeightMm * DotsPerMm);
}

public sealed class TsplOptions
{
    public double GapMm { get; set; } = 2;
    public int Density { get; set; } = 8;
    public int Speed { get; set; } = 4;
    public int Direction { get; set; } = 1;
    /// <summary>false: a 0 bit prints a black dot (TSPL default).</summary>
    public bool InvertBitmap { get; set; } = false;
}

/// <summary>The page size advertised to iOS (Shopee AWB pages are A6).</summary>
public sealed class MediaOptions
{
    public string Name { get; set; } = "iso_a6_105x148mm";
    public int WidthHundredthsMm { get; set; } = 10500;
    public int HeightHundredthsMm { get; set; } = 14800;
}

using LabelBridge.Config;

namespace LabelBridge.Output;

public static class OutputFactory
{
    public static ILabelOutput Create(BridgeOptions o, string baseDir) => o.Output switch
    {
        "tspl" => new TsplOutput(o),
        "file" => new FileOutput(o.JobsDirectory(baseDir), o.Label.Dpi),
        "windows-driver" when OperatingSystem.IsWindows() => new WindowsDriverOutput(o),
        "windows-driver" => throw new PlatformNotSupportedException("windows-driver output only works on Windows"),
        _ => throw new ArgumentException($"Unknown output mode '{o.Output}' (use tspl, windows-driver or file)"),
    };
}

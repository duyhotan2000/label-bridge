using System.Reflection;

namespace LabelBridge;

/// <summary>The service, tray and default settings embedded in the setup exe by build.sh.</summary>
static class Payload
{
    public const string ServiceExe = "payload/LabelBridge.exe";
    public const string TrayExe = "payload/LabelBridge.Tray.exe";
    public const string Settings = "payload/appsettings.json";

    public static bool IsPresent => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ServiceExe) is not null;

    public static long TotalBytes => new[] { ServiceExe, TrayExe, Settings }.Sum(n => Open(n).Length);

    public static void ExtractTo(string resource, string path)
    {
        using var src = Open(resource);
        using var dst = File.Create(path);
        src.CopyTo(dst);
    }

    static Stream Open(string resource) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException($"Setup is missing {resource}; rebuild it with build.sh.");
}

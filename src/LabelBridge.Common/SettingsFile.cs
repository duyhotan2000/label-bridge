using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LabelBridge.Common;

/// <summary>The three settings the tray and installer manage.</summary>
public sealed record PrinterSettings(string Host, int Port, string Name)
{
    public static readonly PrinterSettings Defaults = new("192.168.1.100", 9100, "LabelBridge Printer");
}

/// <summary>
/// Reads and updates Bridge:PrinterHost/PrinterPort/PrinterName in appsettings.json,
/// leaving every other key as it is.
/// </summary>
public static class SettingsFile
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static PrinterSettings? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        var bridge = Load(path)["Bridge"];
        var d = PrinterSettings.Defaults;
        return new PrinterSettings(
            (string?)bridge?["PrinterHost"] ?? d.Host,
            bridge?["PrinterPort"] is JsonValue port ? port.GetValue<int>() : d.Port,
            (string?)bridge?["PrinterName"] ?? d.Name);
    }

    /// <summary>The service's IPP/status-page port (default 631).</summary>
    public static int IppPort(string path) =>
        File.Exists(path) && Load(path)["Bridge"]?["IppPort"] is JsonValue v ? v.GetValue<int>() : 631;

    public static string? OutputMode(string path) =>
        File.Exists(path) ? (string?)Load(path)["Bridge"]?["Output"] : null;

    public static void Update(string path, PrinterSettings s)
    {
        var root = File.Exists(path) ? Load(path) : new JsonObject();
        if (root["Bridge"] is not JsonObject bridge)
        {
            bridge = new JsonObject();
            root["Bridge"] = bridge;
        }
        bridge["PrinterHost"] = s.Host;
        bridge["PrinterPort"] = s.Port;
        bridge["PrinterName"] = s.Name;
        File.WriteAllText(path, root.ToJsonString(Indented), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static JsonObject Load(string path)
    {
        // PowerShell's Set-Content -Encoding UTF8 writes a BOM; File.ReadAllText strips it.
        var text = File.ReadAllText(path, Encoding.UTF8);
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
               ?? throw new InvalidDataException($"{path} is not a JSON object");
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LabelBridge.Common;

/// <summary>Validation shared by the Settings window and the installer. Each method returns null when valid.</summary>
public static partial class SettingsValidator
{
    public static string? ValidateHost(string host)
    {
        host = host.Trim();
        if (host.Length == 0)
            return "Printer IP is required.";
        if (DottedQuad().IsMatch(host))
            return IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
                ? null
                : "Printer IP is not a valid IPv4 address.";
        return HostName().IsMatch(host) && host.Length <= 253 ? null : "Printer IP must be an IPv4 address or host name.";
    }

    public static string? ValidatePort(string port) =>
        int.TryParse(port.Trim(), out var p) && p is >= 1 and <= 65535 ? null : "Port must be a number from 1 to 65535.";

    /// <summary>The AirPrint (DNS-SD instance) name is limited to 63 UTF-8 bytes.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "AirPrint name is required.";
        if (name.Any(char.IsControl))
            return "AirPrint name cannot contain control characters.";
        return Encoding.UTF8.GetByteCount(name.Trim()) <= 63 ? null : "AirPrint name is too long (max 63 bytes).";
    }

    public static IReadOnlyList<string> Validate(string host, string port, string name) =>
        [.. new[] { ValidateHost(host), ValidatePort(port), ValidateName(name) }.OfType<string>()];

    [GeneratedRegex(@"^\d+(\.\d+){3}$")]
    private static partial Regex DottedQuad();

    [GeneratedRegex(@"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$")]
    private static partial Regex HostName();
}

using System.Text.RegularExpressions;
using LabelBridge.Config;
using LabelBridge.Ipp;
using Makaretu.Dns;

namespace LabelBridge.Discovery;

/// <summary>Announces the printer over mDNS as an AirPrint printer (_ipp._tcp with the _universal subtype).</summary>
public sealed partial class BonjourAdvertiser(BridgeOptions o, Guid uuid, ILogger<BonjourAdvertiser> log) : IHostedService, IDisposable
{
    MulticastService? _mdns;
    ServiceDiscovery? _sd;
    ServiceProfile? _profile;

    public static string HostName(string machineName) =>
        $"labelbridge-{NonDnsChars().Replace(machineName.ToLowerInvariant(), "-")}.local";

    public static IReadOnlyDictionary<string, string> BuildTxt(BridgeOptions o, Guid uuid, string hostName) =>
        new Dictionary<string, string>
        {
            ["txtvers"] = "1",
            ["qtotal"] = "1",
            ["rp"] = "ipp/print",
            ["ty"] = o.PrinterName,
            ["product"] = "(LabelBridge)",
            ["pdl"] = string.Join(",", PrinterAttributes.DocumentFormats),
            ["URF"] = string.Join(",", PrinterAttributes.UrfSupported(o)),
            ["Color"] = "F",
            ["Duplex"] = "F",
            ["UUID"] = uuid.ToString(),
            ["kind"] = "label",
            ["note"] = "",
            ["priority"] = "50",
            ["adminurl"] = $"http://{hostName}:{o.IppPort}/",
        };

    public static ServiceProfile BuildProfile(BridgeOptions o, Guid uuid, string hostName)
    {
        var profile = new ServiceProfile(o.PrinterName, "_ipp._tcp", (ushort)o.IppPort) { HostName = hostName };
        // The SRV record must point at our host name, and the address records must answer for it.
        foreach (var srv in profile.Resources.OfType<SRVRecord>())
            srv.Target = hostName;
        foreach (var a in profile.Resources.OfType<AddressRecord>())
            a.Name = hostName;
        profile.Subtypes.Add("_universal");
        foreach (var (key, value) in BuildTxt(o, uuid, hostName))
        {
            if (key == "txtvers")
                continue; // ServiceProfile already adds txtvers=1
            profile.AddProperty(key, value);
        }
        return profile;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var hostName = HostName(Environment.MachineName);
        _profile = BuildProfile(o, uuid, hostName);

        _mdns = new MulticastService();
        _sd = new ServiceDiscovery(_mdns);
        _mdns.Start();
        _sd.Advertise(_profile);
        _sd.Announce(_profile);
        log.LogInformation("Advertising '{Name}' as {Host}:{Port} over Bonjour", o.PrinterName, hostName, o.IppPort);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sd is not null && _profile is not null)
            _sd.Unadvertise(_profile);
        _mdns?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _sd?.Dispose();
        _mdns?.Dispose();
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NonDnsChars();
}

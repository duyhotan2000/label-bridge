using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using LabelBridge.Config;
using LabelBridge.Ipp;
using Makaretu.Dns;

namespace LabelBridge.Discovery;

/// <summary>
/// Announces the printer over mDNS as an AirPrint printer (_ipp._tcp with the _universal subtype).
/// Re-advertises when the PC's network addresses change (e.g. the service started before
/// Wi-Fi/DHCP was up) and re-announces periodically.
/// </summary>
public sealed partial class BonjourAdvertiser(BridgeOptions o, Guid uuid, ILogger<BonjourAdvertiser> log) : IHostedService, IDisposable
{
    static readonly TimeSpan ReannounceEvery = TimeSpan.FromMinutes(10);
    static readonly TimeSpan ChangeDebounce = TimeSpan.FromSeconds(3);

    readonly Lock _lock = new();
    readonly string _hostName = HostName(Environment.MachineName);
    MulticastService? _mdns;
    ServiceDiscovery? _sd;
    ServiceProfile? _profile;
    string _addressKey = "";
    Timer? _timer;

    public sealed record NicInfo(NetworkInterfaceType Type, OperationalStatus Status, bool HasGateway, IReadOnlyList<IPAddress> Addresses);

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

    /// <summary>
    /// IPv4 addresses of interfaces that are up and have a default gateway (the real LAN),
    /// skipping loopback, tunnels and link-local 169.254.x.x. Falls back to any up IPv4 address
    /// when no interface has a gateway. IPv6 is skipped because the IPP server listens on IPv4.
    /// </summary>
    public static IReadOnlyList<IPAddress> SelectAddresses(IEnumerable<NicInfo> nics)
    {
        var usable = nics
            .Where(n => n.Status == OperationalStatus.Up &&
                        n.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (n.HasGateway, Addresses: n.Addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                            !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .ToList()))
            .Where(n => n.Addresses.Count > 0)
            .ToList();
        var withGateway = usable.Where(n => n.HasGateway).SelectMany(n => n.Addresses).ToList();
        return withGateway.Count > 0 ? withGateway : [.. usable.SelectMany(n => n.Addresses)];
    }

    static IReadOnlyList<IPAddress> CurrentAddresses() =>
        SelectAddresses(NetworkInterface.GetAllNetworkInterfaces().Select(n =>
        {
            var props = n.GetIPProperties();
            return new NicInfo(n.NetworkInterfaceType, n.OperationalStatus,
                props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)),
                [.. props.UnicastAddresses.Select(u => u.Address)]);
        }));

    public static ServiceProfile BuildProfile(BridgeOptions o, Guid uuid, string hostName, IReadOnlyList<IPAddress> addresses)
    {
        var profile = new ServiceProfile(o.PrinterName, "_ipp._tcp", (ushort)o.IppPort, addresses) { HostName = hostName };
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
        _mdns = new MulticastService();
        _sd = new ServiceDiscovery(_mdns);
        _mdns.Start();
        Refresh();
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        _timer = new Timer(_ => Refresh(), null, ReannounceEvery, ReannounceEvery);
        return Task.CompletedTask;
    }

    void OnNetworkChanged(object? sender, EventArgs e) => _timer?.Change(ChangeDebounce, ReannounceEvery);

    /// <summary>Re-advertises if the addresses changed, otherwise re-announces.</summary>
    void Refresh()
    {
        lock (_lock)
        {
            if (_sd is null)
                return;
            try
            {
                var addresses = CurrentAddresses();
                var key = string.Join(",", addresses);
                if (_profile is null || key != _addressKey)
                {
                    if (_profile is not null)
                        _sd.Unadvertise(_profile);
                    _profile = BuildProfile(o, uuid, _hostName, addresses);
                    _addressKey = key;
                    _sd.Advertise(_profile);
                    if (addresses.Count == 0)
                        log.LogWarning("No LAN address yet; Bonjour will advertise once the network is up");
                    else
                        log.LogInformation("Advertising '{Name}' as {Host}:{Port} ({Addresses}) over Bonjour",
                            o.PrinterName, _hostName, o.IppPort, key);
                }
                if (addresses.Count > 0)
                    _sd.Announce(_profile);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Bonjour refresh failed");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _timer?.Dispose();
        lock (_lock)
        {
            if (_sd is not null && _profile is not null)
                _sd.Unadvertise(_profile);
            _mdns?.Stop();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _sd?.Dispose();
        _mdns?.Dispose();
    }

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex NonDnsChars();
}

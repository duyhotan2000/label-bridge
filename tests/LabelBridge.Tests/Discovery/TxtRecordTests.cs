using LabelBridge.Config;
using LabelBridge.Discovery;

namespace LabelBridge.Tests.Discovery;

public class TxtRecordTests
{
    [Fact]
    public void TxtRecord_HasAirPrintKeys()
    {
        var uuid = Guid.NewGuid();

        var txt = BonjourAdvertiser.BuildTxt(new BridgeOptions(), uuid, "labelbridge-pc.local");

        Assert.Equal("1", txt["txtvers"]);
        Assert.Equal("ipp/print", txt["rp"]);
        Assert.Contains("image/urf", txt["pdl"]);
        Assert.Contains("W8", txt["URF"]);
        Assert.Contains("RS203-300", txt["URF"]);
        Assert.Equal(uuid.ToString(), txt["UUID"]);
        Assert.Equal("XP-470B Label", txt["ty"]);
        Assert.Equal("F", txt["Color"]);
        Assert.Equal("http://labelbridge-pc.local:631/", txt["adminurl"]);
    }

    [Fact]
    public void HostName_IsDnsSafe()
    {
        Assert.Equal("labelbridge-my-pc-1.local", BonjourAdvertiser.HostName("My PC_1"));
    }

    [Fact]
    public void PrinterIdentity_PersistsUuid()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "printer-id.txt");

        var first = PrinterIdentity.LoadOrCreate(path);
        var second = PrinterIdentity.LoadOrCreate(path);

        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first);
    }
}

public class BonjourRecordTests
{
    [Fact]
    public async Task Profile_AnswersUniversalSubtypeWithSrvAndTxt()
    {
        const string host = "labelbridge-pc.local";
        var o = new LabelBridge.Config.BridgeOptions();
        var profile = BonjourAdvertiser.BuildProfile(o, Guid.NewGuid(), host, [System.Net.IPAddress.Parse("192.168.1.20")]);
        using var mdns = new Makaretu.Dns.MulticastService();
        using var sd = new Makaretu.Dns.ServiceDiscovery(mdns);
        sd.Advertise(profile);

        var ptr = await Resolve(sd, "_universal._sub._ipp._tcp.local", Makaretu.Dns.DnsType.PTR);
        var instance = Assert.Single(ptr.Answers.OfType<Makaretu.Dns.PTRRecord>()).DomainName;
        var srv = await Resolve(sd, instance.ToString(), Makaretu.Dns.DnsType.SRV);
        var txt = await Resolve(sd, instance.ToString(), Makaretu.Dns.DnsType.TXT);

        Assert.StartsWith("XP-470B Label", instance.Labels[0]);
        var srvRecord = Assert.Single(srv.Answers.OfType<Makaretu.Dns.SRVRecord>());
        Assert.Equal(host, srvRecord.Target.ToString());
        Assert.Equal(631, srvRecord.Port);
        var strings = txt.Answers.OfType<Makaretu.Dns.TXTRecord>().SelectMany(t => t.Strings).ToList();
        Assert.Contains("rp=ipp/print", strings);
        Assert.Contains("pdl=image/urf,image/pwg-raster", strings);
        Assert.Single(strings, s => s.StartsWith("txtvers="));
        Assert.All(profile.Resources.OfType<Makaretu.Dns.AddressRecord>(), a => Assert.Equal(host, a.Name!.ToString()));
    }

    static Task<Makaretu.Dns.Message> Resolve(Makaretu.Dns.ServiceDiscovery sd, string name, Makaretu.Dns.DnsType type)
    {
        var q = new Makaretu.Dns.Message();
        q.Questions.Add(new Makaretu.Dns.Question { Name = name, Type = type });
        return sd.NameServer.ResolveAsync(q);
    }
}

public class AddressSelectionTests
{
    static BonjourAdvertiser.NicInfo Nic(string ip, bool gateway = true,
        System.Net.NetworkInformation.NetworkInterfaceType type = System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
        System.Net.NetworkInformation.OperationalStatus status = System.Net.NetworkInformation.OperationalStatus.Up) =>
        new(type, status, gateway, [System.Net.IPAddress.Parse(ip)]);

    [Fact]
    public void SelectAddresses_PrefersLanIPv4WithGateway()
    {
        var picked = BonjourAdvertiser.SelectAddresses([
            Nic("192.168.1.20"),
            Nic("172.20.0.1", gateway: false),                  // Hyper-V / WSL virtual switch
            Nic("169.254.10.10"),                               // no DHCP yet
            Nic("fe80::1"),                                     // IPv6: server listens on IPv4 only
            Nic("127.0.0.1", type: System.Net.NetworkInformation.NetworkInterfaceType.Loopback),
            Nic("10.0.0.5", status: System.Net.NetworkInformation.OperationalStatus.Down),
        ]);

        Assert.Equal(["192.168.1.20"], picked.Select(a => a.ToString()));
    }

    [Fact]
    public void SelectAddresses_NoGatewayAnywhere_FallsBackToUpIPv4()
    {
        var picked = BonjourAdvertiser.SelectAddresses([Nic("192.168.0.9", gateway: false), Nic("169.254.1.1", gateway: false)]);

        Assert.Equal(["192.168.0.9"], picked.Select(a => a.ToString()));
    }

    [Fact]
    public void BuildProfile_AdvertisesOnlyGivenIPv4Addresses()
    {
        var profile = BonjourAdvertiser.BuildProfile(new LabelBridge.Config.BridgeOptions(), Guid.NewGuid(),
            "labelbridge-pc.local", [System.Net.IPAddress.Parse("192.168.1.20")]);

        var a = Assert.Single(profile.Resources.OfType<Makaretu.Dns.AddressRecord>());
        Assert.Equal("192.168.1.20", a.Address.ToString());
        Assert.Equal("labelbridge-pc.local", a.Name!.ToString());
    }
}

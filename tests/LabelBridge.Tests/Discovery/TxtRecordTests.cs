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
        var profile = BonjourAdvertiser.BuildProfile(o, Guid.NewGuid(), host);
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

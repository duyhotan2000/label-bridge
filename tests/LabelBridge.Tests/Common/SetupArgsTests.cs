using LabelBridge.Common;

namespace LabelBridge.Tests.Common;

public class SetupArgsTests
{
    static readonly PrinterSettings Existing = new("192.168.2.102", 9100, "XP-470B Label (Chicken)");

    [Fact]
    public void Parse_AllArgs()
    {
        var a = SetupArgs.Parse(["/SILENT", "/printer=192.168.2.102", "/port=9101", "/name=Shop"]);

        Assert.Equal(new SetupArgs(SetupMode.Install, true, "192.168.2.102", "9101", "Shop"), a);
    }

    [Fact]
    public void Parse_QuotedName()
    {
        Assert.Equal("XP-470B Label (Surface)", SetupArgs.Parse(["/name=\"XP-470B Label (Surface)\""]).Name);
    }

    [Fact]
    public void Parse_Uninstall()
    {
        var a = SetupArgs.Parse(["/uninstall", "/silent"]);

        Assert.Equal(SetupMode.Uninstall, a.Mode);
        Assert.True(a.Silent);
    }

    [Fact]
    public void Parse_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => SetupArgs.Parse(["/bogus"]));
    }

    [Fact]
    public void Resolve_FreshWithoutPrinter_Error()
    {
        var (settings, error) = SetupArgs.Parse(["/silent"]).Resolve(null);

        Assert.Null(settings);
        Assert.Contains("/printer", error);
    }

    [Fact]
    public void Resolve_FreshDefaults()
    {
        var (settings, error) = SetupArgs.Parse(["/silent", "/printer=192.168.2.102"]).Resolve(null);

        Assert.Null(error);
        Assert.Equal(new PrinterSettings("192.168.2.102", 9100, "LabelBridge Printer"), settings);
    }

    [Fact]
    public void Resolve_UpgradeWithoutArgs_KeepsExisting()
    {
        var (settings, error) = SetupArgs.Parse(["/silent"]).Resolve(Existing);

        Assert.Null(error);
        Assert.Equal(Existing, settings);
    }

    [Fact]
    public void Resolve_UpgradeOverridesOnlyGiven()
    {
        var (settings, _) = SetupArgs.Parse(["/silent", "/port=9200"]).Resolve(Existing);

        Assert.Equal(Existing with { Port = 9200 }, settings);
    }

    [Fact]
    public void Resolve_InvalidPort_Error()
    {
        var (settings, error) = SetupArgs.Parse(["/silent", "/port=0"]).Resolve(Existing);

        Assert.Null(settings);
        Assert.NotNull(error);
    }
}

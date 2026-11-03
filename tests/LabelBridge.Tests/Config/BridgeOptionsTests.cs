using LabelBridge.Config;
using Microsoft.Extensions.Configuration;

namespace LabelBridge.Tests.Config;

public class BridgeOptionsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var o = new BridgeOptions();
        Assert.Equal(800, o.Label.WidthDots);
        Assert.Equal(1200, o.Label.HeightDots);
        Assert.Equal("iso_a6_105x148mm", o.Media.Name);
        Assert.Equal("tspl", o.Output);
        Assert.Equal(9100, o.PrinterPort);
    }

    [Fact]
    public void Binds_FromAppSettingsJson()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();
        var o = config.GetSection("Bridge").Get<BridgeOptions>()!;
        Assert.Equal(8, o.Tspl.Density);
        Assert.Equal("fit", o.Label.FitMode);
    }
}

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

    [Fact]
    public void AdvertisedResolutions_FromConfig_ReplacesDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bridge:AdvertisedResolutions:0"] = "300" })
            .Build();

        var o = config.GetSection("Bridge").Get<BridgeOptions>()!;

        Assert.Equal(new[] { 300 }, o.Resolutions);
    }

    [Fact]
    public void AdvertisedResolutions_AppSettings_NoDuplicates()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        var o = config.GetSection("Bridge").Get<BridgeOptions>()!;

        Assert.Equal(new[] { 203, 300 }, o.Resolutions);
        Assert.Equal(new[] { 203, 300 }, new BridgeOptions().Resolutions);
    }
}

public class OutputDirectoryTests
{
    static readonly string Base = Path.Combine(Path.GetTempPath(), "lb-base");

    [Theory]
    [InlineData("jobs")]
    [InlineData("sub/dir")]
    public void JobsDirectory_RelativeInsideBase_Used(string dir)
    {
        var o = new LabelBridge.Config.BridgeOptions { OutputDirectory = dir };

        Assert.Equal(Path.GetFullPath(Path.Combine(Base, dir)), o.JobsDirectory(Base));
    }

    [Theory]
    [InlineData("/etc/evil")]
    [InlineData("C:\\Windows\\System32\\evil")]
    [InlineData("../outside")]
    [InlineData("jobs/../../outside")]
    public void JobsDirectory_OutsideBase_FallsBackToJobs(string dir)
    {
        var o = new LabelBridge.Config.BridgeOptions { OutputDirectory = dir };

        Assert.Equal(Path.GetFullPath(Path.Combine(Base, "jobs")), o.JobsDirectory(Base));
    }

    [Fact]
    public void JobsDirectory_BaseWithTrailingSeparator_KeepsRelativeDir()
    {
        var o = new LabelBridge.Config.BridgeOptions { OutputDirectory = "out" };

        Assert.Equal(Path.GetFullPath(Path.Combine(Base, "out")), o.JobsDirectory(Base + Path.DirectorySeparatorChar));
    }
}

namespace LabelBridge.Tests.Http;

public class TestPrintTests
{
    [Fact]
    public async Task TestPrint_FileMode_WritesLabelAndReturnsZero()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;

        var code = await TestPrint.RunAsync(["--Bridge:Output=file", $"--Bridge:OutputDirectory={dir}"]);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(dir, "test-label-label-01.png")));
    }

    [Fact]
    public async Task TestPrint_UnreachablePrinter_ReturnsOne()
    {
        var code = await TestPrint.RunAsync(["--Bridge:Output=tspl", "--Bridge:PrinterHost=127.0.0.1", "--Bridge:PrinterPort=1", "--Bridge:ConnectTimeoutMs=500"]);

        Assert.Equal(1, code);
    }
}

using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Output;

namespace LabelBridge.Tests.Output;

public class FileOutputTests
{
    [Fact]
    public async Task FileOutput_WritesOnePngPerLabel()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var label = new MonoBitmap(8, 1, [0xF0]);

        await new FileOutput(dir, 203).PrintAsync([label, label], "job-7", CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dir, "job-7-label-01.png")));
        Assert.True(File.Exists(Path.Combine(dir, "job-7-label-02.png")));
    }

    [Fact]
    public void Factory_UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => OutputFactory.Create(new BridgeOptions { Output = "laser" }, "/tmp"));
    }

    [Fact]
    public void Factory_FileMode_UsesOutputDirectoryUnderBaseDir()
    {
        var output = OutputFactory.Create(new BridgeOptions { Output = "file", OutputDirectory = "jobs" }, "/base");

        var file = Assert.IsType<FileOutput>(output);
        Assert.Equal(Path.Combine("/base", "jobs"), file.Directory);
    }
}

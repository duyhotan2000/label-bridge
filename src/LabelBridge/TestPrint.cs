using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Output;

namespace LabelBridge;

/// <summary>`LabelBridge.exe test-print`: prints a built-in test label without the iPhone.</summary>
public static class TestPrint
{
    public static async Task<int> RunAsync(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(baseDir, "appsettings.json"), optional: true)
            .AddCommandLine(args)
            .Build();
        var o = config.GetSection("Bridge").Get<BridgeOptions>() ?? new BridgeOptions();
        try
        {
            var label = Monochrome.Pack(TestLabel.Create(o.Label), o.Label.Threshold);
            await OutputFactory.Create(o, baseDir).PrintAsync([label], "test-label", CancellationToken.None);
            Console.WriteLine($"OK — test label sent via '{o.Output}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED — {ex.Message}");
            return 1;
        }
    }
}

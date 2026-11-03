using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Output;

namespace LabelBridge.Tests.Output;

public class TsplOutputTests
{
    [Fact]
    public async Task TsplOutput_SendsBytesToPrinter()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var o = new BridgeOptions { PrinterHost = "127.0.0.1", PrinterPort = port };
        var label = new MonoBitmap(16, 2, [0xFF, 0x00, 0x0F, 0xF0]);

        var receive = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var ms = new MemoryStream();
            await client.GetStream().CopyToAsync(ms);
            return ms.ToArray();
        });
        await new TsplOutput(o).PrintAsync([label], "job-1", CancellationToken.None);
        var received = await receive.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();

        Assert.Equal(TsplEncoder.Encode([label], o.Label, o.Tspl), received);
    }

    [Fact]
    public async Task TsplOutput_UnreachablePrinter_ThrowsWithinTimeout()
    {
        var o = new BridgeOptions { PrinterHost = "10.255.255.1", ConnectTimeoutMs = 500 };
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new TsplOutput(o).PrintAsync([new MonoBitmap(8, 1, [0])], "job-1", CancellationToken.None));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task TsplOutput_PrinterStopsReading_ThrowsWithinWriteTimeout()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync(); // accepts, never reads
        var o = new BridgeOptions { PrinterHost = "127.0.0.1", PrinterPort = port, WriteTimeoutMs = 1000 };
        var big = Enumerable.Range(0, 60).Select(_ => new MonoBitmap(800, 1200, new byte[100 * 1200])).ToList();
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAsync<IOException>(() => new TsplOutput(o).PrintAsync(big, "job-1", CancellationToken.None));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        (await accepted).Dispose();
        listener.Stop();
    }
}

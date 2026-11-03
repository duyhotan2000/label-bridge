using System.Net.Sockets;
using LabelBridge.Config;
using LabelBridge.Imaging;

namespace LabelBridge.Output;

/// <summary>Sends raw TSPL to the printer's network port (9100).</summary>
public sealed class TsplOutput(BridgeOptions o) : ILabelOutput
{
    public async Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct)
    {
        var data = TsplEncoder.Encode(labels, o.Label, o.Tspl);
        using var client = new TcpClient();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(o.ConnectTimeoutMs);
            try
            {
                await client.ConnectAsync(o.PrinterHost, o.PrinterPort, connect.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"Printer {o.PrinterHost}:{o.PrinterPort} did not answer within {o.ConnectTimeoutMs} ms");
            }
        }
        var stream = client.GetStream();
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
        client.Client.Shutdown(SocketShutdown.Send);
    }
}

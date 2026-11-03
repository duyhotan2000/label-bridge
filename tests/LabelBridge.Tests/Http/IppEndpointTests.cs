using System.Net;
using System.Net.Http.Headers;
using LabelBridge.Ipp;
using LabelBridge.Tests.Ipp;
using LabelBridge.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LabelBridge.Tests.Http;

public sealed class IppEndpointTests : IAsyncLifetime
{
    readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    WebApplication _app = null!;
    HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _app = AppBuilder.Build([
            "--Bridge:IppPort=0", "--Bridge:Output=file", "--Bridge:DisableBonjour=true",
            $"--Bridge:OutputDirectory={_dir}",
        ]);
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var port = new Uri(address.Replace("0.0.0.0", "localhost").Replace("[::]", "localhost")).Port;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    async Task<(HttpResponseMessage Http, IppMessage? Ipp)> Post(IppMessage msg, byte[]? document = null, bool chunked = false)
    {
        var body = IppWriter.Write(msg).Concat(document ?? []).ToArray();
        HttpContent content = chunked
            ? new StreamContent(new NonSeekableStream(body))
            : new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        var resp = await _http.PostAsync("/ipp/print", content);
        IppMessage? ipp = null;
        if (resp.IsSuccessStatusCode)
            ipp = IppReader.Read(new MemoryStream(await resp.Content.ReadAsByteArrayAsync()));
        return (resp, ipp);
    }

    [Fact]
    public async Task Http_GetPrinterAttributes_RoundTrip()
    {
        var (http, ipp) = await Post(IppOperationsTests.Request(IppOp.GetPrinterAttributes));

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        Assert.Equal("application/ipp", http.Content.Headers.ContentType!.MediaType);
        Assert.Equal(IppStatus.Ok, ipp!.Code);
        Assert.NotNull(ipp.Groups.Single(g => g.Tag == IppTag.PrinterAttributes).Find("printer-name"));
    }

    [Fact]
    public async Task Http_PrintJobUrf_WritesLabelPngs()
    {
        var px = RasterEncoders.GrayPixels(840, 1184, (x, y) => x is > 100 and < 700 && y is > 100 and < 1000 ? (byte)0 : (byte)255);
        var urf = RasterEncoders.EncodeUrf(new RasterPage(px, 840, 1184, 203), new RasterPage(px, 840, 1184, 203));
        var print = IppOperationsTests.Request(IppOp.PrintJob,
            new IppAttribute("document-format", new IppValue(IppTag.MimeMediaType, "image/urf")));

        var (_, ipp) = await Post(print, urf, chunked: true);
        Assert.Equal(IppStatus.Ok, ipp!.Code);
        var jobId = (int)ipp.Groups.Single(g => g.Tag == IppTag.JobAttributes).Find("job-id")!.Values[0].Value!;

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var (_, state) = await Post(IppOperationsTests.Request(IppOp.GetJobAttributes,
                new IppAttribute("job-id", new IppValue(IppTag.Integer, jobId))));
            var s = (int)state!.Groups.Single(g => g.Tag == IppTag.JobAttributes).Find("job-state")!.Values[0].Value!;
            if (s == 9) break;
            Assert.True(DateTime.UtcNow < deadline, $"job state stuck at {s}");
            await Task.Delay(50);
        }

        Assert.True(File.Exists(Path.Combine(_dir, $"job-{jobId}-label-01.png")));
        Assert.True(File.Exists(Path.Combine(_dir, $"job-{jobId}-label-02.png")));
    }

    [Fact]
    public async Task Http_GarbageBody_Returns400Status()
    {
        var content = new ByteArrayContent([1, 2, 3, 4, 5]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");

        var resp = await _http.PostAsync("/ipp/print", content);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var (http, _) = await Post(IppOperationsTests.Request(IppOp.GetPrinterAttributes));
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
    }

    [Fact]
    public async Task Http_StatusPage_ShowsPrinterName()
    {
        var text = await _http.GetStringAsync("/");

        Assert.Contains("XP-470B Label", text);
        Assert.Contains("file", text);
    }

    /// <summary>Forces HttpClient to send the body chunked (no Content-Length), like iOS does.</summary>
    sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}

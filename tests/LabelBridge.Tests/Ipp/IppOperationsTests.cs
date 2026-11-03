using LabelBridge.Config;
using LabelBridge.Ipp;
using LabelBridge.Jobs;
using LabelBridge.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelBridge.Tests.Ipp;

public class IppOperationsTests
{
    const string Uri = "ipp://localhost:631/ipp/print";
    readonly BridgeOptions _o = new();
    readonly JobStore _store = new();
    readonly IppOperations _ops;

    public IppOperationsTests()
    {
        var processor = new JobProcessor(_store, () => new FakeOutput(), _o,
            NullLogger<JobProcessor>.Instance, Path.GetTempPath());
        _ops = new IppOperations(_o, _store, processor, Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    internal static IppMessage Request(short op, params IppAttribute[] extra)
    {
        var m = new IppMessage { Code = op, RequestId = 9 };
        var g = m.Group(IppTag.OperationAttributes);
        g.Attributes.Add(new IppAttribute("attributes-charset", new IppValue(IppTag.Charset, "utf-8")));
        g.Attributes.Add(new IppAttribute("attributes-natural-language", new IppValue(IppTag.NaturalLanguage, "en")));
        g.Attributes.Add(new IppAttribute("printer-uri", new IppValue(IppTag.Uri, Uri)));
        g.Attributes.AddRange(extra);
        return m;
    }

    static IppGroup Printer(IppMessage m) => m.Groups.Single(g => g.Tag == IppTag.PrinterAttributes);

    static object?[] Values(IppGroup g, string name) => g.Find(name)!.Values.Select(v => v.Value).ToArray();

    [Fact]
    public void GetPrinterAttributes_All_ContainsAirPrintEssentials()
    {
        var resp = _ops.Handle(Request(IppOp.GetPrinterAttributes), Stream.Null, Uri);

        Assert.Equal(IppStatus.Ok, resp.Code);
        Assert.Equal(9, resp.RequestId);
        var p = Printer(resp);
        Assert.Equal(new object[] { "image/urf", "image/pwg-raster" }, Values(p, "document-format-supported"));
        Assert.Contains("RS203-300", Values(p, "urf-supported"));
        Assert.Equal("iso_a6_105x148mm", Values(p, "media-default").Single());
        Assert.Equal(0, Values(p, "media-top-margin-supported").Single());
        Assert.StartsWith("urn:uuid:", (string)Values(p, "printer-uuid").Single()!);
        Assert.Equal(Uri, Values(p, "printer-uri-supported").Single());
        Assert.NotNull(p.Find("media-col-database"));
        Assert.Equal("utf-8", resp.FindOperation("attributes-charset")!.Values[0].Value);
    }

    [Fact]
    public void GetPrinterAttributes_Requested_ReturnsOnlyThose()
    {
        var req = Request(IppOp.GetPrinterAttributes, new IppAttribute("requested-attributes",
            new IppValue(IppTag.Keyword, "printer-state"), new IppValue(IppTag.Keyword, "printer-name")));

        var p = Printer(_ops.Handle(req, Stream.Null, Uri));

        Assert.Equal(new[] { "printer-name", "printer-state" }, p.Attributes.Select(a => a.Name).Order());
        Assert.Equal(3, Values(p, "printer-state").Single());
    }

    [Fact]
    public void PrintJob_Pdf_RejectedFormat()
    {
        var req = Request(IppOp.PrintJob, new IppAttribute("document-format", new IppValue(IppTag.MimeMediaType, "application/pdf")));

        var resp = _ops.Handle(req, new MemoryStream([1, 2, 3]), Uri);

        Assert.Equal(IppStatus.DocumentFormatNotSupported, resp.Code);
        Assert.Empty(_store.All());
    }

    [Fact]
    public void PrintJob_Urf_ReturnsJobIdAndEnqueues()
    {
        var req = Request(IppOp.PrintJob,
            new IppAttribute("job-name", new IppValue(IppTag.NameWithoutLanguage, "awb")),
            new IppAttribute("document-format", new IppValue(IppTag.MimeMediaType, "image/urf")));

        var resp = _ops.Handle(req, new MemoryStream([1, 2, 3]), Uri);

        Assert.Equal(IppStatus.Ok, resp.Code);
        var job = resp.Groups.Single(g => g.Tag == IppTag.JobAttributes);
        Assert.Equal(1, Values(job, "job-id").Single());
        Assert.Equal($"{Uri}/1", Values(job, "job-uri").Single());
        var stored = Assert.Single(_store.All());
        Assert.Equal("image/urf", stored.Format);
        Assert.Equal("awb", stored.Name);
    }

    [Fact]
    public void PrintJob_OctetStream_TreatedAsUrf()
    {
        var req = Request(IppOp.PrintJob, new IppAttribute("document-format", new IppValue(IppTag.MimeMediaType, "application/octet-stream")));

        _ops.Handle(req, new MemoryStream([1]), Uri);

        Assert.Equal("image/urf", _store.All().Single().Format);
    }

    [Fact]
    public void ValidateJob_Urf_Ok()
    {
        var req = Request(IppOp.ValidateJob, new IppAttribute("document-format", new IppValue(IppTag.MimeMediaType, "image/urf")));

        Assert.Equal(IppStatus.Ok, _ops.Handle(req, Stream.Null, Uri).Code);
    }

    [Fact]
    public void GetJobAttributes_UnknownJob_NotFound()
    {
        var req = Request(IppOp.GetJobAttributes, new IppAttribute("job-id", new IppValue(IppTag.Integer, 99)));

        Assert.Equal(IppStatus.NotFound, _ops.Handle(req, Stream.Null, Uri).Code);
    }

    [Fact]
    public void GetJobAttributes_ReturnsState()
    {
        _ops.Handle(Request(IppOp.PrintJob), new MemoryStream([1]), Uri);
        var req = Request(IppOp.GetJobAttributes, new IppAttribute("job-id", new IppValue(IppTag.Integer, 1)));

        var resp = _ops.Handle(req, Stream.Null, Uri);

        var job = resp.Groups.Single(g => g.Tag == IppTag.JobAttributes);
        Assert.Equal((int)JobState.Pending, Values(job, "job-state").Single());
    }

    [Fact]
    public void GetJobs_ListsJobs()
    {
        _ops.Handle(Request(IppOp.PrintJob), new MemoryStream([1]), Uri);
        _ops.Handle(Request(IppOp.PrintJob), new MemoryStream([1]), Uri);

        var resp = _ops.Handle(Request(IppOp.GetJobs), Stream.Null, Uri);

        Assert.Equal(2, resp.Groups.Count(g => g.Tag == IppTag.JobAttributes));
    }

    [Fact]
    public void CancelJob_Pending_Canceled()
    {
        _ops.Handle(Request(IppOp.PrintJob), new MemoryStream([1]), Uri);

        var resp = _ops.Handle(Request(IppOp.CancelJob, new IppAttribute("job-id", new IppValue(IppTag.Integer, 1))), Stream.Null, Uri);

        Assert.Equal(IppStatus.Ok, resp.Code);
        Assert.Equal(JobState.Canceled, _store.Get(1)!.State);
    }

    [Fact]
    public void UnknownOperation_NotSupported()
    {
        Assert.Equal(IppStatus.OperationNotSupported, _ops.Handle(Request(0x003C), Stream.Null, Uri).Code);
    }

    [Fact]
    public void MissingCharset_BadRequest()
    {
        var req = Request(IppOp.GetPrinterAttributes);
        req.Groups[0].Attributes.RemoveAt(0);

        Assert.Equal(IppStatus.BadRequest, _ops.Handle(req, Stream.Null, Uri).Code);
    }
}

using LabelBridge.Config;
using LabelBridge.Jobs;

namespace LabelBridge.Ipp;

/// <summary>Answers one IPP request.</summary>
public sealed class IppOperations(
    BridgeOptions o, JobStore store, JobProcessor processor, Guid uuid, DateTimeOffset startedAt)
{
    public IppMessage Handle(IppMessage request, Stream documentData, string printerUri)
    {
        var resp = new IppMessage { RequestId = request.RequestId };
        var op = resp.Group(IppTag.OperationAttributes);
        op.Attributes.Add(new IppAttribute("attributes-charset", new IppValue(IppTag.Charset, "utf-8")));
        op.Attributes.Add(new IppAttribute("attributes-natural-language", new IppValue(IppTag.NaturalLanguage, "en")));

        if (request.FindOperation("attributes-charset") is null || request.FindOperation("printer-uri") is null)
        {
            resp.Code = IppStatus.BadRequest;
            return resp;
        }

        resp.Code = request.Code switch
        {
            IppOp.GetPrinterAttributes => GetPrinterAttributes(request, resp, printerUri),
            IppOp.ValidateJob => CheckFormat(request, out _),
            IppOp.PrintJob => PrintJob(request, resp, documentData, printerUri),
            IppOp.GetJobAttributes => GetJobAttributes(request, resp, printerUri),
            IppOp.GetJobs => GetJobs(resp, printerUri),
            IppOp.CancelJob => CancelJob(request),
            _ => IppStatus.OperationNotSupported,
        };
        return resp;
    }

    short GetPrinterAttributes(IppMessage req, IppMessage resp, string printerUri)
    {
        var all = PrinterAttributes.Build(o, printerUri, uuid, store, DateTimeOffset.UtcNow - startedAt);
        var requested = req.FindOperation("requested-attributes")?.Values.Select(v => v.Value as string).ToHashSet();
        var g = resp.Group(IppTag.PrinterAttributes);
        foreach (var a in all.Attributes)
        {
            var include = requested is null || requested.Contains("all") || requested.Contains(a.Name) ||
                ((requested.Contains("printer-description") || requested.Contains("job-template")) &&
                 a.Name != "media-col-database");
            if (include)
                g.Attributes.Add(a);
        }
        return IppStatus.Ok;
    }

    short CheckFormat(IppMessage req, out string format)
    {
        format = req.FindOperation("document-format")?.Values[0].Value as string ?? "image/urf";
        if (format == "application/octet-stream")
            format = "image/urf";
        return PrinterAttributes.DocumentFormats.Contains(format) ? IppStatus.Ok : IppStatus.DocumentFormatNotSupported;
    }

    short PrintJob(IppMessage req, IppMessage resp, Stream documentData, string printerUri)
    {
        var status = CheckFormat(req, out var format);
        if (status != IppStatus.Ok)
            return status;
        var doc = new MemoryStream();
        documentData.CopyTo(doc);
        var name = req.FindOperation("job-name")?.Values[0].Value as string ?? "label";
        var user = req.FindOperation("requesting-user-name")?.Values[0].Value as string ?? "iPhone";
        var job = store.Add(name, user, format, doc.ToArray());
        processor.Enqueue(job);
        AddJob(resp, job, printerUri);
        return IppStatus.Ok;
    }

    short GetJobAttributes(IppMessage req, IppMessage resp, string printerUri)
    {
        if (FindJob(req) is not { } job)
            return IppStatus.NotFound;
        AddJob(resp, job, printerUri);
        return IppStatus.Ok;
    }

    short GetJobs(IppMessage resp, string printerUri)
    {
        foreach (var job in store.All())
            AddJob(resp, job, printerUri);
        return IppStatus.Ok;
    }

    short CancelJob(IppMessage req)
    {
        if (FindJob(req) is not { } job)
            return IppStatus.NotFound;
        if (job.State != JobState.Pending)
            return IppStatus.NotPossible;
        job.State = JobState.Canceled;
        job.Completed = DateTimeOffset.UtcNow;
        job.Document = [];
        return IppStatus.Ok;
    }

    PrintJob? FindJob(IppMessage req)
    {
        if (req.FindOperation("job-id")?.Values[0].Value is int id)
            return store.Get(id);
        // Some clients send only job-uri: ipp://host/ipp/print/<id>
        if (req.FindOperation("job-uri")?.Values[0].Value is string uri &&
            int.TryParse(uri[(uri.LastIndexOf('/') + 1)..], out var fromUri))
            return store.Get(fromUri);
        return null;
    }

    static void AddJob(IppMessage resp, PrintJob job, string printerUri)
    {
        var g = new IppGroup(IppTag.JobAttributes);
        resp.Groups.Add(g);
        var reason = job.State switch
        {
            JobState.Aborted => "aborted-by-system",
            JobState.Completed => "job-completed-successfully",
            JobState.Canceled => "job-canceled-by-user",
            _ => "none",
        };
        g.Attributes.Add(new IppAttribute("job-id", new IppValue(IppTag.Integer, job.Id)));
        g.Attributes.Add(new IppAttribute("job-uri", new IppValue(IppTag.Uri, $"{printerUri}/{job.Id}")));
        g.Attributes.Add(new IppAttribute("job-name", new IppValue(IppTag.NameWithoutLanguage, job.Name)));
        g.Attributes.Add(new IppAttribute("job-state", new IppValue(IppTag.Enum, (int)job.State)));
        g.Attributes.Add(new IppAttribute("job-state-reasons", new IppValue(IppTag.Keyword, reason)));
        g.Attributes.Add(new IppAttribute("job-printer-uri", new IppValue(IppTag.Uri, printerUri)));
    }
}

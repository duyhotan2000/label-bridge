using System.Net;
using System.Text;
using LabelBridge.Config;
using LabelBridge.Discovery;
using LabelBridge.Ipp;
using LabelBridge.Jobs;
using LabelBridge.Output;
using Serilog;
using Serilog.Events;

namespace LabelBridge;

/// <summary>Builds the web host: IPP endpoint, job processor, Bonjour, logging, Windows service support.</summary>
public static class AppBuilder
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var baseDir = AppContext.BaseDirectory;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = baseDir });
        builder.Host.UseWindowsService(s => s.ServiceName = "LabelBridge");

        var o = builder.Configuration.GetSection("Bridge").Get<BridgeOptions>() ?? new BridgeOptions();
        var uuid = PrinterIdentity.LoadOrCreate(Path.Combine(baseDir, "printer-id.txt"));
        var saveDir = Path.Combine(baseDir, o.OutputDirectory);

        builder.Services.AddSerilog(lc =>
        {
            lc.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(baseDir, "logs", "labelbridge-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
            if (OperatingSystem.IsWindows())
                lc.WriteTo.EventLog("LabelBridge", manageEventSource: true, restrictedToMinimumLevel: LogEventLevel.Error);
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Any, o.IppPort);
            k.Limits.MaxRequestBodySize = 200 * 1024 * 1024;
        });

        builder.Services.AddSingleton(o);
        builder.Services.AddSingleton<JobStore>();
        builder.Services.AddSingleton(sp => new JobProcessor(
            sp.GetRequiredService<JobStore>(), () => OutputFactory.Create(o, baseDir), o,
            sp.GetRequiredService<ILogger<JobProcessor>>(), saveDir));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<JobProcessor>());
        builder.Services.AddSingleton(sp => new IppOperations(
            o, sp.GetRequiredService<JobStore>(), sp.GetRequiredService<JobProcessor>(), uuid, DateTimeOffset.UtcNow));
        if (!o.DisableBonjour)
            builder.Services.AddHostedService(sp => new BonjourAdvertiser(o, uuid, sp.GetRequiredService<ILogger<BonjourAdvertiser>>()));

        configure?.Invoke(builder);

        var app = builder.Build();
        app.MapPost("/ipp/print", HandleIpp);
        app.MapPost("/", HandleIpp);
        app.MapGet("/", (JobStore store) => Status(o, store));
        return app;
    }

    static async Task<IResult> HandleIpp(HttpContext ctx, IppOperations ops, BridgeOptions o, ILogger<IppOperations> log)
    {
        var body = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(body, ctx.RequestAborted);
        body.Position = 0;

        IppMessage request;
        try
        {
            request = IppReader.Read(body);
        }
        catch (IppFormatException ex)
        {
            log.LogWarning("Bad IPP request from {Remote}: {Error}", ctx.Connection.RemoteIpAddress, ex.Message);
            return Results.BadRequest();
        }

        var printerUri = $"ipp://{ctx.Request.Host}/ipp/print";
        var response = ops.Handle(request, body, printerUri);
        var jobId = response.Groups.FirstOrDefault(g => g.Tag == IppTag.JobAttributes)?.Find("job-id")?.Values[0].Value;
        log.LogInformation("IPP op 0x{Op:X4} request {RequestId} from {Remote} → status 0x{Status:X4}{Job}",
            request.Code, request.RequestId, ctx.Connection.RemoteIpAddress, response.Code,
            jobId is null ? "" : $" job {jobId}");
        if (o.SaveJobs)
            log.LogInformation("IPP request:\n{Request}\nIPP response:\n{Response}",
                IppDump.Format(request), IppDump.Format(response, isResponse: true));
        return Results.Bytes(IppWriter.Write(response), "application/ipp");
    }

    static string Status(BridgeOptions o, JobStore store)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"LabelBridge — {o.PrinterName}");
        sb.AppendLine($"Output: {o.Output}  Printer: {o.PrinterHost}:{o.PrinterPort}");
        sb.AppendLine();
        sb.AppendLine("Recent jobs:");
        foreach (var j in store.All().TakeLast(10).Reverse())
            sb.AppendLine($"  #{j.Id} {j.Created.ToLocalTime():yyyy-MM-dd HH:mm:ss} {j.State}{(j.Error is null ? "" : " — " + j.Error)}");
        return sb.ToString();
    }
}

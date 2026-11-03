using System.Threading.Channels;
using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Output;
using LabelBridge.Raster;

namespace LabelBridge.Jobs;

/// <summary>Prints jobs strictly one at a time, in arrival order.</summary>
public sealed class JobProcessor(
    JobStore store, Func<ILabelOutput> output, BridgeOptions o, ILogger<JobProcessor> log, string saveDir)
    : BackgroundService
{
    readonly Channel<PrintJob> _queue = Channel.CreateUnbounded<PrintJob>(new UnboundedChannelOptions { SingleReader = true });

    public JobStore Store => store;

    public void Enqueue(PrintJob job) => _queue.Writer.TryWrite(job);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
                await ProcessAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Decode → map → 1-bit → output. Sets the final job state; never throws.</summary>
    public async Task ProcessAsync(PrintJob job, CancellationToken ct)
    {
        if (job.State == JobState.Canceled)
            return;
        job.State = JobState.Processing;
        var jobName = $"job-{job.Id}";
        try
        {
            var labels = new List<MonoBitmap>();
            var n = 0;
            foreach (var page in RasterReader.ReadPages(new MemoryStream(job.Document), job.Format))
            {
                n++;
                var label = LabelMapper.Map(page, o.Label);
                var mono = Monochrome.Pack(label, o.Label.Threshold);
                labels.Add(mono);
                if (o.SaveJobs)
                {
                    Save(page, $"{jobName}-page-{n:D2}.png");
                    Save(mono.ToGray(o.Label.Dpi), $"{jobName}-label-{n:D2}.png");
                }
                log.LogInformation("Job {JobId} page {Page}: {Width}x{Height} at {Dpi} dpi",
                    job.Id, n, page.Width, page.Height, page.Dpi);
            }
            if (labels.Count == 0)
                throw new RasterFormatException("Document has no pages");

            await output().PrintAsync(labels, jobName, ct);
            job.State = JobState.Completed;
            log.LogInformation("Job {JobId} printed {Count} label(s)", job.Id, labels.Count);
        }
        catch (Exception ex)
        {
            job.State = JobState.Aborted;
            job.Error = ex.Message;
            log.LogError(ex, "Job {JobId} failed", job.Id);
        }
        finally
        {
            job.Completed = DateTimeOffset.UtcNow;
            job.Document = [];
        }
    }

    void Save(GrayImage img, string fileName)
    {
        Directory.CreateDirectory(saveDir);
        using var f = File.Create(Path.Combine(saveDir, fileName));
        PngWriter.Write(img, f);
    }
}

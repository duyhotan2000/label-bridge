using LabelBridge.Config;
using LabelBridge.Imaging;
using LabelBridge.Jobs;
using LabelBridge.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelBridge.Tests.Jobs;

public class JobProcessorTests
{
    readonly JobStore _store = new();
    readonly FakeOutput _output = new();
    readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    JobProcessor Processor(BridgeOptions? o = null) =>
        new(_store, () => _output, o ?? new BridgeOptions(), NullLogger<JobProcessor>.Instance, _dir);

    static byte[] A6Urf(int pages)
    {
        var px = RasterEncoders.GrayPixels(840, 1184, (x, y) => x is > 100 and < 700 && y is > 100 and < 1000 ? (byte)0 : (byte)255);
        return RasterEncoders.EncodeUrf(Enumerable.Repeat(new RasterPage(px, 840, 1184, 203), pages).ToArray());
    }

    [Fact]
    public async Task Processor_UrfTwoPages_TwoLabelsToOutput()
    {
        var job = _store.Add("awb", "user", "image/urf", A6Urf(2));

        await Processor().ProcessAsync(job, CancellationToken.None);

        Assert.Equal(JobState.Completed, job.State);
        var labels = Assert.Single(_output.Printed).Labels;
        Assert.Equal(2, labels.Count);
        Assert.All(labels, l => Assert.Equal((800, 1200), (l.Width, l.Height)));
        Assert.Empty(job.Document);
    }

    [Fact]
    public async Task Processor_OutputThrows_JobAbortedAndNextJobRuns()
    {
        _output.ThrowFor = name => name == "job-1" ? new IOException("printer offline") : null;
        var p = Processor();
        var j1 = _store.Add("a", "u", "image/urf", A6Urf(1));
        var j2 = _store.Add("b", "u", "image/urf", A6Urf(1));

        await p.ProcessAsync(j1, CancellationToken.None);
        await p.ProcessAsync(j2, CancellationToken.None);

        Assert.Equal(JobState.Aborted, j1.State);
        Assert.Contains("printer offline", j1.Error);
        Assert.Equal(JobState.Completed, j2.State);
    }

    [Fact]
    public async Task Processor_CorruptRaster_JobAborted()
    {
        var job = _store.Add("bad", "u", "image/urf", [1, 2, 3, 4, 5, 6, 7, 8, 9]);

        await Processor().ProcessAsync(job, CancellationToken.None);

        Assert.Equal(JobState.Aborted, job.State);
        Assert.Empty(_output.Printed);
    }

    [Fact]
    public async Task Processor_TwoJobs_ProcessedInOrder()
    {
        _output.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var p = Processor();
        await p.StartAsync(CancellationToken.None);
        var a = _store.Add("a", "u", "image/urf", A6Urf(1));
        var b = _store.Add("b", "u", "image/urf", A6Urf(1));
        p.Enqueue(a);
        p.Enqueue(b);

        await _output.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(JobState.Processing, a.State);
        Assert.Equal(JobState.Pending, b.State);
        Assert.True(_store.IsBusy);

        _output.Gate.SetResult();
        await WaitFor(() => b.State == JobState.Completed);
        await p.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "job-1", "job-2" }, _output.Printed.Select(x => x.JobName));
    }

    [Fact]
    public async Task Processor_SaveJobs_WritesPngs()
    {
        var job = _store.Add("awb", "u", "image/urf", A6Urf(1));

        await Processor(new BridgeOptions { SaveJobs = true }).ProcessAsync(job, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_dir, "job-1-page-01.png")));
        Assert.True(File.Exists(Path.Combine(_dir, "job-1-label-01.png")));
    }

    [Fact]
    public async Task Processor_CanceledJob_Skipped()
    {
        var job = _store.Add("awb", "u", "image/urf", A6Urf(1));
        job.State = JobState.Canceled;

        await Processor().ProcessAsync(job, CancellationToken.None);

        Assert.Empty(_output.Printed);
        Assert.Equal(JobState.Canceled, job.State);
    }

    [Fact]
    public void Store_KeepsLast50Jobs()
    {
        for (var i = 0; i < 60; i++)
        {
            var j = _store.Add("j", "u", "image/urf", [1]);
            j.State = JobState.Completed;
        }

        Assert.Equal(50, _store.All().Count);
        Assert.Null(_store.Get(1));
        Assert.NotNull(_store.Get(60));
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}

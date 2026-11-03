using LabelBridge.Imaging;
using LabelBridge.Output;

namespace LabelBridge.Tests.TestHelpers;

/// <summary>Records printed jobs; can be told to throw or to block until released.</summary>
public sealed class FakeOutput : ILabelOutput
{
    public List<(string JobName, IReadOnlyList<MonoBitmap> Labels)> Printed { get; } = [];
    public Func<string, Exception?> ThrowFor { get; set; } = _ => null;
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct)
    {
        Started.TrySetResult();
        if (Gate is not null)
            await Gate.Task.WaitAsync(ct);
        lock (Printed)
            Printed.Add((jobName, labels));
        if (ThrowFor(jobName) is { } ex)
            throw ex;
    }
}

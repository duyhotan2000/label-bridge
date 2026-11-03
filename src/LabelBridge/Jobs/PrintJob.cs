namespace LabelBridge.Jobs;

/// <summary>IPP job-state values.</summary>
public enum JobState
{
    Pending = 3,
    Processing = 5,
    Canceled = 7,
    Aborted = 8,
    Completed = 9,
}

public sealed class PrintJob
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string UserName { get; init; }
    public required string Format { get; init; }
    /// <summary>The raster document; emptied once the job finishes.</summary>
    public byte[] Document { get; set; } = [];
    public JobState State { get; set; } = JobState.Pending;
    public string? Error { get; set; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Completed { get; set; }

    public bool IsFinished => State is JobState.Completed or JobState.Aborted or JobState.Canceled;
}

using LabelBridge.Imaging;

namespace LabelBridge.Output;

/// <summary>Sends finished 1-bit labels somewhere. Throws on failure.</summary>
public interface ILabelOutput
{
    Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct);
}

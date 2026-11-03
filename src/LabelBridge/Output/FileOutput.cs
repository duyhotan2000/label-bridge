using LabelBridge.Imaging;

namespace LabelBridge.Output;

/// <summary>Writes labels as PNG files instead of printing (testing on the Mac).</summary>
public sealed class FileOutput(string directory, int dpi) : ILabelOutput
{
    public string Directory { get; } = directory;

    public async Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        for (var i = 0; i < labels.Count; i++)
        {
            await using var f = File.Create(Path.Combine(Directory, $"{jobName}-label-{i + 1:D2}.png"));
            PngWriter.Write(labels[i].ToGray(dpi), f);
        }
    }
}

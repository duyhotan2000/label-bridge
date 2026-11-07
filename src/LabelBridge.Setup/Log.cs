namespace LabelBridge;

/// <summary>Appends to %TEMP%\LabelBridge-Setup.log and forwards to an optional UI sink.</summary>
static class Log
{
    public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "LabelBridge-Setup.log");
    static readonly Lock Gate = new();

    public static Action<string>? Sink { get; set; }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line + Environment.NewLine); } catch { }
        }
        Sink?.Invoke(message);
    }
}

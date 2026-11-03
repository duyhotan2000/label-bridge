namespace LabelBridge.Discovery;

/// <summary>A stable printer UUID so the iPhone sees the same printer across restarts.</summary>
public static class PrinterIdentity
{
    public static Guid LoadOrCreate(string path)
    {
        if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing))
            return existing;
        var id = Guid.NewGuid();
        File.WriteAllText(path, id.ToString());
        return id;
    }
}

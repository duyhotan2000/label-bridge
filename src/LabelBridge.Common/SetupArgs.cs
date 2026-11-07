namespace LabelBridge.Common;

public enum SetupMode
{
    Install,
    Uninstall,
}

/// <summary>LabelBridge-Setup.exe [/silent] [/uninstall] [/printer=IP] [/port=N] [/name="AirPrint name"]</summary>
public sealed record SetupArgs(SetupMode Mode, bool Silent, string? Printer, string? Port, string? Name)
{
    public static SetupArgs Parse(string[] args)
    {
        var a = new SetupArgs(SetupMode.Install, false, null, null, null);
        foreach (var raw in args)
        {
            var eq = raw.IndexOf('=');
            var key = (eq < 0 ? raw : raw[..eq]).TrimStart('/', '-').ToLowerInvariant();
            var value = eq < 0 ? null : raw[(eq + 1)..].Trim().Trim('"');
            a = key switch
            {
                "silent" or "s" or "quiet" => a with { Silent = true },
                "uninstall" => a with { Mode = SetupMode.Uninstall },
                "printer" => a with { Printer = value },
                "port" => a with { Port = value },
                "name" => a with { Name = value },
                _ => throw new ArgumentException($"Unknown option '{raw}'. Use /silent /uninstall /printer=IP /port=N /name=\"AirPrint name\"."),
            };
        }
        return a;
    }

    /// <summary>
    /// Final settings for an install. Upgrade: omitted values keep <paramref name="existing"/>.
    /// Fresh install: /printer is required, port and name default.
    /// </summary>
    public (PrinterSettings? Settings, string? Error) Resolve(PrinterSettings? existing)
    {
        if (existing is null && string.IsNullOrWhiteSpace(Printer))
            return (null, "A fresh install needs /printer=<printer IP>.");
        var basis = existing ?? PrinterSettings.Defaults;
        var host = Printer ?? basis.Host;
        var port = Port ?? basis.Port.ToString();
        var name = Name ?? basis.Name;
        var errors = SettingsValidator.Validate(host, port, name);
        return errors.Count > 0
            ? (null, string.Join(" ", errors))
            : (new PrinterSettings(host.Trim(), int.Parse(port.Trim()), name.Trim()), null);
    }
}

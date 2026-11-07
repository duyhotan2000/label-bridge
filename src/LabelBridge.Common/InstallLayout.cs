namespace LabelBridge.Common;

public static class InstallLayout
{
    public const string ServiceName = "LabelBridge";
    public const string ServiceDisplayName = "LabelBridge AirPrint";
    public const string ServiceExe = "LabelBridge.exe";
    public const string TrayExe = "LabelBridge.Tray.exe";
    public const string UninstallExe = "uninstall.exe";
    public const string SettingsFileName = "appsettings.json";
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LabelBridge";
    public const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "LabelBridge Tray";

    /// <summary>Left behind by the old install.ps1 setup.</summary>
    public static readonly string[] ObsoleteFiles = ["install.ps1", "uninstall.ps1", "LabelBridge.staticwebassets.endpoints.json"];

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LabelBridge");

    public static IReadOnlyDictionary<string, object> UninstallEntry(string installDir, string version, long sizeKb)
    {
        var uninstall = $"\"{installDir}\\{UninstallExe}\" /uninstall";
        return new Dictionary<string, object>
        {
            ["DisplayName"] = "LabelBridge",
            ["DisplayVersion"] = version,
            ["Publisher"] = "LabelBridge",
            ["InstallLocation"] = installDir,
            ["DisplayIcon"] = $"{installDir}\\{TrayExe}",
            ["UninstallString"] = uninstall,
            ["QuietUninstallString"] = uninstall + " /silent",
            ["NoModify"] = 1,
            ["NoRepair"] = 1,
            ["EstimatedSize"] = (int)sizeKb,
        };
    }
}

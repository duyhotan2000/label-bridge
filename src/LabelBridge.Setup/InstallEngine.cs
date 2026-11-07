using System.Diagnostics;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using LabelBridge.Common;
using Microsoft.Win32;

namespace LabelBridge;

/// <summary>Installs, upgrades and uninstalls the LabelBridge service and tray. Must run elevated.</summary>
static class InstallEngine
{
    static readonly string Dir = InstallLayout.DefaultInstallDir;
    static string InDir(string name) => Path.Combine(Dir, name);
    static readonly string SettingsPath = InDir(InstallLayout.SettingsFileName);
    static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", "LabelBridge.lnk");

    public static PrinterSettings? ExistingSettings()
    {
        try { return SettingsFile.Read(SettingsPath); }
        catch (Exception ex) { Log.Write($"Could not read existing settings: {ex.Message}"); return null; }
    }

    public static void Install(PrinterSettings settings, bool startTray)
    {
        var version = typeof(InstallEngine).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Log.Write($"Installing LabelBridge {version} to {Dir}");

        StopEverything();

        Log.Write("Copying files…");
        Directory.CreateDirectory(Dir);
        WithRetry(() => Payload.ExtractTo(Payload.ServiceExe, InDir(InstallLayout.ServiceExe)));
        WithRetry(() => Payload.ExtractTo(Payload.TrayExe, InDir(InstallLayout.TrayExe)));
        var self = Environment.ProcessPath!;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InDir(InstallLayout.UninstallExe)), StringComparison.OrdinalIgnoreCase))
            WithRetry(() => File.Copy(self, InDir(InstallLayout.UninstallExe), overwrite: true));
        if (!File.Exists(SettingsPath))
            Payload.ExtractTo(Payload.Settings, SettingsPath);
        SettingsFile.Update(SettingsPath, settings);
        Log.Write($"Printer {settings.Host}:{settings.Port}, AirPrint name \"{settings.Name}\"");
        foreach (var f in InstallLayout.ObsoleteFiles)
            if (File.Exists(InDir(f)))
            {
                File.Delete(InDir(f));
                Log.Write($"Removed old {f}");
            }

        Log.Write("Registering the Windows service…");
        if (!ServiceExists())
            Sc("create", InstallLayout.ServiceName, "binPath=", $"\"{InDir(InstallLayout.ServiceExe)}\"",
                "start=", "delayed-auto", "DisplayName=", InstallLayout.ServiceDisplayName);
        Sc("config", InstallLayout.ServiceName, "start=", "delayed-auto");
        Sc("description", InstallLayout.ServiceName, "Shares the label printer with iPhones over AirPrint.");
        Sc("failure", InstallLayout.ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000");
        var sddl = Sc("sdshow", InstallLayout.ServiceName).Trim();
        var granted = ServiceSecurity.GrantAuthenticatedUsers(sddl);
        if (granted != sddl)
            Sc("sdset", InstallLayout.ServiceName, granted);

        Log.Write("Allowing users to change settings…");
        Run("icacls.exe", SettingsPath, "/grant", "*S-1-5-32-545:M");

        Log.Write("Opening the firewall for the local network…");
        var exe = InDir(InstallLayout.ServiceExe).Replace("'", "''");
        PowerShell(
            "Get-NetFirewallRule -DisplayName 'LabelBridge IPP','LabelBridge mDNS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; " +
            $"New-NetFirewallRule -DisplayName 'LabelBridge IPP' -Direction Inbound -Protocol TCP -LocalPort {SettingsFile.IppPort(SettingsPath)} -RemoteAddress LocalSubnet -Action Allow -Program '{exe}' | Out-Null; " +
            $"New-NetFirewallRule -DisplayName 'LabelBridge mDNS' -Direction Inbound -Protocol UDP -LocalPort 5353 -RemoteAddress LocalSubnet -Action Allow -Program '{exe}' | Out-Null");

        Log.Write("Adding the tray icon to Windows startup and the Start menu…");
        using (var run = Registry.LocalMachine.CreateSubKey(InstallLayout.RunKey))
            run.SetValue(InstallLayout.RunValueName, $"\"{InDir(InstallLayout.TrayExe)}\"");
        var tray = InDir(InstallLayout.TrayExe).Replace("'", "''");
        var lnk = ShortcutPath.Replace("'", "''");
        PowerShell($"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}'); $s.TargetPath = '{tray}'; " +
                   $"$s.WorkingDirectory = '{Dir.Replace("'", "''")}'; $s.Description = 'LabelBridge settings and status'; $s.Save()");

        using (var key = Registry.LocalMachine.CreateSubKey(InstallLayout.UninstallKey))
            foreach (var (name, value) in InstallLayout.UninstallEntry(Dir, version, DirSizeKb()))
                key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);

        WarnAboutNetwork();

        Log.Write("Starting printing…");
        using (var sc = new ServiceController(InstallLayout.ServiceName))
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }
        if (startTray)
            StartTrayAsUser();
        Log.Write("Done. LabelBridge is running.");
    }

    public static void Uninstall()
    {
        Log.Write($"Uninstalling LabelBridge from {Dir}");
        StopEverything();
        if (ServiceExists())
            Sc("delete", InstallLayout.ServiceName);
        PowerShell("Get-NetFirewallRule -DisplayName 'LabelBridge IPP','LabelBridge mDNS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        using (var run = Registry.LocalMachine.OpenSubKey(InstallLayout.RunKey, writable: true))
            run?.DeleteValue(InstallLayout.RunValueName, throwOnMissingValue: false);
        if (File.Exists(ShortcutPath))
            File.Delete(ShortcutPath);
        Registry.LocalMachine.DeleteSubKeyTree(InstallLayout.UninstallKey, throwOnMissingSubKey: false);

        foreach (var f in new[] { InstallLayout.ServiceExe, InstallLayout.TrayExe, "printer-id.txt" })
            if (File.Exists(InDir(f)))
                WithRetry(() => File.Delete(InDir(f)));
        if (Directory.Exists(InDir("jobs")))
            Directory.Delete(InDir("jobs"), recursive: true);

        // uninstall.exe can't delete itself while running; remove it shortly after we exit.
        var uninstaller = InDir(InstallLayout.UninstallExe);
        if (File.Exists(uninstaller))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{uninstaller}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        Log.Write("Done. Logs and appsettings.json were kept in " + Dir);
    }

    static void StopEverything()
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstallLayout.TrayExe)))
        {
            Log.Write($"Closing tray icon (pid {p.Id})…");
            try { p.Kill(); p.WaitForExit(5000); } catch (Exception ex) { Log.Write($"  {ex.Message}"); }
        }
        if (!ServiceExists())
            return;
        using var sc = new ServiceController(InstallLayout.ServiceName);
        if (sc.Status != ServiceControllerStatus.Stopped)
        {
            Log.Write("Stopping the LabelBridge service…");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
    }

    static bool ServiceExists() =>
        ServiceController.GetServices().Any(s => s.ServiceName.Equals(InstallLayout.ServiceName, StringComparison.OrdinalIgnoreCase));

    static void WarnAboutNetwork()
    {
        var port = SettingsFile.IppPort(SettingsPath);
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port))
            Log.Write($"WARNING: another program is using port {port} (for example an older AirPrint tool). Stop it, or LabelBridge cannot start.");
        var publicNets = PowerShell("(Get-NetConnectionProfile | Where-Object NetworkCategory -eq 'Public').Name", check: false).Trim();
        if (publicNets.Length > 0)
            Log.Write($"WARNING: network '{publicNets}' is set to Public, so iPhones may not find the printer. Switch it to Private in Windows Settings → Network.");
    }

    /// <summary>Starts the tray un-elevated in the logged-in user's session.</summary>
    static void StartTrayAsUser()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{InDir(InstallLayout.TrayExe)}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not start the tray icon now ({ex.Message}); it will start at next login.");
        }
    }

    static long DirSizeKb() =>
        new DirectoryInfo(Dir).EnumerateFiles().Sum(f => f.Length) / 1024;

    static string Sc(params string[] args) => Run("sc.exe", args);

    static string PowerShell(string command, bool check = true) =>
        Run("powershell.exe", check, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command);

    static string Run(string file, params string[] args) => Run(file, true, args);

    static string Run(string file, bool check, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            p.Kill();
            throw new InvalidOperationException($"{file} {args.FirstOrDefault()} timed out");
        }
        var output = stdout.Result + stderr.Result;
        if (check && p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args.Take(2))} failed ({p.ExitCode}): {output.Trim()}");
        return output;
    }

    static void WithRetry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(1000); // the old exe can stay locked for a moment after the service stops
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(1000);
            }
        }
    }
}

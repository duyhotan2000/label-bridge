using System.Diagnostics;
using System.ServiceProcess;
using LabelBridge.Common;

namespace LabelBridge;

/// <summary>Tray icon showing the service state, with Settings / status page / start-stop / Exit.</summary>
sealed class TrayContext : ApplicationContext
{
    static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, InstallLayout.SettingsFileName);

    readonly ServiceControl _service = new();
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _start, _stop, _status;
    readonly System.Windows.Forms.Timer _timer;
    readonly SynchronizationContext _ui;
    SettingsForm? _settings;
    bool _busy;

    public TrayContext(EventWaitHandle showSignal)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _status = new ToolStripMenuItem("LabelBridge") { Enabled = false };
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => ShowSettings()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _start = new ToolStripMenuItem("Start printing", null, async (_, _) => await RunAsync(_service.Start));
        _stop = new ToolStripMenuItem("Stop printing", null, async (_, _) => await RunAsync(_service.Stop));
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _status,
            new ToolStripSeparator(),
            settings,
            new ToolStripMenuItem("Open status page", null, (_, _) => OpenStatusPage()),
            _start,
            _stop,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit (stop printing)", null, async (_, _) => await ExitAsync()),
        ]);

        _icon = new NotifyIcon { ContextMenuStrip = menu, Icon = Icons.Stopped, Visible = true, Text = "LabelBridge" };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _timer = new System.Windows.Forms.Timer { Interval = 3000 };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();

        // A second launch (e.g. from the Start menu) signals us to open Settings.
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => _ui.Post(_ => ShowSettings(), null), null, -1, false);
    }

    void Refresh()
    {
        var status = _service.Status();
        var name = SafeRead()?.Name ?? "LabelBridge";
        var (icon, text) = status switch
        {
            ServiceControllerStatus.Running => (Icons.Running, "Printing is on"),
            ServiceControllerStatus.Stopped => (Icons.Stopped, "Printing is stopped"),
            null => (Icons.Stopped, "Service not installed"),
            _ => (Icons.Pending, "Starting/stopping…"),
        };
        _icon.Icon = icon;
        var tip = $"LabelBridge — {name}: {text}";
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
        _status.Text = $"{name} — {text}";
        _start.Visible = status == ServiceControllerStatus.Stopped;
        _stop.Visible = status == ServiceControllerStatus.Running;
        _start.Enabled = _stop.Enabled = !_busy;
    }

    async Task RunAsync(Action action)
    {
        _busy = true;
        _icon.Icon = Icons.Pending;
        try
        {
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LabelBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    async Task ExitAsync()
    {
        await RunAsync(_service.Stop);
        _timer.Stop();
        _icon.Visible = false;
        ExitThread();
    }

    void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(SettingsPath, _service);
        _settings.FormClosed += (_, _) => Refresh();
        _settings.Show();
        _settings.Activate();
    }

    static void OpenStatusPage()
    {
        var port = 631;
        try { port = SettingsFile.IppPort(SettingsPath); } catch { }
        Process.Start(new ProcessStartInfo($"http://localhost:{port}/") { UseShellExecute = true });
    }

    static PrinterSettings? SafeRead()
    {
        try { return SettingsFile.Read(SettingsPath); } catch { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System.Diagnostics;
using LabelBridge.Common;

namespace LabelBridge;

/// <summary>Three-step wizard: Welcome → Printer settings → Install progress.</summary>
sealed class SetupForm : Form
{
    readonly Panel _welcome = new() { Dock = DockStyle.Fill };
    readonly Panel _settings = new() { Dock = DockStyle.Fill };
    readonly Panel _progress = new() { Dock = DockStyle.Fill };
    readonly TextBox _host = new() { Width = 260 };
    readonly TextBox _port = new() { Width = 80 };
    readonly TextBox _name = new() { Width = 260 };
    readonly Label _errors = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(440, 0) };
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    readonly Button _back = new() { Text = "< Back", AutoSize = true };
    readonly Button _next = new() { Text = "Next >", AutoSize = true };
    readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    readonly CheckBox _openSettings = new() { Text = "Open LabelBridge settings", AutoSize = true, Visible = false };
    readonly bool _upgrade;
    int _step;
    bool _done;

    public SetupForm()
    {
        var existing = InstallEngine.ExistingSettings();
        _upgrade = existing is not null;
        var s = existing ?? PrinterSettings.Defaults with { Host = "" };
        _host.Text = s.Host;
        _port.Text = s.Port.ToString();
        _name.Text = s.Name;

        Text = "LabelBridge Setup";
        Size = new Size(540, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(16);

        var version = typeof(SetupForm).Assembly.GetName().Version?.ToString(3);
        _welcome.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10),
            Text = $"LabelBridge {version}\n\n" +
                   "Shares your Xprinter label printer with iPhones as an AirPrint printer, so you can print " +
                   "Shopee AWB labels straight from the iPhone.\n\n" +
                   (_upgrade ? "LabelBridge is already installed. Setup will upgrade it and keep your settings.\n\n" : "") +
                   "Click Next to continue.",
        });

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
            grid.Controls.Add(c);
        }
        var intro = new Label { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 0, 0, 12),
            Text = "Enter the label printer's network address and the name iPhones should show. You can change these later from the LabelBridge tray icon." };
        grid.Controls.Add(intro);
        grid.SetColumnSpan(intro, 2);
        Row("Printer IP", _host);
        Row("Printer port", _port);
        Row("AirPrint name", _name);
        grid.Controls.Add(_errors);
        grid.SetColumnSpan(_errors, 2);
        _settings.Controls.Add(grid);

        _progress.Controls.Add(_log);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([_cancel, _next, _back, _openSettings]);
        Controls.AddRange([_welcome, _settings, _progress, buttons]);

        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += async (_, _) => await NextAsync();
        _cancel.Click += (_, _) => Close();
        AcceptButton = _next;
        Go(0);
    }

    void Go(int step)
    {
        _step = step;
        _welcome.Visible = step == 0;
        _settings.Visible = step == 1;
        _progress.Visible = step == 2;
        _back.Visible = step == 1;
        _next.Text = step switch { 0 => "Next >", 1 => _upgrade ? "Upgrade" : "Install", _ => "Finish" };
    }

    async Task NextAsync()
    {
        if (_step == 0)
        {
            Go(1);
            return;
        }
        if (_step == 2)
        {
            if (_done && _openSettings.Checked)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.Combine(InstallLayout.DefaultInstallDir, InstallLayout.TrayExe)}\""));
            Close();
            return;
        }

        var errors = SettingsValidator.Validate(_host.Text, _port.Text, _name.Text);
        _errors.Text = string.Join("\n", errors);
        if (errors.Count > 0)
            return;
        var settings = new PrinterSettings(_host.Text.Trim(), int.Parse(_port.Text.Trim()), _name.Text.Trim());

        Go(2);
        _next.Enabled = _cancel.Enabled = false;
        Log.Sink = line => BeginInvoke(() => _log.AppendText(line + Environment.NewLine));
        try
        {
            await Task.Run(() => InstallEngine.Install(settings, startTray: true));
            _done = true;
            _log.AppendText(Environment.NewLine + "LabelBridge is installed. Look for the printer icon in the system tray." + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Log.Write("ERROR: " + ex.Message);
            _log.AppendText(Environment.NewLine + "Setup failed. Details are in " + Log.FilePath + Environment.NewLine);
        }
        finally
        {
            Log.Sink = null;
            _next.Enabled = true;
            _next.Text = "Finish";
            _openSettings.Visible = _done;
        }
    }
}

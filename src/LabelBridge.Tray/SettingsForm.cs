using System.Diagnostics;
using LabelBridge.Common;

namespace LabelBridge;

/// <summary>Printer IP, port and AirPrint name. Save restarts the service; Test print uses the form's values.</summary>
sealed class SettingsForm : Form
{
    readonly string _path;
    readonly ServiceControl _service;
    readonly TextBox _host = new() { Width = 260 };
    readonly TextBox _port = new() { Width = 80 };
    readonly TextBox _name = new() { Width = 260 };
    readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.Firebrick };
    readonly Button _save = new() { Text = "Save", AutoSize = true };
    readonly Button _test = new() { Text = "Test print", AutoSize = true };
    readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    public SettingsForm(string settingsPath, ServiceControl service)
    {
        _path = settingsPath;
        _service = service;
        Text = "LabelBridge Settings";
        Icon = Icons.Running;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AcceptButton = _save;
        CancelButton = _cancel;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
            grid.Controls.Add(c);
        }
        Row("Printer IP", _host);
        Row("Printer port", _port);
        Row("AirPrint name", _name);

        var note = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText };
        var mode = SettingsFile.OutputMode(_path) ?? "tspl";
        note.Text = mode == "tspl"
            ? "The AirPrint name is what iPhones show in the printer list."
            : $"This PC prints via '{mode}'; printer IP/port are only used in tspl mode.";
        grid.Controls.Add(note);
        grid.SetColumnSpan(note, 2);
        grid.Controls.Add(_message);
        grid.SetColumnSpan(_message, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([_cancel, _save, _test]);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);

        var s = SettingsFile.Read(_path) ?? PrinterSettings.Defaults;
        _host.Text = s.Host;
        _port.Text = s.Port.ToString();
        _name.Text = s.Name;

        _save.Click += async (_, _) => await SaveAsync();
        _test.Click += async (_, _) => await TestPrintAsync();
    }

    bool Validate(out PrinterSettings settings)
    {
        var errors = SettingsValidator.Validate(_host.Text, _port.Text, _name.Text);
        settings = errors.Count == 0 ? new PrinterSettings(_host.Text.Trim(), int.Parse(_port.Text.Trim()), _name.Text.Trim()) : PrinterSettings.Defaults;
        Show(errors.Count == 0 ? "" : string.Join("\n", errors), error: true);
        return errors.Count == 0;
    }

    async Task SaveAsync()
    {
        if (!Validate(out var s))
            return;
        await Busy("Saving and restarting printing…", async () =>
        {
            SettingsFile.Update(_path, s);
            await Task.Run(_service.Restart);
        });
        if (_message.ForeColor != Color.Firebrick)
            Close();
    }

    async Task TestPrintAsync()
    {
        if (!Validate(out var s))
            return;
        await Busy("Sending a test label…", async () =>
        {
            var exe = Path.Combine(AppContext.BaseDirectory, InstallLayout.ServiceExe);
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "test-print", "--Bridge:Output=tspl", $"--Bridge:PrinterHost={s.Host}", $"--Bridge:PrinterPort={s.Port}" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = await p.StandardOutput.ReadToEndAsync(timeout.Token) + await p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            if (p.ExitCode != 0)
                throw new InvalidOperationException(output.Trim());
            Show("Test label sent. Check the printer.", error: false);
        });
    }

    async Task Busy(string text, Func<Task> work)
    {
        Show(text, error: false);
        UseWaitCursor = true;
        _save.Enabled = _test.Enabled = false;
        try
        {
            await work();
            if (_message.Text == text)
                Show("Saved. iPhones will see the change within a few seconds.", error: false);
        }
        catch (OperationCanceledException)
        {
            Show("Timed out after 30 seconds.", error: true);
        }
        catch (Exception ex)
        {
            Show(ex.Message, error: true);
        }
        finally
        {
            UseWaitCursor = false;
            _save.Enabled = _test.Enabled = true;
        }
    }

    void Show(string text, bool error)
    {
        _message.Text = text;
        _message.ForeColor = error ? Color.Firebrick : Color.DarkGreen;
    }
}

using LabelBridge.Common;

namespace LabelBridge;

static class Program
{
    /// <summary>
    /// LabelBridge-Setup.exe                         wizard
    /// LabelBridge-Setup.exe /silent /printer=IP [/port=N] [/name="AirPrint name"]
    /// LabelBridge-Setup.exe /uninstall [/silent]
    /// Exit codes: 0 ok, 1 install failed, 2 bad arguments. Log: %TEMP%\LabelBridge-Setup.log
    /// </summary>
    [STAThread]
    static int Main(string[] argv)
    {
        ApplicationConfiguration.Initialize();
        SetupArgs args;
        try
        {
            args = SetupArgs.Parse(argv);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, 2, silent: argv.Any(a => a.TrimStart('/', '-').Equals("silent", StringComparison.OrdinalIgnoreCase)));
        }

        Log.Write($"LabelBridge-Setup {string.Join(' ', argv)}");
        if (args.Mode == SetupMode.Uninstall)
            return Uninstall(args.Silent);

        if (!Payload.IsPresent)
            return Fail("This setup was built without the LabelBridge program files. Rebuild it with build.sh.", 1, args.Silent);

        if (!args.Silent)
        {
            Application.Run(new SetupForm());
            return 0;
        }

        var (settings, error) = args.Resolve(InstallEngine.ExistingSettings());
        if (settings is null)
            return Fail(error!, 2, silent: true);
        try
        {
            InstallEngine.Install(settings, startTray: false);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, 1, silent: true);
        }
    }

    static int Uninstall(bool silent)
    {
        if (!silent && MessageBox.Show("Remove LabelBridge from this PC? iPhones will no longer see the printer.",
                "LabelBridge Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 0;
        try
        {
            InstallEngine.Uninstall();
            if (!silent)
                MessageBox.Show("LabelBridge was removed.", "LabelBridge Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, 1, silent);
        }
    }

    static int Fail(string message, int code, bool silent)
    {
        Log.Write("ERROR: " + message);
        if (!silent)
            MessageBox.Show(message, "LabelBridge Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return code;
    }
}

namespace LabelBridge;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One tray per logged-in user; a second launch asks the running one to open Settings.
        using var single = new Mutex(true, @"Local\LabelBridge.Tray", out var first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LabelBridge.Tray.Show");
        if (!first)
        {
            show.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(show));
    }
}

using System.ComponentModel;
using System.ServiceProcess;
using LabelBridge.Common;

namespace LabelBridge;

/// <summary>Start/stop/query the LabelBridge service. Users are granted these rights by the installer.</summary>
sealed class ServiceControl
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>Null when the service isn't installed.</summary>
    public ServiceControllerStatus? Status()
    {
        try
        {
            using var sc = new ServiceController(InstallLayout.ServiceName);
            return sc.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Start() => Run(sc =>
    {
        if (sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, Timeout);
        }
    });

    public void Stop() => Run(sc =>
    {
        if (sc.Status != ServiceControllerStatus.Stopped)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
        }
    });

    public void Restart()
    {
        Stop();
        Start();
    }

    static void Run(Action<ServiceController> action)
    {
        try
        {
            using var sc = new ServiceController(InstallLayout.ServiceName);
            action(sc);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            throw new InvalidOperationException("Windows refused — reinstall LabelBridge to grant permissions.", ex);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new InvalidOperationException("The LabelBridge service did not respond in time. Check the logs folder.", ex);
        }
    }
}

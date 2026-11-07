using LabelBridge.Common;

namespace LabelBridge.Tests.Common;

public class InstallLayoutTests
{
    [Fact]
    public void UninstallEntry_Values()
    {
        var e = InstallLayout.UninstallEntry(@"C:\Program Files\LabelBridge", "1.1.0", 200000);

        Assert.Equal("LabelBridge", e["DisplayName"]);
        Assert.Equal("1.1.0", e["DisplayVersion"]);
        Assert.Equal(@"""C:\Program Files\LabelBridge\uninstall.exe"" /uninstall", e["UninstallString"]);
        Assert.Equal(@"""C:\Program Files\LabelBridge\uninstall.exe"" /uninstall /silent", e["QuietUninstallString"]);
        Assert.Equal(@"C:\Program Files\LabelBridge\LabelBridge.Tray.exe", e["DisplayIcon"]);
        Assert.Equal(1, e["NoModify"]);
        Assert.Equal(200000, e["EstimatedSize"]);
    }
}

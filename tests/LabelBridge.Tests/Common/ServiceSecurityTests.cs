using LabelBridge.Common;

namespace LabelBridge.Tests.Common;

public class ServiceSecurityTests
{
    const string Default = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

    [Fact]
    public void GrantAuthenticatedUsers_InsertsBeforeSacl()
    {
        Assert.Equal(
            "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWRPWPLORC;;;AU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)",
            ServiceSecurity.GrantAuthenticatedUsers(Default));
    }

    [Fact]
    public void GrantAuthenticatedUsers_NoSacl_Appends()
    {
        Assert.Equal("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWRPWPLORC;;;AU)",
            ServiceSecurity.GrantAuthenticatedUsers("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)"));
    }

    [Fact]
    public void GrantAuthenticatedUsers_Idempotent()
    {
        var once = ServiceSecurity.GrantAuthenticatedUsers(Default);

        Assert.Equal(once, ServiceSecurity.GrantAuthenticatedUsers(once.Trim() + "\r\n"));
    }
}

namespace LabelBridge.Common;

public static class ServiceSecurity
{
    /// <summary>Authenticated Users: query config/status, start, stop, interrogate, user control, read control.</summary>
    public const string AuAce = "(A;;CCLCSWRPWPLORC;;;AU)";

    /// <summary>Adds <see cref="AuAce"/> to the DACL of a service SDDL (from `sc sdshow`), once.</summary>
    public static string GrantAuthenticatedUsers(string sddl)
    {
        sddl = sddl.Trim();
        if (sddl.Contains(AuAce, StringComparison.Ordinal))
            return sddl;
        var sacl = sddl.IndexOf("S:", StringComparison.Ordinal);
        return sacl < 0 ? sddl + AuAce : sddl[..sacl] + AuAce + sddl[sacl..];
    }
}

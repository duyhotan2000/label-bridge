using System.Text;

namespace LabelBridge.Ipp;

/// <summary>Human-readable dump of an IPP message, logged when SaveJobs is on.</summary>
public static class IppDump
{
    static readonly Dictionary<short, string> OpNames = new()
    {
        [IppOp.PrintJob] = "Print-Job",
        [IppOp.ValidateJob] = "Validate-Job",
        [IppOp.CreateJob] = "Create-Job",
        [IppOp.SendDocument] = "Send-Document",
        [IppOp.CancelJob] = "Cancel-Job",
        [IppOp.GetJobAttributes] = "Get-Job-Attributes",
        [IppOp.GetJobs] = "Get-Jobs",
        [IppOp.GetPrinterAttributes] = "Get-Printer-Attributes",
    };

    static readonly Dictionary<short, string> StatusNames = new()
    {
        [IppStatus.Ok] = "successful-ok",
        [IppStatus.OkIgnoredOrSubstituted] = "successful-ok-ignored-or-substituted-attributes",
        [IppStatus.BadRequest] = "client-error-bad-request",
        [IppStatus.NotFound] = "client-error-not-found",
        [IppStatus.DocumentFormatNotSupported] = "client-error-document-format-not-supported",
        [IppStatus.NotPossible] = "client-error-not-possible",
        [IppStatus.InternalError] = "server-error-internal-error",
        [IppStatus.OperationNotSupported] = "server-error-operation-not-supported",
    };

    public static string Format(IppMessage m, bool isResponse = false)
    {
        var names = isResponse ? StatusNames : OpNames;
        var code = names.TryGetValue(m.Code, out var n) ? n : $"0x{m.Code:X4}";
        var sb = new StringBuilder($"IPP/{m.VersionMajor}.{m.VersionMinor} {code} request-id {m.RequestId}");
        foreach (var g in m.Groups)
        {
            sb.AppendLine().Append("  ").Append(GroupName(g.Tag));
            foreach (var a in g.Attributes)
            {
                var tag = a.Values.Count > 0 ? TagName(a.Values[0].Tag) : "?";
                sb.AppendLine().Append($"    {a.Name} ({tag}) = {string.Join(", ", a.Values.Select(Value))}");
            }
        }
        return sb.ToString();
    }

    static string Value(IppValue v) => v.Value switch
    {
        null => $"<{TagName(v.Tag)}>",
        IppCollection c => "{" + string.Join(" ", c.Select(m => $"{m.Name}={string.Join(",", m.Values.Select(Value))}")) + "}",
        IppResolution r => $"{r.X}x{r.Y}{(r.Units == 3 ? "dpi" : "dpcm")}",
        IppRange r => $"{r.Lower}-{r.Upper}",
        byte[] b => Convert.ToHexString(b),
        bool b => b ? "true" : "false",
        var other => other.ToString() ?? "",
    };

    static string GroupName(IppTag t) => t switch
    {
        IppTag.OperationAttributes => "operation-attributes",
        IppTag.JobAttributes => "job-attributes",
        IppTag.PrinterAttributes => "printer-attributes",
        IppTag.UnsupportedAttributes => "unsupported-attributes",
        _ => t.ToString(),
    };

    static string TagName(IppTag t) => t switch
    {
        IppTag.BegCollection => "collection",
        IppTag.NameWithoutLanguage => "name",
        IppTag.TextWithoutLanguage => "text",
        IppTag.MimeMediaType => "mimeMediaType",
        IppTag.RangeOfInteger => "rangeOfInteger",
        IppTag.NoValue => "no-value",
        _ => t.ToString().ToLowerInvariant(),
    };
}

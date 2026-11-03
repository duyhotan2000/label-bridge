using LabelBridge.Ipp;

namespace LabelBridge.Tests.Ipp;

public class IppDumpTests
{
    [Fact]
    public void Format_ListsGroupsAttributesAndValues()
    {
        var m = IppOperationsTests.Request(IppOp.GetPrinterAttributes,
            new IppAttribute("requested-attributes",
                new IppValue(IppTag.Keyword, "printer-state"), new IppValue(IppTag.Keyword, "media-col-database")),
            new IppAttribute("res", new IppValue(IppTag.Resolution, new IppResolution(203, 203))),
            new IppAttribute("col", new IppValue(IppTag.BegCollection, new IppCollection
            {
                new("x-dimension", new IppValue(IppTag.Integer, 10500)),
            })));

        var text = IppDump.Format(m);

        Assert.Contains("Get-Printer-Attributes", text);
        Assert.Contains("request-id 9", text);
        Assert.Contains("operation-attributes", text);
        Assert.Contains("requested-attributes (keyword) = printer-state, media-col-database", text);
        Assert.Contains("res (resolution) = 203x203dpi", text);
        Assert.Contains("col (collection) = {x-dimension=10500}", text);
    }

    [Fact]
    public void Format_ResponseShowsStatusName()
    {
        var m = new IppMessage { Code = IppStatus.DocumentFormatNotSupported, RequestId = 3 };

        Assert.Contains("client-error-document-format-not-supported", IppDump.Format(m, isResponse: true));
    }
}

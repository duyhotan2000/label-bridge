using System.Text;
using LabelBridge.Ipp;

namespace LabelBridge.Tests.Ipp;

public class IppCodecTests
{
    // Get-Printer-Attributes, request-id 1, charset/language/printer-uri.
    internal static byte[] GetPrinterAttributesFixture()
    {
        var b = new List<byte>();
        b.AddRange(Convert.FromHexString("0200000B0000000101"));
        void Attr(byte tag, string name, string value)
        {
            b.Add(tag);
            b.Add((byte)(name.Length >> 8)); b.Add((byte)name.Length);
            b.AddRange(Encoding.ASCII.GetBytes(name));
            b.Add((byte)(value.Length >> 8)); b.Add((byte)value.Length);
            b.AddRange(Encoding.ASCII.GetBytes(value));
        }
        Attr(0x47, "attributes-charset", "utf-8");
        Attr(0x48, "attributes-natural-language", "en");
        Attr(0x45, "printer-uri", "ipp://localhost/ipp/print");
        b.Add(0x03);
        return b.ToArray();
    }

    [Fact]
    public void RoundTrip_AllValueTypes()
    {
        var msg = new IppMessage { Code = IppOp.PrintJob, RequestId = 42 };
        var op = msg.Group(IppTag.OperationAttributes);
        op.Attributes.Add(new IppAttribute("attributes-charset", new IppValue(IppTag.Charset, "utf-8")));
        op.Attributes.Add(new IppAttribute("int", new IppValue(IppTag.Integer, -7)));
        op.Attributes.Add(new IppAttribute("bool", new IppValue(IppTag.Boolean, true)));
        op.Attributes.Add(new IppAttribute("enum", new IppValue(IppTag.Enum, 3)));
        op.Attributes.Add(new IppAttribute("octets", new IppValue(IppTag.OctetString, new byte[] { 1, 2, 3 })));
        op.Attributes.Add(new IppAttribute("res", new IppValue(IppTag.Resolution, new IppResolution(203, 203))));
        op.Attributes.Add(new IppAttribute("range", new IppValue(IppTag.RangeOfInteger, new IppRange(1, 99))));
        op.Attributes.Add(new IppAttribute("kw",
            new IppValue(IppTag.Keyword, "one"), new IppValue(IppTag.Keyword, "two")));
        op.Attributes.Add(new IppAttribute("nv", new IppValue(IppTag.NoValue, null)));
        var size = new IppCollection
        {
            new IppAttribute("x-dimension", new IppValue(IppTag.Integer, 10500)),
            new IppAttribute("y-dimension", new IppValue(IppTag.Integer, 14800)),
        };
        var mediaCol = new IppCollection
        {
            new IppAttribute("media-size", new IppValue(IppTag.BegCollection, size)),
            new IppAttribute("media-top-margin", new IppValue(IppTag.Integer, 0)),
        };
        msg.Group(IppTag.JobAttributes).Attributes.Add(
            new IppAttribute("media-col", new IppValue(IppTag.BegCollection, mediaCol)));

        var read = IppReader.Read(new MemoryStream(IppWriter.Write(msg)));

        Assert.Equal(Dump(msg), Dump(read));
        Assert.Equal(42, read.RequestId);
        Assert.Equal(IppOp.PrintJob, read.Code);
    }

    [Fact]
    public void Read_KnownGetPrinterAttributesRequest()
    {
        var m = IppReader.Read(new MemoryStream(GetPrinterAttributesFixture()));
        Assert.Equal(2, m.VersionMajor);
        Assert.Equal(0, m.VersionMinor);
        Assert.Equal(IppOp.GetPrinterAttributes, m.Code);
        Assert.Equal(1, m.RequestId);
        Assert.Equal("utf-8", m.FindOperation("attributes-charset")!.Values[0].Value);
        Assert.Equal("en", m.FindOperation("attributes-natural-language")!.Values[0].Value);
        Assert.Equal("ipp://localhost/ipp/print", m.FindOperation("printer-uri")!.Values[0].Value);
        Assert.Equal(IppTag.Uri, m.FindOperation("printer-uri")!.Values[0].Tag);
    }

    [Fact]
    public void Read_LeavesStreamAtDocumentData()
    {
        var bytes = GetPrinterAttributesFixture().Concat(new byte[] { 0xDE, 0xAD }).ToArray();
        var s = new MemoryStream(bytes);
        IppReader.Read(s);
        Assert.Equal(0xDE, s.ReadByte());
        Assert.Equal(0xAD, s.ReadByte());
    }

    [Fact]
    public void Read_Truncated_Throws()
    {
        var bytes = GetPrinterAttributesFixture();
        var cut = bytes.Take(30).ToArray();
        Assert.Throws<IppFormatException>(() => IppReader.Read(new MemoryStream(cut)));
    }

    static string Dump(IppMessage m)
    {
        var sb = new StringBuilder();
        foreach (var g in m.Groups)
        {
            sb.Append($"[{g.Tag}]");
            foreach (var a in g.Attributes) DumpAttr(sb, a);
        }
        return sb.ToString();
    }

    static void DumpAttr(StringBuilder sb, IppAttribute a)
    {
        sb.Append($"{a.Name}=");
        foreach (var v in a.Values)
        {
            sb.Append($"{v.Tag}:");
            switch (v.Value)
            {
                case IppCollection c: sb.Append('{'); foreach (var m in c) DumpAttr(sb, m); sb.Append('}'); break;
                case byte[] bytes: sb.Append(Convert.ToHexString(bytes)); break;
                default: sb.Append(v.Value); break;
            }
            sb.Append(',');
        }
        sb.Append(';');
    }
}

using System.Buffers.Binary;
using System.Text;

namespace LabelBridge.Ipp;

public static class IppReader
{
    /// <summary>
    /// Reads the IPP header and attribute groups. Stops right after the
    /// end-of-attributes tag, so <paramref name="s"/> is left at the document data.
    /// </summary>
    public static IppMessage Read(Stream s)
    {
        var m = new IppMessage
        {
            VersionMajor = ReadByte(s),
            VersionMinor = ReadByte(s),
            Code = (short)ReadUInt16(s),
            RequestId = ReadInt32(s),
        };

        IppGroup? group = null;
        IppAttribute? last = null;
        while (true)
        {
            var tag = ReadByte(s);
            if (tag == (byte)IppTag.EndOfAttributes)
                return m;
            if (tag < 0x10)
            {
                group = new IppGroup((IppTag)tag);
                m.Groups.Add(group);
                last = null;
                continue;
            }
            if (group is null)
                throw new IppFormatException("Attribute before any group tag");

            var name = ReadString(s, ReadUInt16(s));
            var value = ReadValue(s, (IppTag)tag);
            if (name.Length == 0)
            {
                if (last is null)
                    throw new IppFormatException("Additional value without an attribute");
                last.Values.Add(value);
            }
            else
            {
                last = new IppAttribute(name, value);
                group.Attributes.Add(last);
            }
        }
    }

    static IppValue ReadValue(Stream s, IppTag tag)
    {
        var len = ReadUInt16(s);
        if (tag == IppTag.BegCollection)
        {
            Skip(s, len);
            return new IppValue(tag, ReadCollection(s));
        }
        var data = ReadBytes(s, len);
        return new IppValue(tag, Decode(tag, data));
    }

    static IppCollection ReadCollection(Stream s)
    {
        var c = new IppCollection();
        IppAttribute? member = null;
        while (true)
        {
            var tag = (IppTag)ReadByte(s);
            Skip(s, ReadUInt16(s)); // member name-length is always 0
            if (tag == IppTag.EndCollection)
            {
                Skip(s, ReadUInt16(s));
                return c;
            }
            if (tag == IppTag.MemberAttrName)
            {
                member = new IppAttribute(ReadString(s, ReadUInt16(s)));
                c.Add(member);
                continue;
            }
            if (member is null)
                throw new IppFormatException("Collection value without member name");
            member.Values.Add(ReadValue(s, tag));
        }
    }

    static object? Decode(IppTag tag, byte[] d)
    {
        switch (tag)
        {
            case IppTag.Integer:
            case IppTag.Enum:
                Expect(d, 4);
                return BinaryPrimitives.ReadInt32BigEndian(d);
            case IppTag.Boolean:
                Expect(d, 1);
                return d[0] != 0;
            case IppTag.Resolution:
                Expect(d, 9);
                return new IppResolution(BinaryPrimitives.ReadInt32BigEndian(d),
                    BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(4)), d[8]);
            case IppTag.RangeOfInteger:
                Expect(d, 8);
                return new IppRange(BinaryPrimitives.ReadInt32BigEndian(d),
                    BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(4)));
            case IppTag.TextWithoutLanguage:
            case IppTag.NameWithoutLanguage:
            case IppTag.Keyword:
            case IppTag.Uri:
            case IppTag.UriScheme:
            case IppTag.Charset:
            case IppTag.NaturalLanguage:
            case IppTag.MimeMediaType:
            case IppTag.MemberAttrName:
                return Encoding.UTF8.GetString(d);
            default:
                if ((byte)tag < 0x20)
                    return null; // out-of-band
                return d;
        }
    }

    static void Expect(byte[] d, int len)
    {
        if (d.Length != len)
            throw new IppFormatException($"Expected {len} value bytes, got {d.Length}");
    }

    static byte ReadByte(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0)
            throw new IppFormatException("Unexpected end of IPP message");
        return (byte)b;
    }

    static ushort ReadUInt16(Stream s) => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(s, 2));

    static int ReadInt32(Stream s) => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(s, 4));

    static string ReadString(Stream s, int len) => Encoding.UTF8.GetString(ReadBytes(s, len));

    static void Skip(Stream s, int len) => ReadBytes(s, len);

    static byte[] ReadBytes(Stream s, int len)
    {
        var buf = new byte[len];
        try
        {
            s.ReadExactly(buf);
        }
        catch (EndOfStreamException)
        {
            throw new IppFormatException("Unexpected end of IPP message");
        }
        return buf;
    }
}

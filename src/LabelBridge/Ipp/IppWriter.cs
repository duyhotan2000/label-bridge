using System.Buffers.Binary;
using System.Text;

namespace LabelBridge.Ipp;

public static class IppWriter
{
    public static byte[] Write(IppMessage m)
    {
        var ms = new MemoryStream();
        ms.WriteByte(m.VersionMajor);
        ms.WriteByte(m.VersionMinor);
        WriteUInt16(ms, (ushort)m.Code);
        WriteInt32(ms, m.RequestId);
        foreach (var g in m.Groups)
        {
            ms.WriteByte((byte)g.Tag);
            foreach (var a in g.Attributes)
                WriteAttribute(ms, a.Name, a.Values);
        }
        ms.WriteByte((byte)IppTag.EndOfAttributes);
        return ms.ToArray();
    }

    static void WriteAttribute(Stream s, string name, List<IppValue> values)
    {
        for (var i = 0; i < values.Count; i++)
            WriteValue(s, i == 0 ? name : "", values[i]);
    }

    static void WriteValue(Stream s, string name, IppValue v)
    {
        s.WriteByte((byte)v.Tag);
        WriteBytes(s, Encoding.UTF8.GetBytes(name));
        if (v.Value is IppCollection c)
        {
            WriteUInt16(s, 0);
            foreach (var member in c)
            {
                s.WriteByte((byte)IppTag.MemberAttrName);
                WriteUInt16(s, 0);
                WriteBytes(s, Encoding.UTF8.GetBytes(member.Name));
                foreach (var mv in member.Values)
                    WriteValue(s, "", mv);
            }
            s.WriteByte((byte)IppTag.EndCollection);
            WriteUInt16(s, 0);
            WriteUInt16(s, 0);
            return;
        }
        WriteBytes(s, Encode(v));
    }

    static byte[] Encode(IppValue v)
    {
        switch (v.Value)
        {
            case null:
                return [];
            case int i:
                var ib = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(ib, i);
                return ib;
            case bool b:
                return [(byte)(b ? 1 : 0)];
            case string str:
                return Encoding.UTF8.GetBytes(str);
            case byte[] bytes:
                return bytes;
            case IppResolution r:
                var rb = new byte[9];
                BinaryPrimitives.WriteInt32BigEndian(rb, r.X);
                BinaryPrimitives.WriteInt32BigEndian(rb.AsSpan(4), r.Y);
                rb[8] = r.Units;
                return rb;
            case IppRange rg:
                var gb = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(gb, rg.Lower);
                BinaryPrimitives.WriteInt32BigEndian(gb.AsSpan(4), rg.Upper);
                return gb;
            default:
                throw new ArgumentException($"Unsupported IPP value type {v.Value.GetType()}");
        }
    }

    static void WriteBytes(Stream s, byte[] data)
    {
        WriteUInt16(s, (ushort)data.Length);
        s.Write(data);
    }

    static void WriteUInt16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        s.Write(b);
    }

    static void WriteInt32(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }
}

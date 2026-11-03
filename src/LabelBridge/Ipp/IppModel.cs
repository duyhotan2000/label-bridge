namespace LabelBridge.Ipp;

/// <summary>Units 3 = dots per inch.</summary>
public readonly record struct IppResolution(int X, int Y, byte Units = 3);

public readonly record struct IppRange(int Lower, int Upper);

/// <summary>The members of a collection value.</summary>
public sealed class IppCollection : List<IppAttribute>
{
}

/// <summary>
/// Value is int | bool | string | byte[] | IppResolution | IppRange | IppCollection,
/// or null for out-of-band tags (no-value, unknown, unsupported).
/// </summary>
public sealed record IppValue(IppTag Tag, object? Value);

public sealed class IppAttribute(string name, params IppValue[] values)
{
    public string Name { get; } = name;
    public List<IppValue> Values { get; } = [.. values];
}

public sealed class IppGroup(IppTag tag)
{
    public IppTag Tag { get; } = tag;
    public List<IppAttribute> Attributes { get; } = [];

    public IppAttribute? Find(string name) => Attributes.FirstOrDefault(a => a.Name == name);
}

public sealed class IppMessage
{
    public byte VersionMajor { get; set; } = 2;
    public byte VersionMinor { get; set; } = 0;
    /// <summary>Operation id in a request, status code in a response.</summary>
    public short Code { get; set; }
    public int RequestId { get; set; }
    public List<IppGroup> Groups { get; } = [];

    /// <summary>Gets the first group with this tag, adding it if missing.</summary>
    public IppGroup Group(IppTag tag)
    {
        var g = Groups.FirstOrDefault(g => g.Tag == tag);
        if (g is null)
        {
            g = new IppGroup(tag);
            Groups.Add(g);
        }
        return g;
    }

    public IppAttribute? FindOperation(string name) =>
        Groups.FirstOrDefault(g => g.Tag == IppTag.OperationAttributes)?.Find(name);
}

public sealed class IppFormatException(string message) : Exception(message);

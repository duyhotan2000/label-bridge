namespace LabelBridge.Ipp;

/// <summary>IPP delimiter and value tags (RFC 8010).</summary>
public enum IppTag : byte
{
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,

    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,

    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,

    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BegCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,

    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,
}

public static class IppOp
{
    public const short PrintJob = 0x0002;
    public const short ValidateJob = 0x0004;
    public const short CreateJob = 0x0005;
    public const short SendDocument = 0x0006;
    public const short CancelJob = 0x0008;
    public const short GetJobAttributes = 0x0009;
    public const short GetJobs = 0x000A;
    public const short GetPrinterAttributes = 0x000B;
}

public static class IppStatus
{
    public const short Ok = 0x0000;
    public const short OkIgnoredOrSubstituted = 0x0001;
    public const short BadRequest = 0x0400;
    public const short NotFound = 0x0406;
    public const short DocumentFormatNotSupported = 0x040A;
    public const short NotPossible = 0x040C;
    public const short InternalError = 0x0500;
    public const short OperationNotSupported = 0x0501;
}

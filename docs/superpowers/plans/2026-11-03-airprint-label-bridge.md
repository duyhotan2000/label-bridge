# AirPrint Label Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows Service that appears to iPhones as an AirPrint printer and prints received Shopee AWB pages (A6) onto 100×150 mm labels on an Xprinter XP-470B.

**Architecture:** An ASP.NET Core (Kestrel) app hosts an IPP endpoint at `/ipp/print` on port 631 and advertises it over mDNS (`_ipp._tcp` + `_universal` subtype). iOS sends raster pages (URF or PWG); a single background job processor decodes them to grayscale, maps each A6 page onto the label at 203 dpi, thresholds it to 1-bit, and hands the labels to one of three outputs: raw TSPL over TCP 9100 (default), the Windows print driver, or PNG files.

**Tech Stack:** C# / .NET 10 (`net10.0`), ASP.NET Core minimal API, `Microsoft.Extensions.Hosting.WindowsServices`, `Makaretu.Dns.Multicast.New` 0.38.0 (mDNS), `Serilog.AspNetCore` + `Serilog.Sinks.File`, `System.Drawing.Common` (Windows driver output only), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-airprint-label-bridge-design.md`

## Global Constraints

- Target `net10.0`; publish `-r win-x64 --self-contained -p:PublishSingleFile=true`.
- Only the formats `image/urf` and `image/pwg-raster` are accepted. Never advertise `application/pdf`.
- Advertised medium is a single A6 page: `iso_a6_105x148mm` (10500 × 14800 hundredths of a mm), all margins 0.
- Physical label default: 100 × 150 mm at 203 dpi = **800 × 1200 dots**.
- Barcodes: gray → 1-bit by threshold (no dithering), applied after any scaling.
- Jobs are processed strictly one at a time, in arrival order.
- Every value that only real hardware can confirm is configurable (see `BridgeOptions`, Task 1).
- Output modes: `tspl` (default), `windows-driver`, `file`.
- The service never crashes on a bad request or an offline printer: the IPP job goes to `aborted` and it is logged.
- No dependency may require installing anything on the iPhone or .NET on the PC.
- The whole test suite runs on macOS (`dotnet test`). Windows-only code is guarded with `OperatingSystem.IsWindows()`.

## Review Focus

1. **Printer off or unreachable** → the job ends as `aborted` within the connect timeout (5 s), the iPhone sees a failure rather than a hang, and the next job still prints. Pinned in Task 5 (`TsplOutput_UnreachablePrinter_ThrowsWithinTimeout`) and Task 6 (`Processor_OutputThrows_JobAbortedAndNextJobRuns`).
2. **iOS sends 24-bit sRGB or 300 dpi raster instead of 8-bit gray at 203 dpi** → it's converted and resampled and prints at the correct physical size. Pinned in Task 3 (`Urf_Rgb24_ConvertsToGray`) and Task 4 (`Map_300DpiInput_ResampledTo203`).
3. **The page has content touching the edges (no white margin) or is blank** → content is scaled down to fit the label without clipping; a blank page yields a blank label, not a crash. Pinned in Task 4 (`Map_ContentWiderThanLabel_ScaledToFit`, `Map_BlankPage_ReturnsBlankLabel`).
4. **Truncated or malformed IPP/raster body** → IPP returns `client-error-bad-request`, or the job is aborted; the service keeps serving. Pinned in Task 2 (`Read_Truncated_Throws`), Task 3 (`Urf_TruncatedStream_ThrowsRasterFormatException`) and Task 7 (`Http_GarbageBody_Returns400Status`).
5. **Two jobs sent back-to-back** → printed in order, never interleaved. Pinned in Task 6 (`Processor_TwoJobs_ProcessedInOrder`).

---

## File Structure

```
LabelBridge.sln
.gitignore
README.md                                  Task 8: install + final hardware test guide
install/install.ps1, install/uninstall.ps1 Task 8
src/LabelBridge/LabelBridge.csproj          Microsoft.NET.Sdk.Web, net10.0
src/LabelBridge/appsettings.json            "Bridge" section = BridgeOptions defaults
src/LabelBridge/Program.cs                  host wiring, `test-print` command (Task 7)
src/LabelBridge/Config/BridgeOptions.cs     all settings (Task 1)
src/LabelBridge/Ipp/IppTag.cs               tag/op/status constants (Task 2)
src/LabelBridge/Ipp/IppModel.cs             IppMessage, IppGroup, IppAttribute, IppValue, IppResolution, IppRange, IppCollection
src/LabelBridge/Ipp/IppReader.cs, IppWriter.cs
src/LabelBridge/Raster/UrfDecoder.cs, PwgDecoder.cs, RasterLine.cs, RasterReader.cs, RasterFormatException.cs (Task 3)
src/LabelBridge/Imaging/GrayImage.cs, MonoBitmap.cs, LabelMapper.cs, Monochrome.cs, PngWriter.cs, TestLabel.cs (Task 4)
src/LabelBridge/Output/ILabelOutput.cs, TsplEncoder.cs, TsplOutput.cs, FileOutput.cs, WindowsDriverOutput.cs, OutputFactory.cs (Task 5)
src/LabelBridge/Jobs/PrintJob.cs, JobStore.cs, JobProcessor.cs (Task 6)
src/LabelBridge/Ipp/PrinterAttributes.cs, IppOperations.cs (Task 6)
src/LabelBridge/Discovery/BonjourAdvertiser.cs (Task 7)
tests/LabelBridge.Tests/...                 mirrors src folders; TestHelpers/RasterEncoders.cs (Task 3)
```

---

### Task 1: Scaffold, configuration

**Files:**
- Create: `.gitignore`, `LabelBridge.sln`, `src/LabelBridge/LabelBridge.csproj`, `src/LabelBridge/Program.cs` (placeholder `app.Run()`), `src/LabelBridge/appsettings.json`, `src/LabelBridge/Config/BridgeOptions.cs`
- Test: `tests/LabelBridge.Tests/Config/BridgeOptionsTests.cs`

**Interfaces:**
- Produces: `BridgeOptions` (namespace `LabelBridge.Config`), bound from configuration section `"Bridge"`:

```csharp
public sealed class BridgeOptions {
    public string PrinterName { get; set; } = "XP-470B Label";   // name shown on iPhone
    public int IppPort { get; set; } = 631;
    public string Output { get; set; } = "tspl";                 // tspl | windows-driver | file
    public string PrinterHost { get; set; } = "192.168.1.100";
    public int PrinterPort { get; set; } = 9100;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public string WindowsPrinterName { get; set; } = "Xprinter XP-470B";
    public string OutputDirectory { get; set; } = "jobs";        // relative to exe dir
    public bool SaveJobs { get; set; } = false;
    public bool DisableBonjour { get; set; } = false;            // tests only
    public int[] AdvertisedResolutions { get; set; } = [203, 300];
    public LabelOptions Label { get; set; } = new();
    public TsplOptions Tspl { get; set; } = new();
    public MediaOptions Media { get; set; } = new();
}
public sealed class LabelOptions { double WidthMm = 100; double HeightMm = 150; int Dpi = 203; string FitMode = "fit" /* fit|crop-center|stretch */; byte Threshold = 160;
    int WidthDots => (int)Math.Round(WidthMm / 25.4 * Dpi); int HeightDots => (int)Math.Round(HeightMm / 25.4 * Dpi); }
public sealed class TsplOptions { double GapMm = 2; int Density = 8; int Speed = 4; int Direction = 1; bool InvertBitmap = false; }
public sealed class MediaOptions { string Name = "iso_a6_105x148mm"; int WidthHundredthsMm = 10500; int HeightHundredthsMm = 14800; }
```
(All members are `{ get; set; }` properties with these defaults; `WidthDots`/`HeightDots` are computed get-only.)

- [ ] **Step 1: Initialize repo and projects**

```bash
git init
dotnet new gitignore
dotnet new sln -n LabelBridge
dotnet new web -n LabelBridge -o src/LabelBridge -f net10.0
dotnet new xunit -n LabelBridge.Tests -o tests/LabelBridge.Tests -f net10.0
dotnet sln add src/LabelBridge tests/LabelBridge.Tests
dotnet add tests/LabelBridge.Tests reference src/LabelBridge
```
Add `jobs/` and `publish/` to `.gitignore`. Delete template sample files (`UnitTest1.cs`, `appsettings.Development.json`).

- [ ] **Step 2: Write the failing test** `BridgeOptionsTests`:
  - `Defaults_MatchSpec`: `new BridgeOptions()` → `Label.WidthDots == 800`, `Label.HeightDots == 1200`, `Media.Name == "iso_a6_105x148mm"`, `Output == "tspl"`, `PrinterPort == 9100`.
  - `Binds_FromAppSettingsJson`: build `ConfigurationBuilder().AddJsonFile(<path to src/LabelBridge/appsettings.json>)`, `.GetSection("Bridge").Get<BridgeOptions>()` → `Tspl.Density == 8`, `Label.FitMode == "fit"`.

- [ ] **Step 3: Run** `dotnet test` → FAIL (`BridgeOptions` not defined).

- [ ] **Step 4: Implement** `BridgeOptions.cs` as above, and write `appsettings.json` with a `"Bridge"` section listing every option and its default, plus `"Logging"`. In the test csproj, copy the app's `appsettings.json` to the output (`<None Include="..\..\src\LabelBridge\appsettings.json" CopyToOutputDirectory="PreserveNewest" />`).

- [ ] **Step 5: Run** `dotnet test` → PASS.

- [ ] **Step 6: Commit** `git add -A && git commit -m "chore: scaffold LabelBridge solution and options"`

---

### Task 2: IPP binary codec

**Files:**
- Create: `src/LabelBridge/Ipp/IppTag.cs`, `IppModel.cs`, `IppReader.cs`, `IppWriter.cs`
- Test: `tests/LabelBridge.Tests/Ipp/IppCodecTests.cs`

**Interfaces:**
- Produces (namespace `LabelBridge.Ipp`):

```csharp
public enum IppTag : byte {
    OperationAttributes = 0x01, JobAttributes = 0x02, EndOfAttributes = 0x03, PrinterAttributes = 0x04, UnsupportedAttributes = 0x05,
    Unsupported = 0x10, Unknown = 0x12, NoValue = 0x13,
    Integer = 0x21, Boolean = 0x22, Enum = 0x23, OctetString = 0x30, DateTime = 0x31, Resolution = 0x32, RangeOfInteger = 0x33,
    BegCollection = 0x34, TextWithLanguage = 0x35, NameWithLanguage = 0x36, EndCollection = 0x37,
    TextWithoutLanguage = 0x41, NameWithoutLanguage = 0x42, Keyword = 0x44, Uri = 0x45, UriScheme = 0x46,
    Charset = 0x47, NaturalLanguage = 0x48, MimeMediaType = 0x49, MemberAttrName = 0x4A }
public static class IppOp { PrintJob = 0x0002, ValidateJob = 0x0004, CreateJob = 0x0005, SendDocument = 0x0006,
    CancelJob = 0x0008, GetJobAttributes = 0x0009, GetJobs = 0x000A, GetPrinterAttributes = 0x000B }   // const short
public static class IppStatus { Ok = 0x0000, OkIgnoredOrSubstituted = 0x0001, BadRequest = 0x0400, NotFound = 0x0406,
    DocumentFormatNotSupported = 0x040A, NotPossible = 0x040C, InternalError = 0x0500, OperationNotSupported = 0x0501 }      // const short
public readonly record struct IppResolution(int X, int Y, byte Units = 3);   // 3 = dots per inch
public readonly record struct IppRange(int Lower, int Upper);
public sealed class IppCollection : List<IppAttribute> { }
public sealed record IppValue(IppTag Tag, object Value);  // Value: int | bool | string | byte[] | IppResolution | IppRange | IppCollection | null (out-of-band)
public sealed class IppAttribute { string Name; List<IppValue> Values; IppAttribute(string name, params IppValue[] values) }
public sealed class IppGroup { IppTag Tag; List<IppAttribute> Attributes; IppAttribute? Find(string name); }
public sealed class IppMessage { byte VersionMajor = 2; byte VersionMinor = 0; short Code; int RequestId; List<IppGroup> Groups;
    IppGroup Group(IppTag tag);              // gets or adds
    IppAttribute? FindOperation(string name); }
public static class IppReader { public static IppMessage Read(Stream s); }   // throws IppFormatException; stops right after end-of-attributes tag so `s` is positioned at document data
public sealed class IppFormatException : Exception
public static class IppWriter { public static byte[] Write(IppMessage m); }
```

Wire format: big-endian. Header = version (2 bytes), code (int16), request-id (int32). Each attribute = value-tag (1), name-length (int16), name, value-length (int16), value; extra values of the same attribute use name-length 0. A collection is `BegCollection` (value-length 0), then for each member a `MemberAttrName` value whose value is the member name followed by the member's value(s) with name-length 0, then `EndCollection` (name-length 0, value-length 0). Value encodings: Integer/Enum int32; Boolean 1 byte; Resolution int32 x, int32 y, int8 units; RangeOfInteger two int32; strings UTF-8.

- [ ] **Step 1: Write failing tests**
  - `RoundTrip_AllValueTypes`: build a message containing every value type (including a nested collection `media-col { media-size { x-dimension 10500, y-dimension 14800 }, media-top-margin 0 }` and a 2-value keyword attribute), `Write` then `Read` → structurally equal (compare name, tags, values).
  - `Read_KnownGetPrinterAttributesRequest`: decode the hex fixture below. Expect version 2.0, code `0x000B`, request-id 1, operation group with `attributes-charset`=`utf-8`, `attributes-natural-language`=`en`, `printer-uri`=`ipp://localhost/ipp/print`.
    ```
    0200000B00000001 01
    47 0012 "attributes-charset" 0005 "utf-8"
    48 001B "attributes-natural-language" 0002 "en"
    45 000B "printer-uri" 0019 "ipp://localhost/ipp/print"
    03
    ```
  - `Read_LeavesStreamAtDocumentData`: the fixture followed by bytes `DE AD` → after `Read`, the next 2 bytes of the stream are `DE AD`.
  - `Read_Truncated_Throws`: the fixture cut off in the middle of an attribute → `IppFormatException`.

- [ ] **Step 2: Run** `dotnet test --filter IppCodecTests` → FAIL.
- [ ] **Step 3: Implement** the types, `IppReader`, and `IppWriter` as specified.
- [ ] **Step 4: Run** `dotnet test --filter IppCodecTests` → PASS.
- [ ] **Step 5: Commit** `git commit -am "feat: IPP message codec"` (with `git add` of the new files)

---

### Task 3: Raster decoders (URF + PWG)

**Files:**
- Create: `src/LabelBridge/Imaging/GrayImage.cs`, `src/LabelBridge/Raster/RasterLine.cs`, `UrfDecoder.cs`, `PwgDecoder.cs`, `RasterReader.cs`, `RasterFormatException.cs`
- Test: `tests/LabelBridge.Tests/Raster/RasterDecoderTests.cs`, `tests/LabelBridge.Tests/TestHelpers/RasterEncoders.cs`

**Interfaces:**
- Produces:

```csharp
namespace LabelBridge.Imaging;
public sealed record GrayImage(int Width, int Height, int Dpi, byte[] Pixels) {  // row-major, 0 = black, 255 = white
    public static GrayImage White(int w, int h, int dpi);
    public byte this[int x, int y] { get; set; } }

namespace LabelBridge.Raster;
public static class RasterReader { public static IEnumerable<GrayImage> ReadPages(Stream s, string mimeType); } // dispatches to UrfDecoder / PwgDecoder; else throws RasterFormatException
public static class UrfDecoder { public static IEnumerable<GrayImage> Decode(Stream s); }
public static class PwgDecoder { public static IEnumerable<GrayImage> Decode(Stream s); }
internal static class RasterLine { static void DecodePage(Stream s, int width, int height, int bytesPerPixel, Span<byte> grayOut); }
```
Pages are decoded lazily (one page in memory at a time).

Formats:
- **URF:** file header is 8 bytes `"UNIRAST\0"` plus a uint32 page count. Each page has a 32-byte header: `bpp u8, colorspace u8, duplex u8, quality u8, reserved u32, reserved u32, width u32, height u32, dpi u32, reserved u32, reserved u32`. Use `bpp` (8 = gray, 24 = RGB) to interpret pixels and ignore colorspace.
- **PWG:** sync word `"RaS2"`. Each page has a 1796-byte header (big-endian). The fields used are: HWResolution x/y at offsets 276/280, cupsWidth at 372, cupsHeight at 376, cupsBitsPerColor at 384, cupsBitsPerPixel at 388 (8 = gray, 24 = RGB), cupsBytesPerLine at 392. Read pages until end of stream.
- **Compressed data (both formats):** for each line group, one byte `r` means the line repeats `r+1` times. Pixel codes then follow until the line is full: `c` 0–127 means the next pixel repeats `c+1` times; `c` 129–255 means `257−c` literal pixels follow; `c` = 128 fills the rest of the line with white.
- **RGB to gray:** `(299 R + 587 G + 114 B) / 1000`.

- [ ] **Step 1: Write the test helpers** `RasterEncoders.EncodeUrf(IEnumerable<(byte[] pixels, int w, int h, int dpi, int bpp)>)` and `EncodePwg(...)`, which produce valid compressed streams. Use repeat runs for runs of 2 or more and literal runs otherwise, and line repeats for identical lines.
- [ ] **Step 2: Write failing tests**
  - `Urf_TwoGrayPages_DecodedExactly`: two 40×30 pages at 203 dpi (page 1 is a gradient, page 2 is a black rectangle on white) → 2 `GrayImage`s with identical pixels, `Dpi == 203`.
  - `Urf_Rgb24_ConvertsToGray`: one 10×10 page with a red (255,0,0) pixel at (3,4) and the rest white → pixel (3,4) == 76, others 255.
  - `Urf_FillCode128_FillsWhite`: a hand-built line `00 | 00 00 | 80` for width 5 → `[0,255,255,255,255]`.
  - `Pwg_GrayPage_ReadsResolutionAndSize`: one 50×20 page at 300 dpi → `Width 50, Height 20, Dpi 300`, pixels exact.
  - `Urf_TruncatedStream_ThrowsRasterFormatException`: encoded stream with its last 10 bytes cut → enumerating throws `RasterFormatException`.
  - `RasterReader_UnknownMime_Throws`: `"application/pdf"` → `RasterFormatException`.
- [ ] **Step 3: Run** `dotnet test --filter RasterDecoderTests` → FAIL.
- [ ] **Step 4: Implement** the decoders as specified.
- [ ] **Step 5: Run** `dotnet test --filter RasterDecoderTests` → PASS.
- [ ] **Step 6: Commit** `feat: URF and PWG raster decoders`

---

### Task 4: Imaging — label mapping, 1-bit, PNG, test label

**Files:**
- Create: `src/LabelBridge/Imaging/MonoBitmap.cs`, `LabelMapper.cs`, `Monochrome.cs`, `PngWriter.cs`, `TestLabel.cs`
- Test: `tests/LabelBridge.Tests/Imaging/LabelMapperTests.cs`, `MonochromeTests.cs`, `PngWriterTests.cs`

**Interfaces:**
- Consumes: `GrayImage` (Task 3), `LabelOptions` (Task 1).
- Produces:

```csharp
public sealed record MonoBitmap(int Width, int Height, byte[] Bits) {   // bit = 1 means black; MSB = leftmost pixel; rows padded to WidthBytes
    public int WidthBytes => (Width + 7) / 8;
    public bool IsBlack(int x, int y);
    public GrayImage ToGray(int dpi); }
public static class LabelMapper { public static GrayImage Map(GrayImage page, LabelOptions o); }       // returns o.WidthDots × o.HeightDots at o.Dpi
public static class Monochrome { public static MonoBitmap Pack(GrayImage img, byte threshold); }        // pixel < threshold → black
public static class PngWriter { public static void Write(GrayImage img, Stream s); }                    // 8-bit grayscale PNG, ZLibStream for IDAT
public static class TestLabel { public static GrayImage Create(LabelOptions o); }
```

`LabelMapper.Map` algorithm:
1. If `page.Dpi != o.Dpi`, resample by factor `o.Dpi / page.Dpi`. When downscaling, use a box filter that averages the source pixels covering each target pixel; when upscaling, use nearest neighbor.
2. `fit` (default):
   - Find the bounding box of pixels `< o.Threshold`. If there are none, return a white label.
   - If the box fits within the label, copy it 1:1, centered.
   - Otherwise, scale the box down uniformly by `min(labelW/boxW, labelH/boxH)` with the box filter, then center it.
3. `crop-center`: don't trim. Center the whole resampled page on the label and cut overflow equally from opposite sides.
4. `stretch`: resample the whole page directly to the label's width × height.

`TestLabel.Create` draws on a white label: a 4-dot black border; 20×20-dot black squares in each corner; and in the middle third, vertical bars with widths 2, 4, 6 and 8 dots alternating with equal gaps.

- [ ] **Step 1: Write failing tests** (all use `new LabelOptions()` defaults: 800×1200 @203 unless noted)
  - `Map_A6PageWithMargins_CopiedOneToOneCentered`: an 840×1184 white page at 203 dpi with a black 700×1100 rectangle at (70,42) → output 800×1200. The rectangle is at (50,50) with size 700×1100, exact.
  - `Map_ContentWiderThanLabel_ScaledToFit`: an 840×1184 page that is entirely black → output 800×1200. Scale = 800/840, so the content becomes 800×≈1128, centered with top = 36. Assert columns 0 and 799 are black in the middle row, the black band starts at row 36±1 and ends at row 1163±1, and the rows outside it are white.
  - `Map_300DpiInput_ResampledTo203`: a 1240×1748 page at 300 dpi with a black 300×300 square at (600,600) → black square about 203×203 (±2) in the output.
  - `Map_BlankPage_ReturnsBlankLabel`: an all-white page → every output pixel is 255.
  - `Map_CropCenter_NoScaling`: an 840×1184 page, `FitMode="crop-center"`, with a black 1-pixel column at x=20 and another at x=819 → output columns 0 and 799 are black (20 columns cropped from each side; vertically, 8 rows of white are added on each side).
  - `Pack_Threshold`: pixels `[0,159,160,255]`, threshold 160 → black, black, white, white. With `Bits[0] == 0b1100_0000`.
  - `Png_HasSignatureAndIhdr`: write a 3×2 image → starts with `89 50 4E 47 0D 0A 1A 0A`; IHDR width 3, height 2, bit depth 8, color type 0.
  - `TestLabel_HasBorderAndCorners`: `Create` → pixel (0,0) is black and pixel (400,100) is white.
- [ ] **Step 2: Run** `dotnet test --filter "LabelMapperTests|MonochromeTests|PngWriterTests"` → FAIL.
- [ ] **Step 3: Implement** the code as specified.
- [ ] **Step 4: Run** the tests → PASS.
- [ ] **Step 5: Commit** `feat: label mapping, 1-bit packing, PNG writer, test label`

---

### Task 5: Outputs — TSPL, file, Windows driver

**Files:**
- Create: `src/LabelBridge/Output/ILabelOutput.cs`, `TsplEncoder.cs`, `TsplOutput.cs`, `FileOutput.cs`, `WindowsDriverOutput.cs`, `OutputFactory.cs`
- Modify: `src/LabelBridge/LabelBridge.csproj` (`dotnet add src/LabelBridge package System.Drawing.Common`)
- Test: `tests/LabelBridge.Tests/Output/TsplEncoderTests.cs`, `TsplOutputTests.cs`, `FileOutputTests.cs`

**Interfaces:**
- Consumes: `MonoBitmap`, `PngWriter` (Task 4); `BridgeOptions` (Task 1).
- Produces:

```csharp
public interface ILabelOutput { Task PrintAsync(IReadOnlyList<MonoBitmap> labels, string jobName, CancellationToken ct); }  // throws on failure
public static class TsplEncoder { public static byte[] Encode(IReadOnlyList<MonoBitmap> labels, LabelOptions label, TsplOptions t); }
public sealed class TsplOutput(BridgeOptions o) : ILabelOutput      // TCP to PrinterHost:PrinterPort; connect timeout ConnectTimeoutMs
public sealed class FileOutput(string directory, int dpi) : ILabelOutput // writes {jobName}-label-{n:D2}.png (n from 1)
public sealed class WindowsDriverOutput(BridgeOptions o) : ILabelOutput // [SupportedOSPlatform("windows")]
public static class OutputFactory { public static ILabelOutput Create(BridgeOptions o, string baseDir); }  // "tspl" | "windows-driver" | "file"; unknown → ArgumentException
```

TSPL bytes for each label: ASCII lines ending in `\r\n`, using invariant culture with no decimals when the value is whole.
```
SIZE 100 mm,150 mm
GAP 2 mm,0 mm
DIRECTION 1
DENSITY 8
SPEED 4
CLS
BITMAP 0,0,<WidthBytes>,<Height>,0,<raw bytes>
PRINT 1,1
```
- Header lines come from the options.
- The raw bitmap bytes are followed by `\r\n` before `PRINT`.
- **Polarity:** by default, a TSPL bit value of 0 means a black dot, so the encoder writes `~MonoBitmap.Bits`. When `InvertBitmap = true`, it writes `Bits` as-is.

`WindowsDriverOutput`: a `PrintDocument` with `PrinterSettings.PrinterName = o.WindowsPrinterName`.
- Paper: `PaperSize("Label", 394, 591)` (100×150 mm in hundredths of an inch), zero margins, `OriginAtMargins = false`.
- One page per label via `HasMorePages`. Each label is drawn as a 1-bpp `Bitmap` with `SetResolution(203,203)`, scaled to the full page bounds, with `InterpolationMode.NearestNeighbor`.
- Throws `InvalidOperationException` if the printer name is not installed (`PrinterSettings.IsValid`).

- [ ] **Step 1: Write failing tests**
  - `Encode_SingleLabel_ExactBytes`: a 16×2 bitmap with `Bits = [0xFF,0x00, 0x0F,0xF0]`, `LabelOptions{WidthMm=100,HeightMm=150}`, default `TsplOptions` → bytes equal `"SIZE 100 mm,150 mm\r\nGAP 2 mm,0 mm\r\nDIRECTION 1\r\nDENSITY 8\r\nSPEED 4\r\nCLS\r\nBITMAP 0,0,2,2,0,"` + `[0x00,0xFF,0xF0,0x0F]` + `"\r\nPRINT 1,1\r\n"`.
  - `Encode_InvertBitmap_WritesBitsAsIs`: same bitmap with `InvertBitmap=true` → the data bytes are `[0xFF,0x00,0x0F,0xF0]`.
  - `Encode_ThreeLabels_ThreePrintCommands`: count of `"PRINT 1,1"` == 3 and count of `"CLS"` == 3.
  - `TsplOutput_SendsBytesToPrinter`: a `TcpListener` on loopback with an ephemeral port; `PrintAsync` with one label → the listener receives exactly `TsplEncoder.Encode(...)`.
  - `TsplOutput_UnreachablePrinter_ThrowsWithinTimeout`: `PrinterHost="10.255.255.1"`, `ConnectTimeoutMs=500` → throws in under 2 s.
  - `FileOutput_WritesOnePngPerLabel`: 2 labels, jobName `"job-7"` → `job-7-label-01.png` and `job-7-label-02.png` exist.
  - `Factory_UnknownMode_Throws`.
- [ ] **Step 2: Run** `dotnet test --filter "Tspl|FileOutput|Factory"` → FAIL.
- [ ] **Step 3: Implement** the code as specified. `TsplOutput` uses `TcpClient.ConnectAsync` with a `CancellationTokenSource(ConnectTimeoutMs)`, then writes everything, flushes and closes. `WindowsDriverOutput` has no unit test because it's Windows-only; it's checked in the final hardware test.
- [ ] **Step 4: Run** → PASS (`dotnet build` must have no errors; CA1416 warnings are only allowed inside `WindowsDriverOutput`).
- [ ] **Step 5: Commit** `feat: TSPL, file and Windows driver outputs`

---

### Task 6: Printer attributes, IPP operations, job queue

**Files:**
- Create: `src/LabelBridge/Ipp/PrinterAttributes.cs`, `src/LabelBridge/Ipp/IppOperations.cs`, `src/LabelBridge/Jobs/PrintJob.cs`, `JobStore.cs`, `JobProcessor.cs`
- Test: `tests/LabelBridge.Tests/Ipp/IppOperationsTests.cs`, `tests/LabelBridge.Tests/Jobs/JobProcessorTests.cs`

**Interfaces:**
- Consumes: the IPP model (Task 2), `RasterReader` (Task 3), `LabelMapper`/`Monochrome`/`PngWriter` (Task 4), `ILabelOutput` (Task 5), `BridgeOptions`.
- Produces:

```csharp
public enum JobState { Pending = 3, Processing = 5, Canceled = 7, Aborted = 8, Completed = 9 }
public sealed class PrintJob { int Id; string Name; string UserName; string Format; byte[] Document; JobState State; string? Error; DateTimeOffset Created; DateTimeOffset? Completed; }
public sealed class JobStore { PrintJob Add(string name, string user, string format, byte[] doc); PrintJob? Get(int id); IReadOnlyList<PrintJob> All(); bool IsBusy { get; } }  // ids from 1, thread-safe; keeps last 50 jobs; Document is released (set to empty) once the job finishes
public sealed class JobProcessor(JobStore store, Func<ILabelOutput> output, BridgeOptions o, ILogger<JobProcessor> log, string saveDir) : BackgroundService {
    public void Enqueue(PrintJob job);                 // Channel<PrintJob>, single reader
    public Task ProcessAsync(PrintJob job, CancellationToken ct); }   // public for tests: decode → map → pack → (SaveJobs: PNGs) → output; sets State Completed or Aborted+Error; never throws
public static class PrinterAttributes { public static IppGroup Build(BridgeOptions o, string printerUri, Guid uuid, JobStore store, TimeSpan uptime); }
public sealed class IppOperations(BridgeOptions o, JobStore store, JobProcessor processor, Guid uuid, DateTimeOffset startedAt) {
    public IppMessage Handle(IppMessage request, Stream documentData, string printerUri); }
```

`PrinterAttributes.Build` must include these (name → value):
- **Charset and language:** `charset-configured`=utf-8; `charset-supported`=utf-8; `natural-language-configured`=en; `generated-natural-language-supported`=en.
- **Protocol:** `ipp-versions-supported`=1.1,2.0; `operations-supported`=the 8 `IppOp` codes minus CreateJob and SendDocument.
- **Identity:** `printer-uri-supported`=printerUri; `uri-security-supported`=none; `uri-authentication-supported`=none; `printer-name`=PrinterName; `printer-info`=PrinterName; `printer-make-and-model`="Xprinter XP-470B (LabelBridge)"; `printer-uuid`="urn:uuid:{uuid}".
- **State:** `printer-state`=3 idle, or 4 if `store.IsBusy`; `printer-state-reasons`=none; `printer-is-accepting-jobs`=true; `queued-job-count`; `printer-up-time` (seconds).
- **Formats:** `document-format-supported`=image/urf,image/pwg-raster; `document-format-default`=image/urf; `pdl-override-supported`=attempted; `compression-supported`=none.
- **URF/PWG capabilities:** `urf-supported`=V1.4,CP1,W8,SRGB24,RS{join("-", AdvertisedResolutions)},IS1,MT1; `pwg-raster-document-resolution-supported`=each resolution; `pwg-raster-document-type-supported`=sgray_8,srgb_8; `printer-resolution-supported`=each resolution; `printer-resolution-default`=first resolution.
- **Color, copies and sides:** `print-color-mode-supported`=monochrome; `print-color-mode-default`=monochrome; `color-supported`=false; `copies-supported`=1–1; `copies-default`=1; `sides-supported`=one-sided; `sides-default`=one-sided.
- **Orientation and quality:** `orientation-requested-supported`=3; `orientation-requested-default`=3; `print-quality-supported`=4; `print-quality-default`=4.
- **Media:** `media-supported`, `media-default` and `media-ready` = Media.Name. `media-col-default`, `media-col-ready` and `media-col-database` = one collection containing `media-size{x-dimension, y-dimension}`, the four margins = 0, `media-source`=main and `media-type`=labels. `media-{bottom,left,right,top}-margin-supported`=0. `media-source-supported`=main; `media-type-supported`=labels.
- **Ready flags:** `printer-kind`=labels; `multiple-document-jobs-supported`=false.

`IppOperations.Handle` rules:
- **Every response:** echo request-id; operation group has `attributes-charset`=utf-8 and `attributes-natural-language`=en.
- **Validation:** missing charset or printer-uri → `BadRequest`. Unknown op → `OperationNotSupported`.
- **Get-Printer-Attributes:** honor `requested-attributes`. `all` or absent → everything; otherwise only the named attributes. The group name `media-col-database` is included only when named or `all`.
- **Validate-Job / Print-Job:** if `document-format` is present and not in the supported list, and not `application/octet-stream`, → `DocumentFormatNotSupported`. `application/octet-stream` is treated as `image/urf`.
- **Print-Job:** read all of `documentData`, then `store.Add` and `processor.Enqueue`. Reply with job group `job-id`, `job-uri`=`{printerUri}/{id}`, `job-state`, `job-state-reasons`=none.
- **Get-Job-Attributes:** `job-id` must exist, else `NotFound`. Returns `job-id`, `job-uri`, `job-name`, `job-state`, `job-state-reasons` (aborted → `aborted-by-system`, completed → `job-completed-successfully`, otherwise none) and `job-printer-uri`.
- **Get-Jobs:** one job group per job (same fields).
- **Cancel-Job:** a pending job → Canceled; finished → `NotPossible`; unknown → `NotFound`.

- [ ] **Step 1: Write failing tests** (use a `FakeOutput : ILabelOutput` that records labels, can be told to throw, and can be blocked with a `TaskCompletionSource`).
  - `GetPrinterAttributes_All_ContainsAirPrintEssentials`: response status Ok; contains `document-format-supported`=[image/urf, image/pwg-raster], `urf-supported` containing `"RS203-300"`, `media-default`=`iso_a6_105x148mm`, `media-top-margin-supported`=0, and `printer-uuid` starting with `urn:uuid:`.
  - `GetPrinterAttributes_Requested_ReturnsOnlyThose`: requested `printer-state`, `printer-name` → exactly those 2 attributes.
  - `PrintJob_Pdf_RejectedFormat`: `document-format`=application/pdf → status 0x040A.
  - `PrintJob_Urf_ReturnsJobIdAndEnqueues`: job-id 1, store has 1 job with format `image/urf`.
  - `UnknownOperation_NotSupported`: code 0x003C → 0x0501.
  - `MissingCharset_BadRequest`.
  - `Processor_UrfTwoPages_TwoLabelsToOutput`: a URF with 2 A6 pages (840×1184 @203) → the output receives 2 `MonoBitmap`s of 800×1200; job Completed.
  - `Processor_OutputThrows_JobAbortedAndNextJobRuns`: the first job's output throws `IOException` → job 1 Aborted with Error set; job 2 Completed.
  - `Processor_CorruptRaster_JobAborted`: random bytes → Aborted, no exception escapes.
  - `Processor_TwoJobs_ProcessedInOrder`: enqueue job A (output blocked) and then job B → B stays Pending until A is released; the output's call order is A, B.
  - `Processor_SaveJobs_WritesPngs`: `SaveJobs=true` → in `saveDir`: `job-1-page-01.png` (as received) and `job-1-label-01.png`.
- [ ] **Step 2: Run** `dotnet test --filter "IppOperationsTests|JobProcessorTests"` → FAIL.
- [ ] **Step 3: Implement** the code as specified.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat: IPP printer attributes, operations and sequential job processor`

---

### Task 7: HTTP host, Bonjour, service wiring, test-print

**Files:**
- Create: `src/LabelBridge/Discovery/BonjourAdvertiser.cs`
- Modify: `src/LabelBridge/Program.cs`, `src/LabelBridge/LabelBridge.csproj` (packages: `Makaretu.Dns.Multicast.New` 0.38.0, `Microsoft.Extensions.Hosting.WindowsServices`, `Serilog.AspNetCore`, `Serilog.Sinks.File`, `Serilog.Sinks.EventLog`)
- Test: `tests/LabelBridge.Tests/Http/IppEndpointTests.cs`, `tests/LabelBridge.Tests/Discovery/TxtRecordTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:

```csharp
public sealed class BonjourAdvertiser(BridgeOptions o, Guid uuid, ILogger<BonjourAdvertiser> log) : IHostedService {
    public static IReadOnlyDictionary<string,string> BuildTxt(BridgeOptions o, Guid uuid); }
public static class AppBuilder { public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null); }  // in Program.cs; tests use configure to swap output & port
public static class PrinterIdentity { public static Guid LoadOrCreate(string path); }   // persists uuid in "printer-id.txt" next to exe
```

**Bonjour:**
- `ServiceProfile(instanceName: o.PrinterName, serviceName: "_ipp._tcp", port: (ushort)o.IppPort)` with `Subtypes.Add("_universal")`, advertised via `ServiceDiscovery.Advertise` and then `Announce`. `Unadvertise` on stop.
- `BuildTxt` keys:
  - `txtvers=1`, `qtotal=1`, `rp=ipp/print`, `ty={PrinterName}`, `product=(LabelBridge)`
  - `pdl=image/urf,image/pwg-raster`, `URF=V1.4,CP1,W8,SRGB24,RS203-300,IS1,MT1` (same list as `urf-supported`)
  - `Color=F`, `Duplex=F`, `UUID={uuid}`, `kind=label`, `note=`, `priority=50`
  - `adminurl=http://{hostname}.local:{port}/`

**HTTP:**
- Kestrel listens on `IPAddress.Any:{IppPort}`.
- `POST /ipp/print` (and `POST /` as an alias) with content-type `application/ipp`: `IppReader.Read(Request.Body)`, then `IppOperations.Handle(req, Request.Body, $"ipp://{Request.Host}/ipp/print")`, then write `IppWriter.Write(resp)` with content-type `application/ipp`. Set `AllowSynchronousIO = false`: buffer the body with `Request.EnableBuffering()` or copy it to a `MemoryStream` first (bodies are under ~20 MB).
- `IppFormatException` → HTTP 400.
- `GET /` → a plain-text status page: printer name, output mode, printer host, and the last 10 jobs with state.

**Service and logging:**
- `builder.Host.UseWindowsService(o => o.ServiceName = "LabelBridge")`.
- Content root = `AppContext.BaseDirectory`.
- Serilog to `logs/labelbridge-.log` (rolling daily, keep 14) next to the exe, plus the console, plus Windows Event Log (source `LabelBridge`, level Error, only when `OperatingSystem.IsWindows()`).
- Log every IPP request at Information: op name, request-id, job-id, status.

**`test-print` command:** if `args[0] == "test-print"`, build the configuration only, `TestLabel.Create` → `Monochrome.Pack` → `OutputFactory.Create(...).PrintAsync`. Print "OK" or the error, and exit with code 0 or 1. No web host.

- [ ] **Step 1: Write failing tests**
  - `TxtRecord_HasAirPrintKeys`: `BuildTxt` → `rp=ipp/print`, `pdl` contains `image/urf`, `URF` contains `W8`, `UUID` equals the guid.
  - `Http_GetPrinterAttributes_RoundTrip`: start the app via `AppBuilder.Build(["--Bridge:IppPort=0", "--Bridge:Output=file"], …)` on an ephemeral port; POST an encoded Get-Printer-Attributes → 200, `application/ipp`, status Ok, `printer-name` present.
  - `Http_PrintJobUrf_WritesLabelPngs`: POST a Print-Job with a 2-page A6 URF body (chunked: send with `HttpContent` without a Content-Length) → poll Get-Job-Attributes until state 9 (timeout 10 s) → two `job-1-label-*.png` files exist in the temp OutputDirectory.
  - `Http_GarbageBody_Returns400Status`: POST 5 random bytes → 400; a following Get-Printer-Attributes still returns 200.
  - Tests pass `--Bridge:DisableBonjour=true`.
- [ ] **Step 2: Run** `dotnet test --filter "IppEndpointTests|TxtRecordTests"` → FAIL.
- [ ] **Step 3: Implement** the code as specified.
- [ ] **Step 4: Run** the full suite with `dotnet test` → all PASS.
- [ ] **Step 5: Manual Mac check (IPP + Bonjour).** Run `sudo dotnet run --project src/LabelBridge -- --Bridge:Output=file --Bridge:SaveJobs=true` (port 631 needs root on macOS; or use `--Bridge:IppPort=8631`). Then:
  - `ippfind _ipp._tcp,_universal` lists the printer.
  - `ipptool -tv ipp://localhost:8631/ipp/print get-printer-attributes.test` passes.
  - On the Mac, System Settings → Printers → Add → the bridge appears. Printing an A6 PDF from Preview → `jobs/job-N-label-01.png` shows the label full-size.
  - Record the results in the commit message.
- [ ] **Step 6: Commit** `feat: HTTP IPP endpoint, Bonjour advertising, Windows service host, test-print`

---

### Task 8: Windows packaging, install scripts, user guide

**Files:**
- Create: `install/install.ps1`, `install/uninstall.ps1`, `README.md`
- Modify: `src/LabelBridge/LabelBridge.csproj` (`RuntimeIdentifier` win-x64 for publish profile, `PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`)

**Interfaces:**
- Consumes: the published `LabelBridge.exe`, `appsettings.json`.

`install.ps1` (run as Administrator from the publish folder):
1. Require admin; otherwise exit with a message.
2. Copy the folder contents to `C:\Program Files\LabelBridge\`, keeping an existing `appsettings.json` and `printer-id.txt`.
3. `sc.exe create LabelBridge binPath= "C:\Program Files\LabelBridge\LabelBridge.exe" start= auto DisplayName= "LabelBridge AirPrint"`, and `sc.exe failure LabelBridge reset= 86400 actions= restart/5000/restart/5000/restart/5000`.
4. Firewall: `New-NetFirewallRule -DisplayName "LabelBridge IPP" -Direction Inbound -Protocol TCP -LocalPort 631 -RemoteAddress LocalSubnet -Action Allow`, and the same for UDP 5353 ("LabelBridge mDNS"). Rules are re-created if they already exist.
5. If `Get-NetConnectionProfile` shows `NetworkCategory -eq 'Public'`, print a warning explaining how to switch the network to Private.
6. Start the service and print its status, plus the URL `http://<pc-ip>:631/` for the status page.

`uninstall.ps1` stops and deletes the service and removes the firewall rules. It leaves the folder in place.

The `README.md` (written for the user) covers:
- **Build:** `dotnet publish src/LabelBridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64`. Copy `publish/win-x64` and `install/` to the PC.
- **Configure:** edit `appsettings.json`. The minimum is `PrinterHost` (the XP-470B's static IP), plus `WindowsPrinterName` if you use driver mode.
- **Install:** right-click PowerShell → Run as Administrator → `.\install.ps1`.
- **Final hardware test checklist** (spec acceptance checks, in order):
  1. `LabelBridge.exe test-print` (tspl mode) prints the test label. If nothing prints, set `"Output": "windows-driver"` and retry. If the label is inverted (black background), set `Tspl.InvertBitmap` to true. If it's upside down, change `Tspl.Direction`.
  2. Check that the iPhone print sheet lists the printer.
  3. Print a single Shopee label and scan its barcode.
  4. Print a 5-label PDF.
  5. With the printer off, print → the job fails on the iPhone. Turn the printer on and print again → it works.
  6. Leave it running for several days.
- **Tuning table:** each config key, its effect and its default.
- **Troubleshooting:** printer not shown on iPhone (network profile, firewall, the same Wi-Fi, check `logs/`); label too light or dark (`Density`); barcode too thin or thick (`Threshold`); sending `jobs/` PNGs with `SaveJobs=true`.

- [ ] **Step 1: Add publish settings** to the csproj and run `dotnet publish src/LabelBridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64` → expect `publish/win-x64/LabelBridge.exe` and `appsettings.json` to exist.
- [ ] **Step 2: Write** `install.ps1`, `uninstall.ps1` and `README.md` as specified.
- [ ] **Step 3: Verify** that the PowerShell scripts parse. If `pwsh` is installed: `pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile('install/install.ps1',[ref]$null,[ref]$e); $e"` → no errors. Otherwise, review them by hand and say so in the commit message.
- [ ] **Step 4: Run** the full test suite again with `dotnet test` → PASS.
- [ ] **Step 5: Commit** `feat: Windows packaging, installer scripts and user guide`

The final hardware test on the real PC, printer and iPhone is done by the user, following the README checklist. It is not part of automated execution.

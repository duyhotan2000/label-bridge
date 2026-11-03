# AirPrint Label Bridge for Xprinter XP-470B — Design

Date: 2026-11-03

## Intent and why

A seller packs Shopee orders and needs to print Shopee AWB (shipping) labels
from an iPhone onto an **Xprinter XP-470B** 4-inch thermal label printer.

Today the printer can only be used from the Windows PC. iPhones print only to
AirPrint printers, and the XP-470B is not one. The goal is: on the iPhone, in
the **Shopee Seller app** or on a **downloaded PDF**, tap Share → Print, pick
the XP-470B, and the label comes out — **with no extra app on the iPhone**.

The way to get there: a background program (Windows Service) on the
always-on Windows PC that pretends to be an AirPrint printer on the LAN,
receives the print job from the iPhone, and forwards it to the XP-470B.

Why build rather than buy (O'Print, Presto, etc.): assumed — free, small,
tuned for exactly this printer and label size. (Claude's guess; not
contradicted by the user.)

## Environment (facts from the user)

- Printer: Xprinter XP-470B, **connected to the LAN** (same Wi-Fi network as
  the PC and iPhone). It already prints PDF labels correctly from the Windows
  PC via the Xprinter driver.
- Windows PC: **x64 (Intel)**, **always on and awake** during packing.
- iPhone on the same LAN.
- Jobs are both **single labels and multi-page PDFs** (many labels, one per
  page); all pages must print in one go, one label per page.
- Developer works on a Mac; the user will read/maintain the code.
- **Shopee AWB PDFs are A6 pages (105×148 mm)**, one label per page.
- Printing from the Shopee Seller app on iOS opens the **standard iOS print
  sheet**, so AirPrint applies there as well as to PDFs in Files.
- The printer has a **static IP address**, so a fixed IP in config is fine.
- **No hardware access during development.** The build is done first, and
  testing on the real printer/PC/iPhone is the final step. The design must
  therefore not depend on early hardware experiments (see "Built-in
  tolerance for unknowns").

## Goals

1. The XP-470B appears in the iOS print dialog automatically (AirPrint
   discovery) — no iPhone app, no manual IP entry.
2. Printing a Shopee AWB label from the Shopee Seller app or from a PDF in
   Files produces a **full-size 100×150 mm label with a scannable barcode**.
3. Multi-page jobs print every page, one label per page, in order.
4. Runs unattended as a Windows Service that starts at boot, no window, no
   login required.
5. Simple configuration file for printer IP, label size, darkness, speed, and
   the printer name shown on the iPhone.

## Non-goals

- Printing from outside the LAN; authentication/passwords.
- Supporting other printers or paper sizes beyond the configured label size.
- A GUI, print-queue viewer, or job history UI.
- Color, duplex, or A4 documents.
- macOS/Linux deployment (the Mac is only a development machine).

## Anti-goals (would be failure even if it "works")

- Labels shrunk onto a page, offset, clipped, or rotated wrongly.
- Barcodes blurred/dithered so couriers can't scan them.
- Needing to install anything on the iPhone.
- Needing to restart the PC or service regularly to keep it discoverable.

## Decided: how it gets made

These were decided with the user:

- **Language/runtime: C#**, .NET 10 (LTS; .NET 8 support ends 2026-11-10 — Claude's call), target **win-x64**.
- Runs as a **Windows Service** on the always-on PC (the PC is the bridge; a
  Raspberry Pi was offered and declined).

These are Claude's calls (user did not object, but builder may revisit with
good reason):

- Published as a **single self-contained .exe** (no .NET install on the PC).
- **Raster-only input from iOS.** The bridge advertises only raster formats
  (`image/urf` and `image/pwg-raster`), **not** `application/pdf`, so iOS
  renders the PDF itself and sends page images. This avoids any PDF library.
- **Output via raw TSPL to the printer's network port (TCP 9100)**, not via
  the Windows driver. Each page image → 1-bit bitmap → TSPL `BITMAP` command →
  `PRINT 1`.
  - Fallback if the XP-470B turns out not to accept TSPL over 9100 (verify
    first — see Risks): print the page image through the installed Windows
    driver with `System.Drawing.Printing`.
- **Black/white conversion uses a threshold, not dithering**, to keep
  barcodes crisp. Threshold is configurable.
- Config in a JSON file next to the .exe (e.g. `appsettings.json`):
  printer IP/port, printer display name, label width/height (default
  100×150 mm), gap, DPI (203), density/darkness, speed, threshold,
  print direction, IPP listen port (default 631).
- Logging to a rolling file next to the .exe (and errors to Windows Event
  Log), enough to diagnose "iPhone can't see printer" and "job received but
  nothing printed".
- An install script (PowerShell) that registers the service, sets it to
  auto-start/restart on failure, and adds **Windows Firewall** rules for the
  IPP port (TCP 631) and mDNS (UDP 5353), restricted to the local subnet.

## How it works

```
iPhone ──(mDNS discovery)──► Bridge advertises "_ipp._tcp" + "_universal" subtype
iPhone ──(IPP over HTTP :631, raster pages)──► Bridge
Bridge ──(decode URF/PWG raster → 1-bit → TSPL)──► XP-470B :9100
```

### 1. Discovery (Bonjour / DNS-SD)

- Advertise service `_ipp._tcp` on the listen port, with subtype
  `_universal._sub._ipp._tcp` (iOS requires this to list the printer).
- TXT record must include at least: `txtvers=1`, `qtotal=1`, `rp=ipp/print`,
  `ty=<display name>`, `pdl=image/urf,image/pwg-raster`,
  `URF=<capabilities consistent with the IPP attributes>`, `Color=F`,
  `Duplex=F`, `UUID=<stable uuid>`, `kind=label` (optional), `adminurl`
  (optional).
- Must coexist with Windows' own mDNS responder (Windows 10/11 has one on
  UDP 5353). Mechanism is the builder's call (Win32 `DnsServiceRegister`
  from dnsapi.dll vs. a managed mDNS library) — whichever reliably publishes
  the subtype and survives network changes/sleep-less long uptimes.
- The UUID is generated once and persisted, so the iPhone sees the same
  printer across restarts.

### 2. IPP server

- HTTP server on port 631 at path `/ipp/print`, handling `application/ipp`
  POSTs, including **chunked request bodies** and `Expect: 100-continue`
  (iOS uses both).
- Minimum operations: `Get-Printer-Attributes`, `Validate-Job`, `Print-Job`,
  `Get-Job-Attributes`, `Get-Jobs`, `Cancel-Job`. Add
  `Create-Job`/`Send-Document` only if iOS needs them in practice.
- Printer attributes must describe exactly one medium matching the **source
  page size, A6 (`iso_a6_105x148mm`)**, portrait, with **zero margins**
  (`media-*-margin-supported = 0`), so iOS renders the Shopee page 1:1
  without scaling it. Mapping A6 onto the physical label happens in the
  bridge (section 4), not in iOS. The advertised media is configurable. `printer-resolution-supported` / URF `RS` must match the
  resolution the bridge expects (see Risks re: 203 vs 300 dpi).
- Monochrome only (`print-color-mode-supported = monochrome`, URF/PWG gray
  8-bit accepted; SRGB24 accepted if iOS insists, converted to gray).
- Jobs are processed **sequentially** (one at a time) so labels from two
  quick taps don't interleave.

### 3. Raster decoding

- Decode both **Apple URF** and **PWG raster** streams (both are simple
  per-page headers + PackBits-style run-length compressed lines).
- Each page in the stream becomes one label, in order. Multi-page jobs are
  the normal case for batch AWB PDFs.

### 4. Conversion and printing

- **A6 page → physical label mapping** (Claude's call). Physical label stock
  is assumed to be 100×150 mm (configurable): 800×1200 dots at 203 dpi. An A6
  page is 105×148 mm, about 840×1184 dots, so it is 5 mm too wide.
  1. Resample the received page to 203 dpi, whatever resolution iOS sent.
  2. Trim white margins: find the bounding box of non-white content.
  3. If the content fits within the label, center it at 1:1. Otherwise scale
     it down uniformly to fit, then center it. Shopee pages normally have
     white margins, so this is usually 1:1 and barcodes are not resampled.
  4. A config option allows `fit` (default, as above), `crop-center`
     (no scaling, cut equally from both edges) or `stretch`.
- Gray → 1-bit by threshold, applied after any scaling.
- Emit TSPL per label: `SIZE`, `GAP`, `DENSITY`, `SPEED`, `DIRECTION`,
  `CLS`, `BITMAP x,y,widthBytes,height,0,<data>`, `PRINT 1`. (Note: in TSPL
  `BITMAP`, bit polarity — whether 0 or 1 means a black dot — must be
  verified on the real printer.)
- Send over a TCP connection to `printer IP:9100`; report the IPP job as
  completed only after the data is sent; report failure (job `aborted`, with
  a log entry) if the printer is unreachable.

## Built-in tolerance for unknowns (Claude's call)

Because there's no hardware access until the final test, every unverified
assumption gets a config switch or fallback, so final testing means changing
settings, not rewriting code:

- **Two output modes, both built:** `tspl` (raw TSPL to IP:9100, default) and
  `windows-driver` (render the label bitmap to the installed Xprinter Windows
  print queue by name via `System.Drawing.Printing`). Choose with config.
  A third mode, `file`, writes labels as PNGs only, for testing on the Mac.
- **TSPL bitmap polarity** (`invertBitmap`), `DIRECTION`, `GAP`, density,
  speed and threshold are all config values.
- **Advertised resolution(s)** are configurable (default: offer 203 and
  300 dpi). The bridge resamples whatever iOS sends to 203 dpi.
- **Diagnostics:** a config flag `saveJobs` writes each received page (as
  received) and each final 1-bit label to PNGs in a `jobs/` folder, plus a
  detailed log of every IPP request and response. The user can send these
  back if something looks wrong.
- **Test-print command:** `LabelBridge.exe test-print`
  prints a built-in test label (a border, a barcode-like bar pattern and
  corner marks) through the configured output mode. This checks the printer
  path without the iPhone.
- **Offline test harness on the Mac:** unit tests on recorded/synthesized URF
  and PWG raster streams, TSPL byte output, and A6→label mapping. Before the
  hardware test, run an end-to-end IPP check with `ipptool` and by printing
  from macOS to the bridge running on the Mac (output mode: save to PNG only,
  `output: file`).

## Acceptance check (what "done" looks like)

1. Fresh install on the Windows PC via the install script; reboot; the
   service is running.
2. On the iPhone, Shopee Seller app → print AWB → the configured printer
   name appears in the printer list without typing anything.
3. A single-label print comes out full-size, correctly oriented, and the
   barcode scans with a phone scanner app.
4. A multi-page PDF (e.g. 5 labels) from the Files app prints 5 labels in
   order.
5. Turning the printer off and printing gives a failed job on the iPhone
   (not a hang), and printing works again once the printer is back on.
6. Works after the PC has been running for several days.

## Risks / things to verify first

All of these are checked during the final hardware test; each has a config
switch or fallback above.

1. **Does the XP-470B accept TSPL on TCP 9100?** First step of the final
   test: `test-print` in `tspl` mode. If not, switch to `windows-driver`.
2. **Resolution negotiation.** iOS may only render URF at certain resolutions
   (300 dpi is the common AirPrint baseline). Handled by resampling.
3. **iOS layout of the Shopee label.** Confirm iOS renders the A6 page 1:1
   when only A6 is offered. Check with `saveJobs` PNGs.
4. **mDNS coexistence** with Windows' built-in responder and any Bonjour
   installation; Windows network profile must be **Private** for discovery
   to work — the install script should warn if it is Public.

## Development notes

- Most of the bridge (IPP server, raster decode, TSPL generation) is plain
  .NET and can run and be tested on the Mac. macOS ships CUPS tools
  (`ipptool`, `ippfind`) useful for exercising the IPP server, and macOS can
  itself print to an AirPrint printer for end-to-end testing.
- Windows-specific parts (service hosting, mDNS registration if using
  dnsapi.dll, firewall rules) are isolated so the rest stays portable.

## Left to the builder

- Choice of HTTP stack (Kestrel vs HttpListener), mDNS mechanism, IPP
  encoding (hand-written vs. a library), project structure, test strategy.
- Exact TXT/IPP attribute set beyond the minimums above, tuned until iOS
  accepts the printer.
- Default density/speed/threshold values (tune on the real printer).

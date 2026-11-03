# LabelBridge — AirPrint for the Xprinter XP-470B

LabelBridge runs as a background service on the Windows PC and makes the
**Xprinter XP-470B** show up on iPhones as an **AirPrint printer**. In the
Shopee Seller app, or on a downloaded AWB PDF, tap **Print** and pick
**XP-470B Label**. The A6 Shopee label prints full-size on a 100×150 mm
sticker. No app is needed on the iPhone.

```
iPhone ──(AirPrint)──► Windows PC: LabelBridge ──(TSPL, port 9100)──► XP-470B
```

Design: `docs/superpowers/specs/2026-11-03-airprint-label-bridge-design.md`

## 1. Build (on the Mac)

```bash
dotnet publish src/LabelBridge -p:PublishProfile=win-x64
```

This creates `publish/win-x64/LabelBridge.exe`, a single file with .NET
built in, plus `appsettings.json`. Copy the `publish/win-x64` folder to the
Windows PC, then copy `install/install.ps1` and `install/uninstall.ps1` into
that same folder.

## 2. Configure

Open `appsettings.json` and set at least:

| Setting | What to put |
|---|---|
| `PrinterHost` | The XP-470B's static IP address, e.g. `192.168.1.50` |
| `WindowsPrinterName` | Only for `"Output": "windows-driver"`: the printer's name exactly as shown in Windows *Printers & scanners* |

## 3. Install (on the Windows PC)

Open PowerShell **as Administrator** in the folder, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The script:
- installs the program to `C:\Program Files\LabelBridge`,
- registers the `LabelBridge` service (starts automatically at boot and restarts if it crashes),
- opens the firewall for the local network only (TCP 631, UDP 5353),
- warns you if the network is set to *Public*.

It then shows the address of the status page. After you edit
`C:\Program Files\LabelBridge\appsettings.json`, restart the service:
`Restart-Service LabelBridge`.

To remove it, run `.\uninstall.ps1` as Administrator.

## 4. Final hardware test

Do these in order. Each one checks something that can only be checked with
the real printer and iPhone.

1. **Printer path (no iPhone).** In `C:\Program Files\LabelBridge`, run
   `.\LabelBridge.exe test-print`. A test label should print: a border,
   black corner squares, and bars in the middle.
   - Nothing prints, or `FAILED` → check `PrinterHost`. If it still fails, set
     `"Output": "windows-driver"` and `WindowsPrinterName`, restart, and try again.
   - The label comes out inverted (mostly black) → set `"InvertBitmap": true`.
   - Upside down → change `"Direction"` from `1` to `0`.
   - Too light or too dark → raise or lower `"Density"` (0–15).
2. **Discovery.** On the iPhone (same Wi-Fi), open any PDF → Share → Print →
   Printer. **XP-470B Label** should be listed.
3. **One Shopee label.** Print an AWB from the Shopee Seller app. It should be
   full-size and the right way up, and the barcode should scan with a phone
   scanner app.
4. **Many labels.** Print a PDF with 5 labels. You should get 5 stickers, in
   order.
5. **Printer off.** Turn the printer off and print. The iPhone should report
   a failure within a few seconds. Turn the printer back on and print again.
   It should work.
6. **Long run.** Leave the PC running for several days and print again.

## Settings reference (`appsettings.json` → `Bridge`)

| Key | Default | Effect |
|---|---|---|
| `PrinterName` | `XP-470B Label` | Name shown on the iPhone |
| `IppPort` | `631` | Port the iPhone prints to (keep 631 for AirPrint) |
| `Output` | `tspl` | `tspl` = raw to printer port; `windows-driver` = via the installed Xprinter driver; `file` = PNG files only |
| `PrinterHost` / `PrinterPort` | `192.168.1.100` / `9100` | Printer address for `tspl` |
| `ConnectTimeoutMs` | `5000` | How long to wait for the printer before failing the job |
| `WindowsPrinterName` | `Xprinter XP-470B` | Printer name for `windows-driver` |
| `SaveJobs` | `false` | `true` saves each received page and final label as PNG in `jobs\` |
| `OutputDirectory` | `jobs` | Folder for `SaveJobs` and `file` output |
| `AdvertisedResolutions` | `[203, 300]` | Resolutions offered to the iPhone (pages are resampled to 203 dpi) |
| `Label.WidthMm` / `HeightMm` | `100` / `150` | Sticker size |
| `Label.FitMode` | `fit` | `fit` = trim white margins, center, shrink only if needed; `crop-center` = no scaling, cut edges; `stretch` = fill the label |
| `Label.Threshold` | `160` | Gray level below which a dot is black. Higher = bolder text and barcodes |
| `Tspl.Density` | `8` | Print darkness 0–15 |
| `Tspl.Speed` | `4` | Print speed |
| `Tspl.Direction` | `1` | Flip to `0` if labels come out upside down |
| `Tspl.GapMm` | `2` | Gap between stickers |
| `Tspl.InvertBitmap` | `false` | Set `true` if labels print inverted |
| `Media.*` | A6 | Page size offered to the iPhone (Shopee AWB PDFs are A6) |

## Troubleshooting

- **iPhone doesn't list the printer.**
  - Check that the iPhone and PC are on the same network.
  - Check that the Windows network profile is **Private**.
  - Check that the service is running: `Get-Service LabelBridge`.
  - Look in `C:\Program Files\LabelBridge\logs\` for errors.
- **Job fails on the iPhone.** Open the status page (`http://<pc-ip>:631/`)
  to see the error for each job.
- **Label looks wrong.** Set `"SaveJobs": true`, restart, print again, and send
  the PNGs from `jobs\`. `job-N-page-01.png` is what the iPhone sent and
  `job-N-label-01.png` is what went to the printer.

## Development

```bash
dotnet test                                    # full test suite (runs on macOS)
dotnet run --project src/LabelBridge -- --Bridge:IppPort=8631 --Bridge:Output=file --Bridge:SaveJobs=true
ipptool -t ipp://127.0.0.1:8631/ipp/print get-printer-attributes.test
```

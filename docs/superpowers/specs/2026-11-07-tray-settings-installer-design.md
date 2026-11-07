# LabelBridge Tray, Settings UI and Installer — Design

Date: 2026-11-07
Builds on: `2026-11-03-airprint-label-bridge-design.md` (LabelBridge service, already deployed and working on two PCs).

## Intent and why

LabelBridge works, but it is invisible and is configured by editing JSON over SSH. The user wants to:
1. See that it's running, and **exit** (stop printing) from the **tray** when needed.
2. Change the **printer IP, printer port and AirPrint name** from a **UI**.
3. Install the whole thing (service + tray) on **other machines** with **one installer**.

## Decisions (from the user)

- **Keep the Windows service** and add a separate tray app (the service still prints after a reboot before anyone logs in).
- **Exit = stop the LabelBridge service and close the tray icon.** The printer disappears from iPhones until printing is started again.
- **The installer is built entirely on the Mac** as a custom C# setup program. No Inno Setup/WiX, and no build tools on the Windows PCs.

- **Exit lasts until the next reboot.**
  - Exit only stops the service. The start type stays delayed-auto, so a reboot brings printing back.
  - Clicking **Start printing**, or opening "LabelBridge" from the Start menu, also starts it again. Opening the tray from the Start menu starts the tray; if the service is stopped, the icon shows "stopped" and the menu offers Start printing. It does not auto-start the service.
  - Because the tray starts at login, logging in again also shows the icon. A logon alone does not restart printing.

## Calls made by Claude (reviewer questions)

- **Output mode:**
  - Settings and the installer edit only `PrinterHost`, `PrinterPort` and `PrinterName`, and never change `Output`.
  - If `Output` isn't `tspl`, the Settings window shows a note: "This PC prints via <mode>; printer IP/port are only used in tspl mode."
  - **Test print** always uses `tspl` with the values in the form, because it is testing exactly those values.
  - Both deployed PCs are in `tspl` mode.
- **Silent mode with missing values:**
  - On an **upgrade**, any of `/printer`, `/port` or `/name` that is omitted keeps the existing value.
  - On a **fresh install**, `/printer` is required (otherwise exit code 2 with a message). `/port` defaults to 9100 and `/name` to "LabelBridge Printer" (user's choice).
  - Invalid values give exit code 2.
- **Existing `install.ps1` installs:**
  - Setup upgrades them in place: it keeps `appsettings.json` (and its values) and `printer-id.txt`.
  - It deletes the leftover `install.ps1`, `uninstall.ps1` and `LabelBridge.staticwebassets.endpoints.json`.
  - It re-creates the firewall rules under the same names.
- **Uninstall:**
  - Deletes `jobs\` (customer label images) and `printer-id.txt`.
  - Keeps `logs\` and `appsettings.json`, so a later reinstall keeps its settings. A reinstall after uninstall gets a new printer identity, so iPhones see it as a new printer, which is expected after an uninstall.

## Goals

1. Tray icon at login for every user, showing whether printing is **running** or **stopped**.
2. The tray menu:
   - **Settings…**
   - **Open status page**
   - **Start printing** / **Stop printing** (whichever applies)
   - **Exit** (stops the service, then closes the tray)
3. **Settings window:**
   - The fields are printer IP, printer port (default 9100) and AirPrint name. The fields are validated.
   - **Save** writes `appsettings.json` and restarts the service.
   - **Test print** prints the built-in test label using the values in the form. This works before saving.
4. All of the above works for a **non-admin** user with no UAC prompt. The installer grants:
   - (a) Users permission to start/stop/query the LabelBridge service;
   - (b) Users modify rights on `appsettings.json`.
5. **`LabelBridge-Setup.exe`** is one file and needs no .NET installed. It asks for admin (UAC) once, and its wizard has three pages:
   - **Welcome**
   - **Printer settings**: IP, port and AirPrint name, prefilled from the existing install on an upgrade
   - **Install**: shows a progress log and ends with Finish
6. **What the installer does:**
   - Stops an existing LabelBridge service and copies the files to `C:\Program Files\LabelBridge`.
   - Writes the settings into `appsettings.json`, keeping every other key on an upgrade, and keeps `printer-id.txt`.
   - Registers the service as delayed-auto with restart-on-failure, and applies the permission grants from goal 4.
   - Adds the firewall rules (TCP 631 and UDP 5353, local subnet, program-scoped).
   - Starts the tray at login for all users (HKLM `Run`) and adds a Start-menu shortcut "LabelBridge".
   - Registers in **Apps → Installed apps**, with an uninstaller.
   - Starts the service and the tray.
   - Warns if the network is set to Public, or if another program holds port 631 (the old AirPrint tool).
7. **Silent mode for SSH installs:**
   - `LabelBridge-Setup.exe /silent /printer=192.168.2.102 /port=9100 /name="XP-470B Label (Surface)"` installs with no UI and returns exit code 0 on success.
   - `/uninstall` (with an optional `/silent`) removes everything.
8. **Uninstall:**
   - Stops and deletes the service, removes the firewall rules, the Run entry, the shortcut and the uninstall entry, and kills the tray.
   - Deletes the program files, `jobs\` and `printer-id.txt`, but keeps `logs\` and `appsettings.json` (see "Calls made by Claude").

## Non-goals

- Code signing (user decided: not needed). SmartScreen may say "unknown publisher" for downloaded copies; the README explains "More info → Run anyway".
- Auto-update.
- Settings beyond IP, port and name; the other keys stay JSON-only.
- Running multiple LabelBridge instances on one PC.

## Anti-goals

- A config UI that needs an admin prompt every time.
- An installer that wipes existing settings or `printer-id.txt` on upgrade.
- Stopping printing just because a user logs off or closes the tray in some way other than Exit. Only Exit stops the service.

## Constraints

- **Target frameworks:** tray and setup use `net10.0-windows` WinForms, built on macOS with `EnableWindowsTargeting=true`, win-x64, self-contained single-file. The service project stays `net10.0`.
- **Validation (shared by the Settings window and the installer):**
  - Printer host: an IPv4 address or a hostname.
  - Port: 1–65535.
  - AirPrint name: 1–63 UTF-8 bytes (DNS-SD instance label limit), with no control characters.
- **Logic that can be tested on the Mac** goes in a plain `net10.0` library, `LabelBridge.Common`, with tests in the existing test project:
  - reading and updating the settings file;
  - validation;
  - parsing setup command lines;
  - building the uninstall-entry values;
  - the service security-descriptor string.

  The WinForms and Windows API code stays thin.
- **Payload:** the setup exe embeds `LabelBridge.exe`, `LabelBridge.Tray.exe` and the default `appsettings.json` as embedded resources. The setup copies itself to the install folder as `uninstall.exe` for the uninstall entry.
- **The service security descriptor** grants Authenticated Users (AU) start, stop and query (`RP WP LC LO RC` on top of the defaults), applied with `sc.exe sdset`.

## Left to the builder

- Icon artwork: a simple generated multi-size `.ico` (a printer glyph) with a running variant and a stopped variant.
- The exact WinForms layout.
- How the tray watches the service: polling every few seconds is fine.
- Using `sc.exe`/`netsh` versus P/Invoke for service and firewall setup. Shelling out is acceptable.

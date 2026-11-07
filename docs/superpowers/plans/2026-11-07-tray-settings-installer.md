# LabelBridge Tray, Settings and Installer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tray app with a Settings window, and a single `LabelBridge-Setup.exe` that installs, upgrades and uninstalls the service and the tray, all built on the Mac.

**Architecture:**
- A new `LabelBridge.Common` library (`net10.0`, tested on the Mac) holds every decision: settings file I/O, validation, setup argument rules, the service security descriptor, the uninstall-entry values and install paths.
- Two thin WinForms exes (`net10.0-windows`, built on macOS with `EnableWindowsTargeting`) do the Windows side:
  - **`LabelBridge.Tray`**: tray icon and Settings window.
  - **`LabelBridge.Setup`**: wizard, silent mode and the install engine.
- Setup embeds the published `LabelBridge.exe` and `LabelBridge.Tray.exe` as resources.
- `build.sh` produces `dist/LabelBridge-Setup.exe`.

**Tech Stack:** C# / .NET 10, WinForms, `System.ServiceProcess.ServiceController`, `Microsoft.Win32.Registry`, System.Text.Json.Nodes, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-07-tray-settings-installer-design.md` (builds on `2026-11-03-airprint-label-bridge-design.md`)

## Global Constraints

- **Install folder:** `C:\Program Files\LabelBridge`.
- **Names:** service `LabelBridge`; files `LabelBridge.exe`, `LabelBridge.Tray.exe`, `uninstall.exe` (a copy of setup).
- **Settings and the installer edit only** `Bridge:PrinterHost`, `Bridge:PrinterPort` and `Bridge:PrinterName`. All other keys are preserved byte-for-byte where possible. They never change `Output`.
- **Validation:** host is an IPv4 address or an RFC 1123 hostname; port is 1–65535; name is 1–63 UTF-8 bytes with no control characters.
- **Fresh-install defaults:** port 9100, name **"LabelBridge Printer"**. A fresh install requires `/printer`.
- **Exit codes:** silent setup returns 0 on success, 2 for bad or missing arguments and 1 for an install failure.
- **Exit in the tray** stops the service only; the start type stays delayed-auto.
- **Upgrade keeps** `appsettings.json` values and `printer-id.txt`, and deletes `install.ps1`, `uninstall.ps1` and `LabelBridge.staticwebassets.endpoints.json`.
- **Uninstall deletes** the program files, `jobs\` and `printer-id.txt`. It keeps `logs\` and `appsettings.json`.
- **The service security descriptor** adds `(A;;CCLCSWRPWPLORC;;;AU)`: Authenticated Users may query, start and stop the service.
- **appsettings.json permissions:** `icacls … /grant *S-1-5-32-545:M` (Users: modify).
- **Single-file builds:** every Windows exe is published `-r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`.
- **No code signing.**

## Review Focus

1. **appsettings.json saved with a UTF-8 BOM** (as PowerShell wrote it on the deployed PCs) → read and update work, other keys are kept. Pinned in Task 1 (`Update_FileWithBom_PreservesOtherKeys`).
2. **Silent upgrade over SSH with no values** → existing IP/port/name are kept, not reset to defaults. Pinned in Task 1 (`Resolve_UpgradeWithoutArgs_KeepsExisting`).
3. **Re-running setup on a PC that already has the AU grant** → the descriptor isn't duplicated. Pinned in Task 1 (`GrantAuthenticatedUsers_Idempotent`).
4. **AirPrint name with Vietnamese or other non-ASCII characters** → counted in UTF-8 bytes, accepted up to 63 bytes. Pinned in Task 1 (`ValidateName_CountsUtf8Bytes`).
5. **Setup run while the tray and service are running** → setup stops both before copying, so files aren't locked. This is checked live in Task 5 (upgrade on the Surface while the tray is running).

---

## File Structure

```
src/LabelBridge.Common/LabelBridge.Common.csproj        net10.0 classlib
src/LabelBridge.Common/SettingsFile.cs                  read/update the 3 keys in appsettings.json
src/LabelBridge.Common/SettingsValidator.cs             host/port/name rules
src/LabelBridge.Common/SetupArgs.cs                     parse + resolve setup command line
src/LabelBridge.Common/ServiceSecurity.cs               SDDL grant
src/LabelBridge.Common/InstallLayout.cs                 paths, names, uninstall-entry values
src/LabelBridge.Tray/…                                  WinForms tray + SettingsForm (Task 3)
src/LabelBridge.Setup/…                                 WinForms wizard + InstallEngine (Task 4)
build.sh                                                publish all + build setup → dist/
tests/LabelBridge.Tests/Common/*Tests.cs
```

---

### Task 1: LabelBridge.Common — settings, validation, setup rules, SDDL, layout

**Files:** create the `LabelBridge.Common` project and the files above, plus the tests in `tests/LabelBridge.Tests/Common/`. Add the project to `LabelBridge.slnx` and reference it from the test project.

**Interfaces (namespace `LabelBridge.Common`):**
```csharp
public sealed record PrinterSettings(string Host, int Port, string Name);
public static class SettingsFile {
    public static PrinterSettings? Read(string path);                     // null if file missing; missing keys → defaults (192.168.1.100, 9100, "LabelBridge Printer")
    public static string? OutputMode(string path);                        // Bridge:Output or null
    public static void Update(string path, PrinterSettings s); }          // creates {"Bridge":{}} if missing; keeps other keys; writes UTF-8 without BOM, indented
public static class SettingsValidator {
    public static string? ValidateHost(string host);                      // null = ok, else message
    public static string? ValidatePort(string port);
    public static string? ValidateName(string name);
    public static IReadOnlyList<string> Validate(string host, string port, string name); }
public enum SetupMode { Install, Uninstall }
public sealed record SetupArgs(SetupMode Mode, bool Silent, string? Printer, string? Port, string? Name) {
    public static SetupArgs Parse(string[] args);                         // /silent /uninstall /printer=… /port=… /name=… (case-insensitive; quotes stripped); unknown → ArgumentException
    public (PrinterSettings? Settings, string? Error) Resolve(PrinterSettings? existing); }  // upgrade: missing → existing; fresh: /printer required; validates
public static class ServiceSecurity { public const string AuAce = "(A;;CCLCSWRPWPLORC;;;AU)";
    public static string GrantAuthenticatedUsers(string sddl); }          // inserts AuAce at end of D: part (before "S:"), idempotent
public static class InstallLayout {
    public const string ServiceName = "LabelBridge", ServiceExe = "LabelBridge.exe", TrayExe = "LabelBridge.Tray.exe", UninstallExe = "uninstall.exe";
    public static readonly string[] ObsoleteFiles = ["install.ps1", "uninstall.ps1", "LabelBridge.staticwebassets.endpoints.json"];
    public static IReadOnlyDictionary<string, object> UninstallEntry(string installDir, string version, long sizeKb); }
```
The uninstall entry values are:
- `DisplayName`="LabelBridge"
- `DisplayVersion`=version
- `Publisher`="LabelBridge"
- `InstallLocation`=dir
- `DisplayIcon`="{dir}\LabelBridge.Tray.exe"
- `UninstallString`="\"{dir}\uninstall.exe\" /uninstall"
- `QuietUninstallString`= the same plus " /silent"
- `NoModify`=1 and `NoRepair`=1
- `EstimatedSize`=sizeKb (stored as an int)

- [ ] **Step 1: Write the failing tests:**
  - `SettingsFileTests`:
    - `Update_FileWithBom_PreservesOtherKeys`: a BOM-prefixed file holding Bridge.Output=tspl, SaveJobs=false and Tspl.Density=8 → after `Update`, the three keys are changed and Output, SaveJobs and Density are unchanged.
    - `Read_MissingFile_ReturnsNull`.
    - `Read_MissingKeys_Defaults`.
    - `Update_MissingFile_Creates`.
  - `SettingsValidatorTests`: valid hosts "192.168.2.102" and "printer.local"; invalid hosts "", "999.1.1.1" and "a b"; ports "0", "65536" and "abc" are invalid; `ValidateName_CountsUtf8Bytes` (a 21-character Vietnamese name of 63 bytes passes, 64 bytes fails); a control character fails.
  - `SetupArgsTests`:
    - `Parse_AllArgs`.
    - `Parse_QuotedName`.
    - `Parse_Unknown_Throws`.
    - `Resolve_FreshWithoutPrinter_Error`.
    - `Resolve_FreshDefaults` (port 9100, name "LabelBridge Printer").
    - `Resolve_UpgradeWithoutArgs_KeepsExisting`.
    - `Resolve_UpgradeOverridesOnlyGiven`.
    - `Resolve_InvalidPort_Error`.
  - `ServiceSecurityTests`:
    - `GrantAuthenticatedUsers_InsertsBeforeSacl`, with input `D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)`, so the AU ACE ends up right before `S:`.
    - `GrantAuthenticatedUsers_NoSacl_Appends`.
    - `GrantAuthenticatedUsers_Idempotent`.
  - `InstallLayoutTests`: `UninstallEntry_Values` (exact UninstallString and QuietUninstallString).
- [ ] **Step 2: Run** `dotnet test --filter Common` → FAIL (compile).
- [ ] **Step 3: Implement** as specified, using `JsonNode.Parse` with the BOM stripped.
- [ ] **Step 4: Run** `dotnet test` → all PASS.
- [ ] **Step 5: Commit** `feat: LabelBridge.Common settings, validation and setup rules`

### Task 2: Build pipeline for Windows WinForms projects on the Mac

**Files:**
- Create `src/LabelBridge.Tray/LabelBridge.Tray.csproj` and `src/LabelBridge.Setup/LabelBridge.Setup.csproj`, each with a placeholder `Program.cs`.
- Create `build.sh`.
- Add `src/LabelBridge/Properties/PublishProfiles/win-x64.pubxml` compression (`EnableCompressionInSingleFile`).

**Interfaces:** `build.sh` publishes three things to `publish/`: the service (`publish/service/LabelBridge.exe`, `appsettings.json`), the tray (`publish/tray/LabelBridge.Tray.exe`), and setup with `-p:PayloadDir=<abs publish dir>`. It copies setup to `dist/LabelBridge-Setup.exe`. Both WinForms csproj files set:
- `OutputType=WinExe`
- `TargetFramework=net10.0-windows`
- `UseWindowsForms=true`
- `EnableWindowsTargeting=true`
- `ApplicationHighDpiMode=PerMonitorV2`

Setup also sets `ApplicationManifest=app.manifest` (`requireAdministrator`), plus `EmbeddedResource` items with `LogicalName` `payload/LabelBridge.exe`, `payload/LabelBridge.Tray.exe` and `payload/appsettings.json`, included only when `PayloadDir` is set.

- [ ] **Step 1:** Create the projects and `build.sh` as specified; add them to the slnx. The test project does *not* reference them.
- [ ] **Step 2: Run** `./build.sh` → expect `dist/LabelBridge-Setup.exe` to exist, then `dotnet test` → PASS.
- [ ] **Step 3: Commit** `build: WinForms tray/setup projects and build.sh`

### Task 3: Tray app and Settings window

**Files:** in `src/LabelBridge.Tray/`, create `Program.cs` (single-instance Mutex `Global\LabelBridge.Tray`), `TrayContext.cs`, `SettingsForm.cs`, `Icons.cs` and `ServiceControl.cs`.

**Behavior (spec goals 1–4):**
- **Icon:** drawn in code. Green dot = running, grey dot = stopped, amber = start/stop pending. The tooltip is "LabelBridge — {PrinterName}: Running/Stopped".
- **Polling:** the tray checks the service every 3 s.
- **Menu:**
  - Settings… (bold, also opened by double-click)
  - Open status page (`http://localhost:{IppPort}/`)
  - Start printing / Stop printing (one visible according to state)
  - separator
  - Exit, which stops the service (waits up to 20 s), then exits the app
- **Settings form:** labeled fields Printer IP, Printer port and AirPrint name, prefilled with `SettingsFile.Read`, plus a note line shown when `OutputMode` isn't `tspl`. Buttons:
  - **Test print** runs `LabelBridge.exe test-print --Bridge:Output=tspl --Bridge:PrinterHost=… --Bridge:PrinterPort=…` (hidden window, 30 s timeout) and shows OK or the error.
  - **Save** validates, then `SettingsFile.Update`, then restarts the service, and shows any validation messages inline.
  - **Cancel.**
- **Errors:** an access-denied error from the service controller shows "Windows refused — reinstall LabelBridge to grant permissions."
- **Opened from the Start menu:** when the app is already running, signal it with an EventWaitHandle `Global\LabelBridge.Tray.Show` to open Settings.

- [ ] **Step 1:** Implement as specified. All decision logic is already in Common (Task 1); keep the forms thin.
- [ ] **Step 2: Run** `./build.sh` → builds with no errors; `dotnet test` → PASS.
- [ ] **Step 3: Commit** `feat: tray app with status, start/stop/exit and settings window`

### Task 4: Setup wizard, silent mode and install engine

**Files:** in `src/LabelBridge.Setup/`, create `Program.cs`, `SetupForm.cs`, `InstallEngine.cs`, `Payload.cs`, `Log.cs` and `app.manifest`.

**Behavior:**
- **`Program`:**
  - Parse the arguments (`SetupArgs`); a parse or resolve error exits with code 2 (silent) or shows a MessageBox.
  - Write a log to `%TEMP%\LabelBridge-Setup.log`.
  - `/uninstall` runs `InstallEngine.Uninstall`; otherwise the GUI wizard runs, or `InstallEngine.Install` when `/silent` is given.
- **`SetupForm`** has three panels:
  - Welcome
  - Settings: the same three fields and validation, prefilled from the existing install
  - Progress: a log TextBox and a Finish button that also offers "Open LabelBridge settings"

  With `/uninstall` and no `/silent`, it asks for confirmation first.
- **`InstallEngine.Install(PrinterSettings s, Action<string> log)`**, in this order:
  1. Kill `LabelBridge.Tray` processes, then stop the service if it exists (wait up to 30 s).
  2. Copy the payload exes into the install dir, and copy itself to `uninstall.exe`. Write `appsettings.json` from the payload only if it doesn't exist, then `SettingsFile.Update(s)`. Delete `ObsoleteFiles`.
  3. Create the service if it's missing: `sc.exe create LabelBridge binPath= "\"…\LabelBridge.exe\"" start= delayed-auto DisplayName= "LabelBridge AirPrint"`. Then always run:
     - `sc.exe config … start= delayed-auto`
     - `sc.exe description …`
     - `sc.exe failure … reset= 86400 actions= restart/5000/restart/5000/restart/5000`
     - `sc.exe sdset LabelBridge <GrantAuthenticatedUsers(sc sdshow output)>`
  4. Run `icacls "<dir>\appsettings.json" /grant *S-1-5-32-545:M`.
  5. Re-create the firewall rules "LabelBridge IPP" (TCP 631) and "LabelBridge mDNS" (UDP 5353) with `powershell -NoProfile -Command New-NetFirewallRule … -RemoteAddress LocalSubnet -Program <exe>` (remove existing ones first).
  6. Write HKLM `…\Run\LabelBridge Tray` = `"<dir>\LabelBridge.Tray.exe"`, and create the Start-menu shortcut `%ProgramData%\Microsoft\Windows\Start Menu\Programs\LabelBridge.lnk` with PowerShell `WScript.Shell`.
  7. Write the uninstall entry under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LabelBridge`.
  8. Warn if anything else holds port 631 (`IPGlobalProperties` listeners while our service is stopped), and warn if any connection profile is Public.
  9. Start the service and wait for Running; start the tray in the user's session (`explorer.exe "<tray exe>"` in GUI mode, skipped in silent mode).

  Each step is logged, and any failing command throws with its output.
- **`InstallEngine.Uninstall(Action<string> log)`:**
  - Kill the tray; stop and delete the service.
  - Remove the firewall rules, the Run value, the shortcut and the uninstall key.
  - Delete the exes, `jobs\` and `printer-id.txt`.
  - Schedule deletion of `uninstall.exe` with `cmd /c ping 127.0.0.1 -n 3 >nul & del "<path>"`.
- **`sc.exe` exit codes** are always checked.

- [ ] **Step 1:** Implement as specified.
- [ ] **Step 2: Run** `./build.sh` → `dist/LabelBridge-Setup.exe` builds; `dotnet test` → PASS.
- [ ] **Step 3: Commit** `feat: LabelBridge-Setup wizard, silent mode and install engine`

### Task 5: Live test on the Surface, docs

**Files:** modify `README.md` (install via `LabelBridge-Setup.exe`, the tray, Settings, silent and SSH commands, SmartScreen note) and mark `install/*.ps1` as legacy in the README.

- [ ] **Step 1:** `scp dist/LabelBridge-Setup.exe surface@192.168.2.25:` and run a silent upgrade over SSH with no values. Expected:
  - exit code 0;
  - the service is Running;
  - the name, IP and port are unchanged (Surface: "XP-470B Label (Surface)", 192.168.2.102, 9100);
  - `printer-id.txt` is unchanged;
  - `sc sdshow LabelBridge` contains `;;;AU)`;
  - the obsolete files are gone;
  - the Run value exists;
  - the uninstall key exists.
- [ ] **Step 2:** Run the silent upgrade again → still exit code 0, with exactly one AU ACE (idempotent).
- [ ] **Step 3:** The user checks on the Surface screen: the tray icon is shown after logging off and on (or after starting it from the Start menu); Settings opens and Test print works; Stop and Start printing work without UAC; Exit stops the service.
- [ ] **Step 4:** Update the README, run `dotnet test` → PASS, then commit `docs: installer and tray usage`.

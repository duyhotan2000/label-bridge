# Installs LabelBridge as a Windows service.
# Run from the folder that holds LabelBridge.exe and appsettings.json:
#   Right-click PowerShell -> Run as Administrator, then:  .\install.ps1
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Please run this script as Administrator (right-click PowerShell -> Run as Administrator).' -ForegroundColor Red
    exit 1
}

$source = $PSScriptRoot
if (-not (Test-Path (Join-Path $source 'LabelBridge.exe'))) {
    $source = Split-Path $PSScriptRoot -Parent
}
if (-not (Test-Path (Join-Path $source 'LabelBridge.exe'))) {
    Write-Host "LabelBridge.exe not found next to this script or in its parent folder." -ForegroundColor Red
    exit 1
}

$target = Join-Path $env:ProgramFiles 'LabelBridge'
$serviceName = 'LabelBridge'

# Stop an existing install before copying over it.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Stopping existing LabelBridge service...'
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Get-ChildItem -Path $source -File | Where-Object { $_.Extension -ne '.ps1' } | ForEach-Object {
    $dest = Join-Path $target $_.Name
    # Keep the user's settings and the printer identity on upgrade.
    if ($_.Name -in @('appsettings.json', 'printer-id.txt') -and (Test-Path $dest)) {
        Write-Host "Keeping existing $($_.Name)"
        return
    }
    Copy-Item $_.FullName $dest -Force
}

$exe = Join-Path $target 'LabelBridge.exe'
if (-not $existing) {
    sc.exe create $serviceName binPath= "`"$exe`"" start= auto DisplayName= "LabelBridge AirPrint" | Out-Null
    sc.exe description $serviceName "Shares the Xprinter XP-470B with iPhones as an AirPrint label printer." | Out-Null
}
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# Firewall: IPP (TCP 631) and Bonjour (UDP 5353), local network only.
foreach ($rule in @(
        @{ Name = 'LabelBridge IPP'; Protocol = 'TCP'; Port = 631 },
        @{ Name = 'LabelBridge mDNS'; Protocol = 'UDP'; Port = 5353 })) {
    Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Protocol $rule.Protocol `
        -LocalPort $rule.Port -RemoteAddress LocalSubnet -Action Allow -Program $exe | Out-Null
}

$public = Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' }
if ($public) {
    Write-Host ''
    Write-Host 'WARNING: your network is set to "Public". iPhones may not find the printer.' -ForegroundColor Yellow
    Write-Host 'Fix: Settings -> Network & Internet -> (your Wi-Fi/Ethernet) -> Network profile type -> Private' -ForegroundColor Yellow
    Write-Host "  or run:  Set-NetConnectionProfile -InterfaceIndex $($public[0].InterfaceIndex) -NetworkCategory Private" -ForegroundColor Yellow
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table -AutoSize Name, Status, StartType

$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -ne 'WellKnown' -and $_.IPAddress -notlike '169.*' } | Select-Object -First 1).IPAddress
Write-Host "Installed to $target"
Write-Host "Status page: http://${ip}:631/"
Write-Host "Settings:    $target\appsettings.json  (restart the service after editing: Restart-Service LabelBridge)"

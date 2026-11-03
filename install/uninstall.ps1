# Removes the LabelBridge service and its firewall rules. Leaves the program folder in place.
# Run as Administrator:  .\uninstall.ps1
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Please run this script as Administrator.' -ForegroundColor Red
    exit 1
}

if (Get-Service -Name 'LabelBridge' -ErrorAction SilentlyContinue) {
    Stop-Service -Name 'LabelBridge' -Force -ErrorAction SilentlyContinue
    sc.exe delete LabelBridge | Out-Null
    Write-Host 'Service removed.'
}
foreach ($name in @('LabelBridge IPP', 'LabelBridge mDNS')) {
    Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
Write-Host "Firewall rules removed. Program files left in $env:ProgramFiles\LabelBridge"

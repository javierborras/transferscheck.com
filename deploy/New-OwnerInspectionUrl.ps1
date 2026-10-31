# Prints a 6-hour owner inspection URL. The query value is a signed ticket, not the password.
[CmdletBinding()]
param(
    [string] $ProductionSettings = "",
    [int] $Hours = 6
)

$ErrorActionPreference = 'Stop'
if ($Hours -lt 1 -or $Hours -gt 6) { throw 'Hours must be from 1 to 6.' }
$here = $PSScriptRoot
if (!$here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (!$ProductionSettings) {
    $latest = Get-ChildItem -Directory (Join-Path $here 'local\snapshots') | Sort-Object Name -Descending | Select-Object -First 1
    if (!$latest) { throw 'No snapshot found. Run Backup-ProductionConfig.ps1 first.' }
    $ProductionSettings = Join-Path $latest.FullName 'site\appsettings.Production.json'
    if (!(Test-Path -LiteralPath $ProductionSettings)) { $ProductionSettings = Join-Path $latest.FullName 'appsettings.Production.json' }
}
$settings = Get-Content -Raw -LiteralPath $ProductionSettings | ConvertFrom-Json
$email = [string]$settings.Owner.Email
$key = [string]$settings.Session.SigningKey
if (!$email -or !$key) { throw 'The production settings file has no owner email or session signing key.' }
$expires = [DateTimeOffset]::UtcNow.AddHours($Hours).AddMinutes(-2)
$payload = "$email|$($expires.ToUnixTimeSeconds())"
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($key))
$hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload))
$hex = ($hash | ForEach-Object { $_.ToString('X2') }) -join ''
$token = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$payload|$hex"))
$ticket = [Uri]::EscapeDataString($token)
Write-Output "https://transferscheck.com/owner?ticket=$ticket"

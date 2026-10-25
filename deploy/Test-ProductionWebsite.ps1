[CmdletBinding()]
param(
    [string] $PublishSettings = "$PSScriptRoot\local\WebDeploy.Production.transferscheck.com-WebDeploy.publishSettings",
    [string] $SnapshotPath = "$PSScriptRoot\local\website-files-snapshot.xml"
)
$ErrorActionPreference = 'Stop'
$msdeploy = 'C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe'
[xml]$settings = Get-Content -LiteralPath $PublishSettings -Raw
$profile = @($settings.publishData.publishProfile) | Where-Object publishMethod -eq 'MSDeploy' | Select-Object -First 1
if (!$profile -or !$profile.userPWD) { throw 'The local WebDeploy profile has no credentials.' }
$password = [string]$profile.userPWD
$site = [string]$profile.msdeploySite
$endpoint = [string]$profile.publishUrl
if (!$endpoint.StartsWith('https://')) { $endpoint = 'https://' + $endpoint }
$uri = [UriBuilder]::new($endpoint)
if ($uri.Uri.IsDefaultPort) { $uri.Port = 8172 }
$endpoint = $uri.Uri.AbsoluteUri.TrimEnd('/')
if ($endpoint -notmatch '/msdeploy.axd') { $endpoint = $endpoint.TrimEnd('/') + '/msdeploy.axd?site=' + [Uri]::EscapeDataString($site) }
if ($password.Contains("'")) { throw 'This profile requires a different credential transport; credentials were not printed.' }
$source = "-source:contentPath='$site',computerName='$endpoint',userName='$($profile.userName)',password='$password',authType=Basic"
# dump reads remote files metadata; it does not sync, upload, delete, or stop the website.
$output = & $msdeploy -verb:dump $source -xml -retryAttempts:0 2>&1
$result = $LASTEXITCODE
if ($result -ne 0) {
    $message = ($output -join "`n").Replace($password, '[redacted]')
    throw ('WebDeploy read-only check failed: ' + $message.Substring(0, [Math]::Min(1200, $message.Length)))
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SnapshotPath) | Out-Null
$output | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
$password = $null; $source = $null
Write-Host "WebDeploy read-only connection succeeded for $site. No server files changed."
Write-Host "Private file metadata snapshot: $SnapshotPath"

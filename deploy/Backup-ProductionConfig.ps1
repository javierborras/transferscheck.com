# Read-only snapshot of production configuration.
# Downloads appsettings, web.config and App_Data from MonsterASP, and exports
# plan / owner-secret / commerce-access rows from db75697.
# Never uploads, deletes, or updates the server or the database.
[CmdletBinding()]
param(
    [string] $PublishSettings = "",
    [string] $DatabaseSettings = ""
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (!$here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (!$PublishSettings) { $PublishSettings = Join-Path $here 'local\WebDeploy.Production.transferscheck.com-WebDeploy.publishSettings' }
if (!$DatabaseSettings) { $DatabaseSettings = Join-Path $here 'local\database-db75697.local.json' }
$msdeploy = 'C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe'
if (!(Test-Path -LiteralPath $msdeploy)) { throw 'Web Deploy is not installed.' }

[xml]$settings = Get-Content -LiteralPath $PublishSettings -Raw
$profile = @($settings.publishData.publishProfile) | Where-Object publishMethod -eq 'MSDeploy' | Select-Object -First 1
if (!$profile -or !$profile.userPWD) { throw 'The local WebDeploy profile has no credentials.' }
$password = [string]$profile.userPWD
if ($password.Contains("'")) { throw 'This profile requires a different credential transport; credentials were not printed.' }
$site = [string]$profile.msdeploySite
$user = [string]$profile.userName
$endpoint = [string]$profile.publishUrl
if (!$endpoint.StartsWith('https://')) { $endpoint = 'https://' + $endpoint }
$uri = [UriBuilder]::new($endpoint)
if ($uri.Uri.IsDefaultPort) { $uri.Port = 8172 }
$endpoint = $uri.Uri.AbsoluteUri.TrimEnd('/')
if ($endpoint -notmatch '/msdeploy.axd') { $endpoint = $endpoint.TrimEnd('/') + '/msdeploy.axd?site=' + [Uri]::EscapeDataString($site) }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = Join-Path $here "local\snapshots\$stamp"
New-Item -ItemType Directory -Force -Path $root | Out-Null

$siteDir = Join-Path $root 'site'
New-Item -ItemType Directory -Force -Path $siteDir | Out-Null
Add-Type -Path 'C:\Program Files\IIS\Microsoft Web Deploy V3\Microsoft.Web.Deployment.dll'
$remoteOptions = New-Object Microsoft.Web.Deployment.DeploymentBaseOptions
$remoteOptions.ComputerName = $endpoint
$remoteOptions.UserName = $user
$remoteOptions.Password = $password
$remoteOptions.AuthenticationType = 'Basic'

function Save-RemoteFile([string] $RemoteRelative, [string] $LocalPath) {
    $folder = Split-Path -Parent $LocalPath
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $remote = $null
    $stream = $null
    $file = $null
    try {
        $remote = [Microsoft.Web.Deployment.DeploymentManager]::CreateObject('contentPath', "$site/$RemoteRelative", $remoteOptions)
        $target = $remote
        try { $stream = $target.GetStream() } catch { $stream = $null }
        if (-not $stream) {
            $pending = New-Object System.Collections.Generic.Queue[object]
            foreach ($child in @($remote.GetChildren())) { $pending.Enqueue($child) }
            $steps = 0
            while ($pending.Count -gt 0 -and -not $stream -and $steps -lt 30) {
                $steps++
                $target = $pending.Dequeue()
                try { $stream = $target.GetStream() } catch { $stream = $null }
                if (-not $stream) {
                    foreach ($child in @($target.GetChildren())) { $pending.Enqueue($child) }
                }
            }
            if (-not $stream) { throw "No file stream under $RemoteRelative." }
        }
        $file = [IO.File]::Create($LocalPath)
        $stream.CopyTo($file)
    } catch {
        $message = $_.Exception.Message.Replace($password, '[redacted]')
        throw "Download failed for $RemoteRelative. $message"
    } finally {
        if ($file) { $file.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($remote) { $remote.Dispose() }
    }
    if ((Get-Item -LiteralPath $LocalPath).Length -le 0) { throw "Download of $RemoteRelative was empty." }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("snapshot=$root")
$interesting = @(
    'appsettings.Production.json',
    'appsettings.json',
    'web.config',
    'TransfersCheck.Api.dll',
    'TransfersCheck.Core.dll',
    'TransfersCheck.Infrastructure.dll'
)
$pulled = $true
foreach ($name in $interesting) {
    try { Save-RemoteFile $name (Join-Path $siteDir $name) }
    catch {
        $pulled = $false
        $lines.Add('serverFilePull=failed')
        break
    }
}
if (-not $pulled) {
    $fallback = Join-Path $here 'local\firebase-patch\appsettings.Production.json'
    if (!(Test-Path -LiteralPath $fallback)) { $fallback = Join-Path $here 'local\appsettings.Production.firebase-prepared.local.json' }
    Copy-Item -LiteralPath $fallback -Destination (Join-Path $siteDir 'appsettings.Production.from-local-prepared.json')
    $keys = Join-Path $here 'local\dataprotection-production-backup'
    if (Test-Path -LiteralPath $keys) { Copy-Item -LiteralPath $keys -Destination (Join-Path $siteDir 'dataprotection-production-backup') -Recurse }
    $lines.Add('productionSettingsSource=local-prepared-copy-not-a-fresh-server-download')
}
$found = @{}
foreach ($file in Get-ChildItem -Recurse -File $siteDir) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
    $lines.Add("$($file.Name) bytes=$($file.Length) sha256=$hash")
}
$productionPath = Join-Path $siteDir 'appsettings.Production.json'
if (!(Test-Path -LiteralPath $productionPath)) { $productionPath = Join-Path $siteDir 'appsettings.Production.from-local-prepared.json' }
$production = Get-Content -Raw $productionPath | ConvertFrom-Json
$ownerSet = -not [string]::IsNullOrEmpty([string]$production.Owner.Password)
$connSet = -not [string]::IsNullOrEmpty([string]$production.ConnectionStrings.TransfersCheck)
$lines.Add("ownerPasswordPresent=$ownerSet connectionStringPresent=$connSet")

$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$db = Get-Content -Raw $DatabaseSettings | ConvertFrom-Json
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString([string]$db.ConnectionStrings.TransfersCheck)
$login = $null
foreach ($key in @('User ID', 'UID', 'UserId')) { if ($builder.ContainsKey($key)) { $login = [string]$builder[$key]; break } }
$sqlPassword = $null
foreach ($key in @('Password', 'PWD')) { if ($builder.ContainsKey($key)) { $sqlPassword = [string]$builder[$key]; break } }
if (!$login -or !$sqlPassword) { throw 'Database settings have no login.' }
$original = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
[Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $sqlPassword, 'Process')
try {
    $exports = @{
        'plans.json' = 'SET NOCOUNT ON; SELECT * FROM Plans FOR JSON PATH;'
        'owner-secrets.json' = 'SET NOCOUNT ON; SELECT * FROM OwnerSecrets FOR JSON PATH;'
        'merchants-config.json' = 'SET NOCOUNT ON; SELECT Id, Alias, PlanCode, AccessUntil, TrialEndsAt, ChecklistStep, ConnectedAt FROM Merchants FOR JSON PATH;'
        'transfer-columns.txt' = "SET NOCOUNT ON; SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = 'Transfers' ORDER BY c.column_id;"
        'table-counts.txt' = 'SET NOCOUNT ON; SELECT t.name, SUM(p.rows) FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1) GROUP BY t.name ORDER BY t.name;'
    }
    foreach ($name in $exports.Keys) {
        $out = Join-Path $root $name
        & $sqlcmd -S 'tcp:db75697.public.databaseasp.net,1433' -d 'db75697' -U $login -N -l 20 -t 60 -b -y 0 -o $out -Q $exports[$name]
        if ($LASTEXITCODE -ne 0) { throw "SQL export failed for $name." }
        $lines.Add("$name bytes=$((Get-Item -LiteralPath $out).Length)")
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $original, 'Process')
    $sqlPassword = $null
    $password = $null
}

$manifest = Join-Path $root 'manifest.txt'
$lines | Set-Content -LiteralPath $manifest -Encoding utf8
Write-Host "Snapshot written. Files and hashes are in the snapshot folder. No server file was uploaded or deleted."
Write-Host $root

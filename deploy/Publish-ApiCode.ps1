# Publish API assemblies only. Refuses configuration, web.config, App_Data and any delete.
# Each file is synced on its own so a busy logs folder cannot exhaust the sync pass limit.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Files,
    [string] $PublishSettings = "",
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (!$here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (!$PublishSettings) { $PublishSettings = Join-Path $here 'local\WebDeploy.Production.transferscheck.com-WebDeploy.publishSettings' }
$allowed = '^(TransfersCheck\.(Api|Core|Infrastructure)\.(dll|pdb))$'
foreach ($file in $Files) {
    $name = [IO.Path]::GetFileName($file)
    if ($name -notmatch $allowed) { throw "Refusing '$name'. Only API, Core and Infrastructure dll/pdb files can be published." }
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing $file" }
}
$msdeploy = 'C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe'
if (!(Test-Path -LiteralPath $msdeploy)) { throw "msdeploy.exe was not found." }
[xml]$settings = Get-Content -LiteralPath $PublishSettings -Raw
$profile = @($settings.publishData.publishProfile) | Where-Object publishMethod -eq 'MSDeploy' | Select-Object -First 1
if (!$profile -or !$profile.userPWD) { throw 'The local WebDeploy profile has no credentials.' }
$password = [string]$profile.userPWD
if ($password.Contains("'")) { throw 'This profile requires a different credential transport; credentials were not printed.' }
$site = [string]$profile.msdeploySite
$endpoint = [string]$profile.publishUrl
if (!$endpoint.StartsWith('https://')) { $endpoint = 'https://' + $endpoint }
$uri = [UriBuilder]::new($endpoint)
if ($uri.Uri.IsDefaultPort) { $uri.Port = 8172 }
$endpoint = $uri.Uri.AbsoluteUri.TrimEnd('/')
if ($endpoint -notmatch '/msdeploy.axd') { $endpoint = $endpoint.TrimEnd('/') + '/msdeploy.axd?site=' + [Uri]::EscapeDataString($site) }
$userName = [string]$profile.userName

function Redact([string] $text) {
    if ($null -eq $text) { return '' }
    return $text.Replace($password, '[redacted]').Replace($userName, '[account]')
}

function Invoke-Msdeploy([string[]] $ArgumentList) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $msdeploy @ArgumentList 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous
    $text = Redact (($output | Out-String))
    return [pscustomobject]@{ Code = $code; Text = $text.Trim() }
}

function Assert-FilePreview([string] $Text, [string] $FileName) {
    if ($Text -match '(?i)appsettings|web\.config|App_Data|TransfersCheck\.(Core|Infrastructure)|\\logs\\|/logs/') {
        throw "Preview mentions a forbidden path. Stopped.`n$Text"
    }
    if ($Text -notmatch '\((\d+) added, (\d+) deleted,') {
        throw "Preview did not report a change count. Stopped.`n$Text"
    }
    if ([int]$Matches[2] -ne 0) { throw "Preview would delete $($Matches[2]) files. Stopped.`n$Text" }
    $mentioned = [regex]::Matches($Text, '(?i)(?:Adding|Updating|Deleting)\s+file\s+\(([^)]+)\)')
    if ($mentioned.Count -eq 0 -and $Text -notmatch '\(0 added, 0 deleted, 0 updated') {
        throw "Preview did not name the file it would change. Stopped.`n$Text"
    }
    foreach ($hit in $mentioned) {
        $leaf = [IO.Path]::GetFileName($hit.Groups[1].Value.Trim().TrimEnd('.'))
        if ($leaf -ne $FileName) { throw "Preview would change '$leaf', which is not $FileName. Stopped.`n$Text" }
    }
    if ($Text -match '(?i)Deleting\s+file') { throw "Preview contains a delete. Stopped.`n$Text" }
}

function Get-RemoteArgs([string] $RemoteName) {
    return "-source:contentPath='$site/$RemoteName',computerName='$endpoint',userName='$userName',password='$password',authType=Basic"
}

function Get-DestArgs([string] $RemoteName) {
    return "-dest:contentPath='$site/$RemoteName',computerName='$endpoint',userName='$userName',password='$password',authType=Basic"
}

function Get-PlansStatus {
    try {
        $probe = Invoke-WebRequest -Uri 'https://auth.transferscheck.com/api/plans' -UseBasicParsing -TimeoutSec 20
        return [int]$probe.StatusCode
    } catch {
        $response = $_.Exception.Response
        if ($response -and $response.StatusCode) { return [int]$response.StatusCode }
        return 0
    }
}

function Wait-Plans([int] $Expected, [int] $Attempts) {
    for ($i = 0; $i -lt $Attempts; $i++) {
        $status = Get-PlansStatus
        if ($status -eq $Expected) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Sync-One([string] $LocalPath, [string] $RemoteName, [switch] $ApplyNow, [int] $LockRetries = 0) {
    $sourceArg = "-source:contentPath='$LocalPath'"
    $destArg = Get-DestArgs $RemoteName
    $preview = Invoke-Msdeploy @('-verb:sync', $sourceArg, $destArg, '-enableRule:DoNotDeleteRule', '-retryAttempts:6', '-retryInterval:2000', '-whatif')
    if ($preview.Code -ne 0) { throw "Preview failed for ${RemoteName}. $($preview.Text)" }
    Assert-FilePreview $preview.Text $RemoteName
    Write-Host $preview.Text
    if (-not $ApplyNow) { return $preview }
    $attempts = 1 + $LockRetries
    for ($n = 1; $n -le $attempts; $n++) {
        $applied = Invoke-Msdeploy @('-verb:sync', $sourceArg, $destArg, '-enableRule:DoNotDeleteRule', '-retryAttempts:12', '-retryInterval:3000')
        if ($applied.Code -eq 0) {
            if ($applied.Text -match '\((\d+) added, (\d+) deleted,') {
                if ([int]$Matches[2] -ne 0) { throw "Publish deleted $($Matches[2]) files while sending ${RemoteName}." }
            }
            Write-Host $applied.Text
            return $applied
        }
        $locked = $applied.Text -match 'ERROR_FILE_IN_USE|ERROR_EXCEEDED_MAX_SYNC_PASSES'
        if (-not $locked -or $n -eq $attempts) { throw "Publish failed for ${RemoteName}. $($applied.Text)" }
        Write-Host "The destination file is in use. Waiting before attempt $($n + 1)."
        Start-Sleep -Seconds 12
    }
}

function Remove-OfflinePage {
    $removed = Invoke-Msdeploy @('-verb:delete', (Get-DestArgs 'app_offline.htm'), '-retryAttempts:6', '-retryInterval:2000')
    Write-Host (Redact $removed.Text)
    if ($removed.Code -ne 0) { throw "app_offline.htm is still on the server. $($removed.Text)" }
}

function Publish-WithOffline([switch] $UseOffline) {
    if ($UseOffline) {
        New-Item -ItemType Directory -Force -Path $offlineDir | Out-Null
        $offlineFile = Join-Path $offlineDir 'app_offline.htm'
        Set-Content -LiteralPath $offlineFile -Value '<!DOCTYPE html><html><body>Mantenimiento breve.</body></html>' -Encoding Ascii
        Sync-One -LocalPath $offlineFile -RemoteName 'app_offline.htm' -ApplyNow | Out-Null
        $script:offlinePlaced = $true
        if (-not (Wait-Plans -Expected 503 -Attempts 12)) {
            Remove-OfflinePage
            $script:offlinePlaced = $false
            if (-not (Wait-Plans -Expected 200 -Attempts 10)) { throw 'The site did not return after the offline page was removed.' }
            throw 'The site did not go offline, so the assemblies were not uploaded.'
        }
        $recycleDest = "-dest:recycleApp='$site',computerName='$endpoint',userName='$userName',password='$password',authType=Basic"
        $recycled = Invoke-Msdeploy @('-verb:sync', '-source:recycleApp', $recycleDest, '-retryAttempts:3', '-retryInterval:2000')
        Write-Host (Redact $recycled.Text)
        Start-Sleep -Seconds 20
    }
    try {
        $script:replaced = $true
        foreach ($file in $Files) {
            Sync-One -LocalPath (Resolve-Path -LiteralPath $file).Path -RemoteName ([IO.Path]::GetFileName($file)) -ApplyNow -LockRetries 4 | Out-Null
        }
    } finally {
        if ($script:offlinePlaced) {
            Remove-OfflinePage
            $script:offlinePlaced = $false
        }
    }
}

$backup = Join-Path ([IO.Path]::GetTempPath()) ("tc-api-rollback-" + [guid]::NewGuid().ToString('N'))
$offlineDir = Join-Path ([IO.Path]::GetTempPath()) ("tc-offline-" + [guid]::NewGuid().ToString('N'))
$offlinePlaced = $false
$replaced = $false
try {
    foreach ($file in $Files) {
        Sync-One -LocalPath (Resolve-Path -LiteralPath $file).Path -RemoteName ([IO.Path]::GetFileName($file)) | Out-Null
    }
    if (-not $Apply) {
        Write-Host 'Preview only. Re-run with -Apply to publish these assemblies.'
        return
    }

    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($file in $Files) {
        $name = [IO.Path]::GetFileName($file)
        $localBackup = Join-Path $backup $name
        $pulled = Invoke-Msdeploy @('-verb:sync', (Get-RemoteArgs $name), "-dest:contentPath='$localBackup'", '-retryAttempts:6', '-retryInterval:2000')
        if ($pulled.Code -ne 0 -or !(Test-Path -LiteralPath $localBackup)) {
            throw "Could not back up the current $name, so nothing was replaced. $($pulled.Text)"
        }
    }

    try {
        Publish-WithOffline -UseOffline
    } catch {
        $reason = Redact "$_"
        if ($reason -match 'did not go offline' -and -not $replaced) {
            Write-Host 'Offline handoff failed. Retrying a single-file sync while the site is online.'
            Publish-WithOffline
        } else {
            throw
        }
    }
}
finally {
    if ($offlinePlaced) {
        try { Remove-OfflinePage } catch { Write-Host (Redact "$_") }
        $offlinePlaced = $false
    }
    if ($Apply) {
        $back = Wait-Plans -Expected 200 -Attempts 30
        if (-not $back -and $replaced -and (Test-Path -LiteralPath $backup)) {
            Write-Host 'The site did not return. Restoring the previous API assemblies.'
            try {
                New-Item -ItemType Directory -Force -Path $offlineDir | Out-Null
                $offlineFile = Join-Path $offlineDir 'app_offline.htm'
                Set-Content -LiteralPath $offlineFile -Value '<!DOCTYPE html><html><body>Mantenimiento breve.</body></html>' -Encoding Ascii
                Sync-One -LocalPath $offlineFile -RemoteName 'app_offline.htm' -ApplyNow | Out-Null
                $offlinePlaced = $true
                Start-Sleep -Seconds 5
                foreach ($saved in Get-ChildItem -File $backup) {
                    Sync-One -LocalPath $saved.FullName -RemoteName $saved.Name -ApplyNow | Out-Null
                }
            } finally {
                if ($offlinePlaced) {
                    try { Remove-OfflinePage } catch { Write-Host (Redact "$_") }
                    $offlinePlaced = $false
                }
            }
            $back = Wait-Plans -Expected 200 -Attempts 20
        }
        if ($back) { Write-Host 'Site check: /api/plans returned 200.' }
        else { Write-Host 'Site check: /api/plans did not return 200 after the publish attempt.' }
    }
    $password = $null
    Remove-Item -LiteralPath $offlineDir -Recurse -Force -ErrorAction SilentlyContinue
}

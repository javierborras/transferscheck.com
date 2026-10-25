[CmdletBinding()]
param(
    [string] $Server = 'db75697.public.databaseasp.net',
    [string] $Database = 'db75697',
    [string[]] $SettingsFiles = @("$PSScriptRoot\local\database-db75697.local.json"),
    [string] $ReportPath = "$PSScriptRoot\local\database-db75697-check.json"
)

$ErrorActionPreference = 'Stop'
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$seen = [Collections.Generic.HashSet[string]]::new()
$originalPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
# Only metadata SELECTs. Never start the API, run bootstrap, or read application rows.
$query = @'
SET NOCOUNT ON;
SELECT DB_NAME() AS [database],
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS engineVersion,
 CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)) AS collation,
 (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()) AS compatibilityLevel,
 HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') AS canCreateTables,
 HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'ALTER') AS canAlterDboSchema,
 JSON_QUERY((SELECT s.name AS [schema], t.name AS [table],
   (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = t.object_id AND p.index_id IN (0,1)) AS approximateRows,
   JSON_QUERY((SELECT c.name AS [name], TYPE_NAME(c.user_type_id) AS [type], c.is_nullable AS nullable
       FROM sys.columns c WHERE c.object_id = t.object_id ORDER BY c.column_id FOR JSON PATH)) AS columns
   FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
   ORDER BY s.name, t.name FOR JSON PATH)) AS tables
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
'@

try {
    foreach ($file in $SettingsFiles) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        try { $settings = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json }
        catch { Write-Host "Skipping unreadable JSON: $([IO.Path]::GetFileName($file))"; continue }
        foreach ($entry in $settings.ConnectionStrings.PSObject.Properties) {
            $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
            try { $builder.set_ConnectionString([string]$entry.Value) } catch { continue }
            $login = $null; $password = $null
            foreach ($key in @('User ID', 'UID', 'UserId')) { if ($builder.ContainsKey($key)) { $login = [string]$builder[$key]; break } }
            foreach ($key in @('Password', 'PWD')) { if ($builder.ContainsKey($key)) { $password = [string]$builder[$key]; break } }
            if (!$login -or !$password) { continue }
            $fingerprint = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$login`0$password")))
            if (!$seen.Add($fingerprint)) { continue }
            Write-Host "Checking $Server / $Database using $([IO.Path]::GetFileName($file)) [$($entry.Name)] (credentials hidden)."
            [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $password, 'Process')
            # -N requires encryption; do not use -C (TrustServerCertificate).
            $output = & $sqlcmd -S "tcp:$Server,1433" -d $Database -U $login -N -l 12 -t 20 -b -y 0 -w 65535 -Q $query 2>&1
            $result = $LASTEXITCODE
            [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $originalPassword, 'Process')
            if ($result -ne 0) {
                $message = $output -join "`n"
                $reason = if ($message -match '18456|Login failed') { 'login rejected' } elseif ($message -match 'certificate|SSL|TLS') { 'TLS validation failed' } else { 'connection or permission error' }
                foreach ($sensitive in @([string]$entry.Value, [string]$password, [string]$login)) {
                    if ($sensitive) { $message = $message.Replace($sensitive, '[redacted]') }
                }
                $password = $null
                Write-Host "Not connected: $reason. Credentials redacted."
                Write-Host $message.Substring(0, [Math]::Min(900, $message.Length))
                continue
            }
            $password = $null
            $json = ($output -join '').Trim()
            $start = $json.IndexOf('{')
            if ($start -lt 0) { throw 'SQL metadata response did not contain JSON.' }
            $report = $json.Substring($start) | ConvertFrom-Json
            $report | Add-Member -NotePropertyName server -NotePropertyValue $Server
            $report | Add-Member -NotePropertyName credentialSourceFile -NotePropertyValue ([IO.Path]::GetFullPath($file))
            $report | Add-Member -NotePropertyName checkedAtUtc -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('o'))
            $directory = Split-Path -Parent $ReportPath
            New-Item -ItemType Directory -Force -Path $directory | Out-Null
            $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding utf8
            $tableCount = if ($null -eq $report.tables) { 0 } else { @($report.tables).Count }
            Write-Host "Connected with certificate validation. Database: $($report.database); engine: $($report.engineVersion); tables: $tableCount."
            $report.tables | Select-Object schema, table, approximateRows | Format-Table -AutoSize
            Write-Host "Metadata-only report: $ReportPath"
            return
        }
    }
    throw 'None of the distinct credentials connected to the specified production database. No database changes were made.'
}
finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $originalPassword, 'Process')
    $password = $null
}

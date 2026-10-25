<#
.SYNOPSIS
    Sube cambios puntuales por FTP a MonsterASP.net (TransfersCheck API) sin necesidad de hacer un WebDeploy completo.

.DESCRIPTION
    Permite subir archivos específicos (DLLs, JSONs, configs), compilar y subir rápidamente
    los assemblies del proyecto, listar archivos remotos o descargar archivos para inspección.
    Gestiona automáticamente 'app_offline.htm' al subir archivos DLL para evitar el error de archivo en uso por IIS.

.PARAMETER FilePath
    Ruta local de uno o varios archivos a subir (soporta comodines como *.dll).

.PARAMETER RemoteSubDir
    Subcarpeta remota relativa a /wwwroot (por defecto la raíz de wwwroot).

.PARAMETER Build
    Compila 'src/TransfersCheck.Api/TransfersCheck.Api.csproj' en Release y sube automáticamente
    las librerías principales del proyecto (TransfersCheck.Api, TransfersCheck.Core, TransfersCheck.Infrastructure).

.PARAMETER IncludePdb
    Si se usa con -Build, incluye también los archivos .pdb para stack traces en producción (por defecto $true).

.PARAMETER List
    Lista los archivos y carpetas existentes en /wwwroot o en la subcarpeta indicada.

.PARAMETER Test
    Verifica las credenciales y conectividad con el servidor FTP.

.PARAMETER Download
    Ruta relativa en el servidor FTP del archivo que deseas descargar (ej: "appsettings.Production.json").

.PARAMETER LocalDest
    Ruta local donde guardar el archivo descargado con -Download.

.PARAMETER Offline
    Fuerza la colocación de 'app_offline.htm' en el servidor para detener temporalmente IIS y liberar archivos en uso.

.PARAMETER NoOffline
    Desactiva la colocación automática de 'app_offline.htm' al subir DLLs.

.EXAMPLE
    # Probar conexión:
    .\deploy\Upload-FtpPatch.ps1 -Test

.EXAMPLE
    # Listar contenido en /wwwroot:
    .\deploy\Upload-FtpPatch.ps1 -List

.EXAMPLE
    # Compilar y subir DLLs del proyecto en 5 segundos (el flujo más rápido para parches):
    .\deploy\Upload-FtpPatch.ps1 -Build

.EXAMPLE
    # Subir un archivo específico a la raíz de wwwroot:
    .\deploy\Upload-FtpPatch.ps1 -FilePath "src\TransfersCheck.Api\appsettings.Production.json"

.EXAMPLE
    # Subir una DLL específica:
    .\deploy\Upload-FtpPatch.ps1 -FilePath "src\TransfersCheck.Api\bin\Release\net10.0\TransfersCheck.Api.dll"

.EXAMPLE
    # Descargar un archivo desde el servidor para inspeccionarlo localmente:
    .\deploy\Upload-FtpPatch.ps1 -Download "web.config" -LocalDest "deploy\local\web.config.remote"
#>

[CmdletBinding(DefaultParameterSetName = 'Upload')]
param(
    [Parameter(ParameterSetName = 'Upload', Position = 0, ValueFromPipeline = $true)]
    [string[]] $FilePath,

    [Parameter(ParameterSetName = 'Upload')]
    [string] $RemoteSubDir = "",

    [Parameter(ParameterSetName = 'Build')]
    [switch] $Build,

    [Parameter(ParameterSetName = 'Build')]
    [bool] $IncludePdb = $true,

    [Parameter(ParameterSetName = 'List')]
    [switch] $List,

    [Parameter(ParameterSetName = 'Test')]
    [switch] $Test,

    [Parameter(ParameterSetName = 'Download')]
    [string] $Download,

    [Parameter(ParameterSetName = 'Download')]
    [string] $LocalDest = "",

    [switch] $Offline,
    [switch] $NoOffline,

    [string] $Server = "site56119.siteasp.net",
    [int] $Port = 21,
    [string] $User = "site56119",
    [string] $Password = "",
    [string] $RemoteRoot = "/wwwroot"
)

$ErrorActionPreference = 'Stop'

# Resolución de password por defecto
if ([string]::IsNullOrWhiteSpace($Password)) {
    if (-not [string]::IsNullOrWhiteSpace($env:MONSTERASP_FTP_PASSWORD)) {
        $Password = $env:MONSTERASP_FTP_PASSWORD
    } else {
        throw 'Set MONSTERASP_FTP_PASSWORD or pass -Password. No credential is stored in this script.'
    }
}

# Ubicar raíz del repositorio (la carpeta padre de deploy/)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

function Write-Step {
    param([string]$Text)
    Write-Host "`n==> $Text" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Text)
    Write-Host "[OK] $Text" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Text)
    Write-Host "[AVISO] $Text" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Text)
    Write-Host "[ERROR] $Text" -ForegroundColor Red
}

# Comprobación de curl.exe (estándar en Windows 10/11)
$curlCmd = Get-Command curl.exe -ErrorAction SilentlyContinue
if (!$curlCmd) {
    throw "curl.exe no se encontró en el PATH. Es necesario para ejecutar transferencias FTP."
}

# Helper: Construir URL FTP
function Get-FtpUrl {
    param([string]$RelativePath = "")
    $normRoot = $RemoteRoot.Trim().TrimEnd('/')
    if (!$normRoot.StartsWith('/')) { $normRoot = "/$normRoot" }

    $cleanRel = $RelativePath.Trim().Replace('\', '/').TrimStart('/')
    if ($cleanRel) {
        return "ftp://$Server$normRoot/$cleanRel"
    } else {
        return "ftp://$Server$normRoot/"
    }
}

# Helper: Eliminar archivo remoto vía FTP (.NET FtpWebRequest o curl)
function Remove-RemoteFile {
    param([string]$RelativePath)
    $url = Get-FtpUrl -RelativePath $RelativePath
    try {
        $req = [System.Net.FtpWebRequest]::Create($url)
        $req.Credentials = [System.Net.NetworkCredential]::new($User, $Password)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $resp = $req.GetResponse()
        $resp.Close()
        return $true
    } catch {
        # Intentar con curl si .NET falla
        $fileName = [System.IO.Path]::GetFileName($RelativePath)
        $dir = [System.IO.Path]::GetDirectoryName($RelativePath)
        if ($dir) { $dir = $dir.Replace('\', '/') } else { $dir = "" }
        $ftpDir = Get-FtpUrl -RelativePath $dir
        & curl.exe -s -u "$($User):$($Password)" "$ftpDir" -Q "DELE $fileName" | Out-Null
        return ($LASTEXITCODE -eq 0)
    }
}

# Helper: Subir archivo individual por FTP con curl
function Send-FtpFile {
    param(
        [string]$LocalFile,
        [string]$RemoteSubFolder = ""
    )

    if (-not (Test-Path -LiteralPath $LocalFile)) {
        Write-Err "No existe el archivo local: $LocalFile"
        return $false
    }

    $fileName = [System.IO.Path]::GetFileName($LocalFile)
    $fileInfo = Get-Item -LiteralPath $LocalFile
    $sizeKb = [Math]::Round($fileInfo.Length / 1024, 1)

    $destRel = if ($RemoteSubFolder) { "$($RemoteSubFolder.TrimEnd('/'))/$fileName" } else { $fileName }
    $targetUrl = Get-FtpUrl -RelativePath $destRel

    Write-Host "  -> Subiendo $fileName ($sizeKb KB) a $targetUrl..." -NoNewline

    $output = & curl.exe -sS -u "$($User):$($Password)" -T "$LocalFile" "$targetUrl" --ftp-create-dirs 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " [OK]" -ForegroundColor Green
        return $true
    } else {
        Write-Host " [FALLO]" -ForegroundColor Red
        Write-Err "Detalle de error de curl:`n$output"
        return $false
    }
}

# Helper: Poner sitio en offline temporal
function Set-SiteOffline {
    Write-Step "Colocando sitio en modo Offline (app_offline.htm) para liberar bloqueos de IIS..."
    $localOffline = Join-Path $ScriptDir "local\app_offline.htm"
    $tempFile = $null

    if (Test-Path -LiteralPath $localOffline) {
        $offlinePath = $localOffline
    } else {
        $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) "app_offline.htm"
        Set-Content -LiteralPath $tempFile -Value "Actualizando TransfersCheck. Volve a intentar en unos segundos." -Encoding utf8
        $offlinePath = $tempFile
    }

    $ok = Send-FtpFile -LocalFile $offlinePath -RemoteSubFolder ""
    if ($tempFile -and (Test-Path $tempFile)) { Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue }

    if ($ok) {
        Write-Host "  Esperando 2 segundos para que IIS descargue los assemblies de memoria..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 2
    }
    return $ok
}

# Helper: Quitar app_offline.htm
function Set-SiteOnline {
    Write-Step "Reactivando el sitio en IIS (eliminando app_offline.htm)..."
    $ok = Remove-RemoteFile -RelativePath "app_offline.htm"
    if ($ok) {
        Write-Success "Sitio reactivado exitosamente en MonsterASP."
    } else {
        Write-Warn "No se pudo eliminar app_offline.htm automáticamente. Verificalo en el WebFTP si el sitio da 503."
    }
}

# ----------------- EJECUCIÓN SEGÚN MODO -----------------

# MODO: Test
if ($PSCmdlet.ParameterSetName -eq 'Test' -or $Test) {
    Write-Step "Probando conexión FTP a $Server (Puerto $Port)..."
    $targetUrl = Get-FtpUrl
    $output = & curl.exe -sS --list-only -u "$($User):$($Password)" "$targetUrl" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Conexión FTP exitosa con MonsterASP ($User@$Server)."
        $lines = ($output -split "`r?`n") | Where-Object { $_.Trim() -ne "" }
        Write-Host "Total de elementos visibles en $($RemoteRoot): $($lines.Count)" -ForegroundColor Gray
    } else {
        Write-Err "Fallo al conectar por FTP: $output"
    }
    exit 0
}

# MODO: List
if ($PSCmdlet.ParameterSetName -eq 'List' -or $List) {
    $targetUrl = Get-FtpUrl -RelativePath $RemoteSubDir
    Write-Step "Listando contenido de $targetUrl..."
    $output = & curl.exe -sS -u "$($User):$($Password)" "$targetUrl" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host $output
    } else {
        Write-Err "Fallo al listar directorio: $output"
    }
    exit 0
}

# MODO: Download
if ($PSCmdlet.ParameterSetName -eq 'Download' -or $Download) {
    $targetUrl = Get-FtpUrl -RelativePath $Download
    $dest = if ($LocalDest) { $LocalDest } else { [System.IO.Path]::GetFileName($Download) }

    Write-Step "Descargando $targetUrl a $dest..."
    $output = & curl.exe -sS -u "$($User):$($Password)" "$targetUrl" -o "$dest" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Archivo descargado en $dest"
    } else {
        Write-Err "Fallo en la descarga: $output"
    }
    exit 0
}

# MODO: Build & Upload
if ($PSCmdlet.ParameterSetName -eq 'Build' -or $Build) {
    Write-Step "Compilando TransfersCheck.Api en modo Release..."
    $projectPath = Join-Path $RepoRoot "src\TransfersCheck.Api\TransfersCheck.Api.csproj"
    if (-not (Test-Path $projectPath)) {
        throw "No se encontró el proyecto en: $projectPath"
    }

    $buildOutput = & dotnet build "$projectPath" -c Release --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Error durante la compilación:"
        Write-Host ($buildOutput -join "`n")
        exit 1
    }
    Write-Success "Compilación completada exitosamente."

    # Identificar binarios clave del proyecto
    $binDir = Join-Path $RepoRoot "src\TransfersCheck.Api\bin\Release\net10.0"
    if (-not (Test-Path $binDir)) {
        throw "No se encontró el directorio de salida: $binDir"
    }

    $coreDlls = @(
        "TransfersCheck.Api.dll",
        "TransfersCheck.Core.dll",
        "TransfersCheck.Infrastructure.dll"
    )

    $filesToUpload = [System.Collections.Generic.List[string]]::new()
    foreach ($dll in $coreDlls) {
        $p = Join-Path $binDir $dll
        if (Test-Path $p) { $filesToUpload.Add($p) }

        if ($IncludePdb) {
            $pdb = Join-Path $binDir ($dll.Replace(".dll", ".pdb"))
            if (Test-Path $pdb) { $filesToUpload.Add($pdb) }
        }
    }

    # También incluir deps.json / runtimeconfig si existen
    $runtimeConfig = Join-Path $binDir "TransfersCheck.Api.runtimeconfig.json"
    if (Test-Path $runtimeConfig) { $filesToUpload.Add($runtimeConfig) }

    Write-Host "Archivos seleccionados para parchear en producción:" -ForegroundColor Yellow
    foreach ($f in $filesToUpload) {
        Write-Host "  * $(Split-Path -Leaf $f)" -ForegroundColor Gray
    }

    # Como son DLLs, colocamos el sitio offline
    Set-SiteOffline | Out-Null
    $failedCount = 0

    try {
        Write-Step "Subiendo binarios por FTP a $RemoteRoot..."
        foreach ($file in $filesToUpload) {
            $res = Send-FtpFile -LocalFile $file -RemoteSubFolder ""
            if (-not $res) { $failedCount++ }
        }
    }
    finally {
        Set-SiteOnline
    }

    if ($failedCount -eq 0) {
        Write-Success "¡Parche desplegado exitosamente! Tu API en MonsterASP ya está corriendo los nuevos cambios."
    } else {
        Write-Err "Se completó la subida pero $failedCount archivo(s) tuvieron errores."
    }
    exit 0
}

# MODO: Upload de archivos individuales o por patrón
if ($FilePath -and $FilePath.Count -gt 0) {
    # Resolver todos los archivos (soportando rutas relativas, absolutas o comodines)
    $resolvedFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($pattern in $FilePath) {
        $items = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue
        if ($items) {
            foreach ($item in $items) { $resolvedFiles.Add($item.FullName) }
        } else {
            if (Test-Path -LiteralPath $pattern) {
                $resolvedFiles.Add((Resolve-Path -LiteralPath $pattern).Path)
            } else {
                Write-Warn "No se encontró ningún archivo con el patrón: $pattern"
            }
        }
    }

    if ($resolvedFiles.Count -eq 0) {
        Write-Err "No se especificaron archivos válidos para subir."
        exit 1
    }

    # Determinar si necesitamos modo offline
    $hasDllOrExe = ($resolvedFiles | Where-Object { $_ -match '\.(dll|exe)$' }).Count -gt 0
    $needsOffline = ($Offline -or ($hasDllOrExe -and (-not $NoOffline)))

    if ($needsOffline) {
        Set-SiteOffline | Out-Null
    }

    $failedCount = 0
    try {
        Write-Step "Subiendo $($resolvedFiles.Count) archivo(s) por FTP..."
        foreach ($file in $resolvedFiles) {
            $res = Send-FtpFile -LocalFile $file -RemoteSubFolder $RemoteSubDir
            if (-not $res) { $failedCount++ }
        }
    }
    finally {
        if ($needsOffline) {
            Set-SiteOnline
        }
    }

    if ($failedCount -eq 0) {
        Write-Success "¡Todos los archivos fueron subidos exitosamente a MonsterASP!"
    } else {
        Write-Err "Hubo errores en $failedCount archivo(s)."
    }
    exit 0
}

# Si no pasó ningún parámetro, mostrar ayuda rápida
Write-Host @"
=============================================================
  MONSTERASP FTP PATCHER - TransfersCheck.com
=============================================================
Uso rápido:

1. COMPILAR Y SUBIR CAMBIOS DE CÓDIGO (Recomendado):
   .\deploy\Upload-FtpPatch.ps1 -Build

2. SUBIR UN ARCHIVO ESPECÍFICO:
   .\deploy\Upload-FtpPatch.ps1 "src\TransfersCheck.Api\appsettings.Production.json"
   .\deploy\Upload-FtpPatch.ps1 "web.config"

3. LISTAR ARCHIVOS REMOTOS:
   .\deploy\Upload-FtpPatch.ps1 -List

4. PROBAR CONEXIÓN:
   .\deploy\Upload-FtpPatch.ps1 -Test

5. DESCARGAR UN ARCHIVO DESDE EL SERVIDOR:
   .\deploy\Upload-FtpPatch.ps1 -Download "appsettings.Production.json" -LocalDest "deploy\local\prod_settings.json"

Para más opciones: Get-Help .\deploy\Upload-FtpPatch.ps1 -Detailed
=============================================================
"@ -ForegroundColor Cyan

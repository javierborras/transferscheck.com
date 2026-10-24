# Compila y levanta los sitios locales de TransfersCheck (mock, API y PWA).
# Los procesos siguen en marcha cuando este script termina.
# ASCII a proposito: Windows PowerShell 5.1 no debe depender de acentos.

$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"

$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LogDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$MockPort = 5099
$ApiPort = 5080
$MockUrl = "http://localhost:$MockPort"
$ApiUrl = "http://localhost:$ApiPort"

function Write-Info([string]$Message) {
    Write-Host $Message
}

function Get-VitePort {
    $path = Join-Path $Repo "web\vite.config.ts"
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Info "No se encontro web/vite.config.ts."
        exit 1
    }
    $text = Get-Content -LiteralPath $path -Raw
    $match = [regex]::Match($text, "port:\s*(\d+)")
    if (-not $match.Success) {
        Write-Info "No se pudo leer el puerto en web/vite.config.ts."
        exit 1
    }
    return [int]$match.Groups[1].Value
}

function Test-TcpAddress([System.Net.IPAddress]$Address, [int]$Port) {
    $tcp = $null
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient($Address.AddressFamily)
        $wait = $tcp.BeginConnect($Address, $Port, $null, $null)
        if (-not $wait.AsyncWaitHandle.WaitOne(400, $false)) {
            return $false
        }
        try {
            $tcp.EndConnect($wait)
        }
        catch {
            return $false
        }
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $tcp) {
            try { $tcp.Close() } catch { }
        }
    }
}

function Test-TcpOpen([int]$Port) {
    $seen = @{}
    $candidates = New-Object System.Collections.Generic.List[System.Net.IPAddress]
    try {
        foreach ($addr in @([System.Net.Dns]::GetHostAddresses("localhost"))) {
            $candidates.Add($addr) | Out-Null
        }
    }
    catch { }
    $candidates.Add([System.Net.IPAddress]::Loopback) | Out-Null
    $candidates.Add([System.Net.IPAddress]::IPv6Loopback) | Out-Null
    foreach ($addr in $candidates) {
        $key = $addr.ToString()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        if (Test-TcpAddress $addr $Port) { return $true }
    }
    return $false
}

function Show-LogTail([string]$Path) {
    Write-Info "Ultimas lineas del log:"
    Write-Info $Path
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Info "(el log no existe)"
        return
    }
    $lines = @(Get-Content -LiteralPath $Path -Tail 50 -ErrorAction SilentlyContinue)
    foreach ($line in $lines) {
        if ($line -match '(?i)(Password|Pwd|Secret|SigningKey|AccessToken)\s*=') {
            Write-Info "[linea con secreto omitida]"
            continue
        }
        if ($line -match '(?i)(Server|Data Source)\s*=.+(Database|Initial Catalog)\s*=') {
            Write-Info "[linea con cadena de conexion omitida]"
            continue
        }
        Write-Info $line
    }
}

function Write-AsciiFile([string]$Path, [string[]]$Lines) {
    $text = ((@($Lines) | Where-Object { $_ -ne $null }) -join "`r`n") + "`r`n"
    [System.IO.File]::WriteAllText($Path, $text, [System.Text.Encoding]::ASCII)
}

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$LogPath
    )
    Push-Location $WorkingDirectory
    try {
        $output = & $FilePath @Arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($output)) {
        if ($null -eq $item) { continue }
        $lines.Add($item.ToString()) | Out-Null
    }
    if ($LogPath) {
        $lines | Out-File -FilePath $LogPath -Encoding ascii
    }
    foreach ($line in $lines) {
        Write-Host $line
    }
    return $code
}

function Resolve-DotNetHost {
    $globalPath = Join-Path $Repo "global.json"
    if (-not (Test-Path -LiteralPath $globalPath)) {
        Write-Info "No se encontro global.json."
        exit 1
    }
    $raw = Get-Content -LiteralPath $globalPath -Raw
    $raw = $raw.TrimStart([char]0xFEFF)
    $cfg = $raw | ConvertFrom-Json
    $required = [string]$cfg.sdk.version
    $roll = [string]$cfg.sdk.rollForward
    if (-not $roll) { $roll = "latestFeature" }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"),
        (Join-Path $env:USERPROFILE ".dotnet")
    )

    $savedRoot = $env:DOTNET_ROOT
    $savedPath = $env:PATH
    $savedLookup = $env:DOTNET_MULTILEVEL_LOOKUP

    foreach ($root in $candidates) {
        $exe = Join-Path $root "dotnet.exe"
        if (-not (Test-Path -LiteralPath $exe)) { continue }

        $env:DOTNET_ROOT = $root
        $env:DOTNET_MULTILEVEL_LOOKUP = "0"
        $env:PATH = "$root;$savedPath"
        Push-Location $Repo
        try {
            $output = & $exe --version 2>&1
            $code = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($code -ne 0) { continue }

        $version = $null
        foreach ($item in @($output)) {
            if ($null -eq $item) { continue }
            $line = $item.ToString().Trim()
            if ($line -match '^\d+\.\d+\.\d+') {
                $version = $line
                break
            }
        }
        if ($version) {
            return @{
                Exe = $exe
                Root = $root
                Version = $version
                Required = $required
                RollForward = $roll
            }
        }
    }

    $env:DOTNET_ROOT = $savedRoot
    $env:PATH = $savedPath
    if ($null -eq $savedLookup -or $savedLookup -eq "") {
        Remove-Item Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
    }
    else {
        $env:DOTNET_MULTILEVEL_LOOKUP = $savedLookup
    }

    Write-Info "No se encontro un SDK de .NET compatible con global.json (version $required, rollForward $roll)."
    Write-Info "Se busco dotnet.exe en:"
    foreach ($root in $candidates) {
        Write-Info "  $root"
    }
    Write-Info "No se descarga ni se instala el SDK."
    exit 1
}

function Start-Detached([string]$CmdFile) {
    $commandLine = "cmd.exe /c `"$CmdFile`""
    try {
        $result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
            CommandLine = $commandLine
            CurrentDirectory = $Repo
        } -ErrorAction Stop
        if ($result.ReturnValue -eq 0 -and $result.ProcessId) {
            return [int]$result.ProcessId
        }
        Write-Info "WMI no pudo iniciar el proceso (codigo $($result.ReturnValue)). Se intenta Start-Process."
    }
    catch {
        Write-Info "WMI no disponible. Se intenta Start-Process."
    }

    $proc = Start-Process -FilePath $CmdFile -WindowStyle Hidden -PassThru
    if (-not $proc) {
        Write-Info "No se pudo iniciar $CmdFile"
        exit 1
    }
    return [int]$proc.Id
}

function Write-DotNetLauncher([string]$Name, [string]$Project, [string]$Url) {
    $log = Join-Path $LogDir "$Name.log"
    $cmd = Join-Path $LogDir "$Name.cmd"
    $exe = $script:DotNetExe
    $auth0 = @()
    if ($Name -eq "api") {
        # Commerce door is Auth0 (ADR-018); locally the mock server plays the tenant.
        $auth0 = @(
            "set `"Auth0__Authority=$MockUrl/auth0`"",
            "set `"Auth0__ClientId=mock-auth0-client`"",
            "set `"Auth0__ClientSecret=mock-auth0-secret`"",
            "set `"Auth0__RedirectUri=$ApiUrl/api/auth0/callback`""
        )
    }
    Write-AsciiFile -Path $cmd -Lines (@(
        "@echo off",
        "setlocal",
        "set `"DOTNET_ROOT=$script:DotNetRoot`"",
        "set `"DOTNET_MULTILEVEL_LOOKUP=0`"",
        "set `"PATH=%DOTNET_ROOT%;%PATH%`"",
        "set `"ASPNETCORE_ENVIRONMENT=Development`"",
        "set `"ASPNETCORE_URLS=$Url`""
    ) + $auth0 + @(
        "cd /d `"$Repo`"",
        "> `"$log`" echo iniciando-$Name",
        "`"$exe`" run --no-build --no-launch-profile --project `"$Project`" >> `"$log`" 2>&1"
    ))
    return $cmd
}

function Write-ViteLauncher([string]$NpmCmd) {
    $log = Join-Path $LogDir "vite.log"
    $cmd = Join-Path $LogDir "vite.cmd"
    $web = Join-Path $Repo "web"
    Write-AsciiFile -Path $cmd -Lines @(
        "@echo off",
        "setlocal",
        "cd /d `"$web`"",
        "> `"$log`" echo iniciando-vite",
        "call `"$NpmCmd`" run dev >> `"$log`" 2>&1"
    )
    return $cmd
}

function Wait-Site([string]$Name, [int]$Port, [string]$Url, [int]$TimeoutSec) {
    $log = Join-Path $LogDir "$Name.log"
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-TcpOpen $Port) {
            Start-Sleep -Seconds 2
            if (Test-TcpOpen $Port) {
                Write-Info "En marcha: $Url"
                return
            }
            Write-Info "$Name abrio el puerto $Port y se detuvo enseguida."
            Show-LogTail $log
            exit 1
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Info "$Name no acepto conexiones en $Url antes de $TimeoutSec segundos."
    Show-LogTail $log
    exit 1
}

function Show-Urls([int]$VitePort) {
    Write-Info "Sitios locales:"
    Write-Info "  Mock (Mercado Pago / FCM): $MockUrl"
    Write-Info "  API (Swagger): $ApiUrl/swagger"
    Write-Info "  PWA: http://localhost:$VitePort/"
}

$vitePort = Get-VitePort
$ViteUrl = "http://localhost:$vitePort/"

Write-Info "Resolviendo SDK de .NET pedido por global.json..."
$dotnet = Resolve-DotNetHost
$script:DotNetExe = $dotnet.Exe
$script:DotNetRoot = $dotnet.Root
$env:DOTNET_ROOT = $dotnet.Root
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "$($dotnet.Root);$env:PATH"
Write-Info "SDK de .NET listo: $($dotnet.Version)"

$mockUp = Test-TcpOpen $MockPort
$apiUp = Test-TcpOpen $ApiPort
$viteUp = Test-TcpOpen $vitePort

if ($mockUp) { Write-Info "Ya esta en marcha: $MockUrl" }
if ($apiUp) { Write-Info "Ya esta en marcha: $ApiUrl" }
if ($viteUp) { Write-Info "Ya esta en marcha: $ViteUrl" }

$needMock = -not $mockUp
$needApi = -not $apiUp
$needVite = -not $viteUp

if ($needMock -or $needApi) {
    $apiProject = Join-Path $Repo "src\TransfersCheck.Api\TransfersCheck.Api.csproj"
    $mockProject = Join-Path $Repo "tools\TransfersCheck.MockServer\TransfersCheck.MockServer.csproj"
    $sln = Join-Path $Repo "TransfersCheck.sln"
    $slnx = Join-Path $Repo "TransfersCheck.slnx"
    $buildLog = Join-Path $LogDir "build.log"

    if ($needMock -and $needApi -and (Test-Path -LiteralPath $sln)) {
        Write-Info "Compilando la solucion..."
        $buildTarget = $sln
    }
    elseif ($needMock -and $needApi -and (Test-Path -LiteralPath $slnx)) {
        Write-Info "Compilando la solucion..."
        $buildTarget = $slnx
    }
    elseif ($needApi) {
        Write-Info "Compilando la API..."
        $buildTarget = $apiProject
    }
    else {
        Write-Info "Compilando el mock..."
        $buildTarget = $mockProject
    }

    $buildCode = Invoke-Native -FilePath $script:DotNetExe -Arguments @("build", $buildTarget, "--verbosity", "minimal") -WorkingDirectory $Repo -LogPath $buildLog
    if ($buildCode -ne 0) {
        Write-Info "Fallo la compilacion de .NET."
        exit $buildCode
    }

    if ($needMock -and $needApi -and $buildTarget -eq $apiProject) {
        $mockCode = Invoke-Native -FilePath $script:DotNetExe -Arguments @("build", $mockProject, "--verbosity", "minimal") -WorkingDirectory $Repo -LogPath (Join-Path $LogDir "build-mock.log")
        if ($mockCode -ne 0) {
            Write-Info "Fallo la compilacion del mock."
            exit $mockCode
        }
    }
}

if ($needVite) {
    $web = Join-Path $Repo "web"
    $modules = Join-Path $web "node_modules"
    $npmCmdInfo = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if (-not $npmCmdInfo) {
        Write-Info "No se encontro npm.cmd en PATH. Instale Node.js y vuelva a ejecutar."
        exit 1
    }
    $script:NpmCmd = $npmCmdInfo.Source
    if (-not (Test-Path -LiteralPath $modules)) {
        Write-Info "node_modules no existe. Ejecutando npm install..."
        $npmCode = Invoke-Native -FilePath $script:NpmCmd -Arguments @("install") -WorkingDirectory $web -LogPath (Join-Path $LogDir "npm-install.log")
        if ($npmCode -ne 0) {
            Write-Info "Fallo npm install."
            exit $npmCode
        }
    }
}

if ($needMock) {
    Write-Info "Iniciando mock en $MockUrl ..."
    $mockProject = Join-Path $Repo "tools\TransfersCheck.MockServer\TransfersCheck.MockServer.csproj"
    $mockCmd = Write-DotNetLauncher -Name "mock" -Project $mockProject -Url $MockUrl
    $mockPid = Start-Detached $mockCmd
    Write-Info "Mock PID $mockPid"
    Wait-Site -Name "mock" -Port $MockPort -Url $MockUrl -TimeoutSec 90
}

if ($needApi) {
    Write-Info "Iniciando API en $ApiUrl ..."
    $apiProject = Join-Path $Repo "src\TransfersCheck.Api\TransfersCheck.Api.csproj"
    $apiCmd = Write-DotNetLauncher -Name "api" -Project $apiProject -Url $ApiUrl
    $apiPid = Start-Detached $apiCmd
    Write-Info "API PID $apiPid"
    Wait-Site -Name "api" -Port $ApiPort -Url $ApiUrl -TimeoutSec 180
}

if ($needVite) {
    Write-Info "Iniciando PWA en $ViteUrl ..."
    $viteCmd = Write-ViteLauncher -NpmCmd $script:NpmCmd
    $vitePid = Start-Detached $viteCmd
    Write-Info "PWA PID $vitePid"
    Wait-Site -Name "vite" -Port $vitePort -Url $ViteUrl -TimeoutSec 90
}

foreach ($check in @(
    @{ Name = "mock"; Port = $MockPort; Url = $MockUrl },
    @{ Name = "api"; Port = $ApiPort; Url = $ApiUrl },
    @{ Name = "vite"; Port = $vitePort; Url = $ViteUrl }
)) {
    if (-not (Test-TcpOpen $check.Port)) {
        Write-Info "El sitio no quedo escuchando: $($check.Url)"
        Show-LogTail (Join-Path $LogDir "$($check.Name).log")
        exit 1
    }
}

Show-Urls $vitePort
exit 0

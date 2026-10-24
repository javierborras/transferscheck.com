$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$env:DOTNET_ROOT = Split-Path $dotnet
$env:PATH = "$(Split-Path $dotnet);$env:PATH"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
New-Item -ItemType Directory -Force -Path (Join-Path $env:LOCALAPPDATA "TransfersCheck\e2e-keys") | Out-Null

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($sqlcmd) {
    & sqlcmd -S "localhost\SQLEXPRESS" -E -b -Q "IF DB_ID(N'TransfersCheckE2E') IS NOT NULL BEGIN ALTER DATABASE TransfersCheckE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE TransfersCheckE2E; END"
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5099"
$mock = Start-Process -FilePath $dotnet -ArgumentList @(
    "run", "--no-launch-profile", "--project", (Join-Path $root "tools\TransfersCheck.MockServer\TransfersCheck.MockServer.csproj")
) -PassThru -WindowStyle Hidden

try {
    $ready = $false
    for ($i = 0; $i -lt 80; $i++) {
        try {
            Invoke-WebRequest -Uri "http://localhost:5099/mock/pushes" -UseBasicParsing | Out-Null
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) { throw "The mock did not listen on port 5099." }

    $env:ASPNETCORE_URLS = "http://localhost:5080"
    $env:ConnectionStrings__TransfersCheck = "Server=localhost\SQLEXPRESS;Database=TransfersCheckE2E;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True"
    $env:Session__SigningKey = "e2e-signing-key-with-32-bytes-min!"
    $env:MercadoPago__ClientId = "mock-client"
    $env:MercadoPago__ClientSecret = "mock-secret"
    $env:MercadoPago__AuthorizationUrl = "http://localhost:5099/authorization"
    $env:MercadoPago__TokenUrl = "http://localhost:5099/oauth/token"
    $env:MercadoPago__ApiBaseUrl = "http://localhost:5099"
    $env:MercadoPago__RedirectUri = "http://localhost:5080/api/connections/mercadopago/callback"
    $env:DataProtection__KeyPath = Join-Path $env:LOCALAPPDATA "TransfersCheck\e2e-keys"
    $env:Auth0__Authority = "http://localhost:5099/auth0"
    $env:Auth0__ClientId = "mock-auth0-client"
    $env:Auth0__ClientSecret = "mock-auth0-secret"
    $env:Auth0__RedirectUri = "http://localhost:5080/api/auth0/callback"
    $env:Checkout__AccessToken = "mock-checkout-token"
    $env:Checkout__WebhookSecret = "test-webhook-secret"
    $env:Checkout__NotificationUrl = "http://localhost:5080/api/billing/webhook"
    $env:Checkout__ReturnUrl = "http://localhost:5183/"
    $env:Firebase__MessagingUrl = "http://localhost:5099/v1/projects/mock/messages:send"
    $env:Firebase__ProjectId = "mock"
    $env:Ui__Origin = "http://localhost:5183"
    $env:Cors__UiOrigin = "http://localhost:5183"
    & $dotnet run --no-launch-profile --project (Join-Path $root "src\TransfersCheck.Api\TransfersCheck.Api.csproj")
}
finally {
    if ($mock -and -not $mock.HasExited) {
        Stop-Process -Id $mock.Id -Force
    }
}

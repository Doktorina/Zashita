$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Zashita.Web.csproj"
$url = "http://localhost:5298"
$port = 5298

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host "  $text" -ForegroundColor Cyan
}

function Write-Ok([string]$text) {
    Write-Host "  OK  $text" -ForegroundColor Green
}

function Write-Tip([string]$text) {
    Write-Host "  $text" -ForegroundColor DarkGray
}

Clear-Host
Write-Host ""
Write-Host "  ================================" -ForegroundColor DarkCyan
Write-Host "   Zashita — демо (тестовые данные)" -ForegroundColor White
Write-Host "  ================================" -ForegroundColor DarkCyan
Write-Host ""

# Освободить порт, если прошлый запуск не закрыли
Get-NetTCPConnection -LocalPort $port, 5188, 7198 -ErrorAction SilentlyContinue |
    ForEach-Object {
        Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue
    }

Write-Step "Сборка проекта (это может занять минуту)..."
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
dotnet build $project -c Debug -v q -nologo -p:NuGetAudit=false | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  Сборка не удалась. Если окно красное — пришлите текст ошибки." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Ok "Сборка готова"

Write-Step "Запуск сайта..."
Write-Tip "Окно не закрывайте — пока оно открыто, сайт работает."
Write-Host ""
Write-Host "  Адрес:  $url" -ForegroundColor Yellow
Write-Host "  Логин:  admin" -ForegroundColor Yellow
Write-Host "  Пароль: 123" -ForegroundColor Yellow
Write-Host ""

# Открыть браузер, когда сайт ответит (не сразу — сначала поднимается сервер)
$openerArgs = @(
    "-NoProfile",
    "-WindowStyle", "Hidden",
    "-Command",
    @"
`$url = '$url'
for (`$i = 0; `$i -lt 90; `$i++) {
    try {
        `$r = Invoke-WebRequest -Uri `$url -UseBasicParsing -TimeoutSec 2
        if (`$r.StatusCode -ge 200) {
            Start-Process `$url
            exit 0
        }
    } catch {}
    Start-Sleep -Seconds 1
}
"@
)
Start-Process -FilePath "powershell.exe" -ArgumentList $openerArgs -WindowStyle Hidden | Out-Null

Write-Tip "Браузер откроется сам, как только сайт будет готов..."
Write-Host ""

dotnet run --project $project --urls $url --no-build

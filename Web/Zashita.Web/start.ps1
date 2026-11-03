$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Zashita.Web.csproj"
$url = "http://localhost:5298"
$port = 5298

Clear-Host
Write-Host ""
Write-Host "  ================================" -ForegroundColor DarkCyan
Write-Host "   Zashita demo (test data)" -ForegroundColor White
Write-Host "  ================================" -ForegroundColor DarkCyan
Write-Host ""

Get-NetTCPConnection -LocalPort $port,5188,7198 -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

Write-Host "  Building... (about 1 minute)" -ForegroundColor Cyan
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
dotnet build $project -c Debug -v q -nologo -p:NuGetAudit=false | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  Build FAILED. Copy the red text and send it." -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "  OK  Build done" -ForegroundColor Green
Write-Host ""
Write-Host "  Starting site..." -ForegroundColor Cyan
Write-Host "  Keep this window open while you use the site." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  URL:      $url" -ForegroundColor Yellow
Write-Host "  Login:    admin" -ForegroundColor Yellow
Write-Host "  Password: 123" -ForegroundColor Yellow
Write-Host ""

$helper = Join-Path $PSScriptRoot "open-browser.ps1"
Start-Process -FilePath "powershell.exe" -ArgumentList @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $helper,
    "-Url", $url
) -WindowStyle Hidden | Out-Null

Write-Host "  Browser will open when ready..." -ForegroundColor DarkGray
Write-Host ""

dotnet run --project $project --urls $url --no-build

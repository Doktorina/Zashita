$project = Join-Path $PSScriptRoot "Zashita.Web.csproj"
Get-NetTCPConnection -LocalPort 5298,5188,7198 -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

Write-Host "Сборка..."
dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Сайт: http://localhost:5298"
Write-Host "Логин: admin / 123"
Write-Host "Внизу страницы: UI: v2026.07.08-1 (PPE changes/writeoff preview print)"
Write-Host ""

dotnet run --project $project --urls "http://localhost:5298"

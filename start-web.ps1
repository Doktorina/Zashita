$scriptDir = $PSScriptRoot
$start = Join-Path $scriptDir "Web\Zashita.Web\start.ps1"
if (-not (Test-Path $start)) {
    Write-Error "Не найден Web\Zashita.Web\start.ps1"
    exit 1
}
& $start

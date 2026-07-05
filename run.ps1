param($Port = 52314)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $env:TEMP "opencode\blazor-publish"

Write-Host "Building Web project..."
Set-Location -LiteralPath (Join-Path $root "NMU.Platform.Web")
dotnet build -c Release --no-restore 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!"; exit 1 }

Write-Host "Publishing..."
Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -o $publishDir --no-restore 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed!"; exit 1 }

Write-Host "Starting server on port $Port..."
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
Set-Location -LiteralPath (Join-Path $root "Server")
dotnet run -c Release --no-launch-profile

Write-Host "Server stopped."

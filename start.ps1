# Start both CORS proxy and Blazor app
Write-Host "Starting CORS proxy on port 52315..."
$proxyJob = Start-Job -ScriptBlock {
    param($ScriptPath)
    & powershell -File $ScriptPath
} -ArgumentList "C:\Users\BooDy\Desktop\Blazor\proxy.ps1"

Start-Sleep -Seconds 3

Write-Host "Starting Blazor app on port 52314..."
Set-Location -LiteralPath "C:\Users\BooDy\Desktop\Blazor\NMU.Platform.Web"
dotnet run --urls http://0.0.0.0:52314

# Builds the release: runs the tests, then puts the single-file program and its SHA-256 into dist\.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build.ps1 [-Repository owner/name]
# -Repository enables the in-app update check; the GitHub build passes it, local builds usually do not.
param([string]$Repository = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

& $dotnet test (Join-Path $root 'BatteryStrip.sln') -c Release -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

$out = Join-Path $root 'src\BatteryStrip\bin\Release\net48'
& $dotnet build (Join-Path $root 'src\BatteryStrip\BatteryStrip.csproj') -c Release -v q "-p:GitHubRepository=$Repository"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$version = (Get-Item (Join-Path $out 'BatteryStrip.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$name = "BatteryStrip-$version.exe"
$target = Join-Path $dist $name
Copy-Item (Join-Path $out 'BatteryStrip.exe') $target -Force
$hash = (Get-FileHash $target -Algorithm SHA256).Hash
"$hash  $name" | Set-Content (Join-Path $dist "BatteryStrip-$version.sha256.txt") -Encoding ascii
Write-Host "Built $target"
Write-Host "SHA-256 $hash"

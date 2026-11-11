# Builds the release and wraps it into dist\EdgeDashboard-Setup-<version>.exe.
# Needs the .NET SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup --source winget).
# Usage:  .\installer\build.ps1 -Version 1.0.1
param([string]$Version = '1.0.0')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed.' }

Remove-Item "$root\publish", "$root\dist" -Recurse -Force -ErrorAction SilentlyContinue
# A plain build, not "dotnet publish": publish leaves out the app's compiled XAML (.xbf) and
# resource index (.pri) for unpackaged WinUI apps, and the result crashes on start.
# The build is already self-contained, so its output folder is the complete app.
dotnet build "$root\EdgeDashboard.csproj" -c Release -o "$root\publish" -v q "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

& $iscc /Qp "/DAppVersion=$Version" "$PSScriptRoot\EdgeDashboard.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Get-ChildItem "$root\dist\*.exe" | ForEach-Object { "{0}  ({1:N0} MB)" -f $_.FullName, ($_.Length / 1MB) }

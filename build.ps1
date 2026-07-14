# VolumeMixer release build.
# Compiles VolumeMixer.cs into a single self-contained .exe using the .NET
# Framework 4.x C# compiler that ships with Windows. No SDK install required.
#
# Output: VolumeMixer.exe (target directory: this script's folder)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src  = Join-Path $root 'VolumeMixer.cs'
$ico  = Join-Path $root 'app.ico'
$out  = Join-Path $root 'VolumeMixer.exe'

# Use the .NET Framework 4.x csc.exe — every modern Windows install has it.
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found at: $csc" }
if (-not (Test-Path $src)) { throw "Source not found: $src" }

# Stop any running instance so we can overwrite the exe.
Get-Process -Name VolumeMixer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 200

$refs = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll'
) | ForEach-Object { "/reference:$_" }

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/debug-',
    '/warn:4',
    "/out:$out"
) + $refs

if (Test-Path $ico) { $args += "/win32icon:$ico" }

$args += $src

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$size = (Get-Item $out).Length
$hash = (Get-FileHash -Path $out -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Built: $out ($([math]::Round($size / 1KB, 1)) KB)"
Write-Output "SHA256: $hash"

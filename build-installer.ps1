# Build the per-user Windows installer executable.
# Output: VolumeMixerSetup.exe, containing the current VolumeMixer.exe payload.

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$app = Join-Path $root 'VolumeMixer.exe'
$source = Join-Path $root 'Installer.cs'
$icon = Join-Path $root 'app.ico'
$output = Join-Path $root 'VolumeMixerSetup.exe'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& (Join-Path $root 'build.ps1')

if (-not (Test-Path -LiteralPath $csc)) { throw "csc.exe not found at: $csc" }
if (-not (Test-Path -LiteralPath $source)) { throw "Source not found: $source" }

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/debug-',
    '/warn:4',
    "/out:$output",
    '/reference:System.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    "/resource:$app,VolumeMixer.Payload.exe",
    $source
)
if (Test-Path -LiteralPath $icon) { $args += "/win32icon:$icon" }

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE" }

$size = (Get-Item -LiteralPath $output).Length
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Built: $output ($([math]::Round($size / 1KB, 1)) KB)"
Write-Output "SHA256: $hash"

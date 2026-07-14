param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use semantic version format, for example 1.0.0."
}

$root = [System.IO.Path]::GetFullPath($PSScriptRoot)
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'release'))
$stage = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot "VolumeMixer-$Version"))
$zip = Join-Path $releaseRoot "VolumeMixer-$Version-win-x64.zip"

$rootPrefix = $root.TrimEnd('\') + '\'
if (-not $releaseRoot.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $stage.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved release paths are outside the project directory.'
}

& (Join-Path $root 'build.ps1')

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

New-Item -ItemType Directory -Path $stage -Force | Out-Null
$packageFiles = @('VolumeMixer.exe', 'install.ps1', 'README.md', 'CHANGELOG.md')
foreach ($file in $packageFiles) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}

$exeHash = (Get-FileHash -LiteralPath (Join-Path $stage 'VolumeMixer.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII -Value "$exeHash  VolumeMixer.exe"

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Output "Release: $zip"
Write-Output "SHA256: $zipHash"

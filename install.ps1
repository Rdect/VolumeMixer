# VolumeMixer installer.
# Copies VolumeMixer.exe to %LOCALAPPDATA%\VolumeMixer\, asks whether to enable
# auto-start at Windows login (creates a per-user Run entry), and
# launches the app. No admin rights required.
#
# Usage:
#   .\install.ps1                 # interactive — prompts for auto-start
#   .\install.ps1 -AutoStart      # install + auto-start (no prompt)
#   .\install.ps1 -NoAutoStart    # install without auto-start (no prompt)
#   .\install.ps1 -Uninstall      # stop process, remove auto-start, delete files

param(
    [switch]$AutoStart,
    [switch]$NoAutoStart,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

if ($AutoStart -and $NoAutoStart) {
    throw 'Use either -AutoStart or -NoAutoStart, not both.'
}

$installDir   = Join-Path $env:LOCALAPPDATA 'VolumeMixer'
$installedExe = Join-Path $installDir 'VolumeMixer.exe'
$srcExe       = Join-Path $PSScriptRoot 'VolumeMixer.exe'

# Keep the old shortcut path only to clean up entries from earlier versions.
$startupDir   = [Environment]::GetFolderPath('Startup')
$startupLnk   = Join-Path $startupDir 'VolumeMixer.lnk'

# Per-user auto-start registry entry.
$runKey         = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$startupApprovedRunKey = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$runValueName   = 'VolumeMixer'

function Stop-Running {
    Get-Process -Name VolumeMixer -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Output "Stopping running instance (PID $($_.Id))"
        Stop-Process -Id $_.Id -Force
    }
    Start-Sleep -Milliseconds 300
}

function Remove-AutoStartRunKey {
    if (Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $runValueName
    }
}

function Set-AutoStart([bool]$enable) {
    if ($enable) {
        if (-not (Test-Path $runKey)) {
            New-Item -Path $runKey -Force | Out-Null
        }
        $command = '"' + $installedExe + '" --startup'
        Set-ItemProperty -Path $runKey -Name $runValueName -Value $command -Type String
        if (-not (Test-Path $startupApprovedRunKey)) {
            New-Item -Path $startupApprovedRunKey -Force | Out-Null
        }
        New-ItemProperty -Path $startupApprovedRunKey -Name $runValueName -PropertyType Binary -Value ([byte[]](2, 0, 0, 0)) -Force | Out-Null

        if (Test-Path $startupLnk) {
            Remove-Item $startupLnk -Force
        }
        Write-Output "Auto-start enabled (Run: $command)"
    } else {
        if (Test-Path $startupLnk) {
            Remove-Item $startupLnk -Force
            Write-Output "Auto-start disabled (removed $startupLnk)"
        }
    }
    if (-not $enable) {
        Remove-AutoStartRunKey
        Remove-ItemProperty -Path $startupApprovedRunKey -Name $runValueName -ErrorAction SilentlyContinue
    }
}

if ($Uninstall) {
    Stop-Running
    Set-AutoStart $false
    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force
        Write-Output "Removed: $installDir"
    }
    Write-Output 'Uninstalled.'
    return
}

if (-not (Test-Path $srcExe)) {
    throw "VolumeMixer.exe not found next to install.ps1. Run .\build.ps1 first."
}

Stop-Running

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir | Out-Null
}
Copy-Item $srcExe $installedExe -Force
Write-Output "Installed: $installedExe"

# Decide auto-start: explicit param wins, otherwise prompt.
$enableAutoStart = $null
if ($AutoStart)         { $enableAutoStart = $true  }
elseif ($NoAutoStart)   { $enableAutoStart = $false }
else {
    $title    = 'VolumeMixer'
    $message  = 'Enable auto-start at Windows login?'
    $choices  = @(
        (New-Object System.Management.Automation.Host.ChoiceDescription '&Yes', 'Run VolumeMixer when you sign in.'),
        (New-Object System.Management.Automation.Host.ChoiceDescription '&No',  'Do not auto-start.')
    )
    try {
        $decision = $Host.UI.PromptForChoice($title, $message, $choices, 0)
        $enableAutoStart = ($decision -eq 0)
    } catch {
        # Non-interactive host (e.g. piped). Default to enabled.
        $enableAutoStart = $true
    }
}

Set-AutoStart $enableAutoStart

Start-Process $installedExe
Write-Output 'Launched. Look for the speaker icon in the system tray.'

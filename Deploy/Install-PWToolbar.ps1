<#
=====================================================================
 PW-User - INSTALL the CATIA toolbar on a customer PC (SILENT)
---------------------------------------------------------------------
 This is the "single click" part: your installer (or the app on first
 run) calls this script, and the customer gets the 4 PW icons in CATIA
 without ever opening Tools > Customize.

 What it does:
   1. detects the CATIA release and the user's CATSettings folder
   2. copies the macro library to %LOCALAPPDATA%\Estichara\PWUser\Macros
   3. backs up the user's current CATSettings (timestamped, restorable)
   4. drops in the prepared Macros/CATFrmLayout settings captured for
      that release by Capture-PWToolbar.ps1
   5. writes an install log

 Requirements / limits (be honest with your customers):
   * CATIA MUST be closed while this runs (it rewrites its settings on
     exit, so a running CATIA would overwrite our files).
   * The prepared settings exist per release: Deploy\Settings\B27, ...
     If the release was never captured, the script stops cleanly and
     the app falls back to its own floating toolbar - no error, no
     broken CATIA.
   * The user's own toolbar customizations are REPLACED (they are
     saved in the backup folder and -Restore puts them back).

 Usage:
   powershell -ExecutionPolicy Bypass -File Install-PWToolbar.ps1
   powershell ... -File Install-PWToolbar.ps1 -Restore
=====================================================================
#>
[CmdletBinding()]
param(
    [switch]$Restore,
    [string]$Release,
    [string]$SettingsPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$ProductRoot = Join-Path $env:LOCALAPPDATA 'Estichara\PWUser'
$MacroTarget = Join-Path $ProductRoot 'Macros'
$BackupRoot  = Join-Path $ProductRoot 'CATSettingsBackup'
$LogFile     = Join-Path $ProductRoot 'toolbar-install.log'

function Write-Log {
    param([string]$Message, [string]$Color = 'Gray')
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host " $Message" -ForegroundColor $Color
    New-Item -ItemType Directory -Force -Path $ProductRoot | Out-Null
    Add-Content -Path $LogFile -Value $line
}

function Get-CatiaSettingsPath {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }
    if ($env:CATUserSettingPath) { return $env:CATUserSettingPath }
    $default = Join-Path $env:APPDATA 'DassaultSystemes\CATSettings'
    if (Test-Path $default) { return $default }
    return $null
}

function Get-CatiaRelease {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        $dir = Join-Path $root 'Dassault Systemes'
        if (Test-Path $dir) {
            $found = Get-ChildItem $dir -Directory -Filter 'B*' -ErrorAction SilentlyContinue |
                     Sort-Object Name -Descending | Select-Object -First 1
            if ($found) { return $found.Name }
        }
    }
    return $null
}

# ---------------------------------------------------------------- restore
if ($Restore) {
    if (-not (Test-Path $BackupRoot)) { Write-Log "Nothing to restore." 'Yellow'; exit 0 }
    $settings = Get-CatiaSettingsPath -Explicit $SettingsPath
    $last = Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $last) { Write-Log "No backup found." 'Yellow'; exit 0 }
    Get-ChildItem $last.FullName -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $settings $_.Name) -Force
        Write-Log ("restored " + $_.Name) 'Green'
    }
    Write-Log "CATIA settings restored from $($last.Name)." 'Cyan'
    exit 0
}

# ---------------------------------------------------------------- checks
Write-Host ""
Write-Host " PW-User - CATIA toolbar installation" -ForegroundColor Cyan
Write-Host " ------------------------------------"

if (Get-Process -Name CNEXT -ErrorAction SilentlyContinue) {
    if (-not $Force) {
        Write-Log "CATIA is running: close CATIA and run the installation again (or use -Force)." 'Yellow'
        exit 2      # 2 = retry later, the app shows its own toolbar meanwhile
    }
    Write-Log "CATIA is running and -Force was given: settings may be overwritten by CATIA on exit." 'Yellow'
}

$release = Get-CatiaRelease -Explicit $Release
if (-not $release) { Write-Log "No CATIA V5 installation found - nothing to do." 'Yellow'; exit 3 }

$settings = Get-CatiaSettingsPath -Explicit $SettingsPath
if (-not $settings) { Write-Log "CATSettings folder not found - nothing to do." 'Yellow'; exit 3 }

$prepared = Join-Path $PSScriptRoot ("Settings\" + $release)
Write-Log "release      : $release"
Write-Log "CATSettings  : $settings"
Write-Log "prepared set : $prepared"

# ------------------------------------------------- 1. the macro library
New-Item -ItemType Directory -Force -Path $MacroTarget | Out-Null
$macroSource = Join-Path $PSScriptRoot 'Macros'
if (Test-Path $macroSource) {
    Copy-Item (Join-Path $macroSource '*') $MacroTarget -Force -Recurse
    Write-Log "macro library installed in $MacroTarget" 'Green'
} else {
    Write-Log "no Deploy\Macros folder - skipping the macro copy" 'DarkGray'
}

# ------------------------------------------------- 2. prepared settings
if (-not (Test-Path $prepared)) {
    Write-Log "This CATIA release ($release) has no captured toolbar yet." 'Yellow'
    Write-Log "The application will use its own floating toolbar instead." 'Yellow'
    exit 4        # 4 = release not supported for the in-CATIA toolbar
}

$stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $BackupRoot $stamp
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$installed = 0
Get-ChildItem $prepared -File -Filter '*.CATSettings' | ForEach-Object {
    $destination = Join-Path $settings $_.Name
    if (Test-Path $destination) { Copy-Item $destination $backup -Force }
    Copy-Item $_.FullName $destination -Force
    Write-Log ("installed " + $_.Name) 'Green'
    $installed++
}

if ($installed -eq 0) {
    Write-Log "The prepared folder contains no .CATSettings file." 'Yellow'
    exit 4
}

Write-Log "Backup of the previous settings: $backup" 'DarkGray'
Write-Log "Done - the 4 PW-User icons appear the next time CATIA starts." 'Cyan'
Write-Host ""
exit 0

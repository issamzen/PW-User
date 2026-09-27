<#
=====================================================================
 PW-User - CAPTURE the CATIA toolbar configuration (YOU run this ONCE
 per CATIA release, on your own reference machine)
---------------------------------------------------------------------
 Workflow:
   1. On a clean CATIA of the release you want to support, declare the
      macro library and drag the 4 PW commands onto a toolbar with
      their icons (the manual steps from ToolbarTest\README).
   2. CLOSE CATIA  (settings are written to disk on exit).
   3. Run this script. It copies the CATSettings files that describe
      the macro library + the toolbar into
         Deploy\Settings\<release>\
   4. Commit that folder. Install-PWToolbar.ps1 then reproduces the
      exact same toolbar on every customer PC of that release, with
      no manual step at all.

 Usage:
   powershell -ExecutionPolicy Bypass -File Capture-PWToolbar.ps1
   powershell ... -File Capture-PWToolbar.ps1 -Release B27
=====================================================================
#>
[CmdletBinding()]
param(
    [string]$Release,
    [string]$SettingsPath
)

$ErrorActionPreference = 'Stop'

# --- the CATSettings files that carry macro libraries + toolbars -----
$FILES = @(
    'Macros.CATSettings',            # declared macro libraries
    'CATFrmLayout.CATSettings',      # toolbars: content, icons, position
    'CATFrmWindowLayout.CATSettings',
    'CATCommandBinding.CATSettings'  # keyboard shortcuts (optional)
)

function Get-CatiaSettingsPath {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }
    if ($env:CATUserSettingPath) { return $env:CATUserSettingPath }
    $default = Join-Path $env:APPDATA 'DassaultSystemes\CATSettings'
    if (Test-Path $default) { return $default }
    throw "CATSettings folder not found. Pass -SettingsPath ""C:\path\to\CATSettings""."
}

function Get-CatiaRelease {
    param([string]$Explicit)
    if ($Explicit) { return $Explicit }
    # CATIA V5 installs as ...\Dassault Systemes\B2x\win_b64\code\bin\CNEXT.exe
    $catia = Get-Process -Name CNEXT -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($catia) {
        if ($catia.Path -match '\\(B\d{2})\\') { return $Matches[1] }
    }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        $dir = Join-Path $root 'Dassault Systemes'
        if (Test-Path $dir) {
            $found = Get-ChildItem $dir -Directory -Filter 'B*' -ErrorAction SilentlyContinue |
                     Sort-Object Name -Descending | Select-Object -First 1
            if ($found) { return $found.Name }
        }
    }
    throw "CATIA release could not be detected. Pass -Release B27 (or your release code)."
}

$settings = Get-CatiaSettingsPath -Explicit $SettingsPath
$rel      = Get-CatiaRelease      -Explicit $Release
$target   = Join-Path $PSScriptRoot ("Settings\" + $rel)

if (Get-Process -Name CNEXT -ErrorAction SilentlyContinue) {
    Write-Warning "CATIA is still running - close it first, otherwise the settings on disk are stale."
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

Write-Host ""
Write-Host " PW-User - capture toolbar settings" -ForegroundColor Cyan
Write-Host " release       : $rel"
Write-Host " CATSettings   : $settings"
Write-Host " captured into : $target"
Write-Host ""

$copied = 0
foreach ($file in $FILES) {
    $source = Join-Path $settings $file
    if (Test-Path $source) {
        Copy-Item $source $target -Force
        Write-Host ("  [captured] " + $file) -ForegroundColor Green
        $copied++
    } else {
        Write-Host ("  [missing ] " + $file + " (normal if unused on this release)") -ForegroundColor DarkGray
    }
}

if ($copied -eq 0) { throw "Nothing captured - check the CATSettings path." }

@"
Captured on : $(Get-Date -Format 'yyyy-MM-dd HH:mm')
Machine     : $env:COMPUTERNAME
Release     : $rel
Source      : $settings
Files       : $copied
"@ | Set-Content (Join-Path $target 'capture-info.txt') -Encoding UTF8

Write-Host ""
Write-Host " Done. Commit Deploy\Settings\$rel and ship it with the installer." -ForegroundColor Cyan
Write-Host ""

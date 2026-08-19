<#
.SYNOPSIS
    Stops WorkTimeTray, removes the autostart entry and the installed program folder.
    The csv log files and the settings folder are left untouched.
#>
[CmdletBinding()]
param([switch]$AlsoRemoveSettings)

$ErrorActionPreference = 'Stop'

$installTo = Join-Path $env:LOCALAPPDATA 'Programs\WorkTimeTray'
$dataDir   = Join-Path $env:LOCALAPPDATA 'WorkTimeTray'
$run       = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

$exe = Join-Path $installTo 'WorkTimeTray.exe'
if (Test-Path $exe) {
    # --quit closes the session that is open right now before the app exits.
    Start-Process -FilePath $exe -ArgumentList '--quit' -Wait
    Start-Sleep -Milliseconds 1200
}
Get-Process WorkTimeTray -ErrorAction SilentlyContinue | Stop-Process -Force

if (Get-ItemProperty -Path $run -Name 'WorkTimeTray' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $run -Name 'WorkTimeTray'
    Write-Host 'Removed the autostart registry entry.'
}

$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'WorkTimeTray.lnk'
if (Test-Path $lnk) {
    Remove-Item $lnk -Force
    Write-Host 'Removed the Startup folder shortcut.'
}

if (Test-Path $installTo) {
    Remove-Item -Recurse -Force $installTo
    Write-Host "Removed $installTo"
}

if ($AlsoRemoveSettings -and (Test-Path $dataDir)) {
    Remove-Item -Recurse -Force $dataDir
    Write-Host "Removed $dataDir"
} else {
    Write-Host "Kept $dataDir (settings and heartbeat). Your csv log files were not touched."
}

Write-Host 'Done.'

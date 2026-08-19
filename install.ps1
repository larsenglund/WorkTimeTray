<#
.SYNOPSIS
    Builds WorkTimeTray, installs it to %LOCALAPPDATA%\Programs\WorkTimeTray,
    points the log at this folder and starts it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -LogDirectory D:\timelog -NoAutostart
#>
[CmdletBinding()]
param(
    # Where the worktime-YYYY.csv files are written. Defaults to the folder holding this script.
    [string]$LogDirectory = '',
    # Do not register the app to start with Windows.
    [switch]$NoAutostart,
    # Do not launch the app when the install finishes.
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not populated yet while param defaults are evaluated, so resolve it here.
if ([string]::IsNullOrWhiteSpace($LogDirectory)) { $LogDirectory = $PSScriptRoot }

$project   = Join-Path $PSScriptRoot 'src\WorkTimeTray\WorkTimeTray.csproj'
$installTo = Join-Path $env:LOCALAPPDATA 'Programs\WorkTimeTray'
$dataDir   = Join-Path $env:LOCALAPPDATA 'WorkTimeTray'
$exe       = Join-Path $installTo 'WorkTimeTray.exe'

# A stale MSBuildSDKsPath in the machine environment breaks SDK resolution.
$env:MSBuildSDKsPath = $null

Write-Host 'Stopping any running instance...'
if (Test-Path $exe) {
    # --quit asks the running instance to close its open session properly before exiting.
    Start-Process -FilePath $exe -ArgumentList '--quit' -Wait
    Start-Sleep -Milliseconds 1200
}
Get-Process WorkTimeTray -ErrorAction SilentlyContinue | ForEach-Object {
    $_ | Stop-Process -Force
}
Start-Sleep -Milliseconds 500

Write-Host "Publishing to $installTo ..."
dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $installTo --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $dataDir, $LogDirectory | Out-Null

$settingsPath = Join-Path $dataDir 'settings.json'
$settings = [ordered]@{
    LogDirectory        = (Resolve-Path $LogDirectory).Path
    MinSessionSeconds   = 30
    IdleTimeoutMinutes  = 30
    ExpectedHoursPerDay = 5.6
    WorkDays            = @('Monday','Tuesday','Wednesday','Thursday','Friday')
    WeekStartsMonday    = $true
    ShowWindowOnStartup = $false
}
$settings | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding UTF8
Write-Host "Settings: $settingsPath  (log -> $($settings.LogDirectory))"

# The app registers itself: HKCU Run key plus a Startup folder shortcut, both with --autostart.
if ($NoAutostart) {
    Start-Process -FilePath $exe -ArgumentList '--autostart-off' -Wait
    Write-Host 'Autostart left disabled.'
} else {
    Start-Process -FilePath $exe -ArgumentList '--autostart-on' -Wait
    Start-Sleep -Milliseconds 400
    $lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'WorkTimeTray.lnk'
    $reg = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WorkTimeTray -ErrorAction SilentlyContinue).WorkTimeTray
    Write-Host "Autostart: Run key $(if ($reg) {'ok'} else {'MISSING'}), Startup shortcut $(if (Test-Path $lnk) {'ok'} else {'MISSING'})"
}

if (-not $NoStart) {
    Start-Process -FilePath $exe
    Write-Host 'Started. Look for the clock icon in the tray.'
}

Write-Host 'Done.'

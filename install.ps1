<#
  Installs FPS Overlay for the current user.

  Run from the unzipped FpsOverlay-win-x64 folder:
      Right-click install.ps1 > Run with PowerShell
  or
      powershell -ExecutionPolicy Bypass -File .\install.ps1 [-StartWithWindows] [-Uninstall]

  - Copies the app to %LOCALAPPDATA%\Programs\FpsOverlay
  - Adds a Start Menu shortcut
  - -StartWithWindows: registers a logon scheduled task that runs it elevated,
    so there's no UAC prompt at every sign-in (asks for admin once, now)
#>
param(
    [switch]$StartWithWindows,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$appName   = 'FPS Overlay'
$taskName  = 'FpsOverlay'
$installTo = Join-Path $env:LOCALAPPDATA 'Programs\FpsOverlay'
$shortcut  = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
$exe       = Join-Path $installTo 'FpsOverlay.exe'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Registering an elevated task (or removing one) needs admin: relaunch elevated with the same switches.
$needsAdmin = $StartWithWindows -or ($Uninstall -and (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue))
if ($needsAdmin -and -not (Test-Admin)) {
    $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($StartWithWindows) { $relaunch += '-StartWithWindows' }
    if ($Uninstall)        { $relaunch += '-Uninstall' }
    Start-Process powershell -Verb RunAs -ArgumentList $relaunch -WorkingDirectory $PSScriptRoot
    return
}

Get-Process FpsOverlay -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $installTo -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "$appName uninstalled. (Settings in $env:APPDATA\FpsOverlay were kept.)"
    return
}

$source = $PSScriptRoot
if (-not (Test-Path (Join-Path $source 'FpsOverlay.exe'))) {
    throw "FpsOverlay.exe not found next to install.ps1. Unzip the whole FpsOverlay-win-x64 download first."
}

New-Item -ItemType Directory -Force -Path $installTo | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $installTo -Recurse -Force
Get-ChildItem $installTo -Recurse | Unblock-File   # clear "downloaded from the internet" marks

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $installTo
$lnk.Description = 'Game FPS and CPU/GPU temperature overlay'
$lnk.Save()

if ($StartWithWindows) {
    $action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $installTo
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Will start automatically when you sign in."
}

Write-Host "$appName installed to $installTo and added to the Start Menu."
if ($StartWithWindows) { Start-ScheduledTask -TaskName $taskName } else { Start-Process $exe }
if (-not (Test-Admin)) { Read-Host 'Press Enter to close' | Out-Null }

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

$logFile = Join-Path $env:LOCALAPPDATA 'FpsOverlay\log.txt'

function Pause-IfInteractive {
    # "Run with PowerShell" closes the window as soon as the script ends; keep it open.
    try { Read-Host "`nPress Enter to close" | Out-Null } catch { }
}

try {
    Write-Host "Stopping any running copy..."
    Get-Process FpsOverlay -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500

    if ($Uninstall) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        Remove-Item $shortcut -ErrorAction SilentlyContinue
        Remove-Item $installTo -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "$appName uninstalled. (Settings in $env:APPDATA\FpsOverlay were kept.)" -ForegroundColor Green
        return
    }

    $source = $PSScriptRoot
    Write-Host "Installing from: $source"
    if (-not (Test-Path (Join-Path $source 'FpsOverlay.exe'))) {
        throw "FpsOverlay.exe isn't next to install.ps1. Extract the whole zip first (right-click it > Extract All), then run install.ps1 from the extracted folder."
    }

    Write-Host "Copying files to $installTo ..."
    New-Item -ItemType Directory -Force -Path $installTo | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $installTo -Recurse -Force
    Get-ChildItem $installTo -Recurse | Unblock-File   # clear "downloaded from the internet" marks

    Write-Host "Adding Start Menu shortcut..."
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($shortcut)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $installTo
    $lnk.Description = 'Game FPS and CPU/GPU temperature overlay'
    $lnk.Save()

    if ($StartWithWindows) {
        Write-Host "Registering start-at-sign-in task..."
        $action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $installTo
        $trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
        $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
        $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    }

    Write-Host "Starting $appName (accept the administrator prompt)..."
    if ($StartWithWindows) { Start-ScheduledTask -TaskName $taskName } else { Start-Process $exe }

    Start-Sleep -Seconds 5
    if (Get-Process FpsOverlay -ErrorAction SilentlyContinue) {
        Write-Host "`n$appName is installed and running." -ForegroundColor Green
        Write-Host "The overlay sits in the top-left corner of your main monitor."
        Write-Host "Its tray icon may be hidden under the ^ arrow next to the clock."
        Write-Host "Ctrl+Shift+F10 shows/hides it. Later you can start it from the Start Menu: '$appName'."
    } else {
        Write-Host "`n$appName was installed but is NOT running." -ForegroundColor Yellow
        if (Test-Path $logFile) {
            Write-Host "Last lines of $logFile :"
            Get-Content $logFile -Tail 15 | ForEach-Object { Write-Host "  $_" }
        } else {
            Write-Host "It never got far enough to write a log. If you declined the administrator prompt, run it again from the Start Menu."
        }
    }
}
catch {
    Write-Host "`nInstall failed: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Pause-IfInteractive
}

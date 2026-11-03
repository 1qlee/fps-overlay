<#
  Installs FPS Overlay for the current user.

  Easiest: double-click install.cmd in the extracted FpsOverlay-win-x64 folder.
  Or from a PowerShell window in that folder:
      powershell -ExecutionPolicy Bypass -File .\install.ps1 [-StartWithWindows] [-Uninstall]

  - Copies the app to %LOCALAPPDATA%\Programs\FpsOverlay
  - Adds a Start Menu shortcut
  - -StartWithWindows: registers a logon scheduled task that runs it elevated,
    so there's no UAC prompt at every sign-in (asks for admin once, now)
#>
param(
    [switch]$StartWithWindows,
    [switch]$Uninstall,
    [switch]$NoPause   # used by install.cmd, which pauses itself
)

$ErrorActionPreference = 'Stop'
$appName   = 'FPS Overlay'
$taskName  = 'FpsOverlay'
$installTo = Join-Path $env:LOCALAPPDATA 'Programs\FpsOverlay'
$shortcut  = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
$exe       = Join-Path $installTo 'FpsOverlay.exe'

function Test-Admin {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$logFile = Join-Path $env:LOCALAPPDATA 'FpsOverlay\log.txt'

function Pause-IfInteractive {
    # Keep the window open so the result can be read. install.cmd also pauses.
    if ($NoPause) { return }
    try { Read-Host "`nPress Enter to close" | Out-Null } catch { }
}

try {
    # Admin is needed to register/remove the elevated startup task, and to close a
    # running copy (the overlay itself runs elevated). Relaunch elevated if so.
    $running    = Get-Process FpsOverlay -ErrorAction SilentlyContinue
    $needsAdmin = $StartWithWindows -or $running -or
                  ($Uninstall -and (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue))
    if ($needsAdmin -and -not (Test-Admin)) {
        $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
        if ($StartWithWindows) { $relaunch += '-StartWithWindows' }
        if ($Uninstall)        { $relaunch += '-Uninstall' }
        Write-Host "Asking for administrator rights to continue (a new window will open)..."
        Start-Process powershell -Verb RunAs -ArgumentList $relaunch -WorkingDirectory $PSScriptRoot
        return
    }

    if ($running) {
        Write-Host "Closing the running overlay..."
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
        if (Get-Process FpsOverlay -ErrorAction SilentlyContinue) {
            throw "Couldn't close the running overlay. Exit it from its tray icon (right-click > Exit) and try again."
        }
    }

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

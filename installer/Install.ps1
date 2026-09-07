[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$productName = 'BlueBridge'
$sourceRoot = Join-Path $PSScriptRoot 'App'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\BlueBridge'
$executablePath = Join-Path $installRoot 'BlueBridge.exe'
$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BlueBridge'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'BlueBridge.lnk'
$backupRoot = Join-Path $env:TEMP ('BlueBridge-setup-backup-' + [Guid]::NewGuid().ToString('N'))

function New-Shortcut {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Target,
        [string]$Arguments = '',
        [string]$WorkingDirectory = '',
        [string]$Description = 'BlueBridge Bluetooth audio receiver',
        [string]$IconLocation = ''
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    if ($IconLocation) {
        $shortcut.IconLocation = $IconLocation
    }
    $shortcut.Save()
}

try {
    if (-not (Test-Path (Join-Path $sourceRoot 'BlueBridge.exe'))) {
        throw 'The App folder is incomplete. Extract the whole ZIP before running INSTALL.cmd.'
    }

    Write-Host '[1/5] Closing an older BlueBridge instance...' -ForegroundColor Cyan
    Get-Process -Name 'BlueBridge' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 350

    if (Test-Path $installRoot) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Move-Item -LiteralPath $installRoot -Destination (Join-Path $backupRoot 'BlueBridge') -Force
    }

    Write-Host '[2/5] Installing application files...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceRoot '*') -Destination $installRoot -Recurse -Force
    Get-ChildItem -LiteralPath $installRoot -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

    Write-Host '[3/5] Creating shortcuts...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
    New-Shortcut -Path $desktopShortcut -Target $executablePath -WorkingDirectory $installRoot -IconLocation ($executablePath + ',0')
    New-Shortcut -Path (Join-Path $startMenuFolder 'BlueBridge.lnk') -Target $executablePath -WorkingDirectory $installRoot -IconLocation ($executablePath + ',0')

    $uninstallScript = Join-Path $installRoot 'Uninstall.ps1'
    New-Shortcut `
        -Path (Join-Path $startMenuFolder 'Uninstall BlueBridge.lnk') `
        -Target "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Arguments ('-NoProfile -ExecutionPolicy Bypass -File "' + $uninstallScript + '"') `
        -WorkingDirectory $installRoot `
        -Description 'Uninstall BlueBridge' `
        -IconLocation ($executablePath + ',0')

    Write-Host '[4/5] Registering uninstall information...' -ForegroundColor Cyan
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueBridge'
    New-Item -Path $uninstallKey -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $productName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value '1.3.0' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'BlueBridge' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $executablePath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $installRoot -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $uninstallScript + '"') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null

    Write-Host '[5/5] Starting BlueBridge...' -ForegroundColor Cyan
    Start-Process -FilePath $executablePath -WorkingDirectory $installRoot

    if (Test-Path $backupRoot) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host 'Installation completed successfully.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ('Installation failed: ' + $_.Exception.Message) -ForegroundColor Red

    try {
        if (Test-Path $installRoot) {
            Remove-Item -LiteralPath $installRoot -Recurse -Force
        }
        $backupApp = Join-Path $backupRoot 'BlueBridge'
        if (Test-Path $backupApp) {
            Move-Item -LiteralPath $backupApp -Destination $installRoot -Force
        }
    }
    catch {
        Write-Host 'The previous installation backup could not be restored automatically.' -ForegroundColor Yellow
    }

    exit 1
}

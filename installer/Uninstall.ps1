[CmdletBinding()]
param(
    [switch]$RemoveSettings
)

$ErrorActionPreference = 'SilentlyContinue'
$installRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BlueBridge'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'BlueBridge.lnk'
$settingsRoot = Join-Path $env:LOCALAPPDATA 'BlueBridge'

Get-Process -Name 'BlueBridge' | Stop-Process -Force
Start-Sleep -Milliseconds 350

Remove-Item -LiteralPath $desktopShortcut -Force
Remove-Item -LiteralPath $startMenuFolder -Recurse -Force
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueBridge' -Recurse -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'BlueBridge' -Force

if ($RemoveSettings) {
    Remove-Item -LiteralPath $settingsRoot -Recurse -Force
}

$cleanupCommand = 'ping 127.0.0.1 -n 3 > nul & rmdir /s /q "' + $installRoot + '"'
Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList '/d', '/c', $cleanupCommand -WindowStyle Hidden

Add-Type -AssemblyName PresentationFramework
$completionMessage = if ($RemoveSettings) {
    'BlueBridge and its settings were removed.'
} else {
    'BlueBridge was removed. Your settings and logs were kept.'
}
[System.Windows.MessageBox]::Show(
    $completionMessage,
    'BlueBridge',
    [System.Windows.MessageBoxButton]::OK,
    [System.Windows.MessageBoxImage]::Information
) | Out-Null

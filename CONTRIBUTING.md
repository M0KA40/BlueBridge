# Contributing to BlueBridge

Thanks for helping improve BlueBridge.

## Before opening a pull request

1. Keep changes focused and explain the user-visible behavior.
2. Build with warnings treated as errors.
3. Test connect, disconnect, one-pass repair, output selection, and close-to-tray on Windows.
4. Do not add background reconnect or polling without prior discussion; predictable manual recovery is a core design choice.
5. Do not modify or repackage the bundled NirSoft files.

## Build check

```powershell
dotnet restore .\src\BlueBridge.csproj
dotnet build .\src\BlueBridge.csproj -c Release -warnaserror
```

For bugs, include the Windows version, Bluetooth adapter, phone model, selected audio endpoint, reproduction steps, and relevant lines from `%LOCALAPPDATA%\BlueBridge\BlueBridge.log`.

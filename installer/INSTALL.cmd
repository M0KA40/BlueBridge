@echo off
setlocal
title BlueBridge Setup
color 0B

echo.
echo   BlueBridge Setup
echo   ----------------
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "SETUP_EXIT=%ERRORLEVEL%"

echo.
if not "%SETUP_EXIT%"=="0" (
    echo Installation did not complete. Error code: %SETUP_EXIT%
    echo Check the message above, then try again.
) else (
    echo BlueBridge is ready.
)
echo.
pause
exit /b %SETUP_EXIT%

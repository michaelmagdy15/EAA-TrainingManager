@echo off
setlocal
cd /d "%~dp0"
echo =================================================================
echo   EAA Training Manager - One-Click Automated Release
echo =================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_release.ps1" %*

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Release completed successfully!
) else (
    echo.
    echo Release encountered an error (Exit Code: %ERRORLEVEL%).
)

echo.
pause

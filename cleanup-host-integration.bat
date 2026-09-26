@echo off
setlocal
echo ============================================================
echo   TriggerPoint Portable - Host Cleanup Utility
echo ============================================================
echo.
echo Removing TriggerPoint context menu entries from Windows Explorer...

reg delete "HKCU\Software\Classes\*\shell\TriggerPoint" /f >nul 2>&1
reg delete "HKCU\Software\Classes\AllFilesystemObjects\shell\TriggerPoint" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Directory\shell\TriggerPoint" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Directory\Background\shell\TriggerPoint" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Drive\shell\TriggerPoint" /f >nul 2>&1

echo Removing TriggerPoint Windows Startup registration (if present)...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "TriggerPoint" /f >nul 2>&1

echo.
echo [OK] All TriggerPoint host integrations have been completely removed.
echo      Your host machine registry is clean.
echo.
pause

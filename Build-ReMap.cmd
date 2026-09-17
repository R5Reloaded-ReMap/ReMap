@echo off
setlocal
cd /d "%~dp0"

where pwsh.exe >nul 2>&1
if errorlevel 1 goto windows_powershell

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Build-ReMap.ps1" -Interactive
set "result=%ERRORLEVEL%"
goto finished

:windows_powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Build-ReMap.ps1" -Interactive
set "result=%ERRORLEVEL%"

:finished
echo.
if not "%result%"=="0" echo The tool stopped with error %result%.
pause
exit /b %result%

@echo off
setlocal
where pwsh.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-GameScriptVariants.ps1" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-GameScriptVariants.ps1" %*
)
set "REMAP_EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%REMAP_EXIT_CODE%"=="0" echo Generation failed with exit code %REMAP_EXIT_CODE%.
if "%REMAP_EXIT_CODE%"=="0" echo Generation completed successfully.
echo Press any key to close this window.
pause >nul
exit /b %REMAP_EXIT_CODE%

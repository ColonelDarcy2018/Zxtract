@echo off
setlocal

set "ROOT=%~1"
if "%ROOT%"=="" set "ROOT=%CD%"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Expand-ArchiveTree.ps1" -Root "%ROOT%" -DeleteArchives
set "EXITCODE=%ERRORLEVEL%"

echo.
echo ExtractUtil archive tree workflow finished with exit code %EXITCODE%.
echo Root: %ROOT%
echo.
pause
exit /b %EXITCODE%

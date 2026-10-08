@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1"
set "INSTALL_EXIT=%ERRORLEVEL%"
if not "%INSTALL_EXIT%"=="0" pause
exit /b %INSTALL_EXIT%

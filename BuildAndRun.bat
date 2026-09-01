@echo off
setlocal EnableExtensions
cd /d "%~dp0"
call BuildRelease.bat
if errorlevel 1 exit /b 1
start "" "%CD%\Voxena\bin\Release\Voxena.exe"

@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PackageRelease.ps1"
exit /b %errorlevel%

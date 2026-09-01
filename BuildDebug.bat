@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ==============================================
echo Voxena 0.3.2 - Debug Build
echo ==============================================

set "MSBUILD="
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
  for /f "usebackq tokens=*" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2^>nul`) do if not defined MSBUILD set "MSBUILD=%%I"
)
if not defined MSBUILD for /f "delims=" %%I in ('where msbuild.exe 2^>nul') do if not defined MSBUILD set "MSBUILD=%%I"
if not defined MSBUILD (
  echo.
  echo ERROR: MSBuild was not found.
  echo Install Visual Studio 2022 Build Tools with the .NET desktop build tools workload.
  goto :fail
)

echo [1/2] Restoring NuGet packages...
"%MSBUILD%" "%CD%\Voxena.sln" /m /t:Restore /p:RestorePackagesConfig=false /v:minimal
if errorlevel 1 goto :fail

echo [2/2] Building Debug x64...
"%MSBUILD%" "%CD%\Voxena.sln" /m /t:Build /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal
if errorlevel 1 goto :fail

echo.
echo Debug output: %CD%\Voxena\bin\Debug
exit /b 0

:fail
echo.
echo DEBUG BUILD FAILED.
exit /b 1

@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ==============================================
echo Voxena 0.3.2 - Release Build
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

echo [2/2] Building Release x64...
"%MSBUILD%" "%CD%\Voxena.sln" /m /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /v:minimal
if errorlevel 1 goto :fail

if not exist "%CD%\Voxena\bin\Release\Voxena.exe" (
  echo ERROR: Voxena.exe was not created.
  goto :fail
)

echo.
echo ==============================================
echo BUILD COMPLETED
echo Output: %CD%\Voxena\bin\Release
echo You can publish the CONTENTS of that folder.
echo End users do not need Visual Studio.
echo ==============================================
exit /b 0

:fail
echo.
echo RELEASE BUILD FAILED.
exit /b 1

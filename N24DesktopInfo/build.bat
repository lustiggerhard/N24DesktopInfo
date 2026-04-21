:: @file     build.bat
:: @brief    Build-Script fuer N24 Desktop Info
:: @author   Gerhard Lustig (gerhard@lustig.at)
:: @version  1.1.0
:: @date     2026-04-21
:: @history
::   2026-04-21  v1.1.0  Fix: explizite -p: Overrides fuer beide Modi
::   2026-02-17  v1.0.0  Initiale Version
@echo off
setlocal enabledelayedexpansion
title N24 Desktop Info - Build

:: Check .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET 8 SDK nicht gefunden!
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo ============================================
echo  N24 Desktop Info - Build
echo ============================================
echo.
echo  [1] Self-Contained  ^(keine Runtime noetig, ~50-70 MB^)
echo  [2] Framework-Dep.  ^(.NET 8 Runtime noetig, ~5 MB^)
echo.
set /p CHOICE="Auswahl [1]: "
if "%CHOICE%"=="" set CHOICE=1

set OUTDIR=.\publish
if exist "%OUTDIR%" rd /s /q "%OUTDIR%"

if "%CHOICE%"=="2" (
    echo.
    echo [BUILD] Framework-Dependent...
    dotnet publish -c Release -r win-x64 -o "%OUTDIR%" ^
        -p:SelfContained=false ^
        -p:PublishSingleFile=true ^
        -p:PublishTrimmed=false ^
        -p:EnableCompressionInSingleFile=false ^
        -p:IncludeNativeLibrariesForSelfExtract=false ^
        -p:DebugType=none ^
        -p:DebugSymbols=false
) else (
    echo.
    echo [BUILD] Self-Contained + Compressed + Trimmed...
    dotnet publish -c Release -r win-x64 -o "%OUTDIR%" ^
        -p:SelfContained=true ^
        -p:PublishSingleFile=true ^
        -p:PublishTrimmed=false ^
        -p:EnableCompressionInSingleFile=true ^
        -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:DebugType=none ^
        -p:DebugSymbols=false
)

if errorlevel 1 (
    echo.
    echo [ERROR] Build fehlgeschlagen!
    pause
    exit /b 1
)

:: Copy config if not present
if not exist "%OUTDIR%\appsettings.json" (
    copy /y appsettings.json "%OUTDIR%\" >nul
)

echo.
echo ============================================
echo  Build erfolgreich!
echo  Ausgabe: %OUTDIR%\N24DesktopInfo.exe
echo ============================================

:: Show file size
for %%F in ("%OUTDIR%\N24DesktopInfo.exe") do (
    set SIZE=%%~zF
    set /a SIZEMB=!SIZE! / 1048576
    echo  Groesse: !SIZEMB! MB
)

echo.
pause
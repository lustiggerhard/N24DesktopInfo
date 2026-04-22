:: @file     build.bat
:: @brief    Build-Script fuer N24 Desktop Info
:: @author   Gerhard Lustig (gerhard@lustig.at)
:: @version  1.5.0
:: @date     2026-04-22
:: @history
::   2026-04-22  v1.5.0  Robusteres publish-Cleanup: loeschen + neu anlegen mit Fehlercheck
::   2026-04-22  v1.4.0  Option 1: nur EXE behalten, alle uebrigen DLLs loeschen
::   2026-04-21  v1.3.0  appsettings.json nicht ins publish; ZIP wird erstellt
::   2026-04-21  v1.2.0  PS1-Scripts werden ins publish-Verzeichnis kopiert
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
echo  [1] Self-Contained  ^(nur EXE, keine Runtime noetig^)
echo  [2] Framework-Dep.  ^(.NET 8 Runtime noetig, ~5 MB^)
echo.
set /p CHOICE="Auswahl [1]: "
if "%CHOICE%"=="" set CHOICE=1

:: publish-Verzeichnis leeren
set OUTDIR=.\publish
if exist "%OUTDIR%" (
    echo.
    echo [CLEAN] publish\ leeren...
    rd /s /q "%OUTDIR%" 2>nul
    :: Kurz warten falls noch Handles offen
    timeout /t 1 /nobreak >nul
    if exist "%OUTDIR%" (
        :: Konnte nicht geloescht werden - manuell leeren
        for /f "delims=" %%F in ('dir /b /a-d "%OUTDIR%\" 2^>nul') do del /f /q "%OUTDIR%\%%F" 2>nul
        for /d %%D in ("%OUTDIR%\*") do rd /s /q "%%D" 2>nul
    )
)
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

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
    echo [BUILD] Self-Contained...
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

:: Bei Self-Contained: alle DLLs und ueberfluessigen Files loeschen
if "%CHOICE%"=="1" (
    echo.
    echo [CLEAN] Ueberfluessige Dateien entfernen...
    for %%F in ("%OUTDIR%\*.dll") do del /f /q "%%F"
    for %%F in ("%OUTDIR%\*.json") do del /f /q "%%F"
    for %%F in ("%OUTDIR%\*.pdb") do del /f /q "%%F"
    for %%F in ("%OUTDIR%\*.exe") do if /i not "%%~nF"=="N24DesktopInfo" del /f /q "%%F"
    if exist "%OUTDIR%\N24DesktopInfo" del /f /q "%OUTDIR%\N24DesktopInfo"
    for /d %%D in ("%OUTDIR%\*") do rd /s /q "%%D"
    echo  [OK] Bereinigt
)

:: appsettings.json reinkopieren
copy /y appsettings.json "%OUTDIR%\" >nul
echo  [OK] appsettings.json kopiert

:: @Tools scripts reinkopieren
if exist "Check-WindowsActivation.ps1" (
    copy /y "Check-WindowsActivation.ps1" "%OUTDIR%\" >nul
    echo  [OK] Check-WindowsActivation.ps1 kopiert
)
if exist "Check-WindowsActivationRegister.ps1" (
    copy /y "Check-WindowsActivationRegister.ps1" "%OUTDIR%\" >nul
    echo  [OK] Check-WindowsActivationRegister.ps1 kopiert
)

echo.
echo ============================================
echo  Build erfolgreich!
echo  Ausgabe: %OUTDIR%\N24DesktopInfo.exe
echo ============================================

for %%F in ("%OUTDIR%\N24DesktopInfo.exe") do (
    set SIZE=%%~zF
    set /a SIZEMB=!SIZE! / 1048576
    echo  EXE-Groesse: !SIZEMB! MB
)

echo.
echo  Inhalt publish\:
dir /b "%OUTDIR%"

echo.
pause
@echo off
REM ============================================================
REM  N24 Desktop Info - Autostart Installer
REM  Erstellt/entfernt Autostart-Verknuepfung
REM ============================================================

set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "SHORTCUT=%STARTUP%\N24DesktopInfo.lnk"
set "TARGET=%~dp0publish\N24DesktopInfo.exe"

if "%1"=="remove" goto :remove

echo.
echo  === N24 Desktop Info - Autostart Setup ===
echo.

if not exist "%TARGET%" (
    echo  FEHLER: %TARGET% nicht gefunden!
    echo  Bitte zuerst build.bat ausfuehren.
    pause
    exit /b 1
)

echo  Erstelle Autostart-Verknuepfung...

powershell -NoProfile -Command ^
    "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('%SHORTCUT%'); $s.TargetPath = '%TARGET%'; $s.WorkingDirectory = '%~dp0publish'; $s.Description = 'N24 Desktop Info Overlay'; $s.Save()"

if exist "%SHORTCUT%" (
    echo  OK: Autostart-Verknuepfung erstellt.
    echo  Pfad: %SHORTCUT%
) else (
    echo  FEHLER: Verknuepfung konnte nicht erstellt werden.
)
echo.
pause
exit /b 0

:remove
echo.
echo  Entferne Autostart-Verknuepfung...
if exist "%SHORTCUT%" (
    del "%SHORTCUT%"
    echo  OK: Autostart-Verknuepfung entfernt.
) else (
    echo  Keine Autostart-Verknuepfung gefunden.
)
echo.
pause
exit /b 0

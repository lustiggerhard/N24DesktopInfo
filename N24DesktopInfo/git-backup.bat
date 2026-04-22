:: @file     git-backup.bat
:: @brief    Automatisches Git-Backup vor jedem Build
:: @author   Gerhard Lustig (gerhard@lustig.at)
:: @version  1.0.0
:: @date     2026-04-21
:: @history
::   2026-04-21  v1.0.0  Initiale Version
@echo off
cd /D "D:\VisualStudio\N24DesktopInfo"
git add -A
git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Auto-Backup vor Build %DATE% %TIME%"
    echo [OK] Git-Backup erstellt.
) else (
    echo [INFO] Keine Aenderungen - kein Commit noetig.
)
exit /b 0
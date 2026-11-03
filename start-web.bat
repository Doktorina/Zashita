@echo off
cd /d "%~dp0"
title Zashita demo
echo.
echo   Starting Zashita demo...
echo   Browser will open http://localhost:5298
echo   Login: admin   Password: 123
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-web.ps1"
echo.
echo   Site stopped.
pause

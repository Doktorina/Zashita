@echo off
chcp 65001 >nul
cd /d "%~dp0"
title Zashita — демо
echo.
echo   Запуск демо Zashita...
echo   Браузер откроется сам на http://localhost:5298
echo   Логин admin / пароль 123
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-web.ps1"
echo.
echo   Сайт остановлен.
pause

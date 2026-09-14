@echo off
title PiRMIiKS UDP Server
cd /d "%~dp0"
Server\Server.exe
echo.
echo Server stopped. Press any key to close this window.
pause >nul


@echo off
title PiRMIiKS UDP Client - Local Test
cd /d "%~dp0"
Client\Client.exe 127.0.0.1 7777
echo.
echo Client stopped. Press any key to close this window.
pause >nul


@echo off
title PiRMIiKS UDP Client - LAN Test
cd /d "%~dp0"
set /p SERVER_IP=Enter server IPv4 address: 
Client\Client.exe %SERVER_IP% 7777
echo.
echo Client stopped. Press any key to close this window.
pause >nul


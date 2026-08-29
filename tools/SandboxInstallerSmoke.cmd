@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\ForecastCenterTools\SandboxInstallerSmoke.ps1" -Installer "C:\ForecastCenterInstaller\ForecastCenter-Setup-0.8.0-x64.exe" -ResultPath "C:\ForecastCenterResults\result.json"

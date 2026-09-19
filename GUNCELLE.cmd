@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Kur.ps1" -Update
if errorlevel 1 echo Guncelleme tamamlanamadi. Eklentiyi kapatip tekrar deneyin.
pause

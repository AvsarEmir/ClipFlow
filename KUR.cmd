@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Kur.ps1"
if errorlevel 1 echo Kurulum tamamlanamadi. Yukaridaki hata mesajini kontrol edin.
pause

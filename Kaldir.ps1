param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Akis'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\NativeHosts.ps1')
Unregister-DiskoraHosts ([IO.Path]::GetFullPath($Destination))
Write-Host 'ClipFlow yerel yardimci baglantilari kaldirildi.' -ForegroundColor Green
Write-Host 'ClipFlow eklentisini kullandiginiz tarayicilarin uzantilar sayfasindan kaldirin.'
Write-Host 'Indirdiginiz dosyalar ve ayarlar korunur.'
Write-Host ('Program klasoru: ' + $Destination)

param(
    [switch]$Update,
    [switch]$PrepareOnly,
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Akis')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'scripts\NativeHosts.ps1')
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not [Environment]::Is64BitOperatingSystem) { throw 'ClipFlow, 64 bit Windows 10/11 gerektirir.' }
$Destination = [IO.Path]::GetFullPath($Destination)
$stage = Join-Path $Destination ('setup-' + [Guid]::NewGuid().ToString('N'))
$bin = Join-Path $Destination 'bin'
New-Item -ItemType Directory -Force -Path $stage,$bin | Out-Null

function Get-Asset($Repo, $Name) {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'ClipFlow-Setup/1.0.0' }
    $asset = $release.assets | Where-Object name -EQ $Name | Select-Object -First 1
    if (-not $asset) { throw "Indirme bulunamadi: $Repo / $Name" }
    return $asset
}
function Get-Checked($Url, $Path, $Hash) {
    if ($Hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Gecerli SHA256 dogrulamasi bulunamadi.' }
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Path
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Indirilen dosya dogrulanamadi: $Path" }
}
function Get-AssetHash($Asset) {
    if ($Asset.digest -match '^sha256:([a-fA-F0-9]{64})$') { return $Matches[1] }
    throw "Yayinci SHA256 ozeti sunmuyor: $($Asset.name)"
}
try {
    Write-Host 'ClipFlow kuruluyor. Bu islem internet hizina gore birkac dakika surebilir.' -ForegroundColor Cyan
    if ($Update -or -not (Test-Path -LiteralPath (Join-Path $bin 'yt-dlp.exe'))) {
        Write-Host '[1/4] yt-dlp indiriliyor ve dogrulaniyor...'
        $asset = Get-Asset 'yt-dlp/yt-dlp' 'yt-dlp.exe'
        $download = Join-Path $stage 'yt-dlp.exe'
        Get-Checked $asset.browser_download_url $download (Get-AssetHash $asset)
        Copy-Item -LiteralPath $download -Destination (Join-Path $bin 'yt-dlp.exe') -Force
    }
    if ($Update -or -not (Test-Path -LiteralPath (Join-Path $bin 'deno.exe'))) {
        Write-Host '[2/4] Deno indiriliyor ve dogrulaniyor...'
        $asset = Get-Asset 'denoland/deno' 'deno-x86_64-pc-windows-msvc.zip'
        $download = Join-Path $stage 'deno.zip'
        Get-Checked $asset.browser_download_url $download (Get-AssetHash $asset)
        Expand-Archive -LiteralPath $download -DestinationPath (Join-Path $stage 'deno')
        Copy-Item -LiteralPath (Join-Path $stage 'deno\deno.exe') -Destination (Join-Path $bin 'deno.exe') -Force
    }
    if ($Update -or -not (Test-Path -LiteralPath (Join-Path $bin 'ffmpeg.exe')) -or -not (Test-Path -LiteralPath (Join-Path $bin 'ffprobe.exe'))) {
        Write-Host '[3/4] FFmpeg indiriliyor ve dogrulaniyor...'
        $url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
        $hashText = (Invoke-WebRequest -UseBasicParsing -Uri ($url + '.sha256')).Content
        $hash = [regex]::Match([string]$hashText, '[a-fA-F0-9]{64}').Value
        $download = Join-Path $stage 'ffmpeg.zip'
        Get-Checked $url $download $hash
        Expand-Archive -LiteralPath $download -DestinationPath (Join-Path $stage 'ffmpeg')
        foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
            $file = Get-ChildItem -LiteralPath (Join-Path $stage 'ffmpeg') -Filter $name -Recurse | Select-Object -First 1
            if (-not $file) { throw "FFmpeg arsivinde $name bulunamadi." }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $bin $name) -Force
        }
        $license = Get-ChildItem -LiteralPath (Join-Path $stage 'ffmpeg') -Filter 'LICENSE*' -Recurse | Select-Object -First 1
        if ($license) { Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $bin 'FFmpeg-LICENSE.txt') -Force }
    }
    Write-Host '[4/4] Tarayici yardimcisi hazirlaniyor...'
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 bulunamadi. Windows Update ile kurun.' }
    $hostFileName = 'ClipFlowHost-1.0.0.exe'
    $hostExe = Join-Path $stage $hostFileName
    & $compiler /nologo /target:exe /platform:x64 /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll "/out:$hostExe" (Join-Path $PSScriptRoot 'native\Host.cs') (Join-Path $PSScriptRoot 'native\Soundpad.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Yerel yardimci derlenemedi.' }
    Copy-Item -LiteralPath $hostExe -Destination (Join-Path $Destination $hostFileName) -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\extension-id.txt') -Destination (Join-Path $Destination 'extension-id.txt') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\firefox-extension-id.txt') -Destination (Join-Path $Destination 'firefox-extension-id.txt') -Force
    foreach ($extensionFolder in @('extension','extension-firefox')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ($extensionFolder + '\manifest.json')))) { throw 'Eksik eklenti paketi. Dosyalari ZIP arsivinden tamamen cikartin.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $extensionFolder) -Destination $Destination -Recurse -Force
    }
    $extensionId = (Get-Content -LiteralPath (Join-Path $Destination 'extension-id.txt') -Raw).Trim()
    if ($extensionId -notmatch '^[a-p]{32}$') { throw 'Eklenti kimligi gecersiz.' }
    $manifest = @{
        name = 'com.akis.downloader'; description = 'ClipFlow YouTube Downloader';
        path = (Join-Path $Destination $hostFileName); type = 'stdio';
        allowed_origins = @("chrome-extension://$extensionId/")
    }
    $manifestPath = Join-Path $Destination 'host.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    $firefoxId = (Get-Content -LiteralPath (Join-Path $Destination 'firefox-extension-id.txt') -Raw).Trim()
    if ($firefoxId -ne 'diskora@diskora.local') { throw 'Firefox eklenti kimligi gecersiz.' }
    $firefoxManifest = @{
        name = 'com.akis.downloader'; description = 'ClipFlow YouTube Downloader';
        path = (Join-Path $Destination $hostFileName); type = 'stdio';
        allowed_extensions = @($firefoxId)
    }
    [IO.File]::WriteAllText((Join-Path $Destination 'host-firefox.json'), ($firefoxManifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (-not $PrepareOnly) {
        Register-DiskoraHosts $Destination
        Write-Host ''
        Write-Host 'Kurulum tamamlandi: Chromium tarayicilari + Firefox.' -ForegroundColor Green
        Write-Host 'Chrome / Edge / Brave / Opera / Opera GX / Vivaldi / Chromium:'
        Write-Host 'Tarayicinin uzantilar sayfasinda Gelistirici modu > Paketlenmemis oge yukle:'
        Write-Host (Join-Path $Destination 'extension') -ForegroundColor Yellow
        Write-Host 'Firefox: about:debugging#/runtime/this-firefox > Gecici eklenti yukle:'
        Write-Host (Join-Path $Destination 'extension-firefox\manifest.json') -ForegroundColor Yellow
        Write-Host 'Firefox kalici kurulum icin Mozilla imzasi gerektirir. README.md dosyasina bakin.'
        Write-Host 'Mevcut ClipFlow / Diskora eklentinizi yenileyin; yeni yardimci bir sonraki baglantida kullanilir.'
    } else { Write-Host "Test dosyalari hazir (kayit defterine yazilmadi): $Destination" -ForegroundColor Green }
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $expectedRoot = $Destination.TrimEnd('\') + '\'
    if (-not $resolvedStage.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedStage) -notmatch '^setup-[a-f0-9]{32}$') { throw 'Gecici klasor yolu dogrulanamadi.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}

param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
& (Join-Path $PSScriptRoot 'Build-Extensions.ps1') -ProjectRoot $ProjectRoot
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = Join-Path $ProjectRoot 'packages'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$version = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'extension\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }

function Write-Package([string]$Path, [System.IO.FileInfo[]]$Files, [string]$Root, [string]$Prefix = '') {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in ($Files | Sort-Object FullName)) {
                $relative = $file.FullName.Substring($Root.TrimEnd('\').Length + 1).Replace('\','/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ($Prefix + $relative), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

$chromium = Join-Path $ProjectRoot 'extension'
$firefox = Join-Path $ProjectRoot 'extension-firefox'
Write-Package (Join-Path $output "ClipFlow-Chromium-$version.zip") @(Get-ChildItem -LiteralPath $chromium -File -Recurse) $chromium
Write-Package (Join-Path $output "ClipFlow-Firefox-$version-unsigned.zip") @(Get-ChildItem -LiteralPath $firefox -File -Recurse) $firefox
# The installer contains only what installation and use require.
$files = @()
foreach ($name in @('README.md','LICENSE','KUR.cmd','Kur.ps1','GUNCELLE.cmd','KALDIR.cmd','Kaldir.ps1')) {
    $files += Get-Item -LiteralPath (Join-Path $ProjectRoot $name) -Force
}
foreach ($folder in @('extension','extension-firefox','native','docs')) {
    $files += @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot $folder) -File -Recurse)
}
$files += Get-Item -LiteralPath (Join-Path $ProjectRoot 'scripts\NativeHosts.ps1')
Write-Package (Join-Path $output "ClipFlow-Windows-$version.zip") $files $ProjectRoot 'ClipFlow/'
Write-Host "Packages ready: $output"

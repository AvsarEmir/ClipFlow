param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$source = Join-Path $ProjectRoot 'extension'
$target = Join-Path $ProjectRoot 'extension-firefox'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
$manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$worker = $manifest.background.service_worker
$manifest.PSObject.Properties.Remove('key')
$manifest.PSObject.Properties.Remove('minimum_chrome_version')
$manifest.background = [PSCustomObject]@{ scripts = @($worker); type = 'module' }
$firefoxId = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'native\firefox-extension-id.txt') -Raw).Trim()
$manifest | Add-Member -NotePropertyName browser_specific_settings -NotePropertyValue @{
    gecko = @{ id = $firefoxId; strict_min_version = '140.0'; data_collection_permissions = @{ required = @('browsingActivity','websiteContent') } }
}
[IO.File]::WriteAllText((Join-Path $target 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Write-Host 'Chromium ve Firefox eklenti dosyalari hazir.'

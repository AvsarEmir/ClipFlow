param([string]$WorkDirectory = (Join-Path $env:TEMP ('diskora-install-test-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path ([IO.Path]::GetFullPath($WorkDirectory)) 'runtime'
if (Test-Path -LiteralPath $runtime) { throw "Use a fresh test directory: $runtime already exists." }
New-Item -ItemType Directory -Path (Join-Path $runtime 'bin') -Force | Out-Null
foreach ($name in @('yt-dlp.exe','deno.exe','ffmpeg.exe','ffprobe.exe')) {
    # These tools are intentionally never executed by this installer test.
    [IO.File]::WriteAllBytes((Join-Path $runtime "bin\$name"), [byte[]]@())
}
. (Join-Path $project 'scripts\NativeHosts.ps1')
$entries = @(Get-DiskoraHostRegistrations $runtime)
if ($entries.Count -ne 6 -or @($entries.SubKey | Select-Object -Unique).Count -ne 6) { throw 'Browser registration plan is incomplete.' }
$expectedParents = @('Software\Microsoft\Edge','Software\Google\Chrome','Software\Chromium','Software\BraveSoftware\Brave-Browser','Software\Vivaldi','Software\Mozilla')
foreach ($parent in $expectedParents) {
    if ($entries.SubKey -notcontains "$parent\NativeMessagingHosts\com.akis.downloader") { throw "Missing registration: $parent" }
}
# Snapshots detect accidental registry writes by PrepareOnly; this test never registers hosts.
function Read-Registrations {
    foreach ($entry in $entries) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
            try {
                $key = $root.OpenSubKey($entry.SubKey)
                $value = '<absent>'
                if ($key) { try { $value = $key.GetValue('') } finally { $key.Dispose() } }
                "$view|$($entry.SubKey)|$value"
            } finally { $root.Dispose() }
        }
    }
}
$before = @(Read-Registrations)
& (Join-Path $project 'Kur.ps1') -PrepareOnly -Destination $runtime
if (Compare-Object $before @(Read-Registrations)) { throw 'PrepareOnly modified registry registrations.' }
$chromium = Get-Content -LiteralPath (Join-Path $runtime 'host.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$firefox = Get-Content -LiteralPath (Join-Path $runtime 'host-firefox.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$addon = Get-Content -LiteralPath (Join-Path $runtime 'extension-firefox\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$id = (Get-Content -LiteralPath (Join-Path $runtime 'extension-id.txt') -Raw).Trim()
if ($chromium.allowed_origins.Count -ne 1 -or $chromium.allowed_origins[0] -ne "chrome-extension://$id/") { throw 'Wrong Chromium origin.' }
if ($firefox.allowed_extensions.Count -ne 1 -or $firefox.allowed_extensions[0] -ne $addon.browser_specific_settings.gecko.id) { throw 'Wrong Firefox add-on ID.' }
if ($chromium.PSObject.Properties.Name -contains 'allowed_extensions' -or $firefox.PSObject.Properties.Name -contains 'allowed_origins') { throw 'Host authorization formats were mixed.' }
foreach ($hostManifest in @($chromium,$firefox)) {
    if ($hostManifest.type -ne 'stdio' -or $hostManifest.name -ne 'com.akis.downloader' -or -not (Test-Path -LiteralPath $hostManifest.path)) { throw 'Invalid native host manifest.' }
}
foreach ($entry in $entries) {
    $expected = if ($entry.SubKey -like 'Software\Mozilla\*') { 'host-firefox.json' } else { 'host.json' }
    if ($entry.Manifest -ne (Join-Path $runtime $expected)) { throw 'Wrong browser manifest mapping.' }
}
Write-Host 'PASS Installer compile, both extension copies, both host manifests, 6 browser registrations and no registry mutations.'

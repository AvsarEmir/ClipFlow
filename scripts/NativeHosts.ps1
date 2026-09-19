# The host name and Chromium ID stay stable to preserve existing Edge installs.
function Get-DiskoraHostRegistrations([string]$Destination) {
    $chromium = Join-Path $Destination 'host.json'
    $firefox = Join-Path $Destination 'host-firefox.json'
    foreach ($parent in @('Software\Microsoft\Edge','Software\Google\Chrome','Software\Chromium','Software\BraveSoftware\Brave-Browser','Software\Vivaldi')) {
        [PSCustomObject]@{ SubKey = "$parent\NativeMessagingHosts\com.akis.downloader"; Manifest = $chromium }
    }
    [PSCustomObject]@{ SubKey = 'Software\Mozilla\NativeMessagingHosts\com.akis.downloader'; Manifest = $firefox }
}

function Register-DiskoraHosts([string]$Destination) {
    foreach ($entry in Get-DiskoraHostRegistrations $Destination) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
            try {
                $key = $root.CreateSubKey($entry.SubKey)
                try { $key.SetValue('', $entry.Manifest, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
            } finally { $root.Dispose() }
        }
    }
}

function Unregister-DiskoraHosts([string]$Destination) {
    foreach ($entry in Get-DiskoraHostRegistrations $Destination) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
            try {
                $key = $root.OpenSubKey($entry.SubKey)
                $owned = $false
                if ($key) { try { $owned = [string]::Equals([string]$key.GetValue(''), $entry.Manifest, [StringComparison]::OrdinalIgnoreCase) } finally { $key.Dispose() } }
                # Leave another installation's registration alone.
                if ($owned) { $root.DeleteSubKey($entry.SubKey, $false) }
            } finally { $root.Dispose() }
        }
    }
}

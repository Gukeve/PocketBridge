[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'runtime'),
    [string]$ScrcpyVersion = '4.1'
)

$ErrorActionPreference = 'Stop'
$headers = @{ 'User-Agent' = 'PocketBridge-runtime-preparer/3.0' }
$platformToolsVersion = '37.0.0'
$platformToolsUri = "https://dl.google.com/android/repository/platform-tools_r$platformToolsVersion-win.zip"
$platformToolsSha256 = '4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PocketBridge-runtime-{0}" -f [guid]::NewGuid().ToString('N'))
$scrcpyDestination = Join-Path $Destination 'scrcpy'
$platformDestination = Join-Path $Destination 'platform-tools'

try {
    $releaseUri = if ([string]::IsNullOrWhiteSpace($ScrcpyVersion)) {
        'https://api.github.com/repos/Genymobile/scrcpy/releases/latest'
    } else {
        $normalizedVersion = if ($ScrcpyVersion.StartsWith('v')) { $ScrcpyVersion } else { "v$ScrcpyVersion" }
        "https://api.github.com/repos/Genymobile/scrcpy/releases/tags/$normalizedVersion"
    }

    Write-Host 'Official components to download:'
    Write-Host "  scrcpy: $releaseUri"
    Write-Host "  Android SDK Platform-Tools: $platformToolsUri"
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -match '^scrcpy-win64-v[\d.]+\.zip$' } | Select-Object -First 1
    if ($null -eq $asset) { throw 'The official release does not contain a Windows x64 scrcpy archive.' }

    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $scrcpyArchive = Join-Path $temporaryRoot $asset.name
    $platformArchive = Join-Path $temporaryRoot 'platform-tools-latest-windows.zip'
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $scrcpyArchive
    Invoke-WebRequest -Uri $platformToolsUri -OutFile $platformArchive

    if (-not [string]::IsNullOrWhiteSpace($asset.digest) -and $asset.digest.StartsWith('sha256:')) {
        $expectedHash = $asset.digest.Substring(7).ToLowerInvariant()
        $actualHash = (Get-FileHash -LiteralPath $scrcpyArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch for $($asset.name)." }
    }
    $actualPlatformHash = (Get-FileHash -LiteralPath $platformArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualPlatformHash -ne $platformToolsSha256) { throw 'SHA-256 mismatch for Android SDK Platform-Tools.' }

    $scrcpyExtract = Join-Path $temporaryRoot 'scrcpy-extracted'
    $platformExtract = Join-Path $temporaryRoot 'platform-extracted'
    Expand-Archive -LiteralPath $scrcpyArchive -DestinationPath $scrcpyExtract
    Expand-Archive -LiteralPath $platformArchive -DestinationPath $platformExtract
    $scrcpyExecutable = Get-ChildItem -LiteralPath $scrcpyExtract -Filter 'scrcpy.exe' -File -Recurse | Select-Object -First 1
    $adbExecutable = Get-ChildItem -LiteralPath $platformExtract -Filter 'adb.exe' -File -Recurse | Select-Object -First 1
    if ($null -eq $scrcpyExecutable) { throw 'The scrcpy archive does not contain scrcpy.exe.' }
    if ($null -eq $adbExecutable) { throw 'The platform-tools archive does not contain adb.exe.' }

    $scrcpySource = $scrcpyExecutable.Directory.FullName
    $platformSource = $adbExecutable.Directory.FullName
    foreach ($file in @('scrcpy.exe', 'scrcpy-server')) {
        if (-not (Test-Path -LiteralPath (Join-Path $scrcpySource $file) -PathType Leaf)) { throw "Missing scrcpy component: $file" }
    }
    foreach ($file in @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt', 'source.properties')) {
        if (-not (Test-Path -LiteralPath (Join-Path $platformSource $file) -PathType Leaf)) { throw "Missing platform-tools component: $file" }
    }

    New-Item -ItemType Directory -Path $scrcpyDestination -Force | Out-Null
    New-Item -ItemType Directory -Path $platformDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $scrcpySource -Force | Where-Object { $_.Name -notin @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll') } | Copy-Item -Destination $scrcpyDestination -Recurse -Force
    foreach ($file in @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'NOTICE.txt', 'source.properties')) {
        Copy-Item -LiteralPath (Join-Path $platformSource $file) -Destination $platformDestination -Force
    }

    [ordered]@{
        component = 'scrcpy'
        source = 'https://github.com/Genymobile/scrcpy/releases'
        version = $release.tag_name
        asset = $asset.name
        sha256 = (Get-FileHash -LiteralPath $scrcpyArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        preparedAtUtc = [DateTime]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scrcpyDestination 'pocketbridge-runtime.json') -Encoding UTF8
    [ordered]@{
        component = 'Android SDK Platform-Tools'
        source = $platformToolsUri
        version = $platformToolsVersion
        sha256 = $platformToolsSha256
        preparedAtUtc = [DateTime]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $platformDestination 'pocketbridge-runtime.json') -Encoding UTF8

    Write-Host "Runtime prepared in $Destination"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

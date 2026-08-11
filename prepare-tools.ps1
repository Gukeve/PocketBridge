[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'tools'),
    [string]$Version = '4.1'
)

$ErrorActionPreference = 'Stop'
$requiredFiles = @('adb.exe', 'scrcpy.exe', 'scrcpy-server', 'AdbWinApi.dll', 'AdbWinUsbApi.dll')
$headers = @{ 'User-Agent' = 'PocketBridge-runtime-preparer/1.0' }
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PocketBridge-{0}" -f [guid]::NewGuid().ToString('N'))

try {
    $releaseUri = if ([string]::IsNullOrWhiteSpace($Version)) {
        'https://api.github.com/repos/Genymobile/scrcpy/releases/latest'
    } else {
        $normalizedVersion = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
        "https://api.github.com/repos/Genymobile/scrcpy/releases/tags/$normalizedVersion"
    }

    Write-Host "Reading official release metadata from $releaseUri"
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -match '^scrcpy-win64-v[\d.]+\.zip$' } | Select-Object -First 1
    if ($null -eq $asset) {
        throw 'The official release does not contain a Windows x64 archive.'
    }

    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $archivePath = Join-Path $temporaryRoot $asset.name
    $extractPath = Join-Path $temporaryRoot 'extracted'
    Write-Host "Downloading official $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $archivePath

    if (-not [string]::IsNullOrWhiteSpace($asset.digest) -and $asset.digest.StartsWith('sha256:')) {
        $expectedHash = $asset.digest.Substring(7)
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
            throw "SHA-256 mismatch for $($asset.name)."
        }
        Write-Host "SHA-256 verified: $actualHash"
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
    $scrcpyExecutable = Get-ChildItem -LiteralPath $extractPath -Filter 'scrcpy.exe' -File -Recurse | Select-Object -First 1
    if ($null -eq $scrcpyExecutable) {
        throw 'The downloaded archive does not contain scrcpy.exe.'
    }

    $runtimeSource = $scrcpyExecutable.Directory.FullName
    $missing = $requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $runtimeSource $_) -PathType Leaf) }
    if ($missing.Count -gt 0) {
        throw "The downloaded archive is incomplete. Missing: $($missing -join ', ')"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $runtimeSource '*') -Destination $Destination -Recurse -Force
    $manifest = [ordered]@{
        source = 'https://github.com/Genymobile/scrcpy/releases'
        version = $release.tag_name
        asset = $asset.name
        sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        preparedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Destination 'pocketbridge-runtime.json') -Encoding UTF8

    Write-Host "PocketBridge runtime prepared in $Destination"
    Get-ChildItem -LiteralPath $Destination -File | Select-Object Name, Length
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

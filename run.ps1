[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $repositoryRoot 'PocketBridge.sln'
$project = Join-Path $repositoryRoot 'PocketBridge.App\PocketBridge.App.csproj'

Push-Location $repositoryRoot
try {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw 'The .NET SDK is not installed. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and run this script again.'
    }

    $sdkVersion = (& dotnet --version 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
        throw 'No SDK compatible with global.json is available. Install a current .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0.'
    }
    if ([Version]$sdkVersion -lt [Version]'8.0.100' -or ([Version]$sdkVersion).Major -ne 8) {
        throw "PocketBridge requires a compatible .NET 8 SDK; global.json selected $sdkVersion. Install the current .NET 8 SDK."
    }

    Write-Host "Using .NET SDK $sdkVersion"
    & dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'PocketBridge restore failed.' }
    & dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'PocketBridge Release build failed.' }
    & dotnet run --project $project -c Release --no-build
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}

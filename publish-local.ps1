[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repositoryRoot 'PocketBridge.App\PocketBridge.App.csproj'
$publishDirectory = Join-Path $repositoryRoot 'publish'
$executable = Join-Path $publishDirectory 'PocketBridge.App.exe'

Push-Location $repositoryRoot
try {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        throw 'The .NET SDK is not installed. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and run this script again.'
    }
    $sdkVersion = (& dotnet --version 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion) -or [Version]$sdkVersion -lt [Version]'8.0.100' -or ([Version]$sdkVersion).Major -ne 8) {
        throw 'PocketBridge requires a compatible .NET 8 SDK selected through global.json.'
    }

    & dotnet restore $project --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'PocketBridge restore failed.' }
    & dotnet publish $project -c Release -r win-x64 --self-contained false --no-restore `
        -p:BundleRuntime=false -p:DebugSymbols=false -p:DebugType=None -o $publishDirectory
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'PocketBridge publish did not produce the expected executable.'
    }

    Write-Host 'PocketBridge published successfully:'
    Write-Host $executable
}
finally {
    Pop-Location
}

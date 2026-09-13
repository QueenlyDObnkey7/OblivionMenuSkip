param([string]$SdkDirectory = (Join-Path $env:APPDATA 'ReadyM.Launcher/Oblivion/Mods/OblivionMp/dlls'))
$ErrorActionPreference = 'Stop'
$SdkDirectory = (Resolve-Path -LiteralPath $SdkDirectory).Path
dotnet build (Join-Path $PSScriptRoot 'Client/OblivionMenuSkip.Client.csproj') -c Release --nologo "-p:ReadyMClientDir=$SdkDirectory"
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests/MenuSkip.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Checks failed' }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json).version
$dist = Join-Path $PSScriptRoot 'dist'
# Fresh staging avoids including stale files in either public archive.
$stage = Join-Path $dist ('stage-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'release/mods/OblivionMenuSkip'
New-Item -ItemType Directory -Path (Join-Path $package 'client') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Client/bin/Release/net10.0/OblivionMenuSkip.Client.dll') -Destination (Join-Path $package 'client')
foreach ($name in @('manifest.json', 'README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $package
}
Compress-Archive -LiteralPath (Join-Path $stage 'release/mods') -DestinationPath (Join-Path $dist "OblivionMenuSkip-$version.zip") -Force
$source = Join-Path $stage 'source/OblivionMenuSkip'
New-Item -ItemType Directory -Path $source -Force | Out-Null
foreach ($name in @('BUILD.ps1', 'README.md', 'LICENSE', '.gitignore', 'manifest.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source
}
foreach ($folder in @('Client', 'Tests')) {
    $target = Join-Path $source $folder
    New-Item -ItemType Directory -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -File |
        Where-Object Extension -In @('.cs', '.csproj', '.tsv') |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target }
}
Compress-Archive -LiteralPath $source -DestinationPath (Join-Path $dist "OblivionMenuSkip-$version-source.zip") -Force
Write-Output "Release and GitHub source archives: $dist"

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet build (Join-Path $projectRoot 'src/SoulsRevive.Windows.csproj') -c Release -nologo -p:Flavor=ReadmeGifs -o $outputDir
if ($LASTEXITCODE -ne 0) { throw 'GIF exporter build failed.' }
& (Join-Path $outputDir 'ReadmeGifs.exe') (Join-Path $projectRoot 'assets\readme')
if ($LASTEXITCODE -ne 0) { throw 'GIF export failed.' }

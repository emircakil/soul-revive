$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDir = Join-Path $projectRoot 'test-output'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$exporter = Join-Path $outputDir 'ReadmeGifs.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $projectRoot 'tools\ReadmeGifs.cs'
& $compiler /nologo /target:exe /main:SoulsRevive.ReadmeGifs /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /out:$exporter @sources
if ($LASTEXITCODE -ne 0) { throw 'GIF exporter build failed.' }
& $exporter (Join-Path $projectRoot 'assets\readme')
if ($LASTEXITCODE -ne 0) { throw 'GIF export failed.' }

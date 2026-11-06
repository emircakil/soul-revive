$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconArgs = @()
if (Test-Path -LiteralPath "$projectRoot\assets\app.ico") { $iconArgs = @("/win32icon:$projectRoot\assets\app.ico") }
& $compiler /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /out:"$outputDir\SoulsRevive.exe" @iconArgs /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll "$projectRoot\src\SoulsRevive.cs"
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Write-Output "Hazır: $outputDir\SoulsRevive.exe"

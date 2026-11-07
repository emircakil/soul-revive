$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$common = @('/nologo', '/target:winexe', '/optimize+', '/utf8output', '/codepage:65001', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Runtime.Serialization.dll', "/resource:$projectRoot\assets\fonts\VT323-Regular.ttf,PixelFont.ttf", "/resource:$projectRoot\assets\fonts\VT323-OFL.txt,PixelFont.License")
# Generate the multi-resolution bonfire icon from the same artwork used by the tray.
$artworkExe = Join-Path $outputDir 'SoulRevive.Artwork.exe'
& $compiler @common /main:SoulsRevive.Program /out:$artworkExe @sources
if ($LASTEXITCODE -ne 0) { throw 'Artwork build failed.' }
$artwork = Start-Process -FilePath $artworkExe -ArgumentList @('--export-assets', ('"' + (Join-Path $projectRoot 'assets') + '"')) -Wait -PassThru -WindowStyle Hidden
if ($artwork.ExitCode -ne 0) { throw 'Artwork export failed.' }
Remove-Item -LiteralPath $artworkExe
$app = Join-Path $outputDir 'SoulsRevive.exe'
& $compiler @common /main:SoulsRevive.Program /out:$app "/win32icon:$projectRoot\assets\app.ico" @sources
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
$setup = Join-Path $outputDir 'SoulRevive-Setup.exe'
& $compiler @common /main:SoulsRevive.SetupProgram /out:$setup "/win32icon:$projectRoot\assets\app.ico" "/resource:$app,SoulsRevive.Payload" @sources
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$hashes = foreach ($file in @($app, $setup)) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $outputDir 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output "App: $app"
Write-Output "Setup: $setup"

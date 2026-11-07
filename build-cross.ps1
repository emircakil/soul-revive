$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$crossDir = Join-Path $projectRoot 'src-cross'
$outputDir = Join-Path $projectRoot 'bin'
$stagingDir = Join-Path $outputDir 'staging'

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$iconPng = Join-Path $projectRoot 'assets\app-icon.png'

# 1. Linux x64
Write-Output "Building Linux x64..."
$linuxOut = Join-Path $stagingDir 'soul-revive-linux-x64'
New-Item -ItemType Directory -Path $linuxOut -Force | Out-Null
dotnet publish (Join-Path $crossDir 'SoulsRevive.Cross.csproj') -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o $linuxOut | Out-Null

# Remove pdb
Get-ChildItem -Path $linuxOut -Filter '*.pdb' | Remove-Item -Force

$desktopEntry = @"
[Desktop Entry]
Name=Soul Revive
Comment=Pixel bonfire desktop companion
Exec=SoulsRevive
Icon=soulrevive
Terminal=false
Type=Application
Categories=Utility;Game;
"@
[IO.File]::WriteAllText((Join-Path $linuxOut 'soulrevive.desktop'), $desktopEntry)
Copy-Item -LiteralPath $iconPng -Destination (Join-Path $linuxOut 'soulrevive.png')

$linuxReadme = @"
Soul Revive for Linux (x64)
============================
To run:
1. Open a terminal in this folder
2. Make the binary executable: chmod +x SoulsRevive
3. Launch: ./SoulsRevive

To install as a desktop application:
- Copy 'soulrevive.png' to ~/.local/share/icons/
- Copy 'soulrevive.desktop' to ~/.local/share/applications/
- Place 'SoulsRevive' and 'libraylib.so' in ~/.local/bin/ (or update the Exec line in soulrevive.desktop)
"@
[IO.File]::WriteAllText((Join-Path $linuxOut 'README.txt'), $linuxReadme)

$linuxTar = Join-Path $outputDir 'soul-revive-linux-x64.tar.gz'
if (Test-Path $linuxTar) { Remove-Item -Force $linuxTar }
tar -czf $linuxTar -C $stagingDir 'soul-revive-linux-x64'
Write-Output "Packaged: $linuxTar"

# 2. macOS arm64 (Apple Silicon)
Write-Output "Building macOS arm64..."
$macArmOut = Join-Path $stagingDir 'soul-revive-macos-arm64'
$macArmApp = Join-Path $macArmOut 'SoulRevive.app'
$macArmContents = Join-Path $macArmApp 'Contents'
$macArmMacOS = Join-Path $macArmContents 'MacOS'
$macArmRes = Join-Path $macArmContents 'Resources'
New-Item -ItemType Directory -Path $macArmMacOS -Force | Out-Null
New-Item -ItemType Directory -Path $macArmRes -Force | Out-Null

dotnet publish (Join-Path $crossDir 'SoulsRevive.Cross.csproj') -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o $macArmMacOS | Out-Null
Get-ChildItem -Path $macArmMacOS -Filter '*.pdb' | Remove-Item -Force

Copy-Item (Join-Path $macArmMacOS 'SoulsRevive') (Join-Path $macArmOut 'SoulsRevive')
Copy-Item (Join-Path $macArmMacOS 'libraylib.dylib') (Join-Path $macArmOut 'libraylib.dylib')
Copy-Item -LiteralPath $iconPng -Destination (Join-Path $macArmRes 'app-icon.png')

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Soul Revive</string>
    <key>CFBundleDisplayName</key>
    <string>Soul Revive</string>
    <key>CFBundleIdentifier</key>
    <string>com.soulsrevive.companion</string>
    <key>CFBundleVersion</key>
    <string>1.3.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>SoulsRevive</string>
    <key>CFBundleIconFile</key>
    <string>app-icon.png</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@
[IO.File]::WriteAllText((Join-Path $macArmContents 'Info.plist'), $infoPlist)

$macReadme = @"
Soul Revive for macOS
=====================
To run:
Double-click SoulRevive.app, or run ./SoulsRevive from the terminal.

Note: On macOS, if you see 'app is damaged or can't be opened' due to Gatekeeper:
Run in Terminal:
xattr -cr SoulRevive.app
"@
[IO.File]::WriteAllText((Join-Path $macArmOut 'README.txt'), $macReadme)

$macArmTar = Join-Path $outputDir 'soul-revive-macos-arm64.tar.gz'
if (Test-Path $macArmTar) { Remove-Item -Force $macArmTar }
tar -czf $macArmTar -C $stagingDir 'soul-revive-macos-arm64'
Write-Output "Packaged: $macArmTar"

# 3. macOS x64 (Intel)
Write-Output "Building macOS x64..."
$macX64Out = Join-Path $stagingDir 'soul-revive-macos-x64'
$macX64App = Join-Path $macX64Out 'SoulRevive.app'
$macX64Contents = Join-Path $macX64App 'Contents'
$macX64MacOS = Join-Path $macX64Contents 'MacOS'
$macX64Res = Join-Path $macX64Contents 'Resources'
New-Item -ItemType Directory -Path $macX64MacOS -Force | Out-Null
New-Item -ItemType Directory -Path $macX64Res -Force | Out-Null

dotnet publish (Join-Path $crossDir 'SoulsRevive.Cross.csproj') -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o $macX64MacOS | Out-Null
Get-ChildItem -Path $macX64MacOS -Filter '*.pdb' | Remove-Item -Force

Copy-Item (Join-Path $macX64MacOS 'SoulsRevive') (Join-Path $macX64Out 'SoulsRevive')
Copy-Item (Join-Path $macX64MacOS 'libraylib.dylib') (Join-Path $macX64Out 'libraylib.dylib')
Copy-Item -LiteralPath $iconPng -Destination (Join-Path $macX64Res 'app-icon.png')
[IO.File]::WriteAllText((Join-Path $macX64Contents 'Info.plist'), $infoPlist)
[IO.File]::WriteAllText((Join-Path $macX64Out 'README.txt'), $macReadme)

$macX64Tar = Join-Path $outputDir 'soul-revive-macos-x64.tar.gz'
if (Test-Path $macX64Tar) { Remove-Item -Force $macX64Tar }
tar -czf $macX64Tar -C $stagingDir 'soul-revive-macos-x64'
Write-Output "Packaged: $macX64Tar"

# Clean up staging
Remove-Item -Recurse -Force $stagingDir

# Update SHA256SUMS.txt
$releaseFiles = @(Get-ChildItem -LiteralPath $outputDir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' -and $_.Extension -in @('.exe', '.gz', '.zip') } | ForEach-Object FullName)
$hashes = foreach ($file in $releaseFiles) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $outputDir 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output "Generated SHA256SUMS.txt for all release packages."

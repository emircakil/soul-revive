[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'osx-arm64', 'osx-x64')]
    [string]$Runtime,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 (pwsh).' }
if (-not $Runtime) {
    if ($IsLinux) { $Runtime = 'linux-x64' }
    elseif ($IsMacOS) {
        $Runtime = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
    }
    else { throw 'Specify -Runtime linux-x64, osx-arm64 or osx-x64. Release packages must be built on their target OS.' }
}
$native = ($IsLinux -and $Runtime -eq 'linux-x64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') -or
    ($IsMacOS -and (($Runtime -eq 'osx-arm64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') -or
    ($Runtime -eq 'osx-x64' -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64')))
if (-not $native -and -not $SkipTests) { throw 'Cross-publishing requires -SkipTests. Use native CI to create verified release archives.' }
$project = Join-Path $PSScriptRoot 'src-cross/SoulsRevive.Cross.csproj'
$output = Join-Path $PSScriptRoot 'bin'
$label = $Runtime.Replace('osx-', 'macos-')
$name = "soul-revive-$label"
# A fresh directory prevents old binaries from leaking into a package. Never delete
# an existing staging tree (it may contain a running development build).
$stage = Join-Path $output ('staging/' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage $name
$app = Join-Path $package 'SoulRevive.app'
$publish = if ($Runtime.StartsWith('osx-')) { Join-Path $app 'Contents/MacOS' } else { $package }
New-Item -ItemType Directory -Path $publish -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Runtime." }
$binary = Join-Path $publish 'SoulsRevive'
$library = Join-Path $publish $(if ($Runtime -eq 'linux-x64') { 'libraylib.so' } else { 'libraylib.dylib' })
if ($native -and $Runtime.StartsWith('osx-')) {
    & (Join-Path $PSScriptRoot 'packaging/macos/build-raylib.ps1') -BuildDirectory (Join-Path $stage 'native') -Destination $library -Runtime $Runtime
}
foreach ($required in @($binary, $library)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing required payload: $required" }
}
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.AssemblyVersion
$version = ([version]$version).ToString(3)
$expected = [regex]::Match((Get-Content (Join-Path $PSScriptRoot 'src/Experience.cs') -Raw), 'AssemblyVersion\("([\d.]+)"\)').Groups[1].Value
if ($version -ne ([version]$expected).ToString(3)) { throw 'Windows and portable versions differ.' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") { throw 'Release tag does not match assembly version.' }
Copy-Item (Join-Path $PSScriptRoot 'assets/fonts/VT323-OFL.txt') (Join-Path $package 'FONT-LICENSE.txt')
if ($Runtime.StartsWith('osx-')) {
    $resources = Join-Path $app 'Contents/Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Soul Revive</string>
<key>CFBundleDisplayName</key><string>Soul Revive</string>
<key>CFBundleIdentifier</key><string>com.soulsrevive.companion</string>
<key>CFBundleVersion</key><string>$version</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleExecutable</key><string>SoulsRevive</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>LSMinimumSystemVersion</key><string>15.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
    [IO.File]::WriteAllText((Join-Path $app 'Contents/Info.plist'), $plist)
    Copy-Item (Join-Path $PSScriptRoot 'assets/app-icon.png') (Join-Path $resources 'app-icon.png')
    if ($native) {
        $iconset = Join-Path $stage 'AppIcon.iconset'
        New-Item -ItemType Directory -Path $iconset | Out-Null
        foreach ($size in @(16,32,128,256,512)) {
            foreach ($scale in @(1,2)) {
                $pixels = $size * $scale
                $suffix = if ($scale -eq 2) { '@2x' } else { '' }
                & sips -z $pixels $pixels (Join-Path $resources 'app-icon.png') --out (Join-Path $iconset "icon_${size}x${size}$suffix.png") | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Icon resize failed.' }
            }
        }
        & iconutil -c icns $iconset -o (Join-Path $resources 'AppIcon.icns')
        if ($LASTEXITCODE -ne 0) { throw 'macOS icon conversion failed.' }
        & plutil -lint (Join-Path $app 'Contents/Info.plist')
        if ($LASTEXITCODE -ne 0) { throw 'Invalid Info.plist.' }
        & chmod +x $binary
        if ($LASTEXITCODE -ne 0) { throw 'Cannot set executable permissions.' }
        # Ad-hoc signatures are needed for local Apple Silicon execution. They are
        # not Developer ID signatures and do not imply Apple notarization.
        Get-ChildItem -LiteralPath $publish -Filter '*.dylib' | ForEach-Object {
            & codesign --force --sign - $_.FullName
            if ($LASTEXITCODE -ne 0) { throw "Signing failed: $($_.Name)" }
        }
        & codesign --force --deep --sign - $app
        if ($LASTEXITCODE -ne 0) { throw 'App bundle signing failed.' }
        & codesign --verify --deep --strict $app
        if ($LASTEXITCODE -ne 0) { throw 'App signature verification failed.' }
    }
    [IO.File]::WriteAllText((Join-Path $package 'README.txt'), "Move SoulRevive.app to Applications and open it. This build is ad-hoc signed, not notarized. If macOS blocks it, use System Settings > Privacy & Security > Open Anyway after verifying the download source. To run in Terminal: SoulRevive.app/Contents/MacOS/SoulsRevive")
}
else {
    Copy-Item (Join-Path $PSScriptRoot 'assets/app-icon.png') (Join-Path $package 'soulrevive.png')
    Copy-Item (Join-Path $PSScriptRoot 'packaging/linux/install.sh') (Join-Path $package 'install.sh')
    [IO.File]::WriteAllText((Join-Path $package 'README.txt'), "Run ./SoulsRevive, or ./install.sh for a per-user desktop launcher. Keep the entire extracted directory together. Requires a glibc x64 desktop with OpenGL 3.3, X11/XWayland, and audio libraries. See the repository README for distribution packages.")
    if ($native) {
        & chmod +x $binary (Join-Path $package 'install.sh')
        if ($LASTEXITCODE -ne 0) { throw 'Cannot set executable permissions.' }
        $dependencies = & ldd $library 2>&1
        if ($LASTEXITCODE -ne 0 -or ($dependencies -match 'not found')) { throw "Missing native dependencies: $dependencies" }
    }
}
if (-not $native -or $SkipTests) {
    Write-Warning "Unverified publish only: $publish. No release archive created; native icon/signing/permissions and smoke tests are required."
    return
}
if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'verify-cross.ps1') -Executable $binary -OutputDirectory (Join-Path $PSScriptRoot "test-output/$label")
}
$archive = Join-Path $output "$name.tar.gz"
& tar -czf $archive -C $stage $name
if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed.' }
# Test the extracted payload, not only the publish directory.
$extracted = Join-Path $stage 'extracted'
New-Item -ItemType Directory -Path $extracted | Out-Null
& tar -xzf $archive -C $extracted
if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
$relativeBinary = if ($Runtime.StartsWith('osx-')) { 'SoulRevive.app/Contents/MacOS/SoulsRevive' } else { 'SoulsRevive' }
if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'verify-cross.ps1') -Executable (Join-Path (Join-Path $extracted $name) $relativeBinary) -OutputDirectory (Join-Path $PSScriptRoot "test-output/$label-archive")
}
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$hash  $name.tar.gz`n")
Write-Output "Verified package: $archive"

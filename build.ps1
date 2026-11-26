$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot 'bin'
$project = Join-Path $projectRoot 'src/SoulsRevive.Windows.csproj'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# The Windows app targets .NET Framework 4.8 (built into Windows) and is compiled with the .NET SDK
# selected by global.json. Users still get one self-contained .exe and need no runtime install.
function Build-Flavor([string]$Flavor, [string[]]$Extra = @()) {
    & dotnet build $project -c Release -nologo "-p:Flavor=$Flavor" -o $outputDir @Extra
    if ($LASTEXITCODE -ne 0) { throw "$Flavor build failed." }
}

# Optional Authenticode signing. Set SOULREVIVE_SIGN_PFX (base64 .pfx) and SOULREVIVE_SIGN_PASSWORD,
# e.g. from GitHub secrets. Without them the build is unsigned, exactly as before.
function Sign-File([string]$File) {
    if (-not $env:SOULREVIVE_SIGN_PFX) { return }
    $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK to sign releases.' }
    $timestamp = if ($env:SOULREVIVE_SIGN_TIMESTAMP) { $env:SOULREVIVE_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
    $pfx = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N') + '.pfx')
    try {
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SOULREVIVE_SIGN_PFX))
        & $signtool.FullName sign /fd SHA256 /td SHA256 /tr $timestamp /f $pfx /p $env:SOULREVIVE_SIGN_PASSWORD /d 'Soul Revive' $File
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $File" }
        & $signtool.FullName verify /pa /q $File
        if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $File" }
    }
    finally { Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue }
}

# Generate the multi-resolution bonfire icon from the same artwork used by the tray.
Build-Flavor 'Artwork'
$artworkExe = Join-Path $outputDir 'SoulRevive.Artwork.exe'
$artwork = Start-Process -FilePath $artworkExe -ArgumentList @('--export-assets', ('"' + (Join-Path $projectRoot 'assets') + '"')) -Wait -PassThru -WindowStyle Hidden
if ($artwork.ExitCode -ne 0) { throw 'Artwork export failed.' }
Remove-Item -LiteralPath $artworkExe

Build-Flavor 'App'
$app = Join-Path $outputDir 'SoulsRevive.exe'
# Sign before embedding, so the installed copy carries the signature too.
Sign-File $app

Build-Flavor 'Setup' @("-p:SetupPayload=$app")
$setup = Join-Path $outputDir 'SoulRevive-Setup.exe'
Sign-File $setup

$hashes = foreach ($file in @($app, $setup)) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $outputDir 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Output "App: $app"
Write-Output "Setup: $setup"

$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot 'bin\SoulRevive-Setup.exe'
if (-not (Test-Path -LiteralPath $setup)) { & (Join-Path $PSScriptRoot 'build.ps1') }
# This is the interactive installer explicitly requested by the user running this script.
Start-Process -FilePath $setup -WindowStyle Normal

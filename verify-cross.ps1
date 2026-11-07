param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'test-output/portable')
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $Executable --self-test $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Portable regression checks failed.' }
$profile = Join-Path $OutputDirectory ('ui-profile-' + [Guid]::NewGuid().ToString('N'))
$image = Join-Path $OutputDirectory 'window.png'
if ($IsLinux -and -not $env:DISPLAY) {
    & xvfb-run -a -s '-screen 0 1280x1024x24' $Executable --profile $profile --smoke-test $image
}
else {
    & $Executable --profile $profile --smoke-test $image
}
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $image)) { throw 'Native window/font/render smoke test failed.' }
Write-Output "PASS portable UI: $image"

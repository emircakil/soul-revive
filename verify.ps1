$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$output = Join-Path $projectRoot 'test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$app = Join-Path $projectRoot 'bin\SoulsRevive.exe'
$setup = Join-Path $projectRoot 'bin\SoulRevive-Setup.exe'
$tests = Start-Process -FilePath $app -ArgumentList @('--self-test', ('"' + $output + '"')) -Wait -PassThru -WindowStyle Hidden
if ($tests.ExitCode -ne 0) { Get-Content (Join-Path $output 'failure.txt'); throw 'Application self-tests failed.' }
Get-Content (Join-Path $output 'results.txt') | Select-Object -Last 1

# Extract and upgrade in an isolated directory without touching the real installation or registry.
$extracted = Join-Path $output ('setup-' + [Guid]::NewGuid().ToString('N'))
$extract = Start-Process -FilePath $setup -ArgumentList @('--extract', ('"' + $extracted + '"')) -Wait -PassThru -WindowStyle Hidden
if ($extract.ExitCode -ne 0) { throw 'Installer extraction failed.' }
$expected = (Get-FileHash -LiteralPath $app).Hash
if ((Get-FileHash -LiteralPath (Join-Path $extracted 'SoulsRevive.exe')).Hash -ne $expected) { throw 'Installer payload differs from app.' }
[IO.File]::WriteAllText((Join-Path $extracted 'settings.json'), '{"TotalWorkSeconds":9876}')
$extract = Start-Process -FilePath $setup -ArgumentList @('--extract', ('"' + $extracted + '"')) -Wait -PassThru -WindowStyle Hidden
if ($extract.ExitCode -ne 0 -or [IO.File]::ReadAllText((Join-Path $extracted 'settings.json')) -ne '{"TotalWorkSeconds":9876}') { throw 'Installer upgrade did not preserve user data.' }
Write-Output 'PASS installer extraction, payload hash and upgrade data preservation'

# Exercise the real updater against two small fixture programs, including restart.
$fixtureDir = Join-Path $output ('updater-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDir -Force | Out-Null
$target = Join-Path $fixtureDir 'Fixture.exe'
$payload = Join-Path $fixtureDir 'Replacement.exe'
$helper = Join-Path $fixtureDir 'Updater.exe'
$beforeSource = Join-Path $fixtureDir 'Before.cs'
$afterSource = Join-Path $fixtureDir 'After.cs'
@'
using System; using System.IO; using System.Reflection; using System.Threading;
class Fixture { static void Main() { string d=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); File.WriteAllText(Path.Combine(d,"started"),"ready"); for(int i=0;i<600&&!File.Exists(Path.Combine(d,"release"));i++)Thread.Sleep(50); } }
'@ | Set-Content -LiteralPath $beforeSource -Encoding utf8
@'
using System; using System.IO; using System.Reflection;
class Fixture { static void Main() { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"restarted"),"updated"); } }
'@ | Set-Content -LiteralPath $afterSource -Encoding utf8
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /out:$target $beforeSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
& $compiler /nologo /target:winexe /out:$payload $afterSource
if ($LASTEXITCODE -ne 0) { throw 'Replacement build failed.' }
Copy-Item -LiteralPath $app -Destination $helper
$oldHash = (Get-FileHash -LiteralPath $target).Hash
$newHash = (Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant()
$hostProcess = $null
$helperProcess = $null
$readyName = 'Local\SoulsRevive.Verify.' + [Guid]::NewGuid().ToString('N')
$ready = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $readyName)
try {
    $hostProcess = Start-Process -FilePath $target -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath (Join-Path $fixtureDir 'started'))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Fixture did not start.' }
        Start-Sleep -Milliseconds 100
    }
    $helperProcess = Start-Process -FilePath $helper -ArgumentList @('--apply-update', $hostProcess.Id, ('"' + $target + '"'), ('"' + $payload + '"'), $newHash, ('"' + $readyName + '"')) -PassThru -WindowStyle Hidden
    if (-not $ready.WaitOne(10000)) { throw 'Updater readiness handshake failed.' }
    [IO.File]::WriteAllText((Join-Path $fixtureDir 'release'), 'exit')
    if (-not $helperProcess.WaitForExit(15000) -or $helperProcess.ExitCode -ne 0) { throw 'Updater helper failed.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath (Join-Path $fixtureDir 'restarted'))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Updated app did not restart.' }
        Start-Sleep -Milliseconds 100
    }
    if ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -ne $newHash) { throw 'Updated executable hash mismatch.' }
    if ((Get-FileHash -LiteralPath ($target + '.update-backup')).Hash -ne $oldHash) { throw 'Rollback copy was not preserved.' }
    Write-Output 'PASS updater handshake, parent exit, atomic replacement, rollback copy and restart'
} finally {
    $ready.Dispose()
    foreach ($process in @($hostProcess, $helperProcess)) { if ($null -ne $process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() } }
}
Write-Output 'All verification passed.'

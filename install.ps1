$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'bin\SoulsRevive.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$installDir = Join-Path $env:LOCALAPPDATA 'SoulsRevive'
$installedExe = Join-Path $installDir 'SoulsRevive.exe'
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$updating = Test-Path -LiteralPath $installedExe
foreach ($app in @(Get-Process -Name SoulsRevive -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExe })) {
    $app.Kill()
    $app.WaitForExit()
}
Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
if (-not $updating) {
$startupProcess = Start-Process -FilePath $installedExe -ArgumentList '--enable-startup' -Wait -PassThru -WindowStyle Hidden
if ($startupProcess.ExitCode -ne 0) { throw 'Windows başlangıç kaydı oluşturulamadı.' }
$expectedCommand = '"' + $installedExe + '" --startup'
$actualCommand = Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SoulsRevive
if ($actualCommand -ne $expectedCommand) { throw 'Windows başlangıç kaydı doğrulanamadı.' }
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Soul Revive.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = 'Piksel şövalyeli hareket molası sayacı'
$shortcut.IconLocation = "$installedExe,0"
$shortcut.Save()
$oldShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Souls Revive.lnk'
if (Test-Path -LiteralPath $oldShortcut) {
    $oldTarget = (New-Object -ComObject WScript.Shell).CreateShortcut($oldShortcut).TargetPath
    if ($oldTarget -eq $installedExe) { Remove-Item -LiteralPath $oldShortcut }
}
# This is the interactive companion window the user requested to see.
Start-Process -FilePath $installedExe -WindowStyle Normal
Write-Output "Kuruldu: $installedExe"
Write-Output "Masaüstü kısayolu: $shortcutPath"


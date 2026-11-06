# Soul Revive 1.1 — Pixel Bonfire Desktop Companion

A small Windows work and break companion with an original pixel knight, a bonfire, a changing sky, and an offline soundscape. Available in Türkçe and English.

## Install

Download **SoulRevive-Setup.exe** from [Releases](https://github.com/emircakil/soul-revive/releases), open it, and select **Kur ve başlat / Install and launch**. No Git, PowerShell commands, development tools, or administrator privileges are needed by users. Setup offers desktop and Windows startup shortcuts and adds the app to Windows Installed Apps.

Setup installs to `%LOCALAPPDATA%\SoulsRevive`. Upgrades keep settings, statistics, and the existing startup preference. For portable use, run **SoulsRevive.exe** directly.

## Your bonfire

- **Çalışıyorum / Working:** every work round begins at dawn, moves through daylight and sunset, and ends after dark. The default round is 30 minutes, followed by a 3-minute break. Choose work durations of 1–180 minutes and breaks of 1–30 minutes in Settings.
- **Mola ver / Take a break:** your knight lies down beside the fire and sleeps, with breathing and floating Z animations. Stand up, walk, or stretch while the timer runs. Completing a break restores health and starts a new dawn. Returning early starts a fresh work round without awarding a completed break.
- **Çalışmıyorum / Chill mode:** the scene follows your computer's local time. The knight alternates between wandering and resting by the fire. The work timer and work statistics stop. Returning to Working starts a new round at dawn.
- **Duraklat / Pause:** freezes the timer and work statistics. Screen lock, sleep, or a long timer gap also pauses work. Resume manually when you return.
- Drag the window to move it. The title-bar **=** opens Settings. **_ / X** hide it to the tray; the timer continues. Double-click the tray bonfire to reveal it, or right-click and choose **Çıkış / Quit** to exit.

## Settings and statistics

**Genel / General:** choose Türkçe or English, Dark mode or Lit mode, timer durations, always-on-top, reminder sounds, and Windows startup. The pixel font is embedded, including Turkish characters; no font installation is needed. Changing durations starts a new round. Other settings preserve your timer.

**Sesler / Sounds:** enable the soundscape and set independent **Fire**, **Ambience**, and **Music** volumes from 0–100%. Fire crackle, wind, and the bonfire melody mix together. Preview and stop audio before saving. All layers are synthesized locally and work offline. Reminder sounds have their own checkbox. Soundscape playback starts disabled until enabled here.

**İstatistikler / Statistics:** shows lifetime work hours, today's work hours, completed work rounds, and today's completed breaks and movement minutes. Only active timer seconds count as work; pauses, breaks, chill, time after expiry, and time while the app is closed are excluded. Lifetime tracking starts with version 1.1; older versions did not record work hours.

Data is saved locally every 15 seconds and on state changes or exit to `%LOCALAPPDATA%\SoulsRevive\settings.json`, with an atomic backup. The app uses no camera or movement detection. Uninstall through Windows Installed Apps; settings and work history are retained for reinstalling.

## In-app updates

Open **Ayarlar > Güncelleme / Settings > Updates**, check for updates, then select **İndir ve güncelle / Download and update** and approve the restart. The app fetches the latest stable GitHub Release, verifies its SHA-256 digest, file size, and assembly version, saves your session, atomically replaces the executable, keeps a rollback copy, and restarts. It never fetches source or runs a build on the user's machine. Failed downloads leave the running version intact.

The release must contain an asset named **SoulsRevive.exe** with a GitHub-provided SHA-256 digest. Drafts, prereleases, and older versions are excluded. Offline, rate-limited, missing-release, and incomplete-release states are displayed in the panel. Until the first GitHub Release is published, the app reports that no release is available.

## Build and verify

Develop on Windows with .NET Framework 4.8 or later (included in current Windows 10/11 installations). No NuGet packages are required.

```powershell
.\build.ps1
.\verify.ps1
```

The build uses the Windows .NET Framework C# compiler. It generates a multi-resolution pixel bonfire icon, embeds the Tiny5 font, and produces:

- `bin\SoulsRevive.exe` — portable application
- `bin\SoulRevive-Setup.exe` — standalone graphical installer with embedded application
- `bin\SHA256SUMS.txt` — package checksums

`verify.ps1` checks timer transitions, work statistics, midnight rollover, legacy settings migration, backup recovery, localization, sound layers, release validation, and tamper rejection. It renders all major views in both themes and languages to `test-output`. It also verifies installer extraction and upgrades in an isolated folder and runs the real update helper against disposable fixture executables to test its readiness handshake, replacement, rollback copy, and restart. Your installed app, registry, and real statistics are untouched by these checks.

To launch setup from a source checkout, run `./install.ps1`. For an isolated UI development profile, run `bin\SoulsRevive.exe --profile "C:\full\path\profile"`.

## Publish a release

1. Update `AssemblyVersion` and `AssemblyFileVersion` in `src/Experience.cs`.
2. Run build and verification, commit changes, then push a matching tag such as `v1.1.0`.
3. `.github/workflows/release.yml` builds, verifies, checks the tag against the embedded version, and publishes the portable app, installer, and checksums to GitHub Releases. Future users can update entirely inside the app.

Regular CI also builds and verifies all packages and uploads UI previews. Release publishing is a maintainer action; building locally does not publish anything.

## Artwork and font

The scene, knight poses, UI ornamentation, bonfire icon, and synthesized audio are original. The embedded [Tiny5](https://github.com/google/fonts/tree/f265cc2d8e08067dac782ba633458b97661ab85d/ofl/tiny5) font by Stefan Schmidt is distributed under the SIL Open Font License 1.1; its license and source reference are in `assets/fonts` and embedded in both executables.

This is an independent fan project with no official Dark Souls assets or recordings.

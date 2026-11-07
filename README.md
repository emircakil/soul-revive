# Soul Revive 1.3 — Pixel Bonfire Desktop Companion

A small Windows, macOS, and Linux work and break companion with an original pixel knight, a bonfire, a changing sky, a cozy tavern, and an offline soundscape. Available in Türkçe and English.

## Animation previews

### Dawn to night — accelerated

A work round's dawn, daylight, sunset, and night compressed into a **22-second loop**. In the app, the sky follows your chosen work duration.

![Accelerated dawn-to-night cycle with the knight and bonfire](assets/readme/day-night-cycle.gif)

### Chill mode — a toast with Solaire

The knight enters the tavern, joins Solaire, and shares a toast. Both characters take turns sipping in this **20-second loop**.

![The knight arrives at the tavern, toasts with Solaire, and shares a drink](assets/readme/solaire-chill.gif)

These GIFs are rendered from the application's current pixel animation code. Run `./export-readme-gifs.ps1` to regenerate them after animation changes.

## Install

Releases are available for Windows, macOS, and Linux on [Releases](https://github.com/emircakil/soul-revive/releases):

### Windows
Download **SoulRevive-Setup.exe**, open it, and select **Kur ve başlat / Install and launch**. No Git, PowerShell commands, development tools, or administrator privileges are needed by users. Setup offers desktop and Windows startup shortcuts and adds the app to Windows Installed Apps. Setup installs to `%LOCALAPPDATA%\SoulsRevive`. For portable use, run **SoulsRevive.exe** directly.

### Linux (x64)
Download **soul-revive-linux-x64.tar.gz**, extract it, and run the executable inside the extracted folder:

```bash
tar -xzf soul-revive-linux-x64.tar.gz
cd soul-revive-linux-x64
./SoulsRevive
# Optional: install for this user and add an application-menu entry.
./install.sh
```

Keep the entire directory together: the self-contained .NET runtime and native libraries ship alongside the executable. No .NET installation is required. The installer copies the full payload to `$XDG_DATA_HOME/soul-revive/app` (default `~/.local/share/soul-revive/app`) and creates a launcher with an absolute path, including support for spaces in the home directory.

The Linux build targets a **glibc 2.38+ x64 desktop**, with OpenGL 3.3 and X11 (or XWayland on a Wayland desktop). Ubuntu 24.04 is the CI baseline; the packaged Raylib library requires newer glibc than Ubuntu 22.04 provides. Alpine/musl, Linux ARM, and native Wayland are not release targets. On Ubuntu 24.04, the graphical/audio libraries can be installed with:

```bash
sudo apt-get install libgl1 libx11-6 libxcursor1 libxrandr2 libxi6 libxinerama1 libasound2t64 libpulse0
```

Settings and statistics live in `$XDG_DATA_HOME/SoulsRevive`, defaulting to `~/.local/share/SoulsRevive`. Installing or replacing the application does not replace that profile.

### macOS (Apple Silicon & Intel)
Requires **macOS 15 or later**.

- **Apple Silicon:** download **soul-revive-macos-arm64.tar.gz**.
- **Intel:** download **soul-revive-macos-x64.tar.gz**.

Extract the archive, move **SoulRevive.app** to Applications, and open it. The bundle includes the runtime, native libraries, a Retina icon, and an ad-hoc signature. It is **not Developer ID signed or notarized**. If macOS blocks the downloaded app, verify its source/checksum and use **System Settings > Privacy & Security > Open Anyway**. Terminal launch uses `SoulRevive.app/Contents/MacOS/SoulsRevive`; there is no second standalone executable outside the bundle.

Settings and statistics are saved to `~/Library/Application Support/SoulsRevive`.

### Platform behavior
The timer, statistics, equipment and original knight/tavern animations share the same source on all platforms. The portable desktop shell uses Raylib, with a managed pixel rasterizer instead of Windows GDI+.

| Behavior | Windows | macOS / Linux |
| --- | --- | --- |
| Work/break/chill, statistics, themes, Turkish/English, equipment | Yes | Yes |
| Offline soundscape and reminder audio | Yes | Yes; gracefully disabled without an audio device |
| Developer animation panel | Separate 4x preview window | Embedded preview, frame stepping, timeline and 0.5x/1x/2x playback |
| Settings | Save/Cancel | Save/Cancel; Escape cancels |
| Minimize / close | Hide to tray | Minimize to Dock/taskbar / save and quit |
| Move window | Drag app | Drag native title bar |
| Always on top | Yes | Requested from window manager; compositor rules may override |
| Automatic pause | Screen lock, suspend, long timer gaps | Suspend/clock jumps or gaps over 5 seconds; pause manually before locking the screen |
| Startup integration / in-app binary updater | Yes | Use OS login items / replace the downloaded application |

The Windows-specific tray, startup and updater instructions below apply to Windows. On macOS/Linux, quitting stops the timer; minimizing keeps it running. Audio playback changes apply on Save.

## Your bonfire

- **Çalışıyorum / Working:** every work round begins at dawn, moves through daylight and sunset, and ends after dark. The default round is 30 minutes, followed by a 3-minute break. Choose work durations of 1–180 minutes and breaks of 1–30 minutes in Settings.
- During active work, the knight randomly sharpens his sword, holds a map still and scans it with his head, or walks toward the bonfire to toss a log. The shield is quickly stowed before handling props; tiny sparks scatter at the sharpening contact point, sharpening stops before the sword rises, and tossed logs follow an independent arc. Each routine lasts 14 seconds, with 10–20 seconds of quiet idle between routines. Pausing freezes the routine; break and mode changes reset it. Sword grips and props share the hands’ pose coordinates.
- **Mola ver / Take a break:** your knight lies down beside the fire and sleeps, with breathing and floating Z animations. Stand up, walk, or stretch while the timer runs. Completing a break restores health and starts a new dawn. Returning early starts a fresh work round without awarding a completed break.
- **Çalışmıyorum / Chill mode:** your knight enters a cozy pixel tavern and sits down with Solaire of Astora. They clink tankards, take turns sipping, and relax by the hearth. The tavern keeps its warm evening lighting regardless of the computer's time; a **Şerefe! / Cheers!** greeting replaces the clock. The work timer and work statistics stop. Returning to Working starts a new round at dawn.
- **Duraklat / Pause:** freezes the timer and work statistics. Sleep or a timer gap over five seconds also pauses work; Windows additionally pauses on screen lock. Resume manually when you return.
- Open Settings with the title-bar gear button. On Windows, **_ / X** hide the app to the tray; the timer continues. Double-click the tray bonfire to reveal it, or right-click and choose **Çıkış / Quit** to exit. On macOS/Linux, minimize keeps running and close saves and quits.

## Settings and statistics

**Genel / General:** choose Türkçe or English, Dark mode or Lit mode, timer durations, always-on-top, reminder sounds, and Windows startup. The pixel font is embedded, including Turkish characters; no font installation is needed. Changing durations starts a new round. Other settings preserve your timer.

**Sesler / Sounds:** enable **Müzik ve ortam seslerini çal / Play music and ambience**, set independent **Fire**, **Ambience**, and **Music** volumes from 0–100%, then select Save for continuous background playback. Fire crackle, wind, and the bonfire melody mix together. Windows additionally offers separate preview/stop playback before saving. Reminder sounds use independent playback, and saved audio preferences apply again at launch. All layers are synthesized locally and work offline. Reminder sounds have their own checkbox. Soundscape playback starts disabled until enabled here.

**İstatistikler / Statistics:** shows lifetime work hours, today's work hours, completed work rounds, and today's completed breaks and movement minutes. The main window displays today's and lifetime work in hours, minutes and seconds; the statistics dialog refreshes while open. Today's total rolls over at midnight, and lifetime hours keep accumulating across restarts and updates. Only active timer seconds count as work; pauses, breaks, chill, time after expiry, and time while the app is closed are excluded. Lifetime tracking starts with version 1.1; older versions did not record work hours.

Data is saved locally every 5 seconds and on state changes, closing the window or exit to `%LOCALAPPDATA%\SoulsRevive\settings.json`. Work totals are also stored independently in `statistics.json`. Both files use atomic backups, and older settings automatically migrate existing hours. Statistics recover independently if settings become damaged. In-app updates retain the active profile and its work history. The app uses no camera or movement detection. Uninstall through Windows Installed Apps; settings and work history are retained for reinstalling.

## Knight equipment

Open **Settings > Knight** to choose armor (Steel knight, Black knight, Golden guardian), sword (Longsword, Broadsword, Estoc), and shield (Cross shield, Round shield, Blue guardian). Each choice is independent and has an immediate equipment preview. Save equips and remembers your choices; Cancel preserves your current gear. Armor carries into work, rest, and tavern scenes, and weapons follow the existing hand and shield-stowing animation anchors. The developer panel uses the equipped appearance.

## Developer animation panel

Press **F8** in the main window, or open **Settings > General > Developer panel**. Select idle, sharpening, map reading, log throwing, sleep, or the tavern. The panel offers a 4x pixel preview, play/pause, previous/next frame, a draggable timeline, restart, 0.5x/1x/2x speed, and day/dawn/night lighting (the tavern retains its own lighting). Escape closes the panel. Preview controls use independent state and do not change your work mode, timer, or statistics.

Sharpening emits a few single-pixel sparks at the blade contact point. During map reading, both hands hold the paper still while the helmet scans left and right.

## In-app updates

Open **Ayarlar > Güncelleme / Settings > Updates**, check for updates, then select **İndir ve güncelle / Download and update** and approve the restart. The app fetches the latest stable GitHub Release, verifies its SHA-256 digest, file size, and assembly version, saves your session, atomically replaces the executable, keeps a rollback copy, and restarts. It never fetches source or runs a build on the user's machine. Failed downloads leave the running version intact.

The release must contain an asset named **SoulsRevive.exe** with a GitHub-provided SHA-256 digest. Drafts, prereleases, and older versions are excluded. Offline, rate-limited, missing-release, and incomplete-release states are displayed in the panel. Until the first GitHub Release is published, the app reports that no release is available.

## Build and verify

The Windows native app uses .NET Framework 4.8 and its existing compiler:

```powershell
./build.ps1
./verify.ps1
```

The portable app uses **.NET 10 SDK**, PowerShell 7 and Raylib-cs 8.1.0. The SDK is selected by `global.json`. End users do not need the SDK or runtime. Build release packages on the matching OS and architecture:

```powershell
# Linux x64
pwsh ./build-cross.ps1 -Runtime linux-x64
# Apple Silicon Mac
pwsh ./build-cross.ps1 -Runtime osx-arm64
# Intel Mac
pwsh ./build-cross.ps1 -Runtime osx-x64
```

On headless Linux, install `xvfb` and `xauth` as well as the graphical libraries listed above. The verification script automatically uses Xvfb when DISPLAY is unset.

Each native build verifies its payload, runs regression checks and a real window/font/render test, creates the archive with executable permissions, extracts it and tests the extracted application again. macOS additionally validates the plist and bundle signature. A missing native library or failed command aborts the build; libraries are resolved from the pinned NuGet dependency, never from an arbitrary cache search.

For development without packaging:

```powershell
dotnet run --project src-cross/SoulsRevive.Cross.csproj -- --profile /absolute/path/to/dev-profile
dotnet run --project src-cross/SoulsRevive.Cross.csproj -- --self-test /absolute/path/to/test-output
```

Windows developers can cross-publish with `./build-cross.ps1 -Runtime linux-x64 -SkipTests` (or either macOS RID). This creates an **unverified staging directory only**, not a release archive. Native signing, permissions and UI execution still require the target system.

Outputs:

- `bin/SoulsRevive.exe` and `bin/SoulRevive-Setup.exe` — Windows application/installer.
- `bin/soul-revive-linux-x64.tar.gz` — Linux payload and per-user installer.
- `bin/soul-revive-macos-arm64.tar.gz`, `bin/soul-revive-macos-x64.tar.gz` — macOS app bundles.
- Each portable archive has a `.sha256` file. The release job creates one combined `SHA256SUMS.txt`.

`verify.ps1` covers Windows timers, statistics, migration, recovery, localization, sound layers, artwork, installer and updater rollback. `verify-cross.ps1` covers the shared core, fractional break accounting, expired-session reload, midnight rollover, atomic profile recovery, single-instance locking, every animation frame, equipment, sound synthesis, Turkish glyphs and settings transactions. It exports 32 UI previews across both languages/themes. Test profiles are isolated from real statistics.

## Publish a release

1. Keep the versions in `src/Experience.cs` and `src-cross/SoulsRevive.Cross.csproj` identical.
2. Build and verify, commit, then push a matching tag such as `v1.3.0`.
3. The reusable `.github/workflows/platforms.yml` builds on Windows, Ubuntu x64, macOS ARM64 and macOS Intel. Both regular CI and releases use this same verification pipeline.
4. The release job waits for **all** platforms, checks the complete asset set, generates combined checksums and only then publishes to GitHub Releases.

Building locally does not publish anything. Apple Developer ID signing/notarization is not configured.

## Artwork and font

The scene, knight poses, tavern sprites (including the fan depiction of Solaire of Astora), UI ornamentation, bonfire icon, and synthesized audio are drawn or generated for this project. The embedded [VT323](https://fonts.google.com/specimen/VT323) font by Peter Hull is distributed under the SIL Open Font License 1.1; its license and source reference are in `assets/fonts` and embedded in both executables.

This is an independent fan project with no official Dark Souls assets or recordings.

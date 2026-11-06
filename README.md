# Soul Revive — Windows Break Reminder & Pixel Art Desktop Companion

A Dark Souls-inspired Windows break reminder with an animated pixel art knight and bonfire. Customize your work and movement intervals, watch your knight's health fade as you work, and rekindle the fire with a movement break.

Built for developers, gamers, and anyone spending long sessions at a desk, Soul Revive brings a small desktop companion to your workday. It runs offline using .NET Framework 4.x and stores your settings locally.

## Features

- **Animated pixel art companion** with breathing, walking, a wind-blown cape, and a flickering bonfire.
- **Customizable break timer** with 1–180 minute work sessions and 1–30 minute movement breaks.
- **Health-based reminders** that turn your next movement break into a chance to restore your knight.
- **Windows notifications and optional sound** when a work session or break ends.
- **System tray controls**, a draggable window, and an always-on-top option.
- **Automatic startup** at Windows sign-in, configurable in Settings.
- **Pause on screen lock or sleep**, with manual resume when you return.
- **English and Turkish localization**, selectable in Settings.
- **Daily break statistics** and locally saved timer progress.

## Quick start

On Windows, open PowerShell in the project folder and run

```powershell
.\build.ps1
.\install.ps1
```

Launch **Soul Revive** from the desktop shortcut. Open the gear menu to choose **English** or **Türkçe** and set your preferred intervals.

The build script uses the .NET Framework C# compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Installation runs under your Windows user account.

## Usage

- After installation, launch the app using the **Soul Revive** desktop shortcut. It also starts automatically when you sign in to Windows.
- The default cycle is **30 minutes of work / 3 minutes of movement**. The knight's health decreases while you work. When the timer expires, the app displays a notification and opens its window.
- Select **Take a break** to start your break, then stand up and take a short walk. The timer tracks the break duration. Movement and posture are not detected, and the app does not use a camera.
- Completing a break restores health and starts a new work cycle. Selecting **Return early** immediately starts a full work cycle using your configured duration. Only completed breaks count toward your daily break total.
- Use **Pause / Resume** to control the timer. Locking the screen or putting the computer to sleep pauses it. Select **Resume** when you return.
- Drag an empty part of the window to move it. Use the gear button to adjust durations, sound, always-on-top behavior, and Windows startup. Changing durations starts a new cycle.
- Select **− / ×** to hide the window in the system tray while the timer continues running. Double-click the knight icon in the tray to bring it back. To close the app completely, right-click the tray icon and select **Quit**.
- Daily break counts and movement time are stored locally. Settings and the latest saved timer state are stored in `%LOCALAPPDATA%\SoulsRevive\settings.json`. Movement time represents time spent with the break timer running.

## Installation and build

In PowerShell, run `./build.ps1`, followed by `./install.ps1`. The installer copies the executable to `%LOCALAPPDATA%\SoulsRevive`, creates a desktop shortcut, and enables automatic startup for the current user through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SoulsRevive`. Updating an existing installation preserves its startup setting. Administrator privileges are not required.

For portable use, run `bin\SoulsRevive.exe`. Enable automatic startup in Settings. If you move the executable afterward, enable the startup setting again to update its location.

To disable automatic startup, clear **Start at Windows sign-in** in Settings. To uninstall, first disable automatic startup, quit the app through its tray menu, then delete the `%LOCALAPPDATA%\SoulsRevive` folder and desktop shortcut.

## Verification

Run `bin\SoulsRevive.exe --self-test "C:\full\path\test-output"` to check timer transitions, notification counts, pausing, sleep gaps, break completion and early return, duration changes, daily resets, and settings persistence. The command generates `results.txt`, a window preview, and pixel art images.

This is an independent fan project. All pixel artwork is original, with no official Dark Souls assets or artwork taken from the game.

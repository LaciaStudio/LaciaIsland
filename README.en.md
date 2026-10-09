<p align="center">
  <img src="docs/poster-hero.jpg" alt="Lacia Island — Dynamic Island and notch for Windows" width="100%">
</p>

<h1 align="center">Lacia Island</h1>

<p align="center">
  Dynamic Island and notch for Windows: a pure black island at the top of the screen with jelly-like, iPhone-style animation.<br>
  Music, calls, timers, volume, keyboard layout, charging and headphones — all in one place.
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/badge/Download-LaciaIsland.exe-111111?style=for-the-badge" alt="Download LaciaIsland.exe"></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/Avalonia-11.3-8B44AC" alt="Avalonia 11.3">
  <img src="https://img.shields.io/badge/license-MIT-2EA043" alt="MIT">
</p>

<p align="center"><a href="README.md">Русский</a> · <b>English</b></p>

---

> The app itself speaks Russian for now (settings, tray menu, labels).

## Features

- **Music** from any player that shows up in the Windows media controls: Spotify, Yandex Music, Apple Music, YouTube and VK in the browser. Cover art, bars that follow the real sound, seeking, play/pause and track switching. The cover updates with every track.
- **Calls.** When an app uses the microphone (Discord, Telegram, Zoom, Teams…), the island shows the call time and your voice level. Mute the mic right from the island; the button on the right brings the app to the front.
- **Timer and stopwatch** from the quick panel or the tray menu; pause and cancel right on the island. When time is up, the island chimes and says “Готово” (done).
- **Two activities at once.** With music playing and a timer running, a bubble splits off the island: the bridge stretches, snaps and springs back, just like on an iPhone. Swipe to swap them, click the bubble to open it. In notch mode the second activity is shown as an icon on the right inside the notch.
- **Indicators:** volume (scroll the mouse wheel right over the island), RU/EN keyboard layout, laptop charging and low battery, headphones connecting with their battery level.
- **Notch mode.** The island sticks to the top edge of the screen with soft “shoulders”. One click to switch.
- **Size and length** change live: sliders in the settings, and Ctrl + mouse wheel for the length.
- **Stays out of the way:** clicks next to the island go to the windows below, and it hides in fullscreen games and videos. Autostart and monitor selection included.

## In motion

<p align="center"><img src="docs/demo-expand.gif" alt="Player expands on click" width="760"></p>
<p align="center"><sub>Click — the player expands and collapses back</sub></p>

<p align="center"><img src="docs/demo-split.gif" alt="A second activity splits off as a bubble" width="760"></p>
<p align="center"><sub>Start a timer — a bubble drips off the island</sub></p>

<p align="center"><img src="docs/demo-call.gif" alt="A Discord call" width="760"></p>
<p align="center"><sub>Calls: duration, voice level, mic mute</sub></p>

<p align="center"><img src="docs/demo-notch.gif" alt="Notch mode" width="760"></p>
<p align="center"><sub>Notch mode</sub></p>

<p align="center"><img src="docs/demo-length.gif" alt="Changing the length" width="760"></p>
<p align="center"><sub>Ctrl + mouse wheel — island length</sub></p>

<p align="center"><img src="docs/demo-huds.gif" alt="Volume, layout, charging, headphones" width="760"></p>
<p align="center"><sub>Volume, keyboard layout, charging, headphones</sub></p>

## Gallery

<table>
  <tr>
    <td><img src="docs/feature-music.jpg" alt="Music from any player"></td>
    <td><img src="docs/feature-drip.jpg" alt="Two activities"></td>
  </tr>
  <tr>
    <td><img src="docs/feature-calls-timer.jpg" alt="Calls and timer"></td>
    <td><img src="docs/feature-notch.jpg" alt="Notch mode"></td>
  </tr>
  <tr>
    <td><img src="docs/feature-huds.jpg" alt="Indicators"></td>
    <td><img src="docs/feature-quick-panel.jpg" alt="Quick panel"></td>
  </tr>
</table>

## Install

1. Download `LaciaIsland.exe` from [Releases](../../releases/latest).
2. Run it. Nothing to install, no .NET needed — everything is inside one file.
3. Windows may say “Windows protected your PC” because the app has no paid code-signing certificate. Click **More info → Run anyway**.
4. To start it with Windows, turn on **«Запускать вместе с Windows»** (start with Windows) in the settings.

Requires Windows 10 (version 2004 or newer) or Windows 11, 64-bit.

**Update:** right-click the tray icon → «Выход» (exit), replace the exe and run it again.

**Uninstall:** turn autostart off, exit from the tray, delete the exe and the `%APPDATA%\LaciaIsland` and `%TEMP%\.net\LaciaIsland` folders.

## Controls

| Action | Result |
|---|---|
| Click the island | expand or collapse (player, call, timer); with no activities — the quick panel |
| Right-click | quick panel: clock, timers, stopwatch, island ↔ notch, settings |
| Middle mouse button | play / pause |
| Mouse wheel over the island | volume |
| Ctrl + mouse wheel | island length |
| Swipe left or right | swap the two activities |
| Click the bubble next to the island | open the second activity |
| Click the cover art in the expanded player | switch to the player app (Spotify, browser…) |
| Button at the bottom right of the player | Windows sound settings |
| Drag the progress bar | seek |
| Tray icon | click — settings, right-click — mode, timer, exit |

## Settings

Open them by clicking the tray icon or the gear in the quick panel. Changes are saved instantly.

- **Look:** island or notch, size, length, top offset, monitor.
- **Behavior:** expand on hover, hide when nothing is happening (move the mouse to the top edge of the screen, where the island is, to show it), hide in fullscreen apps, start with Windows.
- **Activities:** what to show (music, live visualizer, calls, volume, layout, charging, headphones, timer sound), apps that should not count as calls, quick timer presets.

Settings and the log live in `%APPDATA%\LaciaIsland\` (`settings.json`, `log.txt`); the settings have a «Папка с логами» (logs folder) button.

## FAQ

**My music doesn't show up.** The island reads tracks from the Windows media controls — the panel that appears next to the volume. If your player isn't there, Lacia Island can't see it either. Browsers (Chrome, Edge, Firefox, Yandex Browser) send music and video there on their own.

**It shows a call when I'm not in one.** Some app keeps the microphone open. NVIDIA Broadcast, Voicemeeter and Krisp are already excluded; add any other to «Не считать звонком» (not a call) in the settings — the name the island shows, or its exe name.

**The mouse wheel doesn't change the volume.** The cursor has to be right over the island, and the Windows setting “Scroll inactive windows when I hover over them” must be on (it is by default).

**My antivirus or SmartScreen complains.** The exe is unsigned. If you'd rather not trust it, build it yourself — it's one command, see below.

**The animation stutters.** Exit the island from the tray and run `LaciaIsland.exe --diag`. While something animates, `log.txt` gets a line with fps and frame times every 5 seconds. Attach it to an issue.

### Command-line options

If the island is already running, exit it from the tray first: a second instance closes right away (unless started with `--multi`).

| Option | Purpose |
|---|---|
| `--demo` | play a demo scenario with made-up data |
| `--diag` | log fps and animation start delays |
| `--avalonia-window` | fallback renderer: a regular Avalonia window (clicks don't pass through it) |
| `--multi` | allow a second instance |

## How it works

- The island is drawn in code, frame by frame, into a small transparent window (`UpdateLayeredWindow`). The window never takes focus, and clicks next to the island go to the windows below. Frames run on their own timer at the monitor's refresh rate (up to 144 Hz), independent of the system compositor, so animations start instantly.
- The “jelly” is spring physics, like `spring` in SwiftUI: width, height and corner radius are separate springs, and squash & stretch come from their velocities.
- The shape is a squircle with smooth corners. The second activity's bubble is joined to the island by a metaball bridge that stretches and snaps.
- Music comes from Global System Media Transport Controls. Volume, devices, the microphone and the visualizer bars use Core Audio through NAudio (WASAPI loopback + FFT).
- Calls are detected from Windows' own record of which app is using the microphone — the same data behind the mic icon in the tray.
- Settings window and tray — Avalonia UI.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Download the sources (**Code → Download ZIP**) or clone the repository, then run in its folder:

```
build.cmd
```

`LaciaIsland.exe` appears in the `publish` folder. On Linux, `bash build.sh` does the same.

Run without building an exe (Windows):

```
dotnet run --project src/LaciaIsland -f net10.0-windows10.0.19041.0
```

On Linux you can watch the demo without system features: `dotnet run --project src/LaciaIsland -f net10.0`.

### Project layout

```
src/LaciaIsland/
  Core/               springs, island shape, metaballs
  Model/              settings and data models
  UI/                 controller, drawing, views (music, call, timer, indicators, panel), settings window
  Services/           interfaces, timer, spectrum, demo data
  Platform/Windows/   media controls, Core Audio, mic & camera, power, keyboard layout, layered window
tools/FrameRenderer/  off-screen frame renderer — it made the GIFs and pictures in docs/
tools/icons/          icon generator
```

## License

[MIT](LICENSE). Third-party libraries and the font are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Lacia Island is an independent project and is not affiliated with Apple. Dynamic Island and iPhone are trademarks of Apple Inc.

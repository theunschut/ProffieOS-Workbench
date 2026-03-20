# ProffieOS Workbench

A web-based remote control for [ProffieOS](https://github.com/profezzorn/ProffieOS) lightsabers, connecting over **Web Bluetooth** or **WebUSB** directly from your browser.

> Forked from [profezzorn/lightsaber-web-bluetooth](https://github.com/profezzorn/lightsaber-web-bluetooth). The original single-file app is preserved in [`old/`](old/).

## Features

- Connect via **BLE** (multiple UART profiles supported) or **WebUSB**
- Reconnect to previously authorized known devices (BLE/USB)
- Browse and activate **presets**
- Play **tracks** from the SD card
- Send **control commands** (on/off, clash, blast, force, lockup, drag, melt, stab, lightning)
- **Edit presets**: name, font, track, blade styles (with color/int argument editors), variation
- **Settings**: brightness, clash threshold, blade length, gesture ignition options
- Auto-reconnect on disconnect (BLE and USB)
- Toast notifications for errors
- PWA — installable, works offline after first load

## Requirements

- A Chromium-based browser (Chrome, Edge) — required for Web Bluetooth / WebUSB
- A ProffieOS-based saber with BLE or USB connectivity

## Tech Stack

- [Blazor WebAssembly](https://learn.microsoft.com/en-us/aspnet/core/blazor/) (.NET 10)
- [MudBlazor](https://mudblazor.com/) component library (dark theme)
- Lightweight JavaScript interop over browser BLE/USB APIs, with connection resilience and diagnostics

## Running locally

```bash
dotnet run --project ProffieOS.Workbench
```

Then open the URL printed by `dotnet run` (default launch profile uses `https://localhost:7054` and `http://localhost:5135`).

## Deployment

### GitHub Pages

Merging to `master` automatically builds and deploys via GitHub Actions.

### Docker

```bash
docker compose up --build
```

Runs on port `8080`. Set `BASE_PATH` in `docker-compose.yml` if serving from a sub-path.

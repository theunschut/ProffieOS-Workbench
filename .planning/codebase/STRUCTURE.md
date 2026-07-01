# Codebase Structure

**Analysis Date:** 2026-07-01

## Directory Layout

```
ProffieOS-Workbench/
├── ProffieOS.Workbench/          # New Blazor WebAssembly app (active)
│   ├── Components/               # Reusable Razor components
│   │   ├── ControlButton.razor(.cs)
│   │   ├── ControlsLayout.razor(.cs)
│   │   ├── ControlsPanel.razor(.cs)
│   │   ├── EditPanel.razor(.cs)
│   │   ├── KnownDevicesPanel.razor(.cs)
│   │   ├── PresetsBar.razor(.cs)
│   │   ├── PresetsPanel.razor(.cs)
│   │   ├── SettingsPanel.razor(.cs)       ← Config/settings UI
│   │   ├── StyleEditor.razor(.cs)
│   │   ├── TracksPanel.razor(.cs)
│   │   └── VariationEditor.razor(.cs)
│   ├── Helpers/                  # Utility functions
│   │   └── ColorConverter.cs
│   ├── Layout/                   # App-level layout
│   │   └── MainLayout.razor(.cs)
│   ├── Models/                   # Data models
│   │   ├── NamedStyle.cs
│   │   ├── Preset.cs            # Preset structure
│   │   ├── SettingItem.cs       # BoolSettingItem, IntSettingItem
│   │   ├── StyleArgument.cs
│   │   └── UartProfile.cs       # BLE service/char UUIDs
│   ├── Pages/                    # Routed pages
│   │   ├── Dashboard.razor(.cs)  # Main control page
│   │   ├── EditPreset.razor(.cs) # Preset editor
│   │   ├── Home.razor(.cs)       # Connection/device select
│   │   └── Settings.razor(.cs)   # Settings page (loads blade length, etc.)
│   ├── Properties/
│   │   └── launchSettings.json   # Dev launch config
│   ├── Services/                 # Core business logic (singletons)
│   │   ├── SaberCommandService.cs        # Low-level protocol: send/receive
│   │   ├── SaberConnectionService.cs     # BLE/USB connection lifecycle
│   │   └── SaberStateService.cs          # Central state store + polling loop
│   ├── wwwroot/                  # Static assets + JS interop
│   │   ├── css/
│   │   │   └── app.css          # Main stylesheet
│   │   ├── js/
│   │   │   ├── bluetooth.js     # Web Bluetooth API wrapper
│   │   │   └── usb.js           # WebUSB API wrapper
│   │   ├── index.html           # Entry point
│   │   └── icon-*.png           # PWA icons
│   ├── App.razor                # Root Blazor component
│   ├── Program.cs               # Service registration, startup
│   └── ProffieOS.Workbench.csproj
│
├── old/                          # Legacy single-file app (preserved)
│   ├── app.html                 # 1783-line monolithic app (original)
│   ├── test.html                # Test harness
│   ├── sw.js                    # Service Worker (PWA)
│   ├── manifest.json            # PWA manifest
│   ├── icon-256.png             # PWA icon
│   └── Starjedi.ttf             # Font file
│
├── ProffieOS.Workbench.slnx     # Solution file
├── Dockerfile                    # Container build
├── docker-compose.yml            # Local Docker run
├── nginx.conf                    # Reverse proxy config
├── README.md
├── .gitignore
└── .planning/
    └── codebase/                 # This directory: architecture docs
        ├── ARCHITECTURE.md       # System design & data flow
        ├── STRUCTURE.md          # This file
        ├── STACK.md              # Tech stack & dependencies
        └── INTEGRATIONS.md       # External APIs & services
```

## Directory Purposes

**`ProffieOS.Workbench/Components/`:**
- Purpose: Reusable UI building blocks for dashboard, presets, settings, controls
- Contains: Razor markup + C# code-behind files
- Key files: 
  - `SettingsPanel.razor(.cs)` - Renders blade length, brightness, gesture settings (reads from `SaberStateService.BladeLengths`, `GestureIntSettings`, etc.)
  - `PresetsPanel.razor(.cs)` - Lists presets, handles rename/delete/select
  - `EditPanel.razor(.cs)` - Edits preset name/font/track/styles

**`ProffieOS.Workbench/Pages/`:**
- Purpose: Routed pages (full-screen views)
- Contains: Pages with navigation state and async lifecycle
- Key files:
  - `Home.razor(.cs)` - Initial connection screen (device selection)
  - `Dashboard.razor(.cs)` - Main control (presets, tracks, volume, controls)
  - `Settings.razor(.cs)` - Settings page; loads blade length/brightness/gesture settings with 2-second timeout
  - `EditPreset.razor(.cs)` - Preset editing

**`ProffieOS.Workbench/Services/`:**
- Purpose: Core business logic and state management
- Contains: Singleton services (registered in `Program.cs`)
- Key files:
  - `SaberStateService.cs` - Central state store for presets, settings, polling loop
  - `SaberConnectionService.cs` - BLE/USB connection, reconnect logic
  - `SaberCommandService.cs` - Protocol: command send/receive, tagging, timeouts

**`ProffieOS.Workbench/Models/`:**
- Purpose: Data structures
- Contains: Plain C# classes/records
- Key files:
  - `Preset.cs` - Name, font, track, styles, variation
  - `SettingItem.cs` - `BoolSettingItem(BaseCmd, Variable, Label, Value)`, `IntSettingItem(...)`
  - `UartProfile.cs` - BLE service/characteristic UUIDs for multiple UART profiles
  - `NamedStyle.cs` - Style name, arguments, template ID

**`ProffieOS.Workbench/wwwroot/js/`:**
- Purpose: JavaScript interop bridge for browser APIs
- Contains: IIFE modules that expose async functions to Blazor
- Key files:
  - `usb.js` - WebUSB: device discovery, connect, read loop, write
  - `bluetooth.js` - Web Bluetooth: device discovery, GATT connect, characteristic I/O

**`old/`:**
- Purpose: Legacy single-file reference app
- Contains: Original HTML/JS monolithic application
- Key file: `app.html` (1783 lines) - Entire app in one file; equivalent features to new app
- Not deployed; only preserved for reference/migration comparison

## Key File Locations

**Entry Points:**
- `ProffieOS.Workbench/wwwroot/index.html` - Browser entry; loads Blazor runtime
- `ProffieOS.Workbench/Program.cs` - .NET entry; registers services and starts WebAssembly host
- `ProffieOS.Workbench/App.razor` - Root Blazor component; houses Router

**State Management:**
- `ProffieOS.Workbench/Services/SaberStateService.cs` - Central state for presets, settings, polling loop (lines 1-561)
  - Public properties: `Presets`, `BladeLengths`, `GestureIntSettings`, `GestureBoolSettings`, `CurrentPresetIndex`, `Volume`, `BatteryVoltage`, `IsOn`, etc.
  - Key methods: `StartAsync()`, `LoadSettingsAsync()`, `SaveBladeLengthAsync()`, `SaveClashThresholdAsync()`, `SaveIntGestureAsync()`, etc.

**Configuration Loading:**
- `ProffieOS.Workbench/Services/SaberStateService.cs:LoadSettingsValuesAsync()` (lines 411-470)
  - Loads: SD toggle, blade dimming, clash threshold, blade lengths, gesture bool/int settings
  - Called by: `LoadSettingsBackgroundAsync()` during startup, and explicitly by `Settings.razor.cs` on page load
  - Pattern: Each `GetOptional(cmd)` returns trimmed value or null if command unsupported; `TryLoadBoolSetting()` / `TryLoadIntSetting()` parse and add to lists

**Display of Blade Length & Swing-on-Speed:**
- Component: `ProffieOS.Workbench/Components/SettingsPanel.razor` (lines 61-75 for blade length, lines 139-156 for sensitivity/timing)
  - Renders: `@for (var b = 1; b <= State.BladeLengths.Count; b++)` → displays `State.BladeLengths[blade - 1]`
  - For swing-on-speed: `SensitivityIntSettings` loop renders `entry.Setting.Label` (e.g., "swing on speed") and value
- Page: `ProffieOS.Workbench/Pages/Settings.razor(.cs)`
  - Calls `State.LoadSettingsAsync()` on page init
  - Awaits with 2-second timeout; shows loading bar during wait

**Write/Save Flow:**
- Example: `SettingsPanel.razor.cs:SaveBladeLength()` (lines 99-102)
  - Calls `State.SaveBladeLengthAsync(blade, length)`
- Implementation: `SaberStateService.SaveBladeLengthAsync()` (lines 491-496)
  - Updates local `BladeLengths[blade-1]`
  - Sends command: `await commands.Send($"set_blade_length {blade} {length}")`
  - If exception caught in component, shows Snackbar error toast

**JS/USB Interop:**
- Write entry: `ProffieOS.Workbench/wwwroot/js/usb.js:write()` (lines 216-238)
  - Checks `if (_endpointOut === -1 || !_device) throw new Error('USB not connected')`
  - Retries up to 3 times; on failure, calls `notifyDisconnected()` and throws error
- Disconnect notification: `notifyDisconnected()` (lines 75-82)
  - Calls `_dotnetRef.invokeMethodAsync('OnDisconnected')`
  - Triggers .NET side `SaberCommandService.OnDisconnected()`

## Naming Conventions

**Files:**

- Pages: PascalCase `.razor(.cs)` with page name (e.g., `Dashboard.razor`, `Settings.razor`)
- Components: PascalCase `.razor(.cs)` with component name (e.g., `SettingsPanel.razor`, `PresetsBar.razor`)
- Services: PascalCase `.cs` ending in `Service` (e.g., `SaberCommandService.cs`)
- Models: PascalCase `.cs` with singular noun (e.g., `Preset.cs`, `SettingItem.cs`)
- JS modules: camelCase `.js` with transport type (e.g., `usb.js`, `bluetooth.js`)

**Directories:**

- Plural nouns for collections: `Components/`, `Services/`, `Models/`, `Pages/`
- Descriptive names: `Helpers/`, `Layout/`, `Properties/`, `wwwroot/`
- Web root assets: `wwwroot/` (static files, JS, CSS)

**C# Identifiers:**

- Properties: PascalCase (e.g., `BladeLengths`, `GestureIntSettings`, `IsConnected`)
- Private fields: camelCase with underscore prefix (e.g., `_device`, `_pendingTcs`, `_dotnetRef`)
- Methods: PascalCase (e.g., `SendBladeLengthAsync()`, `LoadSettingsValuesAsync()`)
- Local variables: camelCase (e.g., `bladeMax`, `bladeLength`)
- Constants/enums: PascalCase (e.g., `ConnectionState.Connected`)

**JavaScript Identifiers:**

- Functions: camelCase (e.g., `notifyDisconnected()`, `deviceLabel()`)
- Variables: camelCase (e.g., `_device`, `_reading`, `_dotnetRef`)
- Logging: Prefixed with module name (e.g., `console.warn('[UsbInterop]', message)`)

## Where to Add New Code

**New Feature (e.g., "Add mute button to controls"):**
- Primary code: `ProffieOS.Workbench/Components/ControlsPanel.razor(.cs)` - Add UI button and click handler
- Command dispatch: `SaberStateService.cs` - Add `MuteAsync()` method that calls `commands.Send("mute")`
- Tests: `ProffieOS.Workbench.Tests/ControlsPanelTests.cs` (if test project exists)

**New Component/Module (e.g., "Add preset preview panel"):**
- Implementation: Create `ProffieOS.Workbench/Components/PresetPreview.razor` + `.cs` code-behind
- Register in parent page/layout (no global registration needed for nested components)
- Inject services via `@inject SaberStateService State`
- Subscribe to `State.StateChanged` in `OnInitialized()` for live updates

**Utilities/Helpers (e.g., "Add color format validator"):**
- Shared helpers: `ProffieOS.Workbench/Helpers/ColorConverter.cs` - Add static utility method
- Import via `@using ProffieOS.Workbench.Helpers` in Razor files

**New Setting Type (e.g., "Add gesture float setting support"):**
- Model: Add `FloatSettingItem(BaseCmd, Variable, Label, float Value)` record to `Models/SettingItem.cs`
- Load: Add `TryLoadFloatSetting()` method in `SaberStateService.LoadSettingsValuesAsync()`
- Save: Add `SaveFloatGestureAsync()` method in `SaberStateService`
- Display: Add new `GestureFloatSettings` list property and rendering logic in `SettingsPanel.razor`

**New Device Connection Type (e.g., "Add Serial over TCP"):**
- JS interop: Create `ProffieOS.Workbench/wwwroot/js/tcp.js` with `connect()`, `write()`, `reconnect()`, `stop()` methods
- Service: Update `SaberConnectionService.ConnectAsync()` to handle new transport type
- Add profile enum: Update `SaberConnectionService` to track which transport is in use (parallel to `_isBle` flag)

## Special Directories

**`ProffieOS.Workbench/wwwroot/`:**
- Purpose: Static web assets (served directly by HTTP)
- Generated: No; all checked in
- Committed: Yes
- Contains: HTML, CSS, JS, images, fonts

**`ProffieOS.Workbench/Properties/launchSettings.json`:**
- Purpose: Visual Studio launch profiles
- Generated: Automatically created by dotnet CLI
- Committed: Yes
- Contains: Dev HTTP/HTTPS ports, environment variables

**`.planning/codebase/`:**
- Purpose: Architecture documentation generated by GSD mapper
- Generated: Yes; created by `/gsd-map-codebase` command
- Committed: Yes; checked into git
- Contains: ARCHITECTURE.md, STRUCTURE.md, STACK.md, INTEGRATIONS.md

**`old/`:**
- Purpose: Legacy reference implementation
- Generated: No; historical code
- Committed: Yes; preserved for comparison
- Contains: Original single-file HTML/JS app and supporting assets

---

*Structure analysis: 2026-07-01*

<!-- refreshed: 2026-07-01 -->
# Architecture

**Analysis Date:** 2026-07-01

## System Overview

This repository contains two applications:

1. **New App**: Blazor WebAssembly (.NET 10) + MudBlazor UI component framework
2. **Legacy App**: Original single-file HTML/JavaScript app (preserved in `/old/` for reference)

The new app replaces the legacy app while maintaining the same protocol (serial/UART command interface over BLE or WebUSB).

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                          UI & Page Layer                                 │
│  Pages: Home, Dashboard, Settings, EditPreset (Razor Components)        │
│  `ProffieOS.Workbench/Pages/*.razor(.cs)`                               │
└─────────────────────┬───────────────────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────────────────┐
│                   Component & View Model Layer                           │
│  Components: SettingsPanel, ControlsPanel, PresetsBar, etc.             │
│  `ProffieOS.Workbench/Components/*.razor(.cs)`                          │
└─────────────────────┬───────────────────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────────────────┐
│                    Service & State Management Layer                      │
│  Services (Singletons):                                                 │
│  • SaberStateService     - Holds all device state (presets, settings)   │
│  • SaberConnectionService - Manages BLE/USB connection lifecycle         │
│  • SaberCommandService   - Protocol: command send/receive, tagging      │
│  `ProffieOS.Workbench/Services/*.cs`                                    │
└─────────────────────┬───────────────────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────────────────┐
│              JavaScript Interop Bridge Layer                             │
│  • UsbInterop      - WebUSB device access & read loop                    │
│  • BluetoothInterop - Web Bluetooth API wrapper                          │
│  `ProffieOS.Workbench/wwwroot/js/*.js`                                  │
└─────────────────────┬───────────────────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────────────────┐
│                   Browser & Device Interface                             │
│  • navigator.usb       - WebUSB API (Chrome/Edge)                        │
│  • navigator.bluetooth - Web Bluetooth API (Chrome/Edge)                 │
│  • ProffieOS Board     - Serial UART command interpreter                │
└─────────────────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| **SaberStateService** | Central state store: presets, settings, track list, font list, blade lengths, gesture settings, battery, on/off state. Exposes `StateChanged` event. Background run loop polls device state. | `ProffieOS.Workbench/Services/SaberStateService.cs` |
| **SaberConnectionService** | Connection lifecycle: BLE discovery, USB discovery, initial connect, reconnect logic (with retry/backoff), handles disconnect events. Manages `ConnectionState` enum. | `ProffieOS.Workbench/Services/SaberConnectionService.cs` |
| **SaberCommandService** | Low-level protocol handler: command queueing, tagging (for ProffieOS 8.x+), timeout handling, JS interop callbacks, response parsing. | `ProffieOS.Workbench/Services/SaberCommandService.cs` |
| **UsbInterop** (JS) | WebUSB: device discovery, connect, read loop, write with retry, disconnect handling, bridge to `.NET` via `invokeMethodAsync`. | `ProffieOS.Workbench/wwwroot/js/usb.js` |
| **BluetoothInterop** (JS) | Web Bluetooth: device discovery (with service filters), GATT connect, characteristic subscription, write with retry, password auth. | `ProffieOS.Workbench/wwwroot/js/bluetooth.js` |
| **Settings Page** | Fetches board settings via `State.LoadSettingsAsync()`, displays with 2-second timeout, shows loading bar. | `ProffieOS.Workbench/Pages/Settings.razor(.cs)` |
| **SettingsPanel Component** | Renders settings UI: blade length, brightness, clash threshold, gesture booleans (ignition/action), gesture integers (timing/sensitivity). Calls `State.SaveXAsync()` methods. | `ProffieOS.Workbench/Components/SettingsPanel.razor(.cs)` |

## Pattern Overview

**Overall:** Event-driven state synchronization with background polling.

**Key Characteristics:**
- **Singleton services**: All three services (Connection, Command, State) are registered as singletons in `Program.cs` so state persists across page navigation
- **Pub/sub via events**: UI components subscribe to `State.StateChanged` and `Connection.StateChanged`, redraw on notification
- **JS interop bridge**: Blazor calls JS methods (`InvokeAsync`, `InvokeVoidAsync`); JS calls back via `DotNetObjectReference` and `invokeMethodAsync`
- **Protocol tagging**: For boards with ProffieOS 8.x+, commands are tagged (`1| command`) to handle out-of-order/mixed responses
- **Graceful degradation**: Settings page shows "loading" for 2 seconds before timeout; missing commands return empty string instead of throwing

## Layers

**Page Layer:**
- Purpose: Route navigation, page-level orchestration
- Location: `ProffieOS.Workbench/Pages/*.razor(.cs)`
- Contains: Blazor routing, page-level async tasks, injection of services
- Depends on: All services (Connection, Command, State), MudBlazor components
- Used by: Browser navigation

**Component Layer:**
- Purpose: UI rendering, delegating state/actions to services
- Location: `ProffieOS.Workbench/Components/*.razor(.cs)`
- Contains: Razor markup for presets, settings, controls, tracks
- Depends on: Services (State, Connection); MudBlazor UI components
- Used by: Pages

**Service Layer:**
- Purpose: Business logic, state management, protocol handling
- Location: `ProffieOS.Workbench/Services/*.cs`
- Contains: State models, async command dispatch, connection lifecycle, event notification
- Depends on: Models, JS runtime
- Used by: Components, Pages

**JS Interop Layer:**
- Purpose: Browser API access (WebUSB, Web Bluetooth), I/O operations
- Location: `ProffieOS.Workbench/wwwroot/js/*.js`
- Contains: Device discovery, connect/reconnect, read loops, write operations
- Depends on: Browser APIs (navigator.usb, navigator.bluetooth)
- Used by: `SaberConnectionService` and `SaberCommandService` via JS interop

**Model Layer:**
- Purpose: Data structures for presets, settings, UART profiles
- Location: `ProffieOS.Workbench/Models/*.cs`
- Contains: Preset, SettingItem, StyleArgument, UartProfile, NamedStyle
- Depends on: None
- Used by: Services, components

## Data Flow

### Primary Request Path: Reading Blade Length from Device

1. User navigates to Settings page → `Settings.razor.cs:OnInitialized()` calls `State.LoadSettingsAsync()`
2. `SaberStateService.LoadSettingsAsync()` → calls `LoadSettingsValuesAsync()` internally
3. `LoadSettingsValuesAsync()` loops: `await GetOptional("get_blade_length 1")`, `await GetOptional("get_blade_length 2")`, etc.
4. `GetOptional()` → `Send("get_blade_length 1", retry: true)` via `SaberCommandService`
5. `SaberCommandService.Send()`:
   - If `UseTagging` is true, wraps as `"1| get_blade_length 1"`, sends to JS
   - If false, sends raw `"get_blade_length 1"`
   - Waits for `_pendingTcs` (TaskCompletionSource) to be resolved
6. JS interop:
   - `UsbInterop.write(bytes)` → `device.transferOut()` → sends to board
   - Board responds with `"-+=BEGIN_OUTPUT=+-\n<response>\n-+=END_OUTPUT=+-"`
   - Read loop calls `dotnetRef.invokeMethodAsync('OnDataReceived', data)`
7. `SaberCommandService.OnDataReceived()`:
   - Buffers incoming chunks
   - When END marker found, strips BEGIN/END, sets `_pendingTcs.TrySetResult(response)`
8. Command returns to `LoadSettingsValuesAsync()` with parsed value (e.g., "200")
9. `int.TryParse("200", out var len)` → stores in `BladeLengths[0]`
10. After all blade queries, `StateChanged?.Invoke()`
11. `SettingsPanel` receives notification, calls `StateHasChanged()`, renders `State.BladeLengths[b-1]` in UI

**Issue: Swing-on-speed / Blade Length / Timing / Sensitivity not displayed in new app**

The code path exists (`TryLoadIntSetting("gesture", "swingonspeed", ...)` at line 465 of `SaberStateService.cs`), but the values are stored in `GestureIntSettings` list and rendered only if they're present. **Possible causes:**

1. **Board doesn't support `get_gesture` command**: Line 175 probes `HasCmd("get_gesture test")`. If this returns false, the gesture settings section is never shown.
2. **Settings loading times out before displaying**: Line 37 of `Settings.razor.cs` waits 2 seconds, then shows UI. If board is slow to respond to gesture commands, timeout fires and UI shows without values loaded.
3. **Timeout exception silently caught**: Line 142 in `SaberCommandService.Send2()` throws `TimeoutException` after 20 seconds. If multiple gesture commands timeout, `SaberStateService.LoadSettingsValuesAsync()` swallows exception at line 189 (try/catch in `LoadSettingsBackgroundAsync`).
4. **`GetOptional()` returns null for unsupported commands**: If board returns "Whut? :get_gesture swingonspeed", then `GetOptional()` returns null and `TryLoadIntSetting()` bails out silently.

### Secondary Flow: Saving Blade Length (Write Config)

1. User changes blade length slider in SettingsPanel → `SaveBladeLength(blade, length)` fires
2. Calls `State.SaveBladeLengthAsync(blade, length)`
3. `SaberStateService.SaveBladeLengthAsync()`:
   - Updates local `BladeLengths[blade-1] = length`
   - Calls `await commands.Send($"set_blade_length {blade} {length}")`
4. `SaberCommandService.Send()` → JS `UsbInterop.write()` → board
5. If write fails after 3 retries in JS, throws error and calls `notifyDisconnected()` which triggers `_dotnetRef.invokeMethodAsync('OnDisconnected')`
6. Error surfaces to `SettingsPanel.SaveBladeLength()` catch block → `Snackbar.Add(ex.Message, Severity.Error)` shows toast

**Issue: "Error: USB not connected at Object.write" exception**

This unhandled JS exception occurs when:
- `UsbInterop.write()` checks `if (_endpointOut === -1 || !_device) throw new Error('USB not connected')`
- This happens if device was disconnected between UI action and actual write
- Exception is thrown from JS, propagated to Blazor as `JSDisconnectedException` or caught by the try/catch in `SendChunked()`
- Currently at line 150-151 in `SaberCommandService.cs`, caught and converted to `OnError` event, but **error event is not subscribed to in UI** — only `Snackbar` in Settings component catches via try/catch blocks

### Connected State Tracking

**New App (Blazor):**
- `SaberConnectionService.State` property: `ConnectionState.Disconnected | Connecting | Connected | Reconnecting`
- Persists as singleton across page navigation
- Set by `SetState()` which calls `StateChanged?.Invoke()` for UI updates
- `IsConnected` flag in `SaberCommandService` (true after `MarkConnected()`)
- On disconnect: JS calls `_dotnetRef.invokeMethodAsync('OnDisconnected')` → `SaberCommandService.OnDisconnected()` → fires `OnDisconnectedAsync` event → `SaberConnectionService.HandleDisconnect()` → initiates reconnect

**Legacy App (JS):**
- Global variables: `ble = null`, `usb = null`, `connected = false`
- State tracked imperatively in JS; no central .NET state synchronization
- On connect: sets `connected = true`
- On disconnect: calls `UpdateScreen()` to redraw UI with disconnected state

### Comparison: Config Display Data Flow

| Aspect | New App (Blazor) | Legacy App (JS) |
|--------|------------------|-----------------|
| **Read initiation** | Settings page `OnInitialized()` calls `State.LoadSettingsAsync()` | Settings button click triggers `UpdateSettings()` async function |
| **Fetch loop** | `LoadSettingsValuesAsync()` awaits each `get_blade_length`, `get_gesture` separately with timeout | Same: awaits each command in while loop |
| **Storage** | Typed lists/properties in `SaberStateService`: `BladeLengths`, `GestureIntSettings`, `GestureBoolSettings` | HTML elements with IDs, DOM reflects state |
| **Display** | Components subscribe to `StateChanged`, re-render `@State.BladeLengths` | HTML generated dynamically, inserted into DOM via `innerHTML` |
| **Write path** | `SaveBladeLengthAsync()` method dispatches to service, error caught at component level | `SaveBladeLength()` function calls `Send()`, no centralized error handling |
| **Error handling** | Try/catch at component level wraps `State.SaveXAsync()` calls | Errors go to browser console; no UI toast unless explicitly added |

## Entry Points

**Blazor App Start:**
- Location: `ProffieOS.Workbench/Program.cs`
- Triggers: Browser loads index.html, runs `dotnet.js` loader
- Responsibilities: Register services (singleton mode), configure MudBlazor, start WebAssembly runtime

**Initial Navigation:**
- Location: `ProffieOS.Workbench/Pages/Home.razor(.cs)`
- Triggers: App boots, Router navigates to `/` (Home page)
- Responsibilities: Prompt user to select BLE or USB, display known devices, initiate connection

**Background State Loop:**
- Location: `SaberStateService.StartAsync() → RunLoop(CancellationToken)`
- Triggers: After successful connection in Home page
- Responsibilities: Poll device every 5 seconds for preset index, track, volume, battery, on/off status

**Settings Load:**
- Location: `Settings.razor.cs:OnInitialized() → LoadSettingsAsync()`
- Triggers: User clicks Settings page
- Responsibilities: Load blade lengths, brightness, clash threshold, gesture settings (with 2-second timeout)

**JS Interop Callbacks:**
- Location: `SaberCommandService` [JSInvokable] methods: `OnDataReceived()`, `OnStatusReceived()`, `OnDisconnected()`
- Triggers: JS read loop receives data, or device disconnect event fires
- Responsibilities: Buffer data, parse responses, invoke callbacks into Blazor async code

## Architectural Constraints

- **Threading:** Blazor WebAssembly is single-threaded event loop. No concurrency needed; commands queued via `SemaphoreSlim` in `SaberCommandService._lock`.
- **Global state:** All state centralized in three singleton services (`SaberStateService`, `SaberConnectionService`, `SaberCommandService`). No scattered mutable state across components.
- **Circular imports:** None detected. Clean unidirectional dependency flow: Pages → Components → Services → Models → JS interop.
- **JS bridge limitations:** Interop works via reference copying (`DotNetObjectReference`). If Blazor object is disposed, JS calls will fail silently or throw unhandled exceptions. Mitigated by keeping services as singletons.
- **Protocol constraints:** Command responses must be delimited by `"-+=BEGIN_OUTPUT=+-"` and `"-+=END_OUTPUT=+-"` markers. If board firmware doesn't include these, parsing fails.
- **Timeout policy:** Hard-coded 20-second timeout per command. On timeout, device considered disconnected. For slow boards or high latency, may trigger spurious reconnects.

## Anti-Patterns

### Anti-Pattern: Mixing Async Task Fire-and-Forget with Exception Handling

**What happens:** In `SaberStateService.LoadInitialData()` (line 183), `LoadSettingsBackgroundAsync()` is started but not awaited. If an exception occurs in settings load, it's caught silently and user never sees error message.

**Why it's wrong:** User thinks settings are loading but no error feedback. Blade length / swing-on-speed never display, and user has no way to know why.

**Do this instead:** In `Settings.razor.cs`, use a timeout pattern with explicit error reporting (lines 36-69): await the load task with 2-second timeout, then explicitly observe the background task and surface errors via Snackbar.

### Anti-Pattern: Checking `UseTagging` Without Verifying Board Supports It

**What happens:** At line 124 in `SaberStateService.RunLoop()`, the code sends `"check| version"` to test tagging support. If response doesn't start with "Whut?", tagging is enabled. But if board is in a weird state, this test could be unreliable.

**Why it's wrong:** Once `UseTagging = true`, all future commands expect tagged responses. If test was wrong, responses won't parse and all commands fail with empty string returns.

**Do this instead:** Add a validation step: after enabling tagging, send a known test command and verify the tag parsing works. If it fails, revert to non-tagged mode.

### Anti-Pattern: Silent Timeout Swallowing Settings Load

**What happens:** At line 189 in `SaberStateService`, `LoadSettingsBackgroundAsync()` has a bare try/catch that swallows all exceptions (including TimeoutException). Settings page shows loading bar for 2 seconds, then shows empty settings with no error message.

**Why it's wrong:** User can't distinguish between "board doesn't support settings" and "board is too slow to respond". No diagnostic information.

**Do this instead:** Catch and log timeout exceptions separately. Surface them to UI as "Settings load timed out" error message, so user can retry or consider connection instability.

## Error Handling

**Strategy:** Defensive with graceful degradation. Timeouts expected; errors logged to browser console; user-facing errors shown as Snackbar toasts only at component level.

**Patterns:**

1. **Command timeout (20 seconds)**: `SaberCommandService.Send2()` line 141-142 uses `CancellationTokenSource(20s)` to trigger timeout exception. `_pendingTcs` is set to exception state, command returns empty string.

2. **JS write failure (3 retries)**: `UsbInterop.write()` line 220-237 retries up to 3 times with exponential backoff. After 3 failures, disconnects and throws error which propagates to Blazor `SendChunked()` task.

3. **JS read loop failure (3 errors)**: `UsbInterop` read loop lines 154-189 allows up to 3 consecutive read errors before notifying disconnect and stopping read.

4. **BLE/USB disconnect**: JS calls `notifyDisconnected()` which invokes `_dotnetRef.invokeMethodAsync('OnDisconnected')`. .NET side triggers `HandleDisconnect()` which attempts reconnect with 10 attempts and 5-second backoff.

5. **Missing optional settings**: `GetOptional()` returns null if response starts with "Whut?" or is empty. `TryLoadBoolSetting()` and `TryLoadIntSetting()` bail silently if null returned.

6. **UI-level component errors**: All component `SaveXAsync()` methods wrap in try/catch and call `Snackbar.Add(ex.Message, Severity.Error)`.

## Cross-Cutting Concerns

**Logging:** Console logging via `console.info/warn/error` in JS files (prefixed with `[UsbInterop]` or `[BluetoothInterop]`). No .NET-side logging framework; errors passed to `OnError` event handler in `SaberCommandService`.

**Validation:** 
- Int parsing: `int.TryParse()` used throughout; invalid values ignored
- Float parsing: `float.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture, ...)` for locale-independent number parsing
- Command format: No validation of command string syntax; sent raw to board

**Authentication:** BLE profiles support optional password authentication via `SendPasswordAndWait()` in `SaberCommandService` (lines 82-88). Password sent over optional password characteristic; response checked for "OK".

**Connection Resilience:**
- USB: Reconnect with backoff (1s + attempt*500ms, max 5s), up to 10 attempts
- BLE: Reconnect every 5 seconds, up to 10 attempts
- After reconnect succeeds, `IsConnected` re-set and state loop resumes

---

*Architecture analysis: 2026-07-01*

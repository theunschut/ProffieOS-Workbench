# Coding Conventions

**Analysis Date:** 2026-07-01

## Naming Patterns

**Files:**
- C# files: PascalCase (e.g., `Preset.cs`, `SaberConnectionService.cs`)
- Razor components: PascalCase with `.razor` extension, codebehind files as `.razor.cs` (e.g., `EditPanel.razor`, `EditPanel.razor.cs`)
- JavaScript files: camelCase (e.g., `bluetooth.js`, `usb.js`)
- Directories: PascalCase (e.g., `Services/`, `Components/`, `Models/`)

**Functions/Methods:**
- C#: PascalCase for public/private methods (e.g., `SendPasswordAndWait`, `ConnectBleAsync`, `MarkConnected`)
- C#: Async methods use `Async` suffix (e.g., `ConnectBleAsync`, `SendChunked`, `LoadSettingsAsync`)
- JavaScript: camelCase for functions (e.g., `writeTxValue`, `detachHandlers`, `resetConnectionState`)
- Callback/event handler naming: `On[EventName]` pattern (e.g., `OnDataReceived`, `OnDisconnected`, `OnStateChanged`)

**Variables:**
- C#: camelCase for private fields, PascalCase for public properties (e.g., `_buffer`, `_pendingTcs`, `IsConnected`)
- C#: Prefix private fields with underscore (e.g., `_device`, `_tx`, `_isBle`)
- JavaScript: camelCase throughout (e.g., `_savedProfiles`, `_connectSeq`, `deviceLabel`)
- Readonly/constant values: camelCase in JS, UPPER_SNAKE_CASE implied preference in resource names

**Types:**
- C#: PascalCase classes/records (e.g., `Preset`, `UartProfile`, `ConnectionState`)
- C#: Enum values: PascalCase (e.g., `ConnectionState.Connected`, `Severity.Error`)
- Records over classes for simple DTOs (e.g., `UartProfile`, `BoolSettingItem`, `IntSettingItem`)
- Enums for state machines (e.g., `enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting }`)

## Code Style

**Formatting:**
- No explicit configuration file found; follows standard .NET conventions
- Indentation: 4 spaces (implied from .NET standard)
- Line length: Generally compact, single statements preferred

**Linting:**
- No explicit linting configuration detected for C# or JavaScript
- Nullable reference types enabled: `<Nullable>enable</Nullable>` in csproj
- Implicit usings enabled: `<ImplicitUsings>enable</ImplicitUsings>` in csproj

## Import Organization

**Order (C#):**
1. System namespaces (`using System.*`)
2. Third-party namespaces (`using Microsoft.*`, `using MudBlazor`)
3. Project namespaces (`using ProffieOS.Workbench.*`)

**Example from `SaberCommandService.cs`:**
```csharp
using System.Text;
using Microsoft.JSInterop;

namespace ProffieOS.Workbench.Services;
```

**Path Aliases:**
- Razor components: Implicit namespace imports via `_Imports.razor`
- All services injected as singletons via Dependency Injection (Program.cs)
- No explicit path aliases used; folder structure mirrors namespace organization

## Error Handling

### JS Interop Error Patterns

The codebase uses three distinct error handling strategies for JS interop calls:

**1. Try-Catch with Message Pass-Through (Service Layer)**

The `SaberConnectionService` and `SaberCommandService` propagate JS interop exceptions directly to callers:

```csharp
// SaberConnectionService.cs lines 68-78
public async Task ConnectBleAsync(string? password = null)
{
    SetState(ConnectionState.Connecting);
    try
    {
        var filters = BleProfiles.Select(p => new { services = new[] { p.ServiceUuid } }).ToArray();
        ConnectedDeviceName = await js.InvokeAsync<string>("BluetoothInterop.requestDevice", filters);
        await ConnectBleInternalAsync(password);
    }
    catch
    {
        SetState(ConnectionState.Disconnected);
        throw;  // Re-throw to let caller handle
    }
}
```

**Exception types caught: Untyped** — No specific JSException catching. All exceptions from `js.InvokeAsync<T>` or `js.InvokeVoidAsync` are caught generically.

**2. Timeout Wrapping with CancellationTokenSource (Command Layer)**

The `SaberCommandService.Send2` method implements a 20-second command timeout:

```csharp
// SaberCommandService.cs lines 134-158
private async Task<string> Send2(string cmd)
{
    if (SendBytesAsync is null) return "";
    await _lock.WaitAsync();
    try
    {
        _pendingTcs = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        cts.Token.Register(() => _pendingTcs.TrySetException(new TimeoutException($"Command timeout: {cmd}")));

        var data = Encoding.UTF8.GetBytes(cmd + '\n');
        await SendChunked(data);

        return await _pendingTcs.Task;
    }
    catch (Exception ex)
    {
        OnError?.Invoke($"Command failed: {ex.Message}");
        return "";  // Returns empty string on failure
    }
    finally
    {
        _pendingTcs = null;
        _lock.Release();
    }
}
```

**Key behavior:**
- Timeout: **20 seconds** for all device commands
- Fires `OnError` event on failure
- Returns empty string `""` on exception rather than throwing
- **Issue**: Timeouts may contribute to lost settings updates (20 seconds is quite long; user reports frequent timeouts)

**3. Fire-and-Forget with Error Event (UI Layer)**

Components use try-catch and report errors via Snackbar:

```csharp
// SettingsPanel.razor.cs lines 81-85
private async Task SaveSd(bool val)
{
    try { await State.SaveSdAsync(val); }
    catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
}
```

**Exception handling:** Catches all exceptions, displays to user, does not re-throw.

### Timeout Handling Conventions

**USB/Serial Operations Timeouts:**

1. **Command Timeout: 20 seconds** (`SaberCommandService.Send2`)
   - File: `Services/SaberCommandService.cs` line 141
   - Used for all device communication via the command protocol
   - If exceeded, fires `OnError` event and returns `""`

2. **Password Status Timeout: 2 seconds** (`SaberCommandService.SendPasswordAndWait`)
   - File: `Services/SaberCommandService.cs` line 85
   - Used for BLE password handshake only
   - Returns `"TIMEOUT"` if not satisfied in 2 seconds

3. **Settings Load Timeout: 2 seconds** (`Settings.razor.cs`)
   - File: `Pages/Settings.razor.cs` line 37
   - Page-level UI timeout for displaying settings
   - If load task doesn't complete, shows spinner but continues loading in background

4. **Device Reconnect Retry: 5 seconds between attempts, 10 attempts max** (`SaberConnectionService`)
   - File: `Services/SaberConnectionService.cs` lines 172-189 (BLE), 195-215 (USB)
   - BLE: Fixed 5-second delay between attempts
   - USB: Exponential backoff starting at 1 second, capped at 5 seconds
   - Example: `await Task.Delay(Math.Min(1000 + attempt * 500, 5000))`

5. **Sync Polling: 50ms delay between attempts, 40 attempts max** (`SaberStateService.Sync`)
   - File: `Services/SaberStateService.cs` lines 76-89
   - Total timeout: ~2 seconds before throwing "Failed to synchronize" error

6. **State Loop Polling: 5 seconds** (`SaberStateService.RunLoop`)
   - File: `Services/SaberStateService.cs` line 148
   - Background loop refresh rate for current preset, volume, battery, etc.

**Save/Write Operation Timeouts:**

All save operations (`SaveNameAsync`, `SaveFontAsync`, `SaveTrackAsync`, `SaveStyleAsync`, `SaveVariationAsync`, etc.) use the 20-second command timeout indirectly via `SaberCommandService.Send()`. No additional retry logic above that layer.

**Issue identified:** The 20-second command timeout + no automatic retry may explain user reports of lost settings. A save command that takes >20 seconds (e.g., over slow/congested USB) will timeout and silently fail (returns `""`), with the user seeing only an error message in the command handler's `OnError` event. No automatic retry, and if the user doesn't see the error message, the save appears to have succeeded locally but is lost.

### Connection Loss Handling

**Disconnect Detection:**
- BLE: Detected by `gattserverdisconnected` event on device
- USB: Detected by `disconnect` event on navigator.usb, or by repeated read failures

**Reconnection Strategy:**
- Automatic reconnection triggered by `HandleDisconnect()` in `SaberConnectionService`
- Calls `ReconnectBleAsync()` or `ReconnectUsbAsync()` depending on connection type
- Both retry up to 10 times with logged `ReconnectAttempt` counter
- After 10 attempts, marks device as `Disconnected` and sets `LastDisconnectReason = "Reconnect timed out"`

**Logging:**
- JS interop layer logs all major operations to browser console (see `bluetooth.js` and `usb.js` logInfo/logWarn/logError)
- .NET services propagate exceptions but don't have centralized logging beyond the `OnError` event

## Logging

**Framework:** No centralized logging framework (Serilog, etc.) detected

**Patterns:**
- Console logging in JavaScript interop files: `logInfo()`, `logWarn()`, `logError()` functions prefixed with module name
- Example: `console.info('[BluetoothInterop]', message, extra ?? '')`
- .NET error reporting via event: `Commands.OnError?.Invoke($"...")`
- UI feedback via Snackbar (MudBlazor): `Snackbar.Add(message, Severity.Error)`

**When to log:**
- Major state transitions (connection established, device selected, reconnecting)
- Operation failures with detailed error context
- Retry attempts with attempt count
- Do NOT log in hot paths (e.g., every data received event)

## Comments

**When to Comment:**
- Complex state machine transitions
- Non-obvious algorithm logic (e.g., tagged response parsing in `SaberCommandService.ParseTaggedResponse`)
- Workarounds or known limitations
- Protocol details (e.g., ProffieOS 8.x+ tagging format)

**JSDoc/TSDoc:**
- Not extensively used in the JavaScript layer
- C# uses XML doc comments for public APIs only (minimal usage observed)
- Example from `SaberCommandService`:
  ```csharp
  /// <summary>
  /// Handles the low-level command protocol: send queue, response parsing,
  /// command tagging (ProffieOS 8.x+), and watchdog.
  /// JS calls back into this class via DotNetObjectReference.
  /// </summary>
  public class SaberCommandService : IAsyncDisposable { ... }
  ```

## Function Design

**Size:** 
- Average method length: 20-50 lines
- Larger methods (80+ lines) appear in state loops and list-building logic
- Prefer small, focused methods; use helper methods for complex operations

**Parameters:**
- No more than 3 parameters per method; use records or objects for multiple values
- Example: `UartProfile` record holds 5 related UUID strings instead of 5 separate parameters
- Optional parameters use C# 8+ nullable reference types (e.g., `string? password = null`)

**Return Values:**
- Async methods return `Task`, `Task<T>`, or `ValueTask` where appropriate
- `Send()` returns `string` (command response or empty string on error)
- No out parameters observed; prefer tuple returns for multiple values
- Void methods use events for communication (e.g., `StateChanged?.Invoke()`)

## Module Design

**Exports:**
- C# uses namespaces for organization; public classes/records are exported implicitly
- Services exported as singletons via DI container (Program.cs)
- No barrel files or index exports

**Barrel Files:**
- `_Imports.razor` serves as the barrel for Razor component imports
- Contains namespace declarations for all common types and services

**Service Pattern:**
- Three core services injected as singletons:
  1. `SaberCommandService` — Low-level command protocol and response parsing
  2. `SaberConnectionService` — BLE/USB connection lifecycle and reconnection
  3. `SaberStateService` — High-level state management and business logic
- Services communicate via injected dependencies and events
- No middleware or interceptor patterns observed

**Initialization Order:**
1. Program.cs registers services as singletons
2. Home.razor calls `Connection.InitAsync()` to probe for BLE/USB availability
3. On successful connection, calls `State.StartAsync()` to begin polling loop
4. State loop calls `LoadInitialData()` and runs continuous refresh via `RunLoop(CancellationToken)`

---

*Convention analysis: 2026-07-01*

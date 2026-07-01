# Codebase Concerns

**Analysis Date:** 2026-07-01

## Bug Report: Missing Settings Display Values

### Symptom 1: Swing-On-Speed, Blade Length, Timing, and Sensitivity Not Displayed

**Issue:** The new app does not display swing-on-speed, blade length, timing, and sensitivity values read from the connected board over WebUSB, while the old app (/old) does show these values.

**Root Cause Analysis:**

The old app (`/old/app.html`) successfully reads and displays these values using functions like:
- Line 1260: `await generateIntSetting("gesture", "swingonspeed", "swing on speed")`
- Line 1246-1254: `get_blade_length [blade]` command loop
- Line 1268: `await generateIntSetting("gesture", "clashdetect", "clash detect")` (timing)
- Line 1270: `await generateIntSetting("gesture", "maxclash", "max clash strength")` (sensitivity)

The new app has code to read these same values:
- File: `ProffieOS.Workbench/Services/SaberStateService.cs:465-469` - Defines the same settings to load
- File: `ProffieOS.Workbench/Services/SaberStateService.cs:544-558` - `TryLoadBoolSetting()` and `TryLoadIntSetting()` functions query the board

**Hypothesis:**

The settings loading code exists but may be **not fully wired up to the UI display**, or there is a **failure during LoadSettingsBackgroundAsync()** that silently fails. Evidence:
- `LoadSettingsBackgroundAsync()` at line 186-194 calls `LoadSettingsValuesAsync()` with no error handling - if a command times out or fails, it silently catches the exception and continues
- The UI in `SettingsPanel.razor` (lines 120-156) expects data in `State.TimingIntSettings` and `State.SensitivityIntSettings`, which are populated by `TryLoadIntSetting()`
- If `GetOptional()` returns `null` (which it does if a command times out or returns empty), `TryLoadIntSetting()` returns early without adding the setting

**Files Involved:**
- `ProffieOS.Workbench/Services/SaberStateService.cs:186-194` - Background loading with swallowed exceptions
- `ProffieOS.Workbench/Services/SaberStateService.cs:411-470` - LoadSettingsValuesAsync with individual command queries
- `ProffieOS.Workbench/Components/SettingsPanel.razor` - UI display layer expects populated settings

**Impact:** Users cannot see or modify swing-on-speed, blade length, timing (lockup delay, force push length), or sensitivity (clash detect, max clash) values even though the board supports these commands.

**Recommendations:**
1. Add detailed logging to `LoadSettingsBackgroundAsync()` and `LoadSettingsValuesAsync()` to log which commands are failing
2. Verify that the board is still connected during settings load - check `IsConnected` flag before attempting loads
3. Add retry logic for individual failed setting commands instead of failing silently
4. Display a loading/error state indicator on the Settings panel during background load so users know settings are being fetched

---

## Bug Report: Unhandled JS Interop Exception on USB Write

### Symptom 2: JS Exception "USB not connected" Bubbles Up Through Blazor

**Issue:** An unhandled JS interop error was thrown in production: "Command failed: USB not connected Error: USB not connected at Object.write (js/usb.js:217:56) at w.processJSCall ... at Object.Qt [as invokeJSJson]"

**Root Cause Analysis:**

The error originates from `ProffieOS.Workbench/wwwroot/js/usb.js:217`:

```javascript
async write(bytes) {
    if (_endpointOut === -1 || !_device) throw new Error('USB not connected');
    // ... retry logic ...
}
```

The problem is **insufficient connection state checking before the caller invokes `write()`**:

1. **JS side check is too late** (`usb.js:217`):
   - The check only validates that `_device` and `_endpointOut` exist
   - It does not check if the device is actually open or if the endpoint is still valid
   - If the device disconnects between the check and the write, an uncaught error is thrown

2. **C# caller does not validate connection state**:
   - File: `ProffieOS.Workbench/Services/SaberConnectionService.cs:160` - Sets `SendBytesAsync` but does not wrap the JS call with connection validation
   - File: `ProffieOS.Workbench/Services/SaberCommandService.cs:161-168` - `SendChunked()` calls `SendBytesAsync!` without checking if connection is still alive
   - The C# side assumes the JS layer will handle all errors, but the JS error is not properly caught by Blazor's JS interop layer

3. **Error handling gap**:
   - `SaberCommandService.Send2()` at line 134-159 wraps the `SendChunked()` call in try-catch at line 149, which does catch the JS exception
   - However, the error message "Command failed: USB not connected" suggests this error may bubble up from a different code path or escape error handling in certain race conditions

**Files Involved:**
- `ProffieOS.Workbench/wwwroot/js/usb.js:216-238` - `write()` method with late connection check
- `ProffieOS.Workbench/Services/SaberConnectionService.cs:157-166` - USB connect setup without pre-write validation
- `ProffieOS.Workbench/Services/SaberCommandService.cs:134-159` - `Send2()` method that should catch write errors
- `ProffieOS.Workbench/Services/SaberCommandService.cs:161-168` - `SendChunked()` directly invokes JS without connection pre-check

**Impact:** Users experience unhandled JS exceptions that crash the app or cause hard-to-debug errors when:
- Device disconnects unexpectedly during a write
- WebUSB connection is lost due to device removal
- Rapid connect/disconnect cycles occur

**Recommendations:**
1. Add a pre-write connection state check in `SendChunked()` before calling `SendBytesAsync!()`:
   ```csharp
   if (!commands.IsConnected) 
       throw new InvalidOperationException("Device not connected");
   ```
2. In `usb.js`, improve the connection check to validate the device is still open:
   ```javascript
   if (_endpointOut === -1 || !_device || !_device.opened) 
       throw new Error('USB not connected');
   ```
3. Implement an explicit "pre-flight check" method in both `UsbInterop` and `BluetoothInterop` that validates connection state before writes
4. Ensure all JS interop errors from writes are properly caught and logged with context (device name, endpoint, etc.)

---

## Bug Report: Frequent Timeouts Causing Unsaved Settings

### Symptom 3: Frequent Timeouts When Writing Config to Board

**Issue:** The new app has frequent timeouts that result in unsaved settings when writing config to the board.

**Root Cause Analysis:**

Multiple timeout-related issues in the code:

1. **Fixed 20-second timeout is too aggressive**:
   - File: `ProffieOS.Workbench/Services/SaberCommandService.cs:141` - Hard-coded `TimeSpan.FromSeconds(20)`
   - The old app (`/old/app.html:471`) uses a 10-second watchdog, but the new app's 20-second timeout may still be too short for:
     - Large settings payloads
     - Boards with slow SD card writes
     - Network congestion on BLE/USB channels

2. **Chunked writing introduces delays without backpressure handling**:
   - File: `ProffieOS.Workbench/Services/SaberCommandService.cs:161-168` - `SendChunked()` sends 20-byte chunks in a tight loop
   - No delay between chunks; if the device's RX buffer fills, the write will block or fail
   - The JS layer (`usb.js:220-232` and `bluetooth.js:240-254`) has 3 retries with backoff, but this is only at the USB/BLE level, not the command protocol level

3. **Settings load can time out during initial sync**:
   - File: `ProffieOS.Workbench/Services/SaberStateService.cs:186-194` - `LoadSettingsBackgroundAsync()` catches all exceptions silently, including timeouts
   - If any of the `TryLoadIntSetting()` or `TryLoadBoolSetting()` commands times out (line 540), the entire settings load fails silently
   - No fallback or retry after disconnect/reconnect

4. **Watchdog and command queue interaction**:
   - File: `ProffieOS.Workbench/Services/SaberCommandService.cs:141-142` - Timeout is registered on the CancellationToken, but there's no watchdog to detect stuck I/O
   - If a write succeeds but the response never arrives, the command hangs for the full 20 seconds before timing out
   - The old app uses a 10-second watchdog that fires periodically to detect hung commands (line 468-472 in `/old/app.html`)

5. **No exponential backoff for transient failures**:
   - File: `ProffieOS.Workbench/Services/SaberCommandService.cs:93-110` - The `Send()` method with tagging has 20 retries but only delays 50ms between retries
   - No exponential backoff; if the device is overloaded, hammering it with retries makes it worse

**Files Involved:**
- `ProffieOS.Workbench/Services/SaberCommandService.cs:141-142` - Hard-coded 20-second timeout
- `ProffieOS.Workbench/Services/SaberCommandService.cs:161-168` - Chunked write without inter-chunk delays
- `ProffieOS.Workbench/Services/SaberStateService.cs:186-194` - Silent exception handling in settings load
- `ProffieOS.Workbench/Services/SaberStateService.cs:411-470` - Sequential loading of many settings (each with independent 20-second timeout)
- `ProffieOS.Workbench/wwwroot/js/usb.js:220-232` - Retry with only 25ms delays between attempts
- `ProffieOS.Workbench/wwwroot/js/bluetooth.js:240-254` - Retry with only 20ms delays between attempts

**Impact:**
- Users lose unsaved settings when a write times out
- No indication to the user that a save failed (error is swallowed)
- Repeated timeout failures on slower connections or devices with limited resources
- Settings load can take 30+ seconds (up to 10 settings × 20-second timeout = 200 seconds in worst case)

**Recommendations:**
1. Implement adaptive timeout based on connection type:
   - USB: 10 seconds (currently 20)
   - BLE: 15 seconds (currently 20)
   
2. Add inter-chunk delays in `SendChunked()` with backpressure handling:
   ```csharp
   private async Task SendChunked(byte[] data)
   {
       for (var i = 0; i < data.Length; i += 20)
       {
           var chunk = data.Skip(i).Take(20).ToArray();
           await SendBytesAsync!(chunk);
           if (i + 20 < data.Length)
               await Task.Delay(5);  // Small delay between chunks
       }
   }
   ```

3. Implement exponential backoff in tagged retries (line 97-106):
   ```csharp
   if (!retry) return "";
   var delay = Math.Min(50 * (attempt + 1), 500);  // Cap at 500ms
   await Task.Delay(delay);
   ```

4. Add logging to settings load to identify which commands are timing out
5. Implement a "settings save confirmation" mechanism that verifies each setting was actually written to the board (read it back)
6. Add a periodic watchdog timer similar to the old app (line 468-472 in `/old/app.html`) that fires every 5 seconds to detect truly hung commands

---

## Architecture & Design Issues

### Silent Exception Handling in Background Tasks

**Pattern:** File `SaberStateService.cs:186-194` uses `catch { /* best-effort */ }` with no logging.

**Problem:** 
- Makes debugging production issues extremely difficult
- No telemetry or error reporting
- Users have no idea settings failed to load

**Recommendation:** Log all exceptions in background tasks for debugging:
```csharp
private async Task LoadSettingsBackgroundAsync()
{
    try
    {
        await LoadSettingsValuesAsync();
        SettingsLoaded = true;
        Notify();
    }
    catch (Exception ex) 
    { 
        commands.OnError?.Invoke($"Settings load failed: {ex.Message}");
    }
}
```

### No Connection State Polling During Operations

**Pattern:** Once connected, the code assumes the connection remains valid until `OnDisconnected` fires.

**Problem:**
- WebUSB/BLE can become "silently disconnected" where the JS layer doesn't immediately detect it
- A write can fail before `OnDisconnected` callback fires
- Race condition between connection loss and in-flight commands

**Recommendation:** Add periodic connection state probes (e.g., `ping` command) during long-running operations

---

## Test Coverage Gaps

**Untested Areas:**
- Connection loss during settings load
- Timeout handling in chunked writes
- Rapid connect/disconnect cycles
- Settings save confirmation (verify written values)
- Error message propagation from JS to UI

**Risk:** Silent failures and data loss (unsaved settings) go undetected until production.

---

*Concerns audit: 2026-07-01*

# Testing Patterns

**Analysis Date:** 2026-07-01

## Test Framework

**Status:** No test framework detected in active codebase

**Runner:** Not configured

**Assertion Library:** Not configured

**Run Commands:** Not available

## Test Coverage

**Summary:** Zero test files detected in the repository.

```bash
# Search result
find . -name "*.test.*" -o -name "*.spec.*" -o -name "*Test.cs" -o -name "*Tests.cs"
# No matches found in ProffieOS.Workbench/
```

**What's not tested:**
- Core service logic: `SaberCommandService`, `SaberConnectionService`, `SaberStateService`
- USB/BLE interop layer: `bluetooth.js`, `usb.js`
- Command protocol parsing: Tagged response parsing, state synchronization
- Retry and timeout logic: Reconnection attempts, command timeouts
- Preset/settings save flow: All persistence operations
- UI components: All Razor components and page logic

## Testing Recommendations

Given the critical nature of USB/BLE communication and the reported issue of lost settings (timeout-related), test coverage should prioritize:

### Priority 1: Command Protocol & Timeouts

**What to test:**
- `SaberCommandService.Send2` timeout behavior:
  - Command completes within 20 seconds → returns result
  - Command exceeds 20 seconds → throws `TimeoutException`
  - `OnError` event fires on timeout
  - Pending TCS is cleaned up in finally block
- `SaberCommandService.ParseTaggedResponse` parsing:
  - Valid tagged response format → parsed correctly
  - Malformed tags → returns `(false, "")`
  - Line number/length mismatch → returns `(false, "")`
  - Correct tag extraction across multiple lines
- `SaberCommandService.SendPasswordAndWait`:
  - Status received within 2 seconds → returns status
  - Timeout after 2 seconds → returns `"TIMEOUT"`

**Files to test:**
- `Services/SaberCommandService.cs` (entire file is critical path)

**Example test structure:**
```csharp
[TestClass]
public class SaberCommandServiceTests
{
    [TestMethod]
    public async Task Send_WhenResponseReceivedWithin20Seconds_ReturnsResponse()
    {
        var service = new SaberCommandService();
        // Setup response callback
        // Act: await service.Send("test_command")
        // Assert: response matches expected value
    }

    [TestMethod]
    public async Task Send_WhenTimeout_ThrowsTimeoutException()
    {
        var service = new SaberCommandService();
        // Setup: configure to never receive response
        // Act & Assert: await service.Send("slow_command") throws TimeoutException
    }

    [TestMethod]
    public void ParseTaggedResponse_WithValidTags_ReturnsCorrectResult()
    {
        var raw = "1,5,101|hello\n2,5,101|world";
        var (ok, result) = SaberCommandService.ParseTaggedResponse(raw, 101);
        Assert.IsTrue(ok);
        Assert.AreEqual("hello\nworld", result);
    }
}
```

### Priority 2: Connection Lifecycle

**What to test:**
- `SaberConnectionService.ConnectBleAsync`:
  - JS interop call succeeds → state transitions to Connected
  - JS interop call fails → state reverts to Disconnected, exception propagates
  - Password handling: successful auth → connected, failed auth → exception
- `SaberConnectionService.ReconnectBleAsync` / `ReconnectUsbAsync`:
  - Retry loop: attempts up to 10 times
  - Backoff timing: correct delays between attempts
  - After 10 failures: marks as Disconnected, sets `LastDisconnectReason`
  - Early success: returns and marks Connected before all attempts exhausted

**Files to test:**
- `Services/SaberConnectionService.cs`

### Priority 3: State Management & Polling

**What to test:**
- `SaberStateService.Sync`:
  - Recovers from command misalignment within 40 attempts
  - Throws if no synchronization after all attempts
- `SaberStateService.RunLoop`:
  - Runs continuously on background task
  - Fetches preset, track, volume, battery on 5-second intervals
  - Calls `Notify()` after each cycle
  - Cancellation token stops the loop cleanly
- `SaberStateService.LoadSettingsValuesAsync`:
  - Handles optional commands gracefully (returns null for missing features)
  - Parses float/int values correctly with invariant culture
  - Blade length queries: stops when no more blades

**Files to test:**
- `Services/SaberStateService.cs`

### Priority 4: USB/BLE JavaScript Interop

**What to test:**
- `bluetooth.js`:
  - `writeChunk` retries up to 3 times with exponential backoff
  - Fallback write method selection (writeValueWithoutResponse → writeValue → etc.)
  - Characteristic value parsing: UTF-8 decode, event firing
  - Disconnect handler: prevents cross-connect message leaks via `_connectSeq` check
- `usb.js`:
  - `write` retries up to 3 times
  - Transfer status checking: 'ok' success, other statuses trigger retry/disconnect
  - Read loop: handles status errors with retry up to 3 times
  - Cleanup: closes device, releases interface, removes listeners
  - Device resolution: rebinds to replacement device if original disappeared

**Files to test:**
- `wwwroot/js/bluetooth.js`
- `wwwroot/js/usb.js`

### Priority 5: UI Error Handling & Save Flow

**What to test:**
- `Home.razor.cs`:
  - Connection error caught and displayed in Snackbar
  - `_busy` flag prevents concurrent connection attempts
  - Navigation to dashboard on successful connection
- `SettingsPanel.razor.cs`:
  - Save operations wrapped in try-catch
  - Failures displayed in Snackbar
  - State changes trigger component re-render
- `Dashboard.razor.cs`:
  - Settings load timeout (2-second UI timeout, continues in background)
  - Observe task fires `OnError` if load fails after UI timeout
- `Settings.razor.cs`:
  - Settings already loaded → shown immediately
  - Settings not loaded → fires background load
  - Timeout → shows spinner, continues in background

**Files to test:**
- `Pages/Home.razor.cs`
- `Pages/Dashboard.razor.cs`
- `Pages/Settings.razor.cs`
- `Components/SettingsPanel.razor.cs`
- `Components/EditPanel.razor.cs`

## Current Test Infrastructure

**Test Runner:** None configured

**Mock Framework:** None detected (would need to mock `IJSRuntime` and services)

## Recommended Test Setup

**Framework selection:**
- **xUnit** or **MSTest** (MSTest aligns with .NET ecosystem conventions)
- **Moq** for mocking IJSRuntime and service dependencies
- **FluentAssertions** for readable assertions

**Project structure:**
```
ProffieOS.Workbench.Tests/
├── Services/
│   ├── SaberCommandServiceTests.cs
│   ├── SaberConnectionServiceTests.cs
│   └── SaberStateServiceTests.cs
├── Pages/
│   ├── HomeTests.cs
│   └── DashboardTests.cs
└── Components/
    ├── SettingsPanelTests.cs
    └── EditPanelTests.cs
```

**Mock setup pattern:**
```csharp
public class SaberCommandServiceTests
{
    private Mock<IJSRuntime> _jsRuntimeMock;
    private SaberCommandService _service;

    [TestInitialize]
    public void Setup()
    {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _service = new SaberCommandService();
    }

    // Tests...
}
```

## Legacy Application Testing

The legacy JS/HTML app (`old/app.html`, `old/sw.js`) has:
- Service worker for offline caching
- No test files
- No test framework configured

**Note:** The legacy app is preserved for reference only and is not actively maintained. No testing is recommended for it.

## Known Testing Gaps

### USB/Serial Write Failures
Currently no test coverage for:
- Write retries (3 attempts with 25ms delay per retry)
- Failure notifications back to .NET
- Connection state after repeated write failures

**Why it matters:** The user's timeout issue likely involves write failures that aren't being retried or reported correctly.

### Save Flow End-to-End
No integration tests covering:
- User clicks "Save" → command sent → 20-second timeout → error shown in UI
- Save operation succeeds locally but fails in device → UI shows success but device loses data
- Multiple save operations queued while one times out

**Why it matters:** Critical for validating the fix to the lost-settings issue.

### Reconnection Under Load
No tests for:
- Reconnection attempts while other commands are pending
- Timeout during reconnection process
- State consistency after reconnection completes

---

*Testing analysis: 2026-07-01*

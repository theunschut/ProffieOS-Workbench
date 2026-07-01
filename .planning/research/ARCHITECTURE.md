# Architecture Research

**Domain:** Retrofitting reliability (connection-state machine, timeout/watchdog, write-confirmation, error surfacing) into an existing layered Blazor WASM WebUSB/WebBLE app
**Researched:** 2026-07-01
**Confidence:** HIGH (based on direct reading of `SaberStateService.cs`, `SaberConnectionService.cs`, `SaberCommandService.cs`, `usb.js`, `Settings.razor.cs`, and `/old/app.html`'s watchdog — not external ecosystem survey, since the correct pattern here is dictated by the existing codebase's own seams, not by a generic library choice)

## Standard Architecture

### System Overview (existing — unchanged by this milestone)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Pages / Components (Settings.razor, SettingsPanel.razor, Dashboard...) │
│  - subscribe to StateChanged events, call State.SaveXAsync()            │
└─────────────────────────────┬─────────────────────────────────────────────┘
                              │ (1) UI actions, (4) error/state notifications
┌─────────────────────────────┴─────────────────────────────────────────────┐
│  SaberStateService (singleton)                                           │
│  - Presets/settings state, RunLoop polling, LoadSettingsBackgroundAsync   │
│  - NEW: must surface load/save failures instead of swallowing them       │
└──────────────┬───────────────────────────────────┬────────────────────────┘
              │ (2) commands.Send(cmd)             │ reads Connection.State
┌──────────────┴──────────────────────┐  ┌──────────┴────────────────────────┐
│  SaberCommandService (singleton)    │  │  SaberConnectionService (singleton)│
│  - protocol send/receive, tagging   │◄─┤  - ConnectionState machine         │
│  - NEW: per-command watchdog,       │  │  - connect/reconnect/backoff       │
│    write-confirmation hook          │  │  - NEW: single source of truth for │
│  - IsConnected flag (already exists)│  │    "is it safe to write right now" │
└──────────────┬───────────────────────┘  └──────────┬────────────────────────┘
              │ (3) SendBytesAsync(bytes) — JS interop
┌──────────────┴─────────────────────────────────────┴────────────────────────┐
│  usb.js / bluetooth.js                                                      │
│  - transferOut/write, retry-with-backoff, notifyDisconnected()              │
│  - NEW: opened/state check moved earlier, doesn't change public API shape  │
└───────────────────────────────────────────────────────────────────────────────┘
```

This milestone does **not** add a new layer or a new service. It adds a small amount of state and a few narrow contracts *inside* the three existing singletons, plus wires an event path from `SaberStateService`'s background loop up to the UI that doesn't currently exist reliably.

### Component Responsibilities (post-fix, deltas marked NEW)

| Component | Responsibility (existing) | Delta this milestone (NEW) |
|-----------|----------------------------|------------------------------|
| `SaberConnectionService` | Owns `ConnectionState` enum, connect/reconnect/backoff | Becomes the **single authority** callers query before any write — expose `bool CanWrite` / `EnsureConnectedOrThrow()` derived from `State` + `commands.IsConnected`, so command layer and JS layer both check the same source of truth instead of each keeping a separate flag |
| `SaberCommandService` | Command send/receive, tagging, per-command 20s timeout | Add a **watchdog timer** independent of the per-command `CancellationTokenSource`, a **pre-write connection check** before `SendChunked`, and **typed exceptions** (`NotConnectedException`, `CommandTimeoutException`) instead of swallow-and-return-`""` |
| `SaberStateService` | Central state, `RunLoop` polling, `LoadSettingsBackgroundAsync` | Replace bare `catch {}` in `RunLoop` and `LoadSettingsBackgroundAsync` with **typed catch + event raise**; add a `LastSettingsLoadError` property + `SettingsLoadFailed` event that UI can subscribe to unconditionally (not just the page that happened to call `LoadSettingsAsync()` synchronously) |
| `usb.js` / `bluetooth.js` | Device I/O, retry, disconnect notification | Tighten the "connected" check in `write()` (validate `_device.opened`, not just presence), no structural change — still throw on failure, .NET side now always catches typed |
| UI components (`Settings.razor.cs`, `SettingsPanel.razor.cs`, etc.) | Snackbar on caught exceptions in their own try/catch | Subscribe to the new `SettingsLoadFailed` / `OnError` events at a **shared** point (e.g. `MainLayout` or a small `IErrorNotificationService`) so background-loop failures surface even when no page-level `try/catch` is in the call chain |

## Recommended Approach (not a new project structure — a retrofit)

No new folders are needed. All changes live inside the three existing files plus a thin addition:

```
ProffieOS.Workbench/
├── Services/
│   ├── SaberConnectionService.cs   # add CanWrite / connection-state guard
│   ├── SaberCommandService.cs      # add watchdog, typed exceptions, pre-write check
│   ├── SaberStateService.cs        # replace bare catches, add LastSettingsLoadError + event
│   └── ErrorNotificationService.cs # NEW, optional, thin — see "Data Flow" below
├── wwwroot/js/
│   ├── usb.js                      # tighten write() guard only
│   └── bluetooth.js                # tighten write() guard only
```

### Structure Rationale

- **No new service boundary for "connection state machine"** — `SaberConnectionService.ConnectionState` already *is* the state machine (Disconnected/Connecting/Connected/Reconnecting). The gap isn't the state machine's existence, it's that nothing else *consults* it before a write. Adding a new state-machine service would duplicate `IsConnected` (already tracked separately in `SaberCommandService`) and create a second source of truth — worse, not better.
- **Watchdog belongs in `SaberCommandService`**, next to the per-command timeout it's meant to backstop, not as a separate service. The `/old` app's watchdog (`app.html:457-473`) is a periodic 10s check independent from the request/response cycle — it detects "callback never arrived" even if the promise/token machinery itself is stuck. That property (decoupled from the awaited task) is the reason it needs to be a **standalone timer**, not just a shorter `CancellationTokenSource`.
- **`ErrorNotificationService` is optional and thin** — only needed if there's currently no single place the UI can subscribe to background errors. Given `MainLayout.razor` already wraps every page, subscribing there (or via a tiny pub/sub singleton) is enough; this is not a new architectural layer, just a wiring fix.

## Architectural Patterns

### Pattern 1: Guard Clause at the Write Boundary (pre-write connection validation)

**What:** Before any `SendBytesAsync` / `SendChunked` call, check a single authoritative "connected" signal and fail fast with a typed exception rather than letting the JS layer throw a raw `Error('USB not connected')` that crosses the interop boundary as an opaque string.

**When to use:** Every write path — settings saves, preset saves, control commands. This is the direct fix for Concern #2 (uncaught "USB not connected" JS exception).

**Trade-offs:** Adds one property check per call (negligible cost). The only risk is a race between the check and the actual write (device disconnects in the gap) — this is inherent to WebUSB/BLE and can't be fully eliminated, only narrowed. The JS-side retry-and-notify path already exists as the last line of defense; the C# guard just prevents the *avoidable* case (writing when we already know we're disconnected).

**Example:**
```csharp
// SaberCommandService.cs
public class NotConnectedException(string cmd) : Exception($"Cannot send '{cmd}': not connected");

private async Task<string> Send2(string cmd)
{
    if (!IsConnected || SendBytesAsync is null)
        throw new NotConnectedException(cmd);
    // ...existing lock/timeout/send logic unchanged...
}
```
Callers (`SaberStateService.SaveXAsync`) already wrap in UI-level try/catch in some places — this just guarantees the exception is *typed* and thrown consistently instead of returning `""` silently or leaking a raw JS error string.

### Pattern 2: Independent Watchdog Timer, Decoupled from the Per-Command Timeout

**What:** A single periodic timer (started on connect, stopped on disconnect) that tracks "time since last successful response" across *all* commands, separate from the per-command `CancellationTokenSource`. If the gap exceeds a threshold, it force-fails the pending command and can proactively signal the connection layer to consider the link dead — mirroring `/old/app.html`'s `WatchDog()`/`RunWatchDog()` (10s tick, 20s staleness threshold, `Die("timeout")` on trip).

**When to use:** Add to `SaberCommandService` once, wired to `IsConnected`/`MarkConnected()`/`Die()` which already exist. This directly targets Concern #3 (fixed 20s timeout with no independent hang detection) and is the standard pattern for serial/RPC-style protocols with a single in-flight request slot — a watchdog is cheap because there's only ever one `_pendingTcs` to babysit (per the existing `SemaphoreSlim _lock` design).

**Trade-offs:** A second timer to manage (must be disposed alongside connection teardown — plug into existing `MarkConnected()`/`Die()`/`DisposeAsync()`). Threshold must be tuned per transport (BLE typically needs a longer allowance than USB due to characteristic write latency) — CONCERNS.md already recommends splitting the flat 20s into transport-specific values (10s USB / 15s BLE); the watchdog threshold should track whichever per-command timeout is active, not a separate hardcoded constant.

**Example (sketch, fits into existing class shape):**
```csharp
private Timer? _watchdog;
private DateTime _lastActivity = DateTime.UtcNow;

public void MarkConnected()
{
    IsConnected = true;
    _buffer.Clear();
    _tagNumber = 0;
    _useTagging = false;
    _lastActivity = DateTime.UtcNow;
    _watchdog ??= new Timer(_ => CheckWatchdog(), null, 5000, 5000);
}

private void CheckWatchdog()
{
    if (_pendingTcs is null) return; // nothing in flight, nothing to babysit
    if (DateTime.UtcNow - _lastActivity > _commandTimeout)
        Die("Watchdog: command hung past timeout");
}

// Reset _lastActivity wherever a response actually arrives: OnDataReceived, OnStatusReceived
```
This reuses the *existing* `Die()` method (already cancels `_pendingTcs`, clears buffer) — no new cancellation plumbing needed, just a second trigger path into it.

### Pattern 3: Typed, Escalating Exceptions Instead of Bare `catch {}` + Silent Return

**What:** Replace `catch { /* best-effort */ }` and `return ""` patterns with a small exception hierarchy (`NotConnectedException`, `CommandTimeoutException`, `ProtocolException`) caught at exactly one place per call chain (background loop, or UI action), always logged and always raised as an event/property the UI can observe.

**When to use:** `LoadSettingsBackgroundAsync` (Concern #1, the swing-on-speed/blade-length bug) and `RunLoop`'s per-iteration `catch { /* keep running */ }`. These aren't places to *stop* catching broadly (the loop must keep running even on transient errors) — they're places to stop catching *silently*. Catch broadly, but always: (a) capture the exception into a property, (b) fire an event, (c) log via `OnError` (which already exists on `SaberCommandService` but currently has no UI subscriber for this path).

**Trade-offs:** None significant — this is strictly additive (a property + an event) with no change to control flow or retry behavior. The only design decision is *granularity*: log every transient timeout during normal polling (noisy, but this is exactly what CONCERNS.md's recommendation #1 asks for — "Add detailed logging... to log which commands are failing") versus only the terminal failure (quieter, matches user-facing needs better). Recommendation: log all at DEBUG-equivalent (console), but only raise the *user-facing* event on the terminal outcome (e.g., "settings load failed after N attempts" rather than one toast per failed sub-command).

**Example:**
```csharp
// SaberStateService.cs
public Exception? LastSettingsLoadError { get; private set; }
public event Action<Exception>? SettingsLoadFailed;

private async Task LoadSettingsBackgroundAsync()
{
    try
    {
        await LoadSettingsValuesAsync();
        SettingsLoaded = true;
        LastSettingsLoadError = null;
        Notify();
    }
    catch (Exception ex)
    {
        LastSettingsLoadError = ex;
        commands.OnError?.Invoke($"Settings load failed: {ex.Message}"); // already exists, just unused today
        SettingsLoadFailed?.Invoke(ex);
        Notify(); // let UI redraw with an error indicator instead of a silent empty list
    }
}
```

## Data Flow

### Current (broken) flow for a background failure

```
RunLoop() → LoadInitialData() → _ = LoadSettingsBackgroundAsync()  (fire-and-forget)
                                        ↓
                              LoadSettingsValuesAsync() throws (timeout, disconnect, etc.)
                                        ↓
                              catch { /* best-effort */ }        ← dies here, nothing downstream knows
```
Only the *foreground* path (`Settings.razor.cs` calling `State.LoadSettingsAsync()` directly, with its own `ObserveLoadTask`) has any error visibility, and only if the user happens to be on the Settings page while the background load is still running. If the background load already completed (successfully or not) before the user navigates there, `SettingsLoaded` is left `false` with no error trail.

### Required flow after this milestone

```
RunLoop() → LoadInitialData() → _ = LoadSettingsBackgroundAsync()  (still fire-and-forget — that's correct, don't block RunLoop)
                                        ↓
                              LoadSettingsValuesAsync() throws
                                        ↓
                              catch (Exception ex) {
                                  LastSettingsLoadError = ex;
                                  SettingsLoadFailed?.Invoke(ex);   ← NEW: always fires, regardless of who's listening
                                  Notify();                        ← NEW: UI redraws even without a subscriber
                              }
                                        ↓
                    ┌───────────────────┴────────────────────┐
                    ↓                                        ↓
     Settings.razor (if currently mounted)      Any always-mounted shell (MainLayout / global toast host)
     subscribes to SettingsLoadFailed            subscribes to SettingsLoadFailed once, shows Snackbar
     directly, shows inline error state          regardless of which page is active
```

The key structural change is **not** adding a new event — it's making sure *something is always subscribed*, independent of page lifecycle. `MainLayout.razor` (already wraps every route per `ARCHITECTURE.md`) is the natural place: it can subscribe once in `OnInitialized()` for the lifetime of the app (singleton services + a layout component that's never disposed mid-session make this safe) and call `Snackbar.Add()` centrally. Page-level subscriptions (like `Settings.razor.cs`'s current pattern) can remain for page-specific inline states (e.g. a "retry" button), but should no longer be the *only* path an error can reach the user.

### Write-confirmation flow (new capability, not present today)

```
SaveBladeLengthAsync(blade, length)
        ↓
commands.IsConnected check (guard clause, Pattern 1)   ← throws NotConnectedException if not
        ↓
commands.Send($"set_blade_length {blade} {length}")
        ↓
   [NEW, optional per CONCERNS.md recommendation #5]
   readback: commands.Send($"get_blade_length {blade}", retry: true)
        ↓
   compare readback == length
        ↓
   if mismatch → throw ProtocolException("write not confirmed") → caught by UI → Snackbar
```
This is the most invasive addition and should be scoped narrowly (see Build Order below) — it doubles the round-trips for every save, so it's a candidate for opt-in per setting type (e.g., only for settings previously proven to silently fail) rather than blanket application.

## Suggested Build Order (dependency-driven, for roadmap phase sequencing)

The three fixes named in PROJECT.md ("connection-state validation," "timeout/watchdog rework," "error surfacing") have a natural dependency order — get this wrong and later phases have nothing reliable to build on:

1. **Error surfacing first** (typed exceptions + events, Pattern 3). This has no dependency on the other two and is the cheapest, lowest-risk change. It also becomes the **verification mechanism** for phases 2 and 3 — you can't confirm the watchdog or connection-guard fixes are working unless failures are visible. Doing this first means every subsequent phase can be manually verified by watching for a Snackbar/log instead of guessing from silent symptoms.

2. **Pre-write connection-state validation second** (Pattern 1). Depends on (1) only in the sense that its failure path should use the new typed exception; otherwise it's structurally independent of the watchdog. This is also the most direct fix for the reported "USB not connected" crash and is low-risk (single guard clause, no new timers/state).

3. **Timeout/watchdog rework third** (Pattern 2). This is the highest-complexity change (new timer, lifecycle tied to connect/disconnect, transport-specific thresholds per CONCERNS.md) and benefits most from (1) already being in place to observe its behavior, and from (2) already having tightened the connection-state signal the watchdog will key off of (`IsConnected`). Attempting the watchdog before error surfacing exists means a watchdog trip is just as invisible as today's silent timeout.

4. **Write-confirmation (readback verification) last, and scoped narrowly.** This is explicitly the highest-cost, highest-risk item (doubles round-trips, adds new protocol interactions) and is not required to fix the three reported bugs — it's a hardening measure. Per PROJECT.md's Out of Scope ("no full redesign") and the emphasis on minimal structural disruption, recommend making this **optional/deferred** to a later phase or limiting it to the specific settings the parity audit proves are currently silently lost, rather than universal readback-after-every-write.

**Cross-cutting, do throughout:** the parity audit (comparing every `/old` feature against the new app) should happen in parallel with or just before phase 1, since it determines exactly which `TryLoadXSetting` calls and save paths need the new error-surfacing wiring — it's audit data, not a blocking dependency for the code changes themselves.

## Anti-Patterns to Avoid During This Retrofit

### Anti-Pattern 1: Introducing a New "ConnectionStateMachine" Class Alongside the Existing `ConnectionState` Enum

**What people do:** See "connection-state machine" in the milestone name and build a fresh state-machine abstraction (e.g., a `Stateless`-style library, or a hand-rolled transition table) as a new service.

**Why it's wrong:** `SaberConnectionService.ConnectionState` already models the states needed (Disconnected/Connecting/Connected/Reconnecting) and is already the single place `StateChanged` fires from. A second state representation (plus `SaberCommandService.IsConnected` as a *third*, currently-separate boolean) multiplies the ways these can drift out of sync — which is closer to the root cause of Concern #2 than a missing state machine is. PROJECT.md explicitly rules out "new state machine, rebuilt from scratch" as out of scope.

**Do this instead:** Consolidate `IsConnected` to be *derived from* `ConnectionService.State == Connected` (or have `SaberCommandService` observe `SaberConnectionService.StateChanged` instead of maintaining its own flag independently) so there is exactly one source of truth, then add the guard clause described in Pattern 1 against that single source.

### Anti-Pattern 2: Fixing the Watchdog by Just Shortening the Per-Command Timeout

**What people do:** Change `TimeSpan.FromSeconds(20)` to a smaller number and call it "watchdog fixed."

**Why it's wrong:** The per-command `CancellationTokenSource` and a watchdog solve different failure modes. The CTS handles "this specific command took too long." A watchdog (as in `/old/app.html`) handles "the whole channel has gone quiet and nothing will ever complete," including cases where the awaited `Task` itself never resolves due to a bug in the response-parsing path (e.g., a malformed tagged response that never satisfies `ParseTaggedResponse`). Shortening the CTS timeout alone doesn't add that independent liveness check, and CONCERNS.md's own analysis (transient failures need backoff, not just tighter timeouts) argues against simply shrinking the window, which would increase spurious reconnects on slower boards.

**Do this instead:** Keep (or right-size per transport) the per-command timeout, and add the independent periodic timer from Pattern 2 as a *second, orthogonal* safety net.

### Anti-Pattern 3: Wrapping Every Existing Call Site in try/catch Individually

**What people do:** Grep for every `commands.Send(...)` call across `SaberStateService` and add local try/catch + Snackbar at each one.

**Why it's wrong:** This is exactly the pattern already in place today (component-level try/catch in `SettingsPanel.razor.cs`) and it's precisely what left `LoadSettingsBackgroundAsync` (a fire-and-forget call with no caller-side try/catch) uncovered. Duplicating this pattern everywhere increases surface area for future silent gaps rather than closing the systemic one, and directly conflicts with the "minimal structural disruption" constraint by touching dozens of call sites instead of a handful of chokepoints.

**Do this instead:** Centralize error capture at the **chokepoints** already identified — `Send2()` in `SaberCommandService` (all commands funnel through here) and `LoadSettingsBackgroundAsync`/`RunLoop` in `SaberStateService` (the two fire-and-forget entry points) — and let those chokepoints raise the shared event described in Pattern 3 / the Data Flow section. Fewer places to get right, and it matches the "audit + targeted fix" framing from PROJECT.md rather than a call-site-by-call-site patch.

## Integration Points

### Internal Boundaries (all pre-existing, unchanged shape)

| Boundary | Communication | Notes for this milestone |
|----------|---------------|---------------------------|
| UI ↔ `SaberStateService` | Direct method calls + `StateChanged` event | Add `SettingsLoadFailed` event alongside existing `StateChanged`; don't repurpose `StateChanged` itself for errors (keep concerns separated — one event for "data changed," one for "an operation failed") |
| `SaberStateService` ↔ `SaberCommandService` | Direct method calls (`commands.Send(...)`) | No new call shape; `Send()`/`Send2()` should throw typed exceptions instead of swallowing to `""`, which is a breaking change for any caller relying on `""` as a sentinel (`GetOptional()` currently checks `s.StartsWith("Whut?")` or empty — audit call sites that treat `""` as "unsupported" vs. "failed" before changing this, since these are semantically different today and conflating them was itself part of Concern #1) |
| `SaberCommandService` ↔ `SaberConnectionService` | `SaberConnectionService` reads `commands.IsConnected`/calls `commands.MarkConnected()`/`Die()`; `commands.OnDisconnectedAsync` event flows the other way | Natural place to consolidate the "single source of truth" from Anti-Pattern 1 — inject `SaberConnectionService` state observation into `SaberCommandService`, or vice versa, but pick one direction and stop duplicating |
| `SaberCommandService`/`SaberConnectionService` ↔ `usb.js`/`bluetooth.js` | JS interop via `DotNetObjectReference` / `IJSRuntime.InvokeAsync` | JS-side tightening (validate `_device.opened`) is additive and doesn't change the interop contract; `write()` continues to throw, .NET side now always catches with a typed wrapper instead of the current mixed handling |

### External Services

Not applicable — no external services beyond the browser's WebUSB/Web Bluetooth APIs, which are already fully wrapped by the existing JS interop layer and are out of scope to change structurally.

## Sources

- Direct code reading (HIGH confidence, primary source of truth for this retrofit): `ProffieOS.Workbench/Services/SaberStateService.cs`, `SaberConnectionService.cs`, `SaberCommandService.cs`, `ProffieOS.Workbench/wwwroot/js/usb.js`, `ProffieOS.Workbench/Pages/Settings.razor.cs`
- `/old/app.html` lines 451-473 (existing watchdog pattern used as the reference design for Pattern 2) — HIGH confidence, same repo, author-owned comparison point (though `/old` itself is not to be modified per PROJECT.md constraints)
- `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/CONCERNS.md` (prior GSD codebase mapping — HIGH confidence, produced from direct code inspection of this repo)
- `.planning/PROJECT.md` (milestone scope and constraints — HIGH confidence, authored by project owner)

---
*Architecture research for: Blazor WASM WebUSB/WebBLE reliability retrofit (ProffieOS Workbench)*
*Researched: 2026-07-01*

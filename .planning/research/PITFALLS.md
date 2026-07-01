# Pitfalls Research

**Domain:** WebUSB/WebBluetooth device-communication reliability in a Blazor WASM (.NET) app via JS interop
**Researched:** 2026-07-01
**Confidence:** MEDIUM (browser-API spec gaps and Blazor exception-pipeline behavior are confirmed by primary sources — WICG/WebBluetoothCG spec-repo issues and the dotnet/aspnetcore team's own by-design ruling; project-specific severity/frequency assessments are inferred from `CONCERNS.md` and are therefore lower confidence than the general pattern)

## Critical Pitfalls

### Pitfall 1: Fire-and-forget async work silently detaches from Blazor's exception pipeline

**What goes wrong:**
A background method (`async void`, or a `Task` kicked off without `await` — e.g. `_ = LoadSettingsBackgroundAsync()` or `Task.Run(...)` never awaited) throws, and the exception simply vanishes. No `ErrorBoundary` fires, no global handler runs, nothing appears in the console unless you added your own catch. This is exactly the shape of the diagnosed bug in `SaberStateService.LoadSettingsBackgroundAsync()` (`SaberStateService.cs:186-194`), which wraps its work in a bare `catch {}` — but the underlying danger is broader: even *without* the bare catch, an unawaited fire-and-forget call would have swallowed the exception anyway, just less visibly.

**Why it happens:**
This is confirmed **by-design** behavior in Blazor WASM, not a bug the framework will fix (dotnet/aspnetcore#24787): fire-and-forget async operations execute outside the synchronous execution context that Blazor's global exception handler monitors, so exceptions thrown inside them never reach `ErrorBoundary` or `DispatchExceptionAsync`. Developers reach for fire-and-forget because background polling/loading naturally wants to run without blocking the caller — but the framework offers no safety net for that pattern.

**How to avoid:**
- Never leave an async background method un-awaited *and* uncaught at the same time. If the call site truly cannot await it (e.g. starting a background loop from a sync context), the method itself must have an internal try-catch around its *entire* body that funnels every exception through an explicit reporting path (event, logger, `OnError` callback) — never a bare `catch {}`.
- Convert `async void` to `async Task` everywhere except true DOM/UI event handlers (where Blazor already handles `async Task` event handlers correctly and triggers re-render).
- Treat "settings failed to load" and "write failed" as first-class UI states, not silent no-ops — the user must be able to tell the difference between "still loading" and "failed silently."

**Warning signs:**
- Any `catch { }` (empty body) or `catch (Exception) { /* comment only */ }` in the codebase — grep for this pattern across all services.
- Any method invoked with a leading `_ = SomeAsyncMethod()` or a bare `SomeAsyncMethod();` call (discarding the task) without an internal catch.
- User reports of "it just doesn't work, no error shown" — the signature symptom of this pitfall.

**Phase to address:**
Phase 1 (root-cause fix for the diagnosed settings-load bug) — but treat it as a *pattern* to eliminate everywhere, not a one-line fix in `LoadSettingsBackgroundAsync()`. Grep the whole service layer for the same shape before closing the phase.

---

### Pitfall 2: JS interop write() checks connection state too early/loosely, so disconnect-during-write throws uncaught

**What goes wrong:**
`usb.js:217`'s `write()` checks `_endpointOut === -1 || !_device` before writing — a check that's stale by the time the actual `transferOut()` call happens if the device unplugs in between. The resulting `Error('USB not connected')` (or the browser's own `NetworkError` from `transferOut` itself) can escape through the JS interop boundary as an uncaught JS exception that Blazor's marshaling layer doesn't wrap cleanly, producing the "Command failed: USB not connected ... at Object.write ... at invokeJSJson" crash reported in production.

**Why it happens:**
This isn't just a code-quality slip — it reflects a genuine, **unresolved WebUSB spec gap** (WICG/webusb#219): when a device disconnects mid-`transferOut`/`transferIn`, the spec only guarantees a generic `NetworkError`, and the `disconnect` event on `navigator.usb` fires *after* that error on Chrome/Windows. There is no reliable way, at the moment of the throw, to distinguish "device was unplugged" from "some other transfer error" — so a "check `_device` exists" guard is fundamentally insufficient; the device object can still exist and report "opened" while the underlying transfer fails.

**How to avoid:**
- Do not rely on a single boolean pre-check to prevent write failures — it narrows the window but can never close it. Instead, wrap every `transferOut`/`transferIn` call in its own try-catch *in JS*, and translate any rejection (regardless of message text) into a structured error object passed back to .NET (e.g. `{ code: 'transfer-failed', message, likelyDisconnect: true }`) rather than a raw thrown `Error`.
- On the C# side, every JS interop call into the write/read path must be wrapped in try-catch for `JSException` — per Microsoft's own guidance, JS errors are never auto-caught by Blazor and must be caught explicitly at each `InvokeAsync`/`InvokeVoidAsync` call site. Treat "SendChunked calls SendBytesAsync without checking IsConnected" as the systemic instance of this, not a single call site.
- After any transfer failure, immediately re-validate device state (`device.opened`, or re-query `navigator.usb.getDevices()`) rather than trusting error message text — because the spec explicitly does not guarantee the error identifies disconnection.
- Treat any write failure as "assume disconnected, transition state machine to Disconnected/Reconnecting" rather than trying to classify the error and only sometimes reconnecting — the spec gap means you can't reliably tell the difference anyway.

**Warning signs:**
- Any JS `write()`/`read()` function whose only connection guard is a property check (`!_device`, `_endpointOut === -1`) with no try-catch around the actual transfer call.
- C# call sites that invoke a JS interop method without a surrounding try-catch, or that only catch at a much higher layer (e.g. only in `Send2()` but not in `SendChunked()` which calls the JS function directly).
- Error messages reaching users that look like raw JS stack traces ("at Object.write (js/usb.js:217:56)") — a sign the exception escaped .NET's catch entirely rather than being translated into a domain error.

**Phase to address:**
Phase 1 (targeted fix for the diagnosed USB write crash) — but the fix must be "wrap transfer calls in JS + validate/reconnect after any failure," not "add one more state check," since the spec gap means checks alone cannot fully prevent this.

---

### Pitfall 3: WebBluetooth GATT operations aren't serialized, causing "operation already in progress" failures that masquerade as flakiness

**What goes wrong:**
If more than one GATT read/write/notification-subscribe is in flight concurrently on the same device, the browser can throw a "GATT operation already in progress" error — a real, long-standing, **unresolved gap** in the Web Bluetooth spec itself (WebBluetoothCG/web-bluetooth#188, open since 2015, spec maintainers confirming "there is no way to query the state of the implementation other than responding to the errors"). This class of error is a strong candidate for explaining the recurring, never-fully-diagnosed BLE flakiness across the project's history (PRs "fix?", "fiix?", "fiiix?") — it's exactly the kind of transient, hard-to-reproduce, platform-dependent failure that invites trial-and-error patching instead of systematic fixes, because the browser gives you no query API to detect the unsafe state in advance.

**Why it happens:**
Developers assume the browser/OS Bluetooth stack queues concurrent GATT calls the way many other async APIs do. It doesn't (or doesn't reliably, across platforms) — the spec puts the burden of serialization entirely on the page author. Any code path where a settings-load loop, a periodic state-poll (`RunLoop`), and a user-triggered save could all issue GATT operations without a shared queue/lock is exposed to this.

**How to avoid:**
- Introduce a single, explicit per-connection command queue/lock (the codebase already has `_lock` in `SaberCommandService` for the command protocol — verify it actually wraps *every* path that touches the JS `write`/`read`/`startNotifications` calls, including any BLE-specific calls that might bypass the shared queue, e.g. password handshake, direct notification subscription during connect).
- Never issue two JS interop BLE calls concurrently just because they're logically independent (e.g. background settings poll firing while a user-initiated save is in flight) — serialize at the connection-service level, not just at the command level.
- When this error is caught, do not treat it as a "device is broken" signal — retry it (it's a self-inflicted client-side race, not a device fault), but retry through the same serialized queue so the retry doesn't collide again.

**Warning signs:**
- Any JS BLE call path (`bluetooth.js`) invoked from more than one entry point in C# without going through the shared `_lock`/queue.
- Error strings containing "GATT operation" or "already in progress" in past bug reports, browser console logs, or the PR history — worth actively grepping past issue/PR descriptions for this exact phrase since it would confirm this pitfall is the root cause of some of the "fix?" commits.
- BLE-only flakiness that doesn't reproduce on USB — a signal the bug is in concurrent-GATT-access territory rather than the shared command protocol.

**Phase to address:**
The audit phase should explicitly search prior BLE bug history/logs for this error signature before writing any BLE fix — given the author has no BLE test hardware, confirming this as *the* root cause (vs. guessing) via log/code inspection is the only high-confidence path available. If confirmed, the fix (centralize all BLE GATT calls through one serialized queue) is a natural fit for the same phase that addresses the write-validation pitfall above, since both are about untrusted concurrent access to the JS interop boundary.

---

### Pitfall 4: Fixed timeout + no independent watchdog conflates "still working" with "hung," causing false failures and undetected hangs

**What goes wrong:**
A single fixed timeout (20s in the current code) used both to bound "how long we wait for a response" and implicitly to detect "the device stopped responding" does both jobs poorly: it's too long to give fast user feedback when a command actually hung early (a "write succeeded but response never arrived" case sits idle for the full 20s before anything happens), and too short/rigid for legitimately slow operations (large payloads, slow SD card writes, congested BLE). The result matches the diagnosed symptom exactly: frequent timeouts on saves, with no distinction between "device is slow" and "device stopped responding entirely."

**Why it happens:**
It's simpler to write one timeout than to separate "maximum time to wait for completion" from "how do I know progress is still happening." A true watchdog needs either periodic liveness signals from the device/transport or a separate shorter check that fires *before* the outer timeout to distinguish "no progress at all" from "still receiving/sending data." The old app's separate 10s watchdog (referenced in `CONCERNS.md`) is exactly this second mechanism, and it was dropped in the rewrite.

**How to avoid:**
- Separate the "overall command deadline" from "hang detection." Use a watchdog that resets on any observed progress (chunk sent, partial response byte received) and only fires when there has been *zero* progress for N seconds — independent from the total-completion timeout, which can be longer.
- Make timeout duration connection-type-aware (USB is typically faster/more reliable than BLE; a single 20s value for both is already known to be wrong per the audit).
- Add exponential backoff (with jitter) to retries instead of fixed short delays (currently 50ms/25ms/20ms across different retry loops) — fixed short delays under load amplify contention rather than relieving it, especially relevant given the GATT-serialization pitfall above (a retry storm during "operation in progress" errors is the last thing you want).
- Add inter-chunk backpressure awareness in `SendChunked()` — sending 20-byte chunks in a tight loop with no pacing assumes the receiving buffer always has room; add either a small delay or (better) flow-control awareness if the protocol supports it.

**Warning signs:**
- Any single `TimeSpan.FromSeconds(N)` constant reused across multiple purposes (completion deadline AND hang detection AND retry cutoff) — a sign the concerns aren't actually separated.
- Retry loops with fixed millisecond delays that don't grow with attempt count.
- User reports of "save appeared to hang, then failed" vs. "save failed instantly" both mapping to the same code path/timeout value — you can't tell these apart in the current design, and you should be able to.

**Phase to address:**
The dedicated timeout/watchdog-rework phase already scoped in `PROJECT.md` ("save/write timeout strategy reworked... watchdog for hung commands, backoff on retry"). This pitfall confirms that phase needs a *watchdog mechanism*, not just tuned timeout numbers — retuning 20s to a different fixed number without adding hang-detection independent of the deadline just moves the same problem.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|--------------------|-----------------|------------------|
| Bare `catch { }` in background loops | Fast to write, "app doesn't crash" | Silent data/feature loss, undiagnosable production bugs (this milestone's root cause) | Never |
| Single fixed timeout for all connection types/operations | Simple, one constant to reason about | Wrong for BLE vs USB, conflates hang-detection with deadline | Only as a temporary placeholder before the watchdog rework phase |
| Retrying without backoff | Fast to implement, "just try again" | Amplifies device/GATT-stack contention under load, especially post-disconnect | Never for BLE; borderline acceptable for USB single-command retries if capped low (2-3 tries) |
| Treating JS interop calls as "can't fail" (no try-catch at every call site) | Less boilerplate | Uncaught JSException escapes to console as an unhandled crash, exactly the diagnosed bug | Never |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|-----------------|-------------------|
| WebUSB (`transferOut`/`transferIn`) | Trusting the error message/type to identify "device disconnected" vs. other failures | Treat every transfer rejection as a possible disconnect; re-validate device state and let the connection state machine decide, don't parse error text |
| Web Bluetooth (GATT read/write/notify) | Issuing concurrent GATT operations from independent code paths (poll loop + user action) | Serialize ALL GATT access for a given device through one queue/lock, including handshake/notification-subscribe calls, not just the main command protocol |
| Blazor `IJSRuntime` interop | Assuming JS exceptions propagate through Blazor's global/ErrorBoundary handling automatically | Wrap every `InvokeAsync`/`InvokeVoidAsync` in try-catch for `JSException`; never assume a higher-layer catch will save you if a lower layer calls JS directly |
| Blazor background/polling loops | Starting background async work with `async void` or an unawaited `Task.Run` | Use `async Task` + explicit internal try-catch that reports errors via an event/logger; never rely on the framework to surface the failure |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|-----------------|
| Tight-loop chunked writes with no inter-chunk delay/backpressure | Writes silently fail or stall on larger payloads/slow devices | Add pacing or flow-control awareness between chunks | Breaks first on BLE (smaller MTU, slower stack) and on larger settings payloads |
| Sequential per-setting loads each with an independent full-length timeout | Settings load can take 30s-200s worst case (per audit) | Load settings in parallel where protocol allows, or short-circuit remaining loads after first timeout with a clear "partial load" state | Breaks as soon as more than ~5-10 settings are queried sequentially |
| Fixed short retry delays under contention | Retry storms during GATT "operation in progress" or USB re-enumeration | Exponential backoff with jitter | Breaks as soon as any transient contention occurs (BLE reconnect races, GATT queue collisions) |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Passing raw JS error messages/stack traces to end users unfiltered | Minor: could leak internal file paths (`js/usb.js:217`) or implementation details in error UI | Translate JS errors into user-facing domain messages before displaying; keep raw stack traces in console/dev logs only |
| No validation that responses are tied to the request that triggered them (if tagging/sequencing is weak) | A stale/mismatched response from a hung earlier command could be misapplied to a later command's `TaskCompletionSource` | Ensure the tagged-response protocol correctly discards/ignores responses whose tag doesn't match the currently pending command before wiring in the watchdog rework |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|--------------|-------------------|
| Silent settings-load failure (current bug) | User sees blank/missing values with no indication anything went wrong | Show explicit loading/error/partial states per settings group, driven by the exception now being surfaced instead of swallowed |
| Silent save timeout (current bug) | User believes a setting saved when it didn't; discovers data loss later, possibly after disconnecting | Every save must resolve to a visible success/failure state before the user can navigate away; consider read-back verification for critical settings |
| Generic "USB not connected" crash surfaced as a raw error dialog | User doesn't know if this is transient (unplug/replug) or fatal | Map to a specific "device disconnected — reconnect?" UI state feeding into the same reconnection state machine already used for BLE |

## "Looks Done But Isn't" Checklist

- [ ] **Exception handling in background loops:** Often missing — verify no `catch { }` (empty body) remains anywhere in `SaberStateService`/`SaberConnectionService`/`SaberCommandService`; grep the whole service layer, not just the reported line numbers.
- [ ] **JS interop write/read paths:** Often missing try-catch at *every* call site, not just the outermost one — verify `SendChunked()` and any other direct JS call sites (not only `Send2()`) have their own guard.
- [ ] **BLE GATT serialization:** Often missing coverage for handshake/notification-subscribe calls made outside the main command queue — verify ALL BLE JS calls funnel through the same lock, not just command sends.
- [ ] **Watchdog vs. timeout separation:** Often "fixed" by just changing the timeout number — verify the fix actually adds independent hang-detection, not just a different fixed duration.
- [ ] **Reconnect logic parity between BLE and USB:** Often asymmetric — verify both paths validate connection state identically before writes, given the audit found the check exists conceptually but isn't applied consistently.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|-----------------|-----------------|
| Silent exception swallowing already shipped to users | LOW | Add logging + surfaced error event; no data migration needed, purely additive |
| Uncaught JS exception crash reports already collected | LOW | Retrofit try-catch at identified call sites; verify against the existing forum-reported repro steps |
| BLE flakiness history with unclear root cause | MEDIUM | Systematic log-grep of past issue reports for "GATT operation" / disconnect-timing error strings before attempting further code changes; avoid another round of blind "fix?" commits |
| Users who already lost settings to timeout bugs | HIGH (trust, not code) | Ship the fix with clear release notes; no way to recover already-lost data, but consider adding read-back save verification going forward so this class of loss becomes detectable in real time |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|--------------------|----------------|
| Fire-and-forget swallowed exceptions | Phase 1: Settings-load fix | Grep confirms zero empty `catch {}` blocks remain; manually disconnect mid-load on real USB hardware and confirm an error surfaces in the UI |
| JS write() uncaught exception on disconnect | Phase 1 (or dedicated write-path phase): USB/BLE write validation | Manually unplug the board mid-save on real hardware; confirm no raw JS stack trace reaches the console/UI, and the app transitions to a disconnect/reconnect state instead |
| GATT operation concurrency (BLE flakiness root cause candidate) | Audit phase, before any BLE code changes | Log/code review confirms all BLE JS calls pass through one serialized queue; since no BLE hardware is available, this is verified by code inspection + historical error-string grep, not live testing |
| Fixed timeout conflating hang-detection with deadline | Dedicated timeout/watchdog rework phase (already scoped in PROJECT.md) | Confirm an independent watchdog exists that fires on "no progress" separately from the overall completion deadline; test on USB by artificially delaying board responses if possible |

## Sources

- [Blazor Basics: Handling Errors and Exception Logging (Telerik)](https://www.telerik.com/blogs/blazor-basics-handling-errors-exception-logging-blazor-applications) — MEDIUM confidence
- [Handle errors in ASP.NET Core Blazor apps (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-8.0) — MEDIUM confidence
- [ASP.NET Core Blazor JavaScript interoperability (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0) — MEDIUM confidence
- [dotnet/aspnetcore#24787 — global exception handler does not catch fire-and-forget async exceptions](https://github.com/dotnet/aspnetcore/issues/24787) — MEDIUM confidence, primary source (framework team ruling: by design)
- [WICG/webusb#219 — Can't detect device disconnection when transferIn is waiting](https://github.com/WICG/webusb/issues/219) — MEDIUM confidence, primary source (spec repo, open gap)
- [WICG/webusb#223 — A transfer error has occurred](https://github.com/WICG/webusb/issues/223) — LOW/MEDIUM confidence (corroborating community reports)
- [WebBluetoothCG/web-bluetooth#188 — GATT operation in progress - how to handle it?](https://github.com/WebBluetoothCG/web-bluetooth/issues/188) — MEDIUM confidence, primary source (spec repo, open gap since 2015)
- [WebBluetoothCG/web-bluetooth#256 — Clarify GATT connection status / behavior](https://github.com/WebBluetoothCG/web-bluetooth/issues/256) — MEDIUM confidence, corroborating
- [Communicating with Bluetooth devices over JavaScript (Chrome for Developers)](https://developer.chrome.com/docs/capabilities/bluetooth) — MEDIUM confidence, official docs
- [AWS Builders' Library: Timeouts, retries, and backoff with jitter](https://aws.amazon.com/builders-library/timeouts-retries-and-backoff-with-jitter/) — MEDIUM confidence, industry-standard pattern reference
- `.planning/codebase/CONCERNS.md` — project-specific diagnosed bugs (primary internal source)
- `.planning/codebase/CONVENTIONS.md` — project-specific current error-handling/timeout conventions (primary internal source)

---
*Pitfalls research for: WebUSB/WebBluetooth device-communication reliability, Blazor WASM JS interop*
*Researched: 2026-07-01*

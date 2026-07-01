# Stack Research

**Domain:** Reliability/stabilization pass for a Blazor WebAssembly (.NET 10) app doing WebUSB/Web Bluetooth JS interop
**Researched:** 2026-07-01
**Confidence:** MEDIUM-HIGH (Microsoft Learn + Polly official docs cross-verified; some WASM-specific nuances inferred from general JS-interop docs since no source addresses this exact combination directly)

## Context Recap

This is a stabilization milestone, not a rebuild. The existing stack is `net10.0` + `MudBlazor 9.1.0`, three singleton services (`SaberStateService`, `SaberConnectionService`, `SaberCommandService`), and a thin JS interop layer (`usb.js`, `bluetooth.js`) that's already required by the browser APIs. Nothing below proposes replacing that shape. The question is what to add/change to fix: silent exception swallowing, no watchdog on hung commands, fixed 20s timeout, late connection-state checks, and no backoff strategy.

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| Polly.Core | 8.7.0 | Retry/timeout/circuit-breaker pipelines around JS interop calls | Current de facto standard .NET resilience library (Microsoft's own `Microsoft.Extensions.Http.Resilience` is built on it). On .NET 8+ target frameworks `Polly.Core` has **zero external dependencies** — it doesn't drag in unrelated packages, which matters for a WASM app where every referenced assembly adds to download size. It is fully `async`/`ValueTask`-based with no reliance on the thread pool, so it runs correctly on WASM's single-threaded runtime (delays use `Task.Delay`, which the WASM synchronization context supports natively). Confidence: MEDIUM (cross-verified via NuGet.org + Polly official docs, both official sources, but no source specifically confirms WASM compatibility — inferred from its dependency-free/async-only design). |
| `Microsoft.Extensions.Logging` (built-in, already part of `Microsoft.AspNetCore.Components.WebAssembly`) | matches `net10.0` | Structured logging for background tasks, replacing bare `catch {}` and ad-hoc `console.info` calls | Already referenced transitively by the Blazor WASM SDK — no new package needed. `ILogger<T>` injected into the three services gives every swallowed exception a category, level, and message instead of silent disappearance. In WASM, the default `WebAssemblyConsoleLogger` writes to the browser devtools console, matching where the JS-side logs (`[UsbInterop]`, `[BluetoothInterop]`) already go — so C# and JS logs land in the same place developers already look. Confidence: HIGH (built-in framework behavior, not a research claim). |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Polly.Core `TimeoutStrategy` | 8.7.0 (part of Polly.Core) | Per-connection-type command timeout (replacing the hard-coded `TimeSpan.FromSeconds(20)` in `SaberCommandService.cs:141`) | Wrap the `Send`/`Send2` call in a pipeline with `AddTimeout(TimeSpan)`. Configure two named pipelines (or one pipeline built per connection type) — shorter for USB (which the CONCERNS.md audit already recommends at ~10s), longer for BLE (~15s) to account for GATT's inherently higher and more variable latency. |
| Polly.Core `RetryStrategy` with exponential backoff | 8.7.0 | Replace the flat 50ms-delay/20-retry loop in `SaberCommandService.cs:93-110` | `AddRetry` with `BackoffType.Exponential`, a capped `MaxDelay` (e.g. 500ms as CONCERNS.md already suggests), and `MaxRetryAttempts` tuned per operation (fewer retries for user-initiated writes so the UI doesn't appear to hang; more retries tolerated for background polling). Exponential backoff specifically matters for BLE, where "GATT operation already in progress" is a *rate* problem, not a connectivity problem — retrying immediately makes it worse. |
| Polly.Core `CircuitBreakerStrategy` (optional, evaluate but don't over-apply) | 8.7.0 | Stop hammering a board that has gone consistently unresponsive (e.g., mid-reconnect-storm) | Only worth adding around the *write* path, not every read. A circuit breaker after N consecutive command failures short-circuits further attempts and surfaces a clear "device unresponsive" state to `SaberConnectionService`, rather than letting each of 10 reconnect attempts independently timeout for 20s each (200s of visible hang, per CONCERNS.md's own math). This is optional polish — the reliability floor (retry+timeout+logging) is the priority; add this only if time permits within the milestone. |
| A single `SemaphoreSlim`-backed **command queue/serializer** for BLE GATT calls (already partially present via `SaberCommandService._lock`) | n/a — pattern, not a package | Prevent "GATT operation already in progress" by construction rather than by catching it | Web Bluetooth's GATT server has no query-able busy state and throws `InvalidStateError`/`NetworkError` when two GATT operations overlap on the same characteristic. The existing `_lock` in `SaberCommandService` already partially does this for commands — audit whether `BluetoothInterop`'s notify-subscribe/write/read-back paths and connection-lifecycle calls (`connect`, `getPrimaryService`, `getCharacteristic`) also funnel through this same serialization, since those calls happen outside the command queue during connect/reconnect and are a plausible source of the recurring BLE "fix?"/"fiix?"/"fiiix?" churn noted in PROJECT.md. |

### What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `Microsoft.Extensions.Http.Resilience` / `AddStandardResilienceHandler` | This package is purpose-built to wrap `HttpClient`'s `DelegatingHandler` pipeline. There is no `HttpClient` in the WebUSB/BLE path — the "request" is a JS interop call to `navigator.usb`/`navigator.bluetooth`, not an HTTP request. Pulling this package in gets you `HttpClient`-specific abstractions you can't use and unnecessary WASM payload weight. | `Polly.Core` directly — build a `ResiliencePipeline` by hand with `ResiliencePipelineBuilder` and call `pipeline.ExecuteAsync(ct => JS.InvokeAsync<T>(...), cancellationToken)`. This is the documented, supported way to apply Polly to an arbitrary delegate that isn't an `HttpClient` call. |
| A full rewrite of the connection/command layer into a formal state machine library (e.g. Stateless) | Explicitly out of scope per PROJECT.md — "Full connection/command layer redesign... deferred; this milestone is audit + targeted fixes, not a rebuild." Introducing a state-machine library is exactly the kind of net-new architectural surface this milestone is meant to avoid. | Keep the existing `ConnectionState` enum in `SaberConnectionService`; add a `IsConnected`/preflight check method as CONCERNS.md recommends, without introducing a new abstraction layer. |
| Custom retry/backoff hand-rolled again in C# (as currently exists, ad hoc, in three different places: `SaberCommandService.Send()`, `usb.js` write retry, `bluetooth.js` write retry) | The current bugs (frequent timeout-caused data loss, no backoff) exist precisely because retry/backoff logic is duplicated and inconsistent across three layers with no shared policy. Adding a fourth bespoke retry loop compounds the problem instead of fixing it. | Centralize retry/backoff *policy* in one place (the C# side, via Polly) and simplify the JS side to a single low-level "try once, throw on failure" operation — let C# decide whether/how to retry. This also directly serves the stated constraint "minimize new JS surface area, don't grow it." |
| Blazor Server-style `JSDisconnectedException` handling patterns lifted wholesale from Microsoft's Blazor Server docs | Most published guidance for `JSDisconnectedException` (e.g., wrapping module disposal in try/catch to survive SignalR circuit loss) is written for Blazor **Server**, where the .NET runtime and the browser are different processes connected over SignalR. In Blazor **WebAssembly**, .NET and JS run in the same browser process/thread — there is no SignalR circuit to lose, so this specific exception type is largely a non-issue here. Don't spend milestone effort defensively coding around a Server-hosting-model failure mode. | Focus WASM error handling on: (1) `JSException` thrown when JS code inside `usb.js`/`bluetooth.js` throws — this is the real, relevant exception type per the reported bug ("Command failed: USB not connected... at Object.write"); and (2) `TaskCanceledException`/`OperationCanceledException` from your own timeout `CancellationToken`, not from Blazor's interop transport layer. |
| A generic/shared timeout constant reused unchanged for both USB and BLE | The current single `TimeSpan.FromSeconds(20)` in `SaberCommandService.cs:141` is the direct cause flagged in CONCERNS.md ("may still be too short" for large payloads, "no watchdog to detect stuck I/O"). Treating USB and BLE identically ignores that they have fundamentally different latency profiles (USB: sub-second normally; BLE: GATT connection-interval-bound, can legitimately take longer per operation). | Parameterize timeout by connection type at the point `SaberCommandService` is constructed/configured (USB ~10s, BLE ~15s, per CONCERNS.md's own recommendation), and add a separate, shorter **watchdog** check (e.g., every 5s) that can proactively flag "no bytes received in N seconds" before the full command timeout elapses — mirroring what `/old/app.html` already did with its 10s watchdog. |

## Patterns for the Specific Problems in Scope

### 1. Silent exception swallowing (`SaberStateService.LoadSettingsBackgroundAsync`, `catch {}`)

**Pattern:** Never use a bare `catch { }`. At minimum, log via injected `ILogger<SaberStateService>`; ideally also raise a typed event (`SettingsLoadFailed?.Invoke(ex)`) so the UI layer (Settings page) can show an actual error state instead of a silently-empty settings panel. This is a code-pattern fix, not a library — no dependency needed beyond the built-in `Microsoft.Extensions.Logging.Abstractions` already available through the Blazor WASM SDK.

**Confidence:** HIGH — this is standard .NET exception-handling hygiene, not a domain-specific finding.

### 2. Late connection-state validation before JS interop write (`usb.js:217`, `SaberCommandService.SendChunked`)

**Pattern:** Two-layer check:
- **C# side (authoritative):** Check `SaberConnectionService.State == ConnectionState.Connected` (or an equivalent `IsConnected` flag on `SaberCommandService`) immediately before invoking `JS.InvokeAsync`, and throw a typed `SaberNotConnectedException` (not a raw JS exception) if not connected — this gives calling code a single, consistent exception type to catch regardless of whether the failure was detected in C# or bubbled from JS.
- **JS side (last-resort/authoritative-in-the-moment):** Check the actual browser object's live state right before the transfer call (`_device.opened` for WebUSB, `_device.gatt.connected` for Web Bluetooth) rather than only checking that a reference exists — a non-null reference to a closed/disconnected device is exactly the race CONCERNS.md identified.

Neither layer alone is sufficient: the C# check can go stale between the check and the call (TOCTOU race inherent to async JS interop), and the JS check alone means every failure surfaces as an unstructured JS exception. Do both.

**Confidence:** HIGH for the general TOCTOU-mitigation pattern (well-established); MEDIUM for the exact browser property names (`_device.opened`, `gatt.connected`) which should be double-checked against current MDN/WICG spec text at implementation time, since browser API surface details shift.

### 3. JS exceptions crashing/silently escaping instead of surfacing (`"Command failed: USB not connected"`)

**Pattern:** In Blazor WASM specifically (not Server):
- A JS-thrown error during `IJSRuntime.InvokeAsync<T>(...)` surfaces in C# as a **catchable `Microsoft.JSInterop.JSException`**. It does not crash the WASM runtime by itself — but if the call site doesn't wrap it in try/catch, it propagates up as an unhandled exception in whatever async chain called it (which, for a fire-and-forget background task, is exactly how it currently disappears silently).
- Every JS interop call site in the write/read path should catch `JSException` specifically (not blanket `Exception`) at the point closest to the JS boundary, translate it into a domain-specific exception (`SaberCommunicationException` or similar) with the original message preserved, and let *that* propagate to callers — so UI code and logging code deal with one exception vocabulary instead of raw JS error strings.
- `JSDisconnectedException` is a Blazor **Server** (SignalR circuit loss) concern; it is not the relevant exception type for a WASM app calling `navigator.usb`/`navigator.bluetooth` — don't invest effort defending against it here.

**Confidence:** MEDIUM — the `JSException`-is-catchable behavior and the WASM-vs-Server distinction for `JSDisconnectedException` are both consistent with Microsoft Learn's JS interop and error-handling docs, but no single source addresses this exact "WebUSB write throws inside JS, propagates through Blazor WASM interop" scenario verbatim; this is a reasonable extrapolation from documented general interop behavior, not a directly verified claim.

### 4. Fixed 20s timeout with no watchdog for hung I/O

**Pattern:** Two independent timers, not one:
- **Command timeout** (existing pattern, just needs per-connection-type values): a `CancellationTokenSource` with `CancelAfter(TimeSpan)`, or Polly's `AddTimeout` strategy, bounds the *total* time a command can take before giving up.
- **Watchdog** (currently missing — this is the gap CONCERNS.md and PROJECT.md both call out, and the one thing `/old/app.html` had that the new app lost): a separate, shorter periodic check (e.g. every 5s, matching `/old`'s cadence) that inspects "have any bytes been received since the last watchdog tick" and can proactively surface a "device seems unresponsive" signal *before* the full command timeout elapses. This is what lets the UI say "still waiting, this is taking longer than usual" instead of a silent 20-second (or 200-second, per CONCERNS.md's worst-case math on sequential settings loads) hang with zero feedback.

Implementation-wise, this doesn't require a special watchdog library — a `PeriodicTimer` (built into .NET since 6.0, works fine in WASM) checking a "last-received-at" timestamp updated by the JS `OnDataReceived` callback is sufficient and adds no new dependency.

**Confidence:** HIGH for the general pattern (this mirrors `/old/app.html`'s own working implementation, which is the closest thing to a proven-in-this-exact-domain reference available); MEDIUM on `PeriodicTimer` specifically being the right primitive (well-established .NET API, but not verified against this codebase's exact async structure).

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|--------------------------|
| Polly.Core (hand-built pipeline) | Hand-rolled retry/timeout code (no library) | If the team wants zero new dependencies at all costs. Feasible — the patterns above (CancellationTokenSource + exponential backoff loop) are not hard to hand-write — but you lose Polly's built-in telemetry hooks (`OnRetry`, `OnTimeout` callbacks) that make future debugging easier, and you re-introduce the "logic duplicated in three places" problem this milestone is trying to eliminate. Given the project's existing constraint to minimize *new JS surface* (not new C# surface), pulling in a well-vetted, dependency-free C# library is a good trade. |
| `Microsoft.Extensions.Logging` (console sink only) | A structured/remote logging sink (Application Insights, Seq, Sentry, etc.) | Not needed for this milestone — there's no deployment/telemetry backend in scope, and PROJECT.md's constraints emphasize minimal surface area. Revisit if a future milestone needs production error telemetry from real users' browsers (useful given the BLE-hardware gap the author has — remote logs from BLE users would be the only way to verify BLE fixes without owning BLE hardware). |
| Application-level GATT operation queue (pattern) | A third-party Web Bluetooth wrapper library (e.g. `web-bluetooth` polyfills/wrappers) | Not recommended — those libraries target Node.js/non-browser environments needing a WebUSB/WebBluetooth *implementation*, not browsers that already have the native API. Adding one would be pure JS surface growth with no benefit, directly against the stated constraint. |

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|------------------|-------|
| `Polly.Core 8.7.0` | `net10.0` (project's `TargetFramework`) | Polly.Core's newest listed target framework is `net8.0`; `net10.0` projects consume it fine via framework rolling-forward (a `net8.0`-targeted package runs unmodified on `net10.0` — this is standard .NET forward compatibility, not something specific to Polly). No `net10.0`-specific build exists or is needed. |
| `Polly.Core 8.7.0` | `Microsoft.AspNetCore.Components.WebAssembly 10.0.4` (already referenced) | No known conflicts; Polly.Core has no dependency on ASP.NET Core assemblies at all, so there's no version-alignment concern between the two. |
| `Microsoft.Extensions.Logging.Abstractions` | Already transitively available | Comes in via `Microsoft.AspNetCore.Components.WebAssembly` — no explicit `PackageReference` needed; just register `builder.Logging` (already likely configured in `Program.cs`) and inject `ILogger<T>` into services. |

## Installation

```bash
cd ProffieOS.Workbench
dotnet add package Polly.Core --version 8.7.0
```

No other new packages are required. Logging uses what's already referenced by the Blazor WASM SDK.

## Sources

- [NuGet Gallery — Polly 8.7.0](https://www.nuget.org/packages/polly/) — version/date confirmation (official, HIGH)
- [Polly.Core on NuGet](https://www.nuget.org/packages/Polly.Core) — target frameworks + dependency list, fetched directly (official, HIGH)
- [Meet Polly: The .NET resilience library](https://www.pollydocs.org/) — current API shape (ResiliencePipeline, strategies) (official, HIGH)
- [Retry resilience strategy — Polly docs](https://www.pollydocs.org/strategies/retry.html) — retry/backoff/delay-generator API details (official, HIGH)
- [Introduction to resilient app development - .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/resilience/) — Polly's role as the standard .NET resilience library (official, HIGH)
- [ASP.NET Core Blazor JavaScript interoperability (JS interop) | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0) — JS interop fundamentals, current for .NET 10 (official, HIGH)
- [Call JavaScript functions from .NET methods in ASP.NET Core Blazor | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-10.0) — InvokeAsync timeout/CancellationToken overloads (official, HIGH)
- [Handle errors in ASP.NET Core Blazor apps | Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-8.0) — JSException/JSDisconnectedException behavior, Server vs WASM distinction (official; note: fetched at the `aspnetcore-8.0` doc version, cross-checked against `aspnetcore-10.0` interop page — behavior described has been stable across these versions per Microsoft Learn's own versioned docs, MEDIUM-HIGH) — **note:** this page's JSDisconnectedException guidance is written primarily for the Server hosting model; treat the WASM-specific inference here as MEDIUM confidence, not directly stated.
- [WebUSB API — WICG spec](https://wicg.github.io/webusb/) — official spec reference for transfer error types (official, HIGH)
- [Can't detect device disconnection when transferIn is waiting incoming data — WICG/webusb#219](https://github.com/WICG/webusb/issues/219) — real-world disconnect-detection gap in transferIn polling loops (community/issue tracker, MEDIUM)
- [GATT operation in progress - how to handle it? — WebBluetoothCG/web-bluetooth#188](https://github.com/WebBluetoothCG/web-bluetooth/issues/188) — GATT concurrency limitation, recommended serialization pattern (community/issue tracker, MEDIUM)
- [bluetooth: WriteValue returns "GATT operation already in progress" — Chromium issue tracker](https://issues.chromium.org/issues/40435612) — confirms this is a Chromium-implementation-level constraint, not app-fixable except by serializing calls (official browser vendor issue tracker, MEDIUM-HIGH)

---
*Stack research for: Blazor WebAssembly WebUSB/WebBLE reliability stabilization*
*Researched: 2026-07-01*

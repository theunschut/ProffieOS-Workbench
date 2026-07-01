# Project Research Summary

**Project:** ProffieOS Workbench — reliability/stabilization milestone
**Domain:** Browser-based hardware configurator (Blazor WebAssembly + WebUSB/Web Bluetooth JS interop)
**Researched:** 2026-07-01
**Confidence:** MEDIUM-HIGH

## Executive Summary

This is not a greenfield build — it's a stabilization retrofit on an existing Blazor WASM (.NET 10) app that talks to Proffie sabers over WebUSB and Web Bluetooth through a thin JS interop layer (usb.js/bluetooth.js) and three singleton services (SaberStateService, SaberConnectionService, SaberCommandService). Comparable tools in this space (ESP Web Tools, Adafruit WebSerial ESPTool, Google's own Web Bluetooth samples) converge on the same reliability-UX baseline this app is missing: an always-visible connection indicator, visible error surfacing instead of silent failure, pre-write connection validation, graceful disconnect handling, a hang-detecting watchdog distinct from a hard timeout, and non-ambiguous save confirmation. All three reported bugs (silently missing settings, an uncaught "USB not connected" crash, frequent save timeouts) trace to three root causes: bare catch{} swallowing in fire-and-forget async work, a stale/too-late connection check before JS writes, and a single flat 20s timeout doing double duty as both completion-deadline and hang-detector with no independent watchdog.

The recommended approach is surgical, not architectural: no new service boundary, no state-machine library, no rebuild. Add Polly.Core (dependency-free, WASM-safe) for retry/timeout pipelines around JS interop calls, add structured ILogger<T> logging (already available via the Blazor WASM SDK) in place of empty catches, and add a small amount of new state (typed exceptions, a watchdog timer, an error-notification event) inside the three existing singletons. SaberConnectionService.ConnectionState already is the state machine needed — the gap is that nothing consistently consults it before writing, not that it's missing. The build order that emerges from the architecture research is: (1) error surfacing first (cheapest, and becomes the verification mechanism for everything after), (2) pre-write connection validation second, (3) timeout/watchdog rework third (highest complexity, benefits from 1 and 2 existing first), (4) write-confirmation/read-back verification last and scoped narrowly, since it doubles round-trips per save.

Key risks: two spec-level gaps in the underlying browser APIs cannot be "fixed" in application code, only mitigated — WebUSB's transferOut/transferIn disconnect detection is inherently unreliable (WICG/webusb#219: no way to reliably distinguish "unplugged" from "other transfer error" at the moment of the throw), and Web Bluetooth GATT operations are not serialized by the browser (WebBluetoothCG/web-bluetooth#188, open since 2015) — concurrent GATT calls from independent code paths (poll loop + user save) throw "GATT operation already in progress," which is a strong candidate for the project's recurring, never-fully-diagnosed BLE flakiness ("fix?"/"fiix?"/"fiiix?" commit history). Both must be handled by defensive application-level patterns (treat every transfer rejection as a possible disconnect; serialize all GATT access through one queue) rather than by trying to detect/query the underlying state directly, because no such query API exists. A second, non-technical risk: the author has no BLE test hardware, so BLE fixes can only be code-review-verified, not live-tested — this should be stated explicitly in any BLE-touching phase's completion criteria rather than assumed away.

## Key Findings

### Recommended Stack

The stack itself does not change — this milestone adds two things to the existing net10.0 + MudBlazor 9.1.0 app: a resilience library and structured logging. Both integrate without new architectural surface.

**Core technologies:**
- Polly.Core 8.7.0 — retry/timeout/circuit-breaker pipelines wrapped around JS interop calls; zero external dependencies, fully async/ValueTask-based so it runs correctly on WASM's single-threaded runtime. Replaces three separately hand-rolled retry loops (C# command layer, usb.js, bluetooth.js) with one centralized policy.
- Microsoft.Extensions.Logging (already transitively available via the Blazor WASM SDK) — inject ILogger<T> into the three services to replace bare catch{} and ad-hoc console logging; WASM's default console logger writes to the same devtools console the JS-side logs already use.
- Explicitly avoid: Microsoft.Extensions.Http.Resilience (built for HttpClient, irrelevant here — there is no HTTP client in this path), a formal state-machine library like Stateless (out of scope per PROJECT.md, and ConnectionState already models what's needed), and a fourth hand-rolled retry loop (the current bug is that retry/backoff logic is already duplicated three ways).

### Expected Features

This milestone's "features" are reliability-UX affordances every mature tool in this space already has — restoring parity with the project's own predecessor (/old/app.html), not innovating.

**Must have (table stakes) — all directly fix one of the 3 reported bugs:**
- Persistent, always-visible connection status indicator
- Visible error surfacing for failed reads/loads (no more bare catch{})
- Pre-write connection validation before every JS interop write
- Graceful disconnect handling (catch JS exceptions, translate to user-facing message, don't crash)
- Command watchdog / hung-command detection (distinct from the flat timeout) — restores /old's 10s-check/20s-kill pattern
- Write/save confirmation feedback (success or explicit failure, never silence)
- Clear "not supported by firmware" vs. "failed to read" distinction
- Systematic parity audit vs. /old (catches any 4th/5th silent gap before another forum bug report)

**Should have (natural follow-ons, add after table stakes land):**
- Adaptive per-connection-type timeout (USB ~10s / BLE ~15s)
- Exponential backoff on retries (vs. today's flat 50ms)
- Retry visibility in UI ("Retrying 2/3...")
- Inter-chunk backpressure delay in SendChunked()

**Defer (explicitly out of scope this milestone):**
- Read-back write verification (doubles round-trips per save; sequence after the timeout/watchdog rework is proven stable)
- Downloadable diagnostic log export
- Connection health "ping" during idle periods
- Per-field inline retry affordances (vs. panel-level banner)
- Automated CI test suite for USB/BLE (browser+hardware dependent, cannot run in CI)
- Full connection/command-layer redesign or new state-machine library
- New settings/features beyond /old parity
- Elaborate reconnect-with-state-restoration UX

### Architecture Approach

No new folders, no new service boundary. All changes live inside the three existing singletons (SaberConnectionService, SaberCommandService, SaberStateService) plus their JS counterparts, with one small optional addition (ErrorNotificationService, thin, only if there's no existing always-mounted place to subscribe to background errors — MainLayout.razor is the natural home).

**Major components (deltas only — responsibilities pre-exist):**
1. SaberConnectionService — becomes the single authority callers query before any write (CanWrite/EnsureConnectedOrThrow()), consolidating what are currently two separately-tracked "connected" booleans into one source of truth.
2. SaberCommandService — adds an independent watchdog timer (decoupled from the per-command CancellationTokenSource), a pre-write connection check, and typed exceptions (NotConnectedException, CommandTimeoutException) instead of swallow-and-return-"".
3. SaberStateService — replaces bare catch{} in RunLoop/LoadSettingsBackgroundAsync with typed catch + event raise (SettingsLoadFailed, LastSettingsLoadError), always fires regardless of which page happens to be mounted.
4. usb.js/bluetooth.js — tighten the "connected" check inside write() (validate _device.opened/gatt.connected live, not just reference presence); no structural/API-shape change.

### Critical Pitfalls

1. **Fire-and-forget async work silently detaches from Blazor's exception pipeline** — by-design framework behavior (dotnet/aspnetcore#24787), not a bug. Avoid by never leaving an unawaited async method uncaught internally; funnel every exception through an explicit event/logger.
2. **JS interop write() checks connection state too early/loosely** — a stale pre-check can't close the disconnect-during-write race; this reflects a genuine unresolved WebUSB spec gap (WICG/webusb#219). Avoid by wrapping every transferOut/transferIn in its own JS try-catch, translating rejections into structured errors, and treating any write failure as "assume disconnected" rather than trying to classify error text.
3. **WebBluetooth GATT operations aren't serialized by the browser** — a long-standing, unresolved spec gap (WebBluetoothCG/web-bluetooth#188, open since 2015) that is a strong root-cause candidate for the project's recurring BLE flakiness. Avoid by serializing ALL GATT access (including handshake/notify-subscribe, not just command sends) through one queue/lock.
4. **Fixed timeout with no independent watchdog conflates "still working" with "hung"** — retuning the timeout number alone doesn't add hang-detection; needs a second, orthogonal, periodic liveness check independent of the completion deadline.
5. **Wrapping every call site individually instead of centralizing at chokepoints** — this is the current (failed) pattern; centralize error capture at Send2() and the two fire-and-forget entry points (LoadSettingsBackgroundAsync, RunLoop) instead of touching dozens of call sites.

## Implications for Roadmap

Based on combined research, suggested phase structure (4 phases, dependency-ordered):

### Phase 1: Error Surfacing & Exception Hygiene
**Rationale:** Cheapest, lowest-risk change; has no dependency on the other fixes; and becomes the verification mechanism for every subsequent phase (you can't confirm the watchdog or connection-guard fixes work unless failures are visible).
**Delivers:** Typed exception hierarchy (NotConnectedException, CommandTimeoutException, ProtocolException), ILogger<T> wired into the three services, SettingsLoadFailed event + LastSettingsLoadError property on SaberStateService, a shared always-mounted subscriber (e.g. in MainLayout.razor) so background-loop failures surface regardless of active page.
**Addresses:** Visible error surfacing for failed reads/loads; clear "not supported" vs. "failed" distinction.
**Avoids:** Pitfall 1 (fire-and-forget swallowed exceptions), Pitfall 5 (per-call-site patching instead of centralizing).

### Phase 2: Pre-Write Connection Validation & Graceful Disconnect Handling
**Rationale:** Directly fixes the reported "USB not connected" crash; structurally independent of the watchdog; low-risk (guard clause, no new timers/state); its failure path uses the typed exceptions from Phase 1.
**Delivers:** SaberConnectionService becomes single source of truth (CanWrite/EnsureConnectedOrThrow()); JS-side write() validates live device state (_device.opened, gatt.connected) immediately before transfer, wrapped in its own try-catch; C# catches JSException at every JS interop call site and translates to typed domain exceptions; disconnect handlers registered proactively rather than discovered on next failed write.
**Uses:** Existing ConnectionState enum (no new state machine); typed exceptions from Phase 1.
**Implements:** Guard-Clause-at-Write-Boundary pattern; consolidation of IsConnected to derive from one source of truth (closes Anti-Pattern 1).
**Avoids:** Pitfall 2 (stale connection check before write) and its underlying spec gap (WICG/webusb#219).

### Phase 3: Timeout/Watchdog Rework
**Rationale:** Highest-complexity change in the retrofit (new timer, lifecycle tied to connect/disconnect, transport-specific thresholds); benefits most from Phase 1 (observability) and Phase 2 (tightened connection signal to key off of) already being in place — attempting this first would make a watchdog trip just as invisible as today's silent timeout.
**Delivers:** Independent periodic watchdog timer in SaberCommandService (mirrors /old/app.html's 10s-check/20s-kill pattern, decoupled from the per-command CancellationTokenSource), per-connection-type timeout values (USB ~10s / BLE ~15s), exponential backoff with capped max delay replacing the flat 50ms retry loop, inter-chunk backpressure delay in SendChunked().
**Uses:** Polly.Core TimeoutStrategy/RetryStrategy for the timeout+backoff plumbing; PeriodicTimer (built into .NET, WASM-safe) for the watchdog itself.
**Addresses:** Command watchdog for hung commands, adaptive per-connection-type timeout, retry visibility, exponential backoff, inter-chunk backpressure.
**Avoids:** Pitfall 4 (fixed timeout conflating hang-detection with deadline), and confirms/addresses Pitfall 3 (GATT serialization) if the audit confirms it as a BLE root cause — centralizing all BLE GATT calls through one serialized queue is a natural fit for this phase since both are about untrusted concurrent access to the JS interop boundary.

### Phase 4: Write Confirmation & Parity Audit (scoped narrowly)
**Rationale:** Highest-cost, highest-risk item (doubles round-trips per save, adds new protocol interactions); not required to fix the 3 reported bugs; sequence last so verification reads inherit a stable timeout/watchdog rather than the old hang risk. The systematic /old-vs-new parity audit should run in parallel with or just before Phase 1 as an input, but the write-confirmation code itself belongs here.
**Delivers:** Write/save confirmation feedback (toast: "Saved"/"Save failed") as the cheap v1; optional read-back verification for the specific settings the parity audit proves are currently silently lost (not blanket application); final parity-audit report against /old/app.html.
**Addresses:** Write/save confirmation feedback, read-back write verification (as a scoped v2), parity audit requirement.

### Phase Ordering Rationale

- Error visibility must exist before anything else can be verified as working — every later phase depends on Phase 1 as its own test harness given there's no automated test suite for USB/BLE flows.
- Connection validation is structurally independent of the watchdog but shares the same "single source of truth" consolidation goal, so it logically precedes the more complex timeout rework.
- The watchdog rework is deliberately last among the "core" fixes because it's the highest-complexity item and needs the connection-state signal (Phase 2) and observability (Phase 1) already solid to be tunable and verifiable.
- Write confirmation/read-back verification is explicitly the optional, highest-cost hardening layer — PROJECT.md and the architecture research both flag it as scoped narrowly and sequenced last, not blocking milestone completion.
- The BLE GATT-serialization fix (Pitfall 3) should be confirmed via log/code review during the audit (no BLE hardware available) before any BLE-specific code changes are made, to avoid another round of blind "fix?" commits.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Timeout/Watchdog Rework):** Needs verification of exact Polly.Core API usage patterns for wrapping arbitrary JS-interop delegates (not HttpClient), and BLE-specific GATT-serialization coverage across all call paths (handshake, notify-subscribe) — no BLE test hardware means this needs careful code-review-based verification, not live testing.
- **Phase 4 (Write Confirmation):** If read-back verification is pursued, needs research into which specific settings the parity audit identifies as silently-lost, to scope verification narrowly rather than doubling all round-trips.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Error Surfacing):** Standard .NET exception-handling hygiene; well-established pattern, no domain-specific research needed.
- **Phase 2 (Pre-Write Validation):** Well-documented guard-clause pattern; browser property names (_device.opened, gatt.connected) should be spot-checked against current MDN/WICG spec at implementation time but the pattern itself is standard.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM-HIGH | Polly.Core version/dependency facts verified via NuGet + official Polly docs; WASM-specific compatibility is a reasonable inference (async/ValueTask-only design), not directly confirmed by a source addressing this exact combination. |
| Features | MEDIUM | Cross-referenced against ESP Web Tools, Adafruit ESPTool, Google's Web Bluetooth samples, and the project's own /old/app.html (primary source, HIGH within this file). Vendor docs are official but general; feature landscape for this specific niche (Proffie saber configurators) has no direct competitor set, so features are extrapolated from adjacent WebUSB/BLE tooling. |
| Architecture | HIGH | Based on direct reading of the actual codebase files (SaberStateService.cs, SaberConnectionService.cs, SaberCommandService.cs, usb.js, Settings.razor.cs) and /old/app.html's working watchdog — this is a retrofit dictated by existing code shape, not a generic pattern lookup. |
| Pitfalls | MEDIUM | The two core browser-spec gaps (WICG/webusb#219, WebBluetoothCG/web-bluetooth#188) are confirmed by primary sources (spec repos, maintainer statements) — HIGH for the general pattern. Project-specific severity/frequency (e.g., "GATT concurrency is the root cause of the fix?/fiix?/fiiix? commits") is inferred from CONCERNS.md and commit history, not directly proven — MEDIUM overall. |

**Overall confidence:** MEDIUM-HIGH

### Gaps to Address

- **BLE GATT-serialization root cause is unconfirmed:** Pitfall 3 is a strong candidate explanation for the project's recurring BLE flakiness but has not been proven against actual past error logs/PR history. Address during the audit phase by grepping historical issues/PRs for "GATT operation"/"already in progress" strings before writing the BLE fix.
- **No BLE test hardware:** All BLE-touching fixes can only be code-review-verified, not live-tested by the author. This should be stated explicitly as a phase completion-criteria caveat, not silently assumed resolved.
- **Exact browser property names** (_device.opened, gatt.connected) used in the pre-write validation pattern should be double-checked against current MDN/WICG spec text at implementation time, since browser API surface details can shift between spec revisions.
- **Polly.Core WASM compatibility** is inferred, not directly source-confirmed for this exact use case (wrapping JS interop calls, not HTTP). Low risk given its dependency-free async-only design, but worth a quick smoke test early in Phase 3 rather than assuming it at plan time.
- **Semantic conflation of "" as both "unsupported" and "failed"** in GetOptional()/current command-layer sentinel handling: changing Send()/Send2() to throw typed exceptions instead of returning "" is a breaking change for callers currently treating empty string as "feature unsupported." Audit all call sites before changing this in Phase 1/2.

## Sources

### Primary (HIGH confidence)
- Direct code reading: ProffieOS.Workbench/Services/SaberStateService.cs, SaberConnectionService.cs, SaberCommandService.cs, wwwroot/js/usb.js, Pages/Settings.razor.cs
- /old/app.html (lines ~440-490, watchdog implementation) — project's own predecessor, directly inspected
- .planning/codebase/CONCERNS.md, .planning/codebase/ARCHITECTURE.md, .planning/codebase/CONVENTIONS.md, .planning/PROJECT.md — prior GSD codebase mapping and milestone scope
- NuGet Gallery — Polly 8.7.0 (https://www.nuget.org/packages/polly/) and Polly.Core on NuGet (https://www.nuget.org/packages/Polly.Core)
- Meet Polly — official docs (https://www.pollydocs.org/) and Retry strategy docs (https://www.pollydocs.org/strategies/retry.html)
- Introduction to resilient app development — Microsoft Learn (https://learn.microsoft.com/en-us/dotnet/core/resilience/)
- ASP.NET Core Blazor JS interop — Microsoft Learn (https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/?view=aspnetcore-10.0)
- dotnet/aspnetcore#24787 (https://github.com/dotnet/aspnetcore/issues/24787) — framework team ruling on fire-and-forget exception handling (by design)
- WICG/webusb#219 (https://github.com/WICG/webusb/issues/219) — WebUSB disconnect-detection spec gap
- WebBluetoothCG/web-bluetooth#188 (https://github.com/WebBluetoothCG/web-bluetooth/issues/188) — GATT concurrency spec gap, open since 2015

### Secondary (MEDIUM confidence)
- Chrome for Developers — Web Bluetooth capabilities guide (https://developer.chrome.com/docs/capabilities/bluetooth)
- Google Chrome Web Bluetooth samples (https://googlechrome.github.io/samples/web-bluetooth/) — Automatic Reconnect, Device Disconnect patterns
- ESP Web Tools official site/source (https://esphome.github.io/esp-web-tools/) — most mature comparable tool surveyed
- micro:bit WebUSB troubleshooting docs; Adafruit WebSerial ESPTool + community reports
- Handle errors in Blazor apps — Microsoft Learn (https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/handle-errors?view=aspnetcore-8.0) — JSException/JSDisconnectedException, Server-vs-WASM distinction
- Chromium issue tracker — "GATT operation already in progress" (https://issues.chromium.org/issues/40435612)
- AWS Builders' Library — Timeouts, retries, and backoff with jitter (https://aws.amazon.com/builders-library/timeouts-retries-and-backoff-with-jitter/)

### Tertiary (LOW confidence)
- WICG/webusb#223 (https://github.com/WICG/webusb/issues/223) — corroborating community reports on transfer errors
- WebBluetoothCG/web-bluetooth#256 (https://github.com/WebBluetoothCG/web-bluetooth/issues/256) — corroborating GATT status clarification discussion
- Adafruit community forum reports on WebSerial ESPTool reliability

---
*Research completed: 2026-07-01*
*Ready for roadmap: yes*

# Phase 1: Error Surfacing - Research

**Researched:** 2026-07-04
**Domain:** Structured error propagation in a Blazor WebAssembly singleton-service architecture (C#/.NET 10), surfaced via MudBlazor UI
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**D-01 (Retry behavior):** Failed settings show a panel-level error banner (not per-field indicators) listing which settings failed, with a **manual retry button**. No automatic/background retry.

**D-02 (Unsupported vs Failed):** Settings the board doesn't support stay **hidden entirely** — same as today's behavior and matches `/old`. Only genuine read failures on settings the board *does* support get a visible error indicator (the panel-level banner from D-01).

**D-03 (implementation note):** Today `GetOptional()` conflates "board said unsupported" (response starts with `Whut?`) with "no response / timeout" — both collapse to `null`/`""`. Distinguishing D-02's two cases requires the code to tell these apart at the source (see D-05).

**D-04 (Cross-page visibility):** When `LoadSettingsBackgroundAsync()` fails while the user is on a different page, a **global MudBlazor Snackbar/toast** fires app-wide immediately — reusing the existing Snackbar pattern already used for write errors. This does not replace the panel-level banner (D-01): the Settings panel still shows the error banner whenever the user later visits Settings, so both the immediate toast and the persistent banner are needed.

**D-05 (Error detail level):** Error messages carry a **specific reason** (e.g. "timed out waiting for board response" vs. "board disconnected" vs. "unexpected response"), not a generic "failed to load." Explicitly includes the breaking change: **`Send()`/`Send2()` must stop returning `""` for every failure case.** Phase 1 scope includes:
  1. Auditing call sites that currently treat an empty-string return as "feature unsupported."
  2. Changing `Send()`/`Send2()` to carry a distinguishable failure reason (typed result or exception).
  3. This is the mechanism that also resolves D-03.

### Claude's Discretion

- Exact mechanism for carrying the failure reason through `Send()`/`Send2()` (e.g., a `Result<T>`-style return, a typed exception hierarchy, or an out-parameter) — **this research resolves it below: typed exception hierarchy.**
- Exact wording/copy of error messages and the retry button.
- Whether logging (Microsoft.Extensions.Logging, per STATE.md decision) is added at the `Send()` layer, the `SaberStateService` layer, or both — **this research resolves it below: both, at different levels.**

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope. (Per-field retry, connection health pings, and diagnostic-log downloads were already excluded at the REQUIREMENTS.md level, not introduced during this discussion.)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ERR-01 | When a setting fails to load from the board, the user sees a visible error/retry indicator for that setting instead of it silently disappearing | Typed exception hierarchy (§ Standard Stack) carries a distinguishable reason from `Send()`/`Send2()` up to `SaberStateService`, which aggregates failures into a new `FailedSettings` collection that `SettingsPanel` renders as the D-01 banner with a retry button (§ Architecture Patterns, § Code Examples) |
| ERR-02 | The settings UI distinguishes "firmware doesn't support this setting" from "reading this setting failed" | `SaberProtocolException` subtype `SaberUnsupportedException` (raised when response starts with `Whut?`) vs. `SaberTimeoutException`/`SaberCommunicationException` (raised on timeout/disconnect) gives `TryLoadIntSetting`/`TryLoadBoolSetting` a way to catch-and-hide the former while catch-and-record the latter (§ Common Pitfalls Pitfall 1, § Code Examples) |

</phase_requirements>

## Summary

This phase is a targeted, surgical change to an already well-understood 3-singleton-service architecture — no new libraries are needed beyond what STACK.md (project-level research) already locked in at HIGH confidence: `Microsoft.Extensions.Logging` (already transitively available, zero new package) for structured logging, injected at both the `SaberCommandService` layer (protocol-level detail: which command, what raw response, what exception) and the `SaberStateService` layer (business-level detail: which named setting failed, aggregate outcome of a load pass). No Polly is needed for this phase specifically — Polly (from STACK.md) is scoped to Phase 3's retry/timeout work; Phase 1 only needs to *stop swallowing* exceptions and *classify* them, not retry them automatically (D-01 explicitly forbids auto-retry).

The concrete mechanism for D-05 is a **small typed exception hierarchy**, not a `Result<T>` return type and not an out-parameter. Three reasons converge on this: (1) the codebase already has zero `Result<T>`/functional-error libraries and a strong idiomatic C# exception-based style everywhere else (every `SaveXAsync` in `SettingsPanel.razor.cs` already does `try/catch (Exception ex) { Snackbar.Add(ex.Message, ...) }` — 20+ call sites depend on catching *something* thrown, not unwrapping a `Result`); (2) industry guidance on Result-vs-exception (verified via WebSearch, cross-checked across multiple 2026 sources) converges on "use Result for *expected* business-rule outcomes, use exceptions for *infrastructure* failures like I/O, timeout, and disconnected-device conditions" — every failure mode in scope here (timeout, disconnect, unexpected response, board explicitly saying "unsupported") is an infrastructure/protocol failure, not a business rule, so exceptions are the correct-by-convention choice, not merely the path of least resistance; (3) `SaberConnectionService` already has a working, minimal precedent for "carry a specific string reason on a property" (`LastDisconnectReason`) that the same author/reviewer will recognize — a parallel `SaberProtocolException`-derived hierarchy is a natural sibling pattern, not a new paradigm.

The other genuinely non-obvious finding from this research: **`ISnackbar` (MudBlazor's snackbar service) cannot be constructor-injected into a singleton service** like `SaberStateService`, because MudBlazor's `SnackbarService` implementation depends on `NavigationManager` in its constructor, and `NavigationManager` is only resolvable inside a component's render tree, not from a plain DI-registered singleton class instantiated at `Program.cs` startup. This is confirmed by a MudBlazor maintainer discussion thread (community source, MEDIUM confidence) but is also directly avoidable: the codebase already has the exact mechanism needed sitting unused for this purpose — a plain C# event (the same `StateChanged` pattern `SaberStateService` and `SaberConnectionService` already use). D-04's "global toast reachable from any page" is best implemented as a new `SaberStateService.SettingsLoadFailed` event that a single long-lived root-level component (e.g. `MainLayout` or `App.razor`) subscribes to once, and that subscriber — which *does* have `ISnackbar` injectable because it's a component — calls `Snackbar.Add()`. This sidesteps the DI limitation entirely without inventing a new notification abstraction, and is a two-line addition consistent with the existing pub/sub architecture.

**Primary recommendation:** Introduce a 3-4 class typed exception hierarchy rooted at `SaberProtocolException` (with `SaberUnsupportedException`, `SaberTimeoutException`, `SaberCommunicationException`, `SaberDisconnectedException` subtypes), have `Send2()` throw instead of swallowing into `""`, inject `ILogger<SaberCommandService>` and `ILogger<SaberStateService>` at both layers, add a `FailedSettings` list + `SettingsLoadFailed` event to `SaberStateService`, render the D-01 banner in `SettingsPanel.razor` from that list, and subscribe to the new event once from a root layout component to fire the D-04 global Snackbar.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Distinguishing failure reason (timeout/unsupported/disconnected/malformed) | API / Backend (C# services) | — | `Send()`/`Send2()` in `SaberCommandService` is the single choke point where every board command passes through; this is where raw responses and exceptions are first observed and must be classified once, not re-classified at every call site |
| Structured logging of failures | API / Backend (C# services) | — | `ILogger<T>` is a .NET service-layer concern; there is no server tier in this app (Blazor WASM is 100% client-side) — "backend" here means the C# service layer, not a remote API |
| Panel-level error banner UI (D-01) | Browser / Client (Razor component) | API / Backend (state aggregation) | Rendering is a `SettingsPanel.razor` concern; but the *data* it renders (which settings failed, with what reason) must be aggregated once in `SaberStateService`, not recomputed per-render |
| Global cross-page Snackbar (D-04) | Browser / Client (root-level component) | API / Backend (event source) | The Snackbar *display* mechanism (`ISnackbar`) is only usable inside a component due to its `NavigationManager` dependency; the *trigger* (a failed background load) originates in the service layer and must reach the component via an event, not direct injection |
| Unsupported-vs-failed distinction (ERR-02) | API / Backend (C# services) | Browser / Client (hide vs. show logic) | The board's `Whut?` response is a protocol-level signal classified once in `Send()`/`GetOptional()`; the UI layer only needs to react to two already-distinct outcomes (thrown `SaberUnsupportedException` = hide; recorded failure = show banner), not re-parse raw strings |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Extensions.Logging.Abstractions` | matches `net10.0`, transitively referenced | `ILogger<SaberCommandService>`, `ILogger<SaberStateService>` for structured, leveled logging of every classified failure | Already locked at HIGH confidence in project-level STACK.md — no new package needed, already part of the Blazor WASM SDK. `[VERIFIED: project STACK.md, HIGH confidence]` |

No other new packages are required for this phase. `Polly.Core` (from STACK.md) is explicitly Phase 3 scope (retry/timeout pipelines) — Phase 1 needs classification and surfacing, not automated retry (D-01 forbids background retry).

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `MudBlazor` `ISnackbar` | 9.1.0 (already referenced) | Global toast for D-04 | Inject into a **root-level layout component** (`MainLayout.razor` or a new always-mounted component), never into a singleton service directly — see Architecture Patterns below for why. `[CITED: MudBlazor GitHub discussion #6209/#10942]` |
| Plain C# `event Action<...>?` | built-in, `System` | Bridge from `SaberStateService` (background failure detected) to the Snackbar-hosting component (D-04) | This is the exact pattern already used for `StateChanged` in all three singleton services — no new abstraction, no event-aggregator library needed. `[VERIFIED: codebase — SaberStateService.cs:40, SaberConnectionService.cs:51]` |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Typed exception hierarchy | `Result<T>`/`OneOf<T1,T2>` (e.g. `FluentResults`, `OneOf`, `ErrorOr` NuGet packages) | Would require rewriting every one of the ~30 existing `await commands.Send(...)` call sites across `SaberStateService.cs` to unwrap a `Result<string>` instead of a plain `string`/exception, PLUS every `SaveXAsync` catch block in components — a much larger migration surface than the phase's stated scope ("audit call sites," not "rewrite every call site's control flow"). Result types are the industry-recommended choice for *expected business outcomes*; every failure mode here (timeout, disconnect, malformed response) is an infrastructure failure by the same industry guidance's own framework, which argues for exceptions. `[CITED: multiple 2026 sources cross-verified, e.g. milanjovanovic.tech, enterprisecraftsmanship.com — MEDIUM confidence, consistent guidance across 4+ independent sources]` |
| Typed exception hierarchy | Out-parameter (`bool TrySend(string cmd, out string result, out FailureReason reason)`) | Doesn't compose with `async`/`await` cleanly (no `out` params on async methods without wrapping in a tuple return, which is functionally identical to a `Result<T>` but with worse ergonomics) and doesn't match any existing codebase convention. Rejected. |
| Root-component-subscribes-to-event for Snackbar | `Blazor.EventAggregator` / `Nefarius.Blazor.EventAggregator` NuGet packages | Adds a new dependency and a new pub/sub abstraction (`IEventAggregator`, `IHandle<T>`) for something a single `event` field already solves with zero new API surface — directly against the project's stated "minimize new surface area" constraint. `[CITED: GitHub — mikoskinen/Blazor.EventAggregator, MEDIUM confidence]` |

**Installation:** No new packages. `ILogger<T>` is already available via `Microsoft.AspNetCore.Components.WebAssembly` (confirmed in project's existing `.csproj`); `ISnackbar` is already available via `MudBlazor` 9.1.0 (already referenced, already used in every `SaveXAsync` catch block in `SettingsPanel.razor.cs` and 8 other components).

**Version verification:** `Microsoft.AspNetCore.Components.WebAssembly` 10.0.4 and `MudBlazor` 9.1.0 confirmed present in `ProffieOS.Workbench.csproj` (read directly). `[VERIFIED: codebase — ProffieOS.Workbench.csproj]`

## Package Legitimacy Audit

No new external packages are introduced by this phase's recommended approach — logging is transitively available via the existing Blazor WASM SDK reference, and Snackbar is transitively available via the existing MudBlazor 9.1.0 reference. Package legitimacy gate is not applicable; nothing to audit.

## Architecture Patterns

### System Architecture Diagram

```text
┌─────────────────────────────────────────────────────────────────┐
│  Board (firmware)                                                │
│  responds "Whut? :cmd" (unsupported) │ times out │ malformed     │
└───────────────────────┬───────────────────────────────────────────┘
                        │ raw string / no response / JSException
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  SaberCommandService.Send() / Send2()                            │
│  ── CLASSIFY once, here ──                                        │
│  • raw.StartsWith("Whut?")  → throw SaberUnsupportedException     │
│  • CancellationToken fires  → throw SaberTimeoutException          │
│  • JSException from write   → throw SaberCommunicationException   │
│  • not connected             → throw SaberDisconnectedException    │
│  ILogger<SaberCommandService>.LogWarning(...) at each throw site  │
└───────────────────────┬───────────────────────────────────────────┘
                        │ typed exception propagates (no more "")
                        ▼
┌─────────────────────────────────────────────────────────────────┐
│  SaberStateService.TryLoadIntSetting() / TryLoadBoolSetting()     │
│  ── REACT differently per exception type ──                       │
│  catch (SaberUnsupportedException)                                 │
│    → swallow silently, setting stays hidden (D-02)                │
│  catch (SaberProtocolException ex)     // timeout/comm/disconnect │
│    → FailedSettings.Add(new FailedSetting(label, ex.Message))     │
│    → ILogger<SaberStateService>.LogError(ex, ...)                  │
│  LoadSettingsBackgroundAsync() no longer has bare catch {}         │
│    → on ANY failure, fire SettingsLoadFailed?.Invoke(reason)      │
│  StateChanged?.Invoke()  (existing mechanism, unchanged)           │
└─────────┬─────────────────────────────────────────┬───────────────┘
         │ StateChanged                            │ SettingsLoadFailed
         ▼                                          ▼
┌─────────────────────────┐          ┌───────────────────────────────┐
│ SettingsPanel.razor      │          │ MainLayout.razor (or App.razor)│
│ (D-01: panel banner)      │          │ (D-04: global toast)           │
│ renders State.FailedSettings│        │ subscribes ONCE at startup,    │
│ with per-item retry button │        │ ISnackbar injectable here      │
│ → calls RetryFailedSetting  │        │ (component context has         │
│   (re-runs Try*Setting for  │        │  NavigationManager available)  │
│   just that one setting)    │        │ → Snackbar.Add(reason, Error)  │
└─────────────────────────┘          └───────────────────────────────┘
```

A reader can trace ERR-01/ERR-02: board response → classified exception in `Send()` → reacted-to differently in `SaberStateService` (hide vs. record) → rendered two ways simultaneously (persistent banner + immediate toast) without either layer needing to re-parse the raw protocol string.

### Recommended Project Structure

No new folders needed — this phase adds files to existing locations:
```
ProffieOS.Workbench/
├── Exceptions/                       # NEW — small folder, 1 file
│   └── SaberExceptions.cs            # SaberProtocolException + 3-4 subtypes
├── Models/
│   └── SettingItem.cs                # ADD: FailedSetting record alongside existing BoolSettingItem/IntSettingItem
├── Services/
│   ├── SaberCommandService.cs        # MODIFY: Send2() throws instead of returning ""
│   └── SaberStateService.cs          # MODIFY: FailedSettings list, SettingsLoadFailed event, classify in Try*Setting
├── Components/
│   └── SettingsPanel.razor(.cs)      # MODIFY: render D-01 banner + retry button
└── Layout/  (or wherever MainLayout.razor lives)
    └── MainLayout.razor(.cs)         # MODIFY: subscribe to SettingsLoadFailed once, fire Snackbar (D-04)
```

### Pattern 1: Typed exception hierarchy for protocol-level failure classification

**What:** A small sealed hierarchy rooted at an abstract `SaberProtocolException : Exception`, with concrete subtypes for each distinguishable failure mode.

**When to use:** Any place `Send()`/`Send2()` currently returns `""` to signal failure. Replace the `""` return with a `throw`.

**Example:**
```csharp
// Source: pattern synthesized from codebase precedent (SaberConnectionService.LastDisconnectReason)
// and industry guidance on exception hierarchies (CITED, MEDIUM confidence — no single official
// source specifies this exact hierarchy shape; this is a standard .NET custom-exception pattern).
namespace ProffieOS.Workbench.Exceptions;

public abstract class SaberProtocolException(string message, string command)
    : Exception(message)
{
    public string Command { get; } = command;
}

/// <summary>Board explicitly said it doesn't support this command ("Whut? :cmd").</summary>
public sealed class SaberUnsupportedException(string command)
    : SaberProtocolException($"Board does not support '{command}'", command);

/// <summary>No response arrived within the command timeout window.</summary>
public sealed class SaberTimeoutException(string command)
    : SaberProtocolException($"Timed out waiting for board response to '{command}'", command);

/// <summary>The device was not connected, or disconnected mid-command.</summary>
public sealed class SaberDisconnectedException(string command, string? reason = null)
    : SaberProtocolException(reason is null
        ? $"Device disconnected while sending '{command}'"
        : $"Device disconnected while sending '{command}': {reason}", command);

/// <summary>A JS interop / transport-level error occurred (not a timeout, not a disconnect).</summary>
public sealed class SaberCommunicationException(string command, string innerMessage)
    : SaberProtocolException($"Communication error sending '{command}': {innerMessage}", command);
```

### Pattern 2: `Send2()` classifies once, at the source

**What:** Replace the existing catch-all `catch (Exception ex) { OnError?.Invoke(...); return ""; }` with per-cause classification.

**When to use:** In `SaberCommandService.Send2()`, which is the single choke point for every command.

**Example:**
```csharp
// Source: pattern derived from existing SaberCommandService.cs:134-159 (read directly),
// modified per D-05. Confidence: HIGH for structure (this is the existing method, minimally
// changed); MEDIUM for exact exception-type mapping (JSException vs generic Exception distinction
// per STACK.md's own note that this exact WASM-JS-interop-exception scenario isn't verbatim
// documented anywhere).
private async Task<string> Send2(string cmd)
{
    if (!IsConnected)
        throw new SaberDisconnectedException(cmd);
    if (SendBytesAsync is null)
        throw new SaberDisconnectedException(cmd, "no transport configured");

    await _lock.WaitAsync();
    try
    {
        _pendingTcs = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        cts.Token.Register(() => _pendingTcs.TrySetException(new SaberTimeoutException(cmd)));

        var data = Encoding.UTF8.GetBytes(cmd + '\n');
        await SendChunked(data);

        var raw = await _pendingTcs.Task;
        if (raw.StartsWith("Whut? :" + cmd) || raw.StartsWith("Whut?"))
            throw new SaberUnsupportedException(cmd);

        return raw;
    }
    catch (Microsoft.JSInterop.JSException jsEx)
    {
        _logger.LogWarning(jsEx, "JS interop failure sending {Command}", cmd);
        throw new SaberCommunicationException(cmd, jsEx.Message);
    }
    catch (SaberProtocolException)
    {
        throw; // already classified — let it propagate as-is
    }
    finally
    {
        _pendingTcs = null;
        _lock.Release();
    }
}
```

**IMPORTANT caveat on the `Whut?` check placement above:** moving the `Whut?` classification into `Send2()` (rather than leaving it in `GetOptional()`/`TryLoad*Setting`) means `Send()`'s tagged-retry loop (`SaberCommandService.cs:93-110`, `ParseTaggedResponse`) must also be checked — currently `ParseTaggedResponse` extracts the response *body* after the tag prefix, so a `Whut?` response would appear inside the parsed `result`, not as the raw response's prefix. **The planner must verify which layer (`Send()` post-tag-parse, or `Send2()` pre-tag-parse) actually sees the `Whut?` string** by tracing an actual tagged response through `ParseTaggedResponse` before implementing — this is a concrete detail this research could not fully resolve without a live board to test against (see Open Questions).

### Pattern 3: `SaberStateService` reacts differently per exception type, aggregates failures, and stops using bare `catch {}`

**What:** `TryLoadIntSetting`/`TryLoadBoolSetting` catch `SaberUnsupportedException` and swallow it (D-02: stays hidden); everything else derived from `SaberProtocolException` gets recorded into a new `FailedSettings` list and logged.

**Example:**
```csharp
// Source: pattern derived from existing SaberStateService.cs:544-558 (read directly), modified per D-02/D-03/D-05.
public List<FailedSetting> FailedSettings { get; } = [];
public event Action<string>? SettingsLoadFailed;

private async Task TryLoadIntSetting(string baseCmd, string variable, string label)
{
    try
    {
        var val = await commands.Send($"get_{baseCmd} {variable}", retry: true);
        GestureIntSettings.Add(new IntSettingItem(baseCmd, variable, label,
            int.TryParse(val, out var i) ? i : 0));
    }
    catch (SaberUnsupportedException)
    {
        // D-02: board doesn't support this — stays hidden, no error shown, no log spam
    }
    catch (SaberProtocolException ex)
    {
        _logger.LogWarning(ex, "Failed to load setting {BaseCmd}/{Variable}", baseCmd, variable);
        FailedSettings.Add(new FailedSetting(label, ex.Message, () => TryLoadIntSetting(baseCmd, variable, label)));
    }
}

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
        // D-04: this is the top-level "the whole background load blew up" case —
        // distinct from per-setting failures already captured in FailedSettings above.
        _logger.LogError(ex, "Background settings load failed");
        SettingsLoadFailed?.Invoke(ex.Message);
        SettingsLoaded = true; // still mark loaded so UI doesn't spin forever
        Notify();
    }
}
```

Note the new `FailedSetting` record carries a retry callback (`Action`/`Func<Task>`) so D-01's "manual retry button" can re-invoke just that one setting's load without re-running the entire `LoadSettingsValuesAsync()` sequence — this avoids the "settings load can take 30+ seconds" problem CONCERNS.md already flagged, since retry is scoped to one failed item, not the whole batch.

### Pattern 4: Root-layout component bridges service-layer failure to Snackbar (D-04)

**What:** Because `ISnackbar` cannot be injected into a singleton service (see Summary), a single always-mounted component subscribes to the new `SettingsLoadFailed` event once and is the only place that calls `Snackbar.Add()` for this specific failure.

**Example:**
```csharp
// Source: pattern derived from MudBlazor community guidance (CITED — GitHub discussion #6209,
// MEDIUM confidence) applied to this codebase's existing StateChanged event-subscription idiom
// (VERIFIED — this exact subscribe-in-OnInitialized/unsubscribe-in-Dispose shape already exists
// in Settings.razor.cs and SettingsPanel.razor.cs).
@inject SaberStateService State
@inject ISnackbar Snackbar
@implements IDisposable

@code {
    protected override void OnInitialized()
    {
        State.SettingsLoadFailed += OnSettingsLoadFailed;
    }

    private void OnSettingsLoadFailed(string reason) =>
        InvokeAsync(() => Snackbar.Add($"Settings failed to load: {reason}", Severity.Error));

    public void Dispose() => State.SettingsLoadFailed -= OnSettingsLoadFailed;
}
```

Place this in whatever component is always mounted regardless of route — check the project's actual layout file (likely `MainLayout.razor`, not directly read in this research session — **planner should confirm the exact file** before writing the task).

### Anti-Patterns to Avoid

- **Injecting `ISnackbar` directly into `SaberStateService`'s constructor:** Will fail at DI resolution time (or at minimum behave unpredictably) because `SnackbarService`'s constructor requires `NavigationManager`, which is not resolvable outside a component's render context for a singleton instantiated in `Program.cs`. `[CITED: MudBlazor GitHub discussion, MEDIUM confidence]`
- **Reintroducing a bare `catch { }` anywhere in the new code:** This is the exact anti-pattern this phase exists to fix (`SaberStateService.cs:194`, `SaberConnectionService.cs:185,214`). Every new catch block must either classify-and-record or classify-and-log; never silently discard.
- **Making `FailedSettings` retry automatically on a timer:** D-01 explicitly locks manual-only retry. Don't add a background poll "just in case" — this reintroduces exactly the board-chatter concern REQUIREMENTS.md's Out of Scope table already excluded ("Connection health ping").
- **Converting every `commands.Send()` call site to return `Result<T>`:** Rejected in Alternatives Considered — would be a much larger migration than this phase's scope, and goes against the codebase's existing idiom (exception + Snackbar catch, used at 20+ sites already).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-component notification without direct DI | A custom pub/sub broker, `MediatR`, or an event-aggregator NuGet package | A plain `event Action<string>?` on `SaberStateService`, subscribed once from a root component | The codebase already has this exact mechanism (`StateChanged`) working correctly across all 3 services; adding a second, different notification paradigm for one new use case is inconsistent and unnecessary surface area |
| Distinguishing "unsupported" from "failed" | Re-parsing raw response strings at every UI call site (`if (s.StartsWith("Whut?"))` scattered around components) | Centralized classification in `Send2()`/`Send()` via typed exceptions | The `Whut?` string format is a protocol implementation detail of the board firmware — it should be translated into a typed signal exactly once, at the protocol boundary, not leaked into UI-layer conditionals |
| Retry-one-failed-setting | Re-running the entire `LoadSettingsValuesAsync()` sequence on every manual retry click | A `FailedSetting.Retry` callback scoped to just that one setting's load method | Re-running the whole sequence on every retry click re-triggers CONCERNS.md's own documented worst case ("up to 10 settings × 20-second timeout = 200 seconds"); scoping retry to one item avoids this entirely and is a near-zero-cost design choice at this phase |

**Key insight:** Every piece of this phase's puzzle (typed exceptions, event-based cross-component notification, per-item retry) already has a working precedent somewhere in this exact codebase (`LastDisconnectReason`, `StateChanged`, the `SaveXAsync`/Snackbar idiom). The research task here was pattern-matching to existing codebase conventions, not importing external best practices wholesale — this keeps the fix minimal and consistent with reviewer expectations.

## Common Pitfalls

### Pitfall 1: Classifying `Whut?` at the wrong layer when tagging is active

**What goes wrong:** `SaberCommandService.Send()` (the tagged-response wrapper) calls `ParseTaggedResponse()`, which strips the `N,len,tag|` prefix and returns only the response *body*. If the board's `Whut?` message is itself wrapped in the tagged-response envelope (e.g. `1,15,3|Whut? :get_x`), then checking `raw.StartsWith("Whut?")` inside `Send2()` (which sees the *pre-parse* wire format) will miss it, because `Send2()`'s `raw` in the tagged case is the *tagged* envelope, not the plain-text response. Checking after `ParseTaggedResponse()` (inside `Send()`) sees the *body* correctly, but that's a different call site than the timeout/disconnect classification which naturally belongs in `Send2()`.

**Why it happens:** Two different response formats (tagged vs. untagged) exist depending on `UseTagging`, and the `Whut?` check needs to happen after whichever parsing step strips protocol framing — but timeout/disconnect classification needs to happen at the lowest level (`Send2()`), before any parsing, because those failures mean there's no response body to parse at all.

**How to avoid:** The planner should have implementation tasks trace an actual tagged response (mocked or, ideally, checked against a real board since USB hardware is available per CLAUDE.md constraints) through `ParseTaggedResponse` to confirm exactly where `Whut?` appears in each mode, THEN decide whether the unsupported-check lives in `Send()` (post-parse, both tagged and untagged paths) or needs duplicating in two places. This research recommends checking in `Send()` after `Send2()` returns (for the untagged case) AND after `ParseTaggedResponse` succeeds (for the tagged case), since these are the two places where a plain-text response body is actually available — but this needs implementation-time verification against real protocol traffic, not just static code reading.

**Warning signs:** If after implementation, a genuinely-unsupported setting starts showing up in the D-01 error banner (instead of staying silently hidden per D-02) on a tagging-enabled board (ProffieOS 8.x+), this is the exact failure mode to check first.

### Pitfall 2: Breaking the ~30 existing "unsupported = empty string" call sites during the D-05 migration

**What goes wrong:** `HasCmd()`, `GetList()`, `Sync()`, and multiple direct `await commands.Send(...)` calls throughout `SaberStateService.cs` currently treat an empty/`Whut?`-prefixed string as a normal, expected return value — not an error. If `Send()`/`Send2()` starts *throwing* `SaberUnsupportedException` instead of returning `""`, every one of these call sites will now get an unhandled exception where they previously got a value they checked with `.StartsWith("Whut?")`.

**Why it happens:** This is precisely the "breaking change" STATE.md's Blockers/Concerns section already flagged and CONTEXT.md's D-05 explicitly took on as in-scope work ("Auditing call sites that currently treat an empty-string return as 'feature unsupported'").

**How to avoid:** Before changing `Send2()`'s throw behavior, grep every call site of `commands.Send(...)` in `SaberStateService.cs` (confirmed via this research: `Sync()` line 81, `LoadPresets()` line 94, `RunLoop()` lines 124/128/130/133/136/138, `LoadInitialData()` line 169, `HasCmd()` line 527, `GetList()` line 518, `GetOptional()` line 540, plus every `SaveXAsync`/`Save*Async` write-only call which doesn't check the response at all). Two categories emerge: (a) sites that check `.StartsWith("Whut?")` on the result and need to be rewritten to catch `SaberUnsupportedException` instead, and (b) write-only sites (`SetPresetAsync`, `TurnOnAsync`, etc.) that don't inspect the response at all and are unaffected either way. This audit is a concrete, enumerable task — the planner should size it as its own task, not fold it silently into the exception-hierarchy task, since ~15 call sites is real migration surface.

**Warning signs:** Any place tagging-detection (`RunLoop()` line 124-125: `commands.UseTagging = !v.StartsWith("Whut?")`) or preset/track/font loading throws unexpectedly after this change is a sign a call site was missed in the audit.

### Pitfall 3: Firing the D-04 Snackbar from a background `Task` without `InvokeAsync`

**What goes wrong:** `LoadSettingsBackgroundAsync()` runs as a fire-and-forget background task (`_ = LoadSettingsBackgroundAsync()` at `SaberStateService.cs:183`). If the `SettingsLoadFailed` event handler in the root component calls `Snackbar.Add()` directly without wrapping in `InvokeAsync`, Blazor's rendering synchronization context may not be respected, risking a `InvalidOperationException` ("The current thread is not associated with the Dispatcher") or a UI update that doesn't render.

**Why it happens:** Background service-layer code doesn't run on Blazor's synchronization context; only UI-invoked code does by default. This is a well-known, generally-documented Blazor gotcha (not specific to this app).

**How to avoid:** Always wrap the Snackbar call in `InvokeAsync(() => Snackbar.Add(...))` from the event handler, exactly as shown in Pattern 4 above. This mirrors the existing pattern already used for `OnStateChanged` in both `Settings.razor.cs:71` and `SettingsPanel.razor.cs:53` (`private void OnStateChanged() => InvokeAsync(StateHasChanged);`) — the codebase already knows this rule; the new event handler must follow the same convention.

**Warning signs:** Snackbar toast doesn't appear at all when a background failure occurs while on an unrelated page, or a console error mentioning "Dispatcher"/"SynchronizationContext."

## Code Examples

Verified/derived patterns (see Pattern 1-4 above for full context) — this section highlights the two smallest, most reusable snippets:

### Distinguishing unsupported vs. failed for D-02/ERR-02

```csharp
// Source: derived from existing GetOptional/TryLoadIntSetting pattern (SaberStateService.cs:538-558,
// read directly), modified per locked decisions D-02/D-03.
try
{
    var val = await commands.Send(cmd, retry: true);
    // ... use val ...
}
catch (SaberUnsupportedException)
{
    // Board doesn't support this — matches /old's hide-entirely behavior. No banner, no log.
}
catch (SaberProtocolException ex)
{
    // Genuine failure on a supported setting — this is what ERR-01's visible indicator is for.
    FailedSettings.Add(new FailedSetting(label, ex.Message, retryCallback));
}
```

### Existing Snackbar idiom this phase extends (not replaces)

```csharp
// Source: ProffieOS.Workbench/Components/SettingsPanel.razor.cs:81-84 (read directly — this
// pattern already exists at 20+ call sites across the codebase and must remain unchanged for
// write-path errors; D-04 only adds a NEW event-driven trigger for the background-load case,
// it does not touch this existing per-save-action pattern).
private async Task SaveSd(bool val)
{
    try { await State.SaveSdAsync(val); }
    catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| `Send()`/`Send2()` returns `""` for every failure mode (timeout, unsupported, disconnected, malformed) | Typed exception hierarchy distinguishes each failure mode at the source | This phase | Callers can react differently (hide vs. show-error) instead of every failure looking identical to "board doesn't support this" |
| Bare `catch { }` in `LoadSettingsBackgroundAsync()` | Catches specific exception, logs via `ILogger`, fires `SettingsLoadFailed` event | This phase | User sees a toast instead of a silently-empty Settings page; developers get a structured log line instead of nothing |
| No aggregate record of "which settings failed to load" | `FailedSettings` list on `SaberStateService`, rendered as a banner | This phase | ERR-01's "visible error/retry indicator" requirement becomes satisfiable; previously there was no data structure to render it from |

**Deprecated/outdated:** None — this is a net-new capability, not a replacement of a deprecated pattern.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The exact file path/name of the root-always-mounted layout component (assumed `MainLayout.razor` or `App.razor`) where the D-04 Snackbar subscription should live | Architecture Patterns Pattern 4, Recommended Project Structure | Low — this is a location-only assumption; if wrong, the planner simply points the same code at the correct existing layout file. This research session did not enumerate `ProffieOS.Workbench/Layout/` directly. |
| A2 | Whether `Whut?`-detection for D-02/D-03 should live inside `Send()` (post-tag-parse) or `Send2()` (pre-tag-parse), or duplicated in both — flagged explicitly in Pitfall 1 as needing live-board verification | Pattern 2, Pitfall 1 | Medium — if implemented at the wrong layer, tagging-enabled boards (ProffieOS 8.x+) could show falsely-classified unsupported settings as failures (or vice versa), directly undermining ERR-02's core distinction. This is the single highest-risk open item in this research; **recommend the planner size a dedicated verification task against real USB hardware before finalizing this classification logic**, since CLAUDE.md confirms USB hardware is available for manual verification. |
| A3 | The complete enumeration of ~15 call sites needing migration in the D-05 audit (Pitfall 2) is based on a single grep pass of `SaberStateService.cs`; other files (`Dashboard.razor.cs`, `Home.razor.cs`, etc.) that also call `commands.Send`-adjacent methods were not individually re-verified line-by-line in this research pass | Pitfall 2 | Medium — an incomplete audit means some call site could still receive an unexpected exception post-migration and crash instead of degrading gracefully. Recommend the planner's audit task re-run its own exhaustive grep rather than trusting this research's list as final. |
| A4 | Result-pattern-vs-exception industry guidance (multiple blog sources, no single official .NET framework doc) is being applied as if authoritative | Summary, Alternatives Considered | Low — this is widely-converged community guidance (4+ independent sources agree), not a single opinionated blogger; but it is not an official Microsoft/.NET framework recommendation, so tag confidence as MEDIUM, not HIGH. |

## Open Questions

1. **Where exactly does `Whut?` classification belong when `UseTagging` is active?**
   - What we know: `Send()` wraps commands with a tag prefix and calls `ParseTaggedResponse` to extract just the body; `Send2()` sees the raw, pre-parse wire response.
   - What's unclear: Whether a `Whut?`-format unsupported response from the board appears wrapped inside the tagged envelope (making `Send2()`'s check miss it) or as a plain-text response even when tagging is active (making `Send2()`'s check work fine). This is a live-protocol behavior question this research (static code reading only) cannot resolve.
   - Recommendation: Size a dedicated implementation-verification task that sends a known-unsupported command to a real connected board with tagging both on and off, and confirms exactly which layer sees the `Whut?` prefix in each mode, before finalizing where the `SaberUnsupportedException` throw happens.

2. **Exact root layout component file for the D-04 Snackbar subscription.**
   - What we know: The pattern (subscribe once, `InvokeAsync(Snackbar.Add)`) is well-established from existing codebase precedent.
   - What's unclear: This research did not read `ProffieOS.Workbench/Layout/*.razor` directly to confirm the exact file that's mounted on every route.
   - Recommendation: Planner should `ls`/grep for the `@Body` render fragment (standard marker of the root layout) before writing this task, and confirm it isn't already subscribing to other events in a way this new subscription should be co-located with.

## Environment Availability

This phase is a pure code/config change (C# exception classes, service logic, Razor component rendering) with no new external tool, service, runtime, or package dependency — every library used (`Microsoft.Extensions.Logging`, `MudBlazor`) is already referenced in the project's `.csproj` and already in use elsewhere in the codebase. No environment audit is needed.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | None detected — no test project exists anywhere in the repository (confirmed via glob for `**/*Test*` and `**/*.csproj`, which returned only the single application project) |
| Config file | none — see Wave 0 |
| Quick run command | none available |
| Full suite command | none available |

**This matches CLAUDE.md's explicit constraint:** "No test suite: Codebase currently has zero automated test coverage (all verification is manual)" and "WebUSB/BLE require a live browser + physical board, so nothing here can run in CI." This is a documented, intentional project constraint, not a gap this phase should silently try to fill by bootstrapping a full test framework — that would be scope creep beyond ERR-01/ERR-02.

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ERR-01 | Failed setting shows visible error/retry indicator instead of silently disappearing | manual-only (real USB board) | none — requires live browser + physical board per CLAUDE.md | N/A — no automated path exists for this project |
| ERR-02 | UI distinguishes "unsupported" from "failed" | manual-only (real USB board) | none — requires forcing a read failure vs. querying an actually-unsupported command on live hardware, per the phase's own Success Criteria #2 ("verifiable by comparing... against a forced read failure on the real USB board") | N/A |

**Justification for manual-only:** This is not a testing gap to close — it is the explicit, documented nature of this project (WebUSB/BLE requires live browser + physical board; CLAUDE.md states "nothing here can run in CI"). The phase's own Success Criteria already specifies manual verification against real hardware as the acceptance method.

**However**, the *pure logic* portions of this phase's work (the exception classification logic in isolation, e.g. "does `ParseTaggedResponse` correctly extract a `Whut?`-prefixed body," or "does `FailedSettings.Add` correctly aggregate multiple failures") are ordinary C# code with no browser/hardware dependency and COULD be unit-testable if a test project existed. This research flags this as a genuine Wave 0 gap below, since introducing even a minimal xUnit project for the pure-logic slice would meaningfully de-risk Pitfall 1 (the tagged-response parsing ambiguity) without needing a physical board for that specific piece.

### Sampling Rate

- **Per task commit:** No automated command exists; rely on `dotnet build` succeeding (compile-time check only) plus code review.
- **Per wave merge:** Manual verification against real USB board per CLAUDE.md's testing constraint, following the phase's own Success Criteria #1 and #2 exactly (force a read failure, compare against an actually-unsupported command).
- **Phase gate:** Manual UAT against real hardware before `/gsd-verify-work`; BLE-path changes (if any parsing logic is shared) are code-review-verified only per CLAUDE.md/STATE.md's explicit BLE-hardware-unavailability constraint.

### Wave 0 Gaps

- [ ] **Optional but recommended:** A minimal xUnit test project (e.g. `ProffieOS.Workbench.Tests/`) covering ONLY the pure-logic slice with no browser/JS-interop dependency — specifically `ParseTaggedResponse`'s behavior when the wrapped body starts with `Whut?`, and `SaberProtocolException` subtype construction/message formatting. This directly de-risks Pitfall 1/Open Question 1 without requiring hardware. **This is new project infrastructure and should be flagged to the user/planner as an explicit scope decision** (adding the repo's first-ever test project), not silently assumed — REQUIREMENTS.md's Out of Scope table excludes "Automated CI test suite for USB/BLE flows" specifically, but a *pure-logic* unit test with zero USB/BLE surface may not be covered by that exclusion; this is a judgment call for the planner/user, not this research to decide unilaterally.
- [ ] No `tests/` or `conftest`-equivalent directory exists; if the above is adopted, `dotnet new xunit` scaffolding is the standard starting point for a `net10.0` project.
- [ ] Framework install (if adopted): `dotnet add package xunit` + `dotnet add package Microsoft.NET.Test.Sdk` in a new test project — **not** in the main `ProffieOS.Workbench.csproj`.

*If the planner/user decides against introducing any test infrastructure (consistent with the project's current zero-test-coverage state and Out of Scope framing), the gap above should be explicitly declined rather than silently dropped — worth a one-line note in the plan's assumptions.*

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-------------------|
| V2 Authentication | No | This phase doesn't touch the existing BLE password-auth flow (`SendPasswordAndWait`) at all |
| V3 Session Management | No | No session/cookie concept in this WASM app |
| V4 Access Control | No | Single-user local tool, no access control surface |
| V5 Input Validation | Marginally relevant | Error messages surfaced to the UI (`ex.Message`) originate from the board's raw response or from this phase's own exception constructors — both are already-trusted-format strings (board firmware output, or hardcoded C# message templates), not attacker-controlled free text from an external network boundary. No new input validation surface is introduced. |
| V6 Cryptography | No | No cryptography involved in error surfacing |
| V7 Error Handling and Logging | **Yes — this is the core of the phase** | Standard control: never expose raw stack traces or internal exception details to end users beyond a human-readable reason string; log full exception detail (including stack trace) via `ILogger`, but surface only `ex.Message` (already the existing pattern in every `SaveXAsync` Snackbar call) to the UI. The typed exception hierarchy's `.Message` values in this research are deliberately human-readable and free of any sensitive internal state (command strings, timing values) — none of which are secrets in this application's threat model. |

### Known Threat Patterns for {stack}

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|----------------------|
| Information disclosure via verbose error messages | Information Disclosure | Not a meaningful threat in this app's context — this is a single-user local device-configuration tool with no multi-tenant or remote-attacker boundary; the "board doesn't respond" / "timed out" strings proposed in this research contain no secrets, credentials, or PII. Standard web-app error-message-redaction guidance (e.g., hiding stack traces from end users in a hosted multi-tenant app) is over-engineering for this specific single-user desktop-tool threat model, but the *practice* of not leaking raw stack traces to the Snackbar UI is still followed here as good hygiene (log full detail via `ILogger`, show only `.Message` via Snackbar) since it costs nothing and matches the existing codebase convention. |
| Log injection via unsanitized board response strings | Tampering | The board's raw response text (which may appear inside exception messages, e.g. `SaberCommunicationException`'s `innerMessage`) is written to `ILogger`. `Microsoft.Extensions.Logging`'s structured logging (`_logger.LogWarning("...{Command}...", cmd)`) already treats `cmd`/response text as a parameter, not string-interpolated into the log template, which is the standard mitigation for log-forging via untrusted input — this is naturally satisfied by using the structured-logging call signature shown in Pattern 2/3 above rather than string-concatenating raw board output into the log message template itself. |

**Note on scope:** Given this app's threat model (local, single-user, browser-to-USB-device tool, no server, no multi-tenant surface, no network-exposed API), the ASVS categories most commonly triggered by web-app error handling (info disclosure to other tenants, error-based enumeration attacks) don't meaningfully apply. V7 (Error Handling and Logging)'s core hygiene practice — don't leak raw internals to end users, do log full detail server-side (here: browser devtools console via the WASM console logger) — is followed as good practice at negligible cost, consistent with the existing codebase convention already in place for Snackbar-surfaced errors.

## Sources

### Primary (HIGH confidence)
- `.planning/research/STACK.md` (project-level research, already HIGH confidence on `Microsoft.Extensions.Logging` recommendation) — reused directly, not re-derived
- Direct codebase reads: `SaberCommandService.cs`, `SaberStateService.cs`, `SaberConnectionService.cs`, `SettingsPanel.razor(.cs)`, `Settings.razor.cs`, `SettingItem.cs`, `Program.cs`, `ProffieOS.Workbench.csproj`, and 9 other component `.razor.cs` files (via grep) — all confirmed directly from the working tree, not assumed
- `/old/app.html` (reference-only baseline) — read directly at the relevant `generateIntSetting`/`HasCmd`/`Whut?` lines to confirm actual `/old` behavior (no visible per-setting error UI exists in `/old`; it only logs to console — this refines CONTEXT.md's framing slightly, see Summary)

### Secondary (MEDIUM confidence)
- [MudBlazor GitHub Discussion #6209 — "Add controllers to MudBlazor server side with SnackbarService"](https://github.com/MudBlazor/MudBlazor/discussions/6209) — confirms `SnackbarService` constructor requires `NavigationManager`, informing the "can't inject into singleton" finding
- [MudBlazor GitHub Discussion #10942 — "Is it possible to dependency inject ISnackbar in MAUI razor hybrid app?"](https://github.com/MudBlazor/MudBlazor/discussions/10942) — corroborates the same constraint from a second independent thread
- [MudBlazor SnackbarService.cs source (GitHub, dev branch)](https://github.com/MudBlazor/MudBlazor/blob/dev/src/MudBlazor/Components/Snackbar/SnackbarService.cs) — confirms constructor signature includes `NavigationManager`
- [Functional Error Handling in .NET With the Result Pattern — milanjovanovic.tech](https://www.milanjovanovic.tech/blog/functional-error-handling-in-dotnet-with-the-result-pattern/) — Result-vs-exception framework (expected business outcome vs. infrastructure failure)
- [Error handling: Exception or Result? — enterprisecraftsmanship.com](https://enterprisecraftsmanship.com/posts/error-handling-exception-or-result/) — independent corroboration of the same framework
- [In C#, When Should You Use Exceptions, Result Objects, or Validation Errors? — pietschsoft.com](https://www.pietschsoft.com/post/2026/05/24/csharp-when-should-you-use-exceptions-result-objects-validation-errors) — third independent corroboration, dated 2026
- [ErrorOr vs OneOf vs FluentResults in .NET — codingdroplets.com](https://codingdroplets.com/erroror-vs-oneof-vs-fluentresults-dotnet-result-pattern) — comparison of Result-pattern library options (considered and rejected, see Alternatives Considered)
- [Microsoft Learn — ASP.NET Core Blazor logging](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/logging?view=aspnetcore-7.0) — confirms `ILogger<T>` DI pattern and `WebAssemblyConsoleLoggerProvider` behavior for client-side apps
- [GitHub — mikoskinen/Blazor.EventAggregator](https://github.com/mikoskinen/Blazor.EventAggregator) — event-aggregator alternative, considered and rejected in favor of the existing plain-event pattern

### Tertiary (LOW confidence)
- None — every claim in this research is either directly verified against the codebase, cited from an official/community documentation source, or explicitly flagged in the Assumptions Log above.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages, both `Microsoft.Extensions.Logging` and `MudBlazor` already confirmed present and already project-level-researched at HIGH confidence
- Architecture (exception hierarchy + event-bridge pattern): HIGH for the overall shape (directly modeled on existing codebase precedent: `LastDisconnectReason`, `StateChanged`); MEDIUM for the exact `Whut?`-classification layer placement (flagged as Open Question 1 / Pitfall 1, needs live-board verification)
- Pitfalls: HIGH for Pitfalls 2 and 3 (directly traceable in code, well-established Blazor gotcha respectively); MEDIUM for Pitfall 1 (requires live protocol behavior this research couldn't observe)

**Research date:** 2026-07-04
**Valid until:** 30 days (stable .NET/MudBlazor APIs; no fast-moving dependencies introduced by this phase)

---
*Phase: 1-Error Surfacing*
*Researched: 2026-07-04*

# Phase 1: Error Surfacing - Context

**Gathered:** 2026-07-04
**Status:** Ready for planning

<domain>
## Phase Boundary

When a board setting fails to load, or a background operation (`LoadSettingsBackgroundAsync`) errors, the user sees it — never silent disappearance. This phase delivers **error visibility and logging only**: no new capabilities, no connection-layer redesign (that's Phase 2/3). Covers ERR-01 and ERR-02.

**Guiding principle for this phase (and this milestone generally):** the rewrite must be **functionally identical to `/old`** — not a literal code copy, but the same functional behavior and capability set. When a gray area is ambiguous, match what `/old` does rather than inventing new UX. This is why "hide unsupported settings" (matching `/old`'s current behavior) was chosen over inventing a new grayed-out-row UI.

</domain>

<decisions>
## Implementation Decisions

### Retry behavior
- **D-01:** Failed settings show a panel-level error banner (not per-field indicators — already excluded by REQUIREMENTS.md Out of Scope) listing which settings failed, with a **manual retry button**. No automatic/background retry — avoids extra board chatter beyond what's already flagged as a concern in REQUIREMENTS.md Out of Scope (connection "ping" chatter).

### Unsupported vs Failed (ERR-02)
- **D-02:** Settings the board doesn't support stay **hidden entirely** — same as today's behavior and matches `/old`. Only genuine read failures on settings the board *does* support get a visible error indicator (the panel-level banner from D-01).
- **D-03 (implementation note, not a re-ask):** Today `GetOptional()` conflates "board said unsupported" (response starts with `Whut?`) with "no response / timeout" — both collapse to `null`/`""`. Distinguishing D-02's two cases requires the code to tell these apart at the source (see D-05).

### Cross-page visibility (background load failures)
- **D-04:** When `LoadSettingsBackgroundAsync()` fails while the user is on a different page, a **global MudBlazor Snackbar/toast** fires app-wide immediately — reusing the existing Snackbar pattern already used for write errors (see `code_context` below). This does not replace the panel-level banner (D-01): the Settings panel still shows the error banner whenever the user later visits Settings, so both the immediate toast and the persistent banner are needed.

### Error detail level
- **D-05:** Error messages carry a **specific reason** (e.g. "timed out waiting for board response" vs. "board disconnected" vs. "unexpected response"), not a generic "failed to load." User explicitly confirmed this includes the underlying breaking change: **`Send()`/`Send2()` must stop returning `""` for every failure case.** Phase 1 scope includes:
  1. Auditing call sites that currently treat an empty-string return as "feature unsupported" (flagged as a blocker in STATE.md).
  2. Changing `Send()`/`Send2()` to carry a distinguishable failure reason (typed result or exception — left to research/planning to decide the concrete mechanism).
  3. This is the mechanism that also resolves D-03 — once failure reason is distinguishable at the `Send()` level, "unsupported" vs "timed out" vs "disconnected" naturally separate.

### Claude's Discretion
- Exact mechanism for carrying the failure reason through `Send()`/`Send2()` (e.g., a `Result<T>`-style return, a typed exception hierarchy, or an out-parameter) is left to the planner/researcher — the user only locked the *requirement* (specific reasons, no more silent `""`), not the *shape* of the fix.
- Exact wording/copy of error messages and the retry button.
- Whether logging (Microsoft.Extensions.Logging, per STATE.md decision) is added at the `Send()` layer, the `SaberStateService` layer, or both — left to research/planning.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Root cause analysis (this phase's primary input)
- `.planning/codebase/CONCERNS.md` — "Bug Report: Missing Settings Display Values" and "Silent Exception Handling in Background Tasks" sections trace the exact root cause this phase fixes: `SaberStateService.cs:186-194` (`LoadSettingsBackgroundAsync`, bare `catch {}`), `SaberStateService.cs:411-470` (`LoadSettingsValuesAsync`, per-setting queries), `SaberStateService.cs:544-558` (`TryLoadBoolSetting()`/`TryLoadIntSetting()`)
- `.planning/codebase/ARCHITECTURE.md` — "Primary Request Path" and "Anti-Pattern: Silent Timeout Swallowing Settings Load" sections; `SettingsPanel.razor` (lines 120-156) is where `State.TimingIntSettings`/`State.SensitivityIntSettings` currently render

### Requirements and scope
- `.planning/REQUIREMENTS.md` — ERR-01, ERR-02 (this phase's requirements); Out of Scope table entry "Per-field inline retry affordances" (locks the panel-level-banner decision in D-01); Out of Scope entry "Connection health ping" (informs D-01's no-auto-retry choice)
- `.planning/PROJECT.md` — Core Value statement (functional parity with `/old`); Key Decisions table; Context section citing the forum bug report this phase addresses
- `.planning/STATE.md` — Accumulated Context → Decisions: "Microsoft.Extensions.Logging for structured error surfacing (Phase 1)" (stack addition already decided); Blockers/Concerns: the `Send()`/`Send2()` breaking-change risk that D-05 explicitly takes on

### Reference-only (do not modify)
- `/old/app.html` — the functional-parity baseline (see `<domain>` guiding principle); check its settings-load and error-handling behavior when in doubt about what "matching `/old`" means for a specific case

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- MudBlazor `Snackbar` — already used for write-error toasts at the component level (e.g., `SettingsPanel` save methods catch and call `Snackbar.Add(ex.Message, Severity.Error)`); D-04's global toast should reuse this same mechanism rather than introducing a new notification system.

### Established Patterns
- Singleton services (`SaberStateService`, `SaberConnectionService`, `SaberCommandService`) with pub/sub via `StateChanged` events — any new "failed settings" state should live in `SaberStateService` and notify via the existing event so `SettingsPanel` re-renders without new plumbing.
- `GetOptional()` → `TryLoadBoolSetting()`/`TryLoadIntSetting()` bail-silently pattern is the exact mechanism D-03/D-05 need to change.

### Integration Points
- `SaberStateService.LoadSettingsBackgroundAsync()` (`SaberStateService.cs:186-194`) — where the bare `catch {}` needs to become a logged, surfaced failure (D-04's toast trigger point).
- `SaberCommandService.Send()`/`Send2()` (`SaberCommandService.cs:93-159`) — where the return-value semantics change for D-05.
- `SettingsPanel.razor` (lines 120-156) — where the panel-level error banner (D-01) and hidden-vs-shown logic (D-02) render.
- `Settings.razor.cs:OnInitialized()` — 2-second timeout pattern already partially does "explicit error reporting"; extend rather than replace per the Architecture doc's anti-pattern recommendation.

</code_context>

<specifics>
## Specific Ideas

No specific UI copy or visual mockups were provided. The one explicit non-negotiable: **the rewrite must stay functionally identical to `/old`** — this phase's decisions were deliberately chosen to match `/old`'s existing behavior (hide-unsupported) except where the roadmap explicitly requires new visibility (visible failure indicator, which `/old` also effectively provides via its own error handling, per PROJECT.md's problem description).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope. (Per-field retry, connection health pings, and diagnostic-log downloads were already excluded at the REQUIREMENTS.md level, not introduced during this discussion.)

</deferred>

---

*Phase: 1-Error Surfacing*
*Context gathered: 2026-07-04*

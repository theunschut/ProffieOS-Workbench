# Roadmap: ProffieOS Workbench — Reliability Stabilization

## Overview

This milestone takes the rewritten Blazor WebAssembly Workbench from "silently loses settings and crashes on disconnect" to "matches `/old`'s reliability, with visible failures instead of silent ones." The four phases build on each other in a deliberate order: first make failures visible (Phase 1), because every later phase needs a way to prove its fix actually worked given there's no automated test suite. Then close the specific reported crash by validating connection state before every write (Phase 2). Then rework the timeout/watchdog/backoff layer, which is the highest-complexity change and depends on the first two phases' observability and tightened connection signal to be tunable and verifiable (Phase 3). Finally, add explicit save confirmation and run the systematic `/old`-vs-new parity audit to catch any remaining silent gaps beyond the 3 already reported (Phase 4). Each phase is a vertical slice: a real, manually-observable behavior change verifiable against the author's USB board (BLE fixes are code-review verified only, per PROJECT.md constraints).

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Error Surfacing** - Failed setting loads and background errors become visible in the UI instead of silently disappearing
- [ ] **Phase 2: Connection Validation & Disconnect Handling** - Writes are guarded against a dead connection; disconnects produce a clear message instead of a crash
- [ ] **Phase 3: Timeout & Watchdog Rework** - Hung commands are detected independently of the timeout, timeouts are tuned per connection type, retries back off, and chunked writes get breathing room
- [ ] **Phase 4: Write Confirmation & Parity Audit** - Every save shows explicit success/failure, and a systematic `/old` comparison closes any remaining silent gaps

## Phase Details

### Phase 1: Error Surfacing

**Mode:** mvp
**Goal**: When a board setting fails to load or a background operation errors, the user sees it — never silent disappearance.
**Depends on**: Nothing (first phase)
**Requirements**: ERR-01, ERR-02
**Success Criteria** (what must be TRUE):

  1. When a setting (swing-on-speed, blade length, timing, sensitivity, or any other) fails to load from a connected board, the user sees a visible error/retry indicator for that specific setting instead of it silently vanishing from the panel
  2. The settings UI clearly distinguishes "this board's firmware doesn't support this setting" from "reading this setting failed" — verifiable by comparing a firmware-unsupported setting against a forced read failure on the real USB board
  3. Background load failures (e.g. `LoadSettingsBackgroundAsync`) surface to the user regardless of which page is currently mounted, instead of being swallowed by a bare `catch {}`

**Plans**: 2 plans
**Wave 1**

- [ ] 01-01-PLAN.md — Typed exception foundation: SaberProtocolException hierarchy, Send2()/Send() throw+classify+log, full SaberStateService call-site migration, live-board Whut? classification verification

**Wave 2** *(blocked on Wave 1 completion)*

- [ ] 01-02-PLAN.md — Visible capability: FailedSetting model + FailedSettings/SettingsLoadFailed on state, D-01 panel error banner + retry, D-04 global cross-page toast in MainLayout

### Phase 2: Connection Validation & Disconnect Handling

**Mode:** mvp
**Goal**: Users can trust that disconnecting the board (mid-write or otherwise) produces a clear message, never a crash or hang, and writes never fire against a dead connection.
**Depends on**: Phase 1
**Requirements**: CONN-01, CONN-02, CONN-03
**Success Criteria** (what must be TRUE):

  1. The board's connection state (connected/connecting/disconnected) is visible in the UI at all times, not only in the moment right after connecting — verifiable by watching the indicator persist across panel navigation on the real USB board
  2. Unplugging the USB board mid-write produces a visible "disconnected" message in the UI instead of an uncaught JS exception or a hung/crashed app
  3. Attempting a write after the board has disconnected is blocked with a clear error before any JS interop call is attempted, instead of throwing an uncaught exception

**Plans**: TBD

### Phase 3: Timeout & Watchdog Rework

**Mode:** mvp
**Goal**: Hung writes are detected and reported well before the full timeout elapses, timeouts fit the connection type in use, and retries/chunking no longer hammer or overflow the board.
**Depends on**: Phase 2
**Requirements**: WRITE-02, WRITE-03, WRITE-04, WRITE-05, WRITE-06
**Success Criteria** (what must be TRUE):

  1. A command that hangs on the real USB board is flagged by the watchdog and surfaced to the user well before the full command timeout elapses, instead of the UI appearing frozen for the whole duration
  2. The command timeout duration differs between USB and BLE connection types (verifiable by code inspection of the configured values; USB behavior confirmed live, BLE confirmed via code review only per project constraints)
  3. When a command retries, the user sees in-progress retry feedback (e.g. "Retrying 2/3...") instead of silence, and the delay between attempts visibly increases rather than staying flat
  4. Writing a large/chunked payload to the real USB board no longer produces the buffer-overflow failures seen before this phase, because a small delay is inserted between chunks

**Plans**: TBD

### Phase 4: Write Confirmation & Parity Audit

**Mode:** mvp
**Goal**: Every save gives the user an explicit success/failure signal, and a systematic comparison against `/old` confirms no other feature has silently regressed.
**Depends on**: Phase 3
**Requirements**: WRITE-01, AUDIT-01
**Success Criteria** (what must be TRUE):

  1. After every save/write attempt on the real USB board, the user sees an explicit confirmation ("Saved" or a specific failure reason) — never silence, even when the write ultimately fails
  2. Every feature in `/old/app.html` has been systematically walked and compared against the new app's equivalent, with a recorded pass/fail per feature
  3. Any additional silent gap the audit finds beyond the 3 originally reported bugs has either been fixed within this milestone or explicitly logged as an out-of-scope follow-up with a reason

**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Error Surfacing | 0/2 | Not started | - |
| 2. Connection Validation & Disconnect Handling | 0/TBD | Not started | - |
| 3. Timeout & Watchdog Rework | 0/TBD | Not started | - |
| 4. Write Confirmation & Parity Audit | 0/TBD | Not started | - |

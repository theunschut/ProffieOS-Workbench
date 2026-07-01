# Requirements: ProffieOS Workbench — Reliability Stabilization

**Defined:** 2026-07-01
**Core Value:** Users can reliably read from and write configuration to a connected ProffieOS board without silent failures or lost settings — matching everything the original Workbench could do, with a better UI.

## v1 Requirements

Requirements for this milestone. Each maps to roadmap phases.

### Connection Status

- [ ] **CONN-01**: User can see the board's connection state (connected/connecting/disconnected) at all times in the UI, not only immediately after connecting
- [ ] **CONN-02**: When the board disconnects unexpectedly (mid-write or otherwise), the app catches the resulting JS interop exception and shows a clear "disconnected" message instead of crashing or hanging
- [ ] **CONN-03**: Before sending any write command, the app validates the connection is still alive and blocks the write with a clear error if not, instead of letting an uncaught JS exception escape

### Error Surfacing

- [ ] **ERR-01**: When a setting (swing-on-speed, blade length, timing, sensitivity, or any other) fails to load from the board, the user sees a visible error/retry indicator for that setting instead of it silently disappearing
- [ ] **ERR-02**: The settings UI distinguishes "this board's firmware doesn't support this setting" from "reading this setting failed," so users aren't left guessing why a value is missing

### Write Reliability

- [ ] **WRITE-01**: After every save/write attempt, the user sees explicit confirmation of success or failure — never silence
- [ ] **WRITE-02**: A command watchdog detects hung/unresponsive commands independently of the overall timeout (restoring the `/old` app's periodic-check pattern), so a stuck write is detected well before the full timeout elapses
- [ ] **WRITE-03**: Command timeout is tuned per connection type (shorter for USB, longer for BLE) instead of one flat value for both
- [ ] **WRITE-04**: Command retries use exponential backoff instead of a fixed short delay, to avoid hammering an already-struggling connection
- [ ] **WRITE-05**: The UI shows retry attempts in progress (e.g. "Retrying 2/3...") instead of retrying silently
- [ ] **WRITE-06**: Chunked writes include a small delay between chunks to avoid overflowing the board's receive buffer

### Parity Audit

- [ ] **AUDIT-01**: Every feature in `/old` has been systematically compared against the new app, and any additional silent gaps beyond the 3 already reported are identified and fixed

## v2 Requirements

None — this milestone doesn't carry a v2 backlog. Items deliberately deferred beyond this milestone are tracked in Out of Scope below instead, since there's no currently-planned follow-up milestone to assign them to.

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Read-back write verification (re-read a setting after writing to confirm it stuck) | Doubles command traffic per save; would inherit the same hang risk being fixed until the watchdog/timeout rework is proven stable. Revisit only if silent save failures persist after this milestone. |
| Full connection/command-layer redesign (new state machine, rebuilt from scratch) | PROJECT.md scopes this milestone as audit + targeted fixes; untestable in CI and unverifiable on BLE, so a rewrite risks new regressions with no safety net |
| New feature work beyond `/old` parity | This milestone is stabilization, not net-new functionality; new ideas surfaced during the audit become backlog items for a future milestone |
| Automated CI test suite for USB/BLE flows | WebUSB/BLE require a live browser + physical hardware; cannot run in CI. Verification is manual (USB) or code-review-level (BLE) |
| Guaranteed BLE-hardware-verified fixes | Author has no BLE test hardware; BLE fixes are code-review verified only, explicitly lower confidence than USB fixes |
| Downloadable diagnostic log / "copy debug info" button | Valuable for future bug reports, but not required to close the 3 known bugs |
| Connection health "ping" during idle/long operations | Adds new background device chatter; increases BLE surface area needing hardware verification that isn't available |
| Per-field inline retry affordances (vs. panel-level error banner) | Panel-level messaging (ERR-01) already solves the core trust problem; finer-grained UI is a future polish pass |
| Elaborate reconnect-with-state-restoration UX (session persistence, auto-resume) | Net-new UX beyond `/old` parity; simple "disconnected — please reconnect" messaging is sufficient for this milestone |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| CONN-01 | TBD | Pending |
| CONN-02 | TBD | Pending |
| CONN-03 | TBD | Pending |
| ERR-01 | TBD | Pending |
| ERR-02 | TBD | Pending |
| WRITE-01 | TBD | Pending |
| WRITE-02 | TBD | Pending |
| WRITE-03 | TBD | Pending |
| WRITE-04 | TBD | Pending |
| WRITE-05 | TBD | Pending |
| WRITE-06 | TBD | Pending |
| AUDIT-01 | TBD | Pending |

**Coverage:**
- v1 requirements: 12 total
- Mapped to phases: 0 (pending roadmap creation)
- Unmapped: 12 ⚠️ (roadmap creation will assign phases)

---
*Requirements defined: 2026-07-01*
*Last updated: 2026-07-01 after initial definition*

---
phase: 01
slug: error-surfacing
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-07-04
---

# Phase 01 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | None — no test project exists anywhere in the repository (confirmed via research: glob for `**/*Test*`/`**/*.csproj` returned only the single application project). Optional: minimal xUnit for the pure-logic slice only — see Wave 0 Requirements. |
| **Config file** | none — see Wave 0 |
| **Quick run command** | none available |
| **Full suite command** | none available |
| **Estimated runtime** | N/A |

---

## Sampling Rate

- **After every task commit:** `dotnet build` (compile-time check only) plus code review — no automated test command exists project-wide
- **After every plan wave:** Manual verification against the real USB board per CLAUDE.md's testing constraint (force a read failure, compare against an actually-unsupported command per phase Success Criteria #1/#2)
- **Before `/gsd-verify-work`:** Manual UAT against real hardware; BLE-path changes (if any parsing logic is shared) are code-review-verified only per CLAUDE.md/STATE.md's explicit BLE-hardware-unavailability constraint
- **Max feedback latency:** N/A — no automated suite; manual verification cadence is per-wave against real hardware

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD (set by planner) | 01 | 1 | ERR-01 | V7 / — | Log full exception via `ILogger`; surface only `ex.Message` to Snackbar/banner, never a raw stack trace | manual | `dotnet build` (compile-only) + live USB board | ❌ W0 (optional) | ⬜ pending |
| TBD (set by planner) | 01 | 1 | ERR-02 | V7 / — | `SaberUnsupportedException` swallowed silently (setting stays hidden); any other `SaberProtocolException` recorded + logged | manual | `dotnet build` (compile-only) + live USB board | ❌ W0 (optional) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*Task ID column is a placeholder — the planner should fill in actual `{padded_phase}-{plan}-{task}` IDs once PLAN.md files exist.*

---

## Wave 0 Requirements

- [ ] **Optional — planner/user judgment call (RESEARCH.md flags this explicitly, does not decide unilaterally):** `ProffieOS.Workbench.Tests/` — a minimal xUnit project with zero USB/BLE surface, covering only pure-logic slices: `ParseTaggedResponse`'s handling of a `Whut?`-prefixed body, and `SaberProtocolException` subtype construction/message formatting. This directly de-risks Open Question 1 / Pitfall 1 (which layer — `Send()` vs `Send2()` — actually sees the `Whut?` prefix under tagging) without requiring hardware. REQUIREMENTS.md's Out of Scope table excludes "Automated CI test suite for USB/BLE flows" specifically; a pure-logic unit test with no USB/BLE surface may not be covered by that exclusion, but this is a scope decision the planner/user should make explicitly rather than assume.
- [ ] If adopted: `dotnet new xunit` scaffolding + `dotnet add package xunit` + `dotnet add package Microsoft.NET.Test.Sdk` in the new test project (not the main `ProffieOS.Workbench.csproj`)
- [ ] If declined: state so explicitly in the plan's assumptions rather than silently dropping the gap

*If declined: "Existing infrastructure (manual verification against real hardware) covers all phase requirements per the documented project constraint (CLAUDE.md: 'No test suite... nothing here can run in CI')."*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Failed setting shows a visible error/retry indicator instead of silently disappearing | ERR-01 | WebUSB requires a live browser + physical board; no CI path exists per CLAUDE.md | Connect the real USB board, force a read failure on a setting the board supports (e.g. disconnect mid-read), confirm the panel-level banner appears listing the failed setting with a working manual retry button |
| UI distinguishes "unsupported" from "failed" | ERR-02 | Requires comparing live board responses (a genuinely-unsupported command vs. a forced failure) which cannot be simulated without hardware | On the real USB board: (1) query a command the connected firmware genuinely doesn't support — confirm it stays hidden with no banner/log noise; (2) force a read failure on a setting the board DOES support — confirm it shows in the D-01 banner with a specific reason (not a generic "failed to load" message) |
| Background load failure surfaces regardless of current page | ERR-01 (Success Criteria #3) | Requires live interaction across page navigation with a real board | While on a page other than Settings, trigger a background settings-load failure on the real board; confirm a global Snackbar toast appears immediately, and confirm the Settings panel still shows the persistent banner when later visited |
| `Whut?` classification layer placement (`Send()` vs `Send2()`, tagged vs untagged) | ERR-02 (implementation correctness) | Live protocol behavior — RESEARCH.md (static code reading only) could not resolve this; needs real tagged/untagged board traffic | Send a known-unsupported command to the real board with tagging ON and OFF; trace which layer actually observes the `Whut?` prefix in each mode; confirm classification happens at the correct layer per Pitfall 1 in RESEARCH.md |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies — N/A for this phase; manual-only verification against real hardware is the documented project constraint (CLAUDE.md), not a coverage gap
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify — N/A (no automated test path exists project-wide); `dotnet build` (compile-time) is the closest automated signal available per task
- [ ] Wave 0 covers all MISSING references — the optional xUnit project above is flagged for an explicit planner/user decision, not silently dropped
- [ ] No watch-mode flags — N/A, no test runner exists
- [ ] Feedback latency < N/A — manual verification cadence is per-wave against real hardware, not applicable to a latency budget
- [ ] `nyquist_compliant: true` — set once the planner/user has made an explicit accept/decline decision on the optional Wave 0 xUnit project

**Approval:** pending

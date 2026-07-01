# ProffieOS Workbench (Rewrite)

## What This Is

A browser-based configuration tool for ProffieOS lightsaber boards — connects over WebUSB or Web Bluetooth directly from the browser (no native app, no drivers) to read and write presets, blade settings, and gesture/timing/sensitivity configuration. It's a from-scratch Blazor WebAssembly (.NET 10) rewrite of the original vanilla-JS "ProffieOS Workbench" (kept for reference in `/old`, which the author doesn't own), built to fix that app's UX and move to a modern, primarily-.NET stack — while matching its feature set.

## Core Value

Users can reliably read from and write configuration to a connected ProffieOS board without silent failures or lost settings — matching everything the original Workbench could do, with a better UI.

## Requirements

### Validated

- ✓ Connect to a ProffieOS board via WebUSB or Web Bluetooth — existing
- ✓ View and edit presets (tracks, fonts) — existing
- ✓ Configure blade length, brightness, and core settings — existing
- ✓ Modern Blazor/MudBlazor UI, replacing the old single-file HTML app — existing

### Active

- [ ] Parity audit: systematically compare every `/old` feature against the new app to find silent gaps beyond the 3 already reported
- [ ] Fix: settings load surfaces failures instead of swallowing them silently (swing-on-speed, blade length, timing, sensitivity, and any other settings the audit finds missing)
- [ ] Fix: USB/BLE write path validates connection state before writing, so a disconnect mid-write can't throw an uncaught JS exception
- [ ] Fix: save/write timeout strategy reworked (per-connection-type timeout, watchdog for hung commands, backoff on retry) so timeouts stop silently losing settings

### Out of Scope

- Full connection/command layer redesign (new state machine, rebuilt from scratch) — deferred; this milestone is audit + targeted fixes, not a rebuild
- New feature work beyond restoring parity — this milestone is stabilization, not net-new functionality
- Guaranteed BLE-hardware-verified fixes — author lacks BLE test hardware, so BLE-path confidence is necessarily lower than USB-path confidence

## Context

- This is a full reimplementation, not a port, of `/old` — which has caused unintentional feature drift the author wasn't aware of until a user reported it via forum.
- Motivation for the rewrite: `/old`'s UX was bad (a single sprawling HTML file), plus a personal preference to avoid JavaScript where possible in favor of .NET.
- The reported bug (paraphrased from a forum post): swing-on-speed, blade length, timing, and sensitivity values aren't shown when connected via WebUSB; a JS interop crash occurs ("Command failed: USB not connected Error: USB not connected at Object.write"); and saves frequently time out, losing settings.
- Codebase mapping (`.planning/codebase/CONCERNS.md`) traced plausible root causes for all three to one underlying pattern: `SaberStateService.LoadSettingsBackgroundAsync()` (`ProffieOS.Workbench/Services/SaberStateService.cs:186-194`) silently swallows exceptions with a bare `catch {}` — no logging, no surfaced error; the fixed 20-second command timeout (`SaberCommandService.cs:141`) has no watchdog for hung commands (the old app had a 10s watchdog); and connection state isn't validated immediately before a JS interop write (`usb.js:217`), so a disconnect mid-write throws uncaught.
- BLE connection/discovery has been a recurring pain point historically — recent PRs (#8–#16: "feature/stability-issues", "feature/BLE-discovery-issues", "feature/ui-fixes-and-ble-connection-fix") show a series of iterative, uncertain fixes (commit messages literally "fix?", "fiix?", "fiiix?") without a systematic test/verification approach. Worth treating BLE reliability as a known-fragile area during the audit, not just the 3 newly reported symptoms.
- No automated test coverage exists anywhere in the new app (confirmed by codebase mapping — see `.planning/codebase/TESTING.md`).
- The author has a real board for WebUSB testing, but no BLE-enabled board — BLE-path fixes will carry lower verification confidence than USB-path fixes.

## Constraints

- **Tech stack**: Prefer .NET/C# over JavaScript wherever feasible; JS interop exists only where WebUSB/WebBluetooth require it — minimize new JS surface area, don't grow it.
- **Ownership**: `/old` is not owned by the author — reference-only source for comparison, never modify it.
- **Testing**: WebUSB/BLE require a live browser + physical board, so nothing here can run in CI. USB-path fixes are verified manually against real hardware; BLE-path fixes rely on code review and awareness of past regressions, since no BLE test hardware is available.
- **No test suite**: Codebase currently has zero automated test coverage (all verification is manual).

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Audit + fix underlying pattern (not a narrow 3-bug patch, not a full redesign) | The 3 reported bugs trace to one root cause (silent exception swallowing + no watchdog); a narrow patch risks leaving other silent gaps undiscovered, a full redesign is bigger than the problem warrants | — Pending |
| Manual verification against real hardware instead of automated tests | WebUSB/BLE can't run in CI; author has a real USB board but no BLE board | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-07-01 after initialization*

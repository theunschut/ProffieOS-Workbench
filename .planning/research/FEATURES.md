# Feature Research

**Domain:** Browser-based hardware configuration tools (WebUSB/Web Bluetooth device configurators — firmware flashers, IoT setup wizards, maker/hobbyist config UIs)
**Researched:** 2026-07-01
**Confidence:** MEDIUM

## Context Note

This is a **stabilization milestone**, not a greenfield feature build. The "features" below are reliability/error-handling UX affordances — the kind of thing every mature tool in this space has and ProffieOS Workbench currently lacks (per `.planning/codebase/CONCERNS.md`: silent `catch {}` swallowing settings-load failures, uncaught JS interop exceptions on write, and 20s+ silent timeouts that lose settings with zero user feedback). Comparable tools surveyed: ESP Web Tools (esphome.github.io/esp-web-tools — the most mature/polished example, distributed as a reusable web component used by ESPHome, Improv Wi-Fi, and third-party ESP32 projects), Adafruit WebSerial ESPTool, micro:bit WebUSB flasher, Google's own Web Bluetooth/WebUSB sample gallery (googlechrome.github.io/samples/web-bluetooth), and the project's own predecessor `/old/app.html` (which already implements a watchdog pattern — 20s stale-callback detection checked every 10s, `Die("timeout")` — that the new app regressed on).

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete or untrustworthy — and directly explain the 3 reported bugs.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Persistent connection status indicator** (connected/connecting/disconnected, visible at all times) | Every surveyed tool (ESP Web Tools, Adafruit ESPTool, Google's own samples) shows connection state continuously, not just at connect-time. Users need to know *before* they hit save whether the board is even listening. | LOW | Workbench already tracks `IsConnected`; the gap is likely display, not tracking. Cheapest, highest-leverage fix. |
| **Visible error surfacing for failed reads/loads** (no more bare `catch {}`) | This is the direct cause of Symptom 1 (missing swing-on-speed/blade-length/timing/sensitivity values) — the old app showed these values; the new app silently drops them on any read failure. Users cannot distinguish "board doesn't support this" from "the read failed." | LOW–MEDIUM | Requires: (1) stop swallowing exceptions in `LoadSettingsBackgroundAsync()`, (2) route errors through `commands.OnError` or equivalent, (3) render a per-field or panel-level error/retry affordance instead of just omitting the field. |
| **Write/save confirmation feedback** (success or failure, never ambiguous silence) | Table stakes for *any* form that persists to a remote/physical target. ESP Web Tools shows explicit install-progress states (Preparing → Installing → Done/Error) rather than leaving users guessing. Workbench today: a failed save just... does nothing visible. | LOW–MEDIUM | Minimum bar: a toast/snackbar or inline status ("Saved" / "Save failed — tap to retry") after every write attempt. Read-back verification (re-reading the value from the board to confirm it stuck) is a stronger but higher-effort version — see Differentiators. |
| **Pre-write connection validation** (check connection is alive immediately before issuing a write, not just at connect-time) | Prevents Symptom 2 — the uncaught "USB not connected" JS exception. Standard defensive pattern across all WebUSB/BLE tools: check-then-act right at the write call site, because WebUSB/BLE connections can silently drop between UI actions. | LOW | Already scoped as a fix in PROJECT.md Active requirements. Add the guard in `SendChunked()`/`Send2()` in C#, and harden the `_device.opened` check in `usb.js`. |
| **Graceful disconnect handling** (catch the JS interop exception, translate to a user-facing message, don't crash the Blazor render) | Chrome's own Web Bluetooth guidance and every tool surveyed treats disconnection as an expected, common event (device out of range, USB unplugged, power loss) — not an exceptional crash path. An uncaught exception bubbling into Blazor's render loop is a correctness bug, not just missing polish. | LOW–MEDIUM | Wrap JS interop calls with try/catch on the C# side at the call boundary (not just deep in `Send2`), and register `ondisconnect`/`gattserverdisconnected`-equivalent handlers to proactively flip UI state to "disconnected" instead of waiting for the next failed write to reveal it. |
| **Command watchdog / hung-command detection** (distinct from a hard timeout) | The old app already had this (10s watchdog, checked every 10s, kills at 20s of silence) — the new app has *only* a flat 20s timeout with no watchdog, which is a regression, not a new feature. Every tool that streams commands over USB/BLE needs a way to detect "no response arriving" separately from "response is taking a while." | MEDIUM | This is the direct root cause of Symptom 3 (frequent timeouts losing settings). Reworking this is already an Active requirement in PROJECT.md — treat it as restoring parity with `/old`, not innovating. |
| **Retry visibility** (user can see a retry is happening, and see if all retries were exhausted) | Silent retries that eventually give up silently are functionally identical to no retry — the user still loses data with no explanation. Google's Web Bluetooth guidance explicitly recommends bounded retries (e.g., 3 attempts with backoff) surfaced to the user, not silent infinite/short retry loops. | LOW–MEDIUM | Workbench's JS layer already retries at the transport level (usb.js/bluetooth.js) and the command layer retries with fixed 50ms delay — none of this is visible to the user. Minimum: log it; better: show "Retrying (2/3)..." during a stalled write. |
| **Clear distinction between "not supported by this firmware/board" vs. "failed to read"** | Directly named in CONCERNS.md recommendation #1. Users need confidence that a missing setting means their board genuinely lacks that feature, not that the tool is broken. Ambiguity here is exactly what triggered the forum bug report. | LOW | Requires the settings loader to track and expose *why* a value is absent (timeout/error vs. command genuinely unsupported/returned empty), not just omit it either way. |

### Differentiators (Nice-to-Have Polish, Not Required for This Milestone)

Valuable, but explicitly **not required** to close this milestone's scope (restoring parity + fixing 3 known bugs). Consider only if time remains after table stakes are solid.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **Read-back write verification** (re-read a setting after writing it to confirm the board actually stored it) | Strongest possible guarantee against "silent save failure" — CONCERNS.md recommendation #5 calls this out explicitly. Some firmware flashers (DFU-based tools) do checksum/hash verification after flashing for the same reason. | MEDIUM–HIGH | Doubles the command traffic for every save (write + read-back), which interacts with the timeout/watchdog rework — sequence this *after* the timeout fix lands, not before, or verification reads will inherit the same hang risk. |
| **Downloadable diagnostic log / "copy debug info" button** | ESP Web Tools ships a "Download Logs" button on its error page specifically so users can paste output into a GitHub issue. Given this project's forum-bug-report discovery pattern, a log export would materially shorten the feedback loop on the *next* silent failure. | LOW–MEDIUM | High leverage for a hobbyist/maker audience (same as ESPHome's) but not required to fix the 3 known bugs — defer unless trivial to bolt onto existing logging once added. |
| **Adaptive per-connection-type timeout** (different timeout for USB vs. BLE, since BLE is inherently slower/less reliable) | CONCERNS.md recommendation #1 suggests USB 10s / BLE 15s vs. today's flat 20s for both. Reflects real-world asymmetry: BLE has higher latency and the project's own commit history (PRs #8–#16, "fix?"/"fiix?"/"fiiix?") shows BLE has been persistently flakier. | LOW–MEDIUM | Cheap to implement once the watchdog rework is underway; bundle it into the same PLAN rather than treating as separate scope. |
| **Exponential backoff on command retry** (vs. today's flat 50ms) | Reduces the "hammering an overloaded device" failure mode CONCERNS.md flags — standard practice per Google's own Web Bluetooth reconnect sample. | LOW | Small, contained change inside `SaberCommandService.Send()`. Nice-to-have relative to just fixing visibility of failures, since backoff tuning without user feedback is invisible to users anyway. |
| **Inter-chunk backpressure delay in chunked writes** | CONCERNS.md flags `SendChunked()` as sending 20-byte chunks with no delay, risking RX buffer overflow on the device. | LOW | Real fix, but it's a root-cause hardening item, not a *user-facing* reliability UX feature — track it as an engineering fix, not a features/UX deliverable, though it belongs in the same milestone. |
| **Rich per-field inline validation/retry affordance** (e.g., a small retry icon next to each individual failed setting, rather than a single panel-level banner) | More polished than a single "some settings failed to load" banner, and lets users retry just the failed field instead of a full reload. | MEDIUM | Nice UX, but panel-level error messaging (table stakes above) already solves the core trust problem. Defer to a future UI-polish pass unless the panel-level version turns out trivial to extend. |
| **Connection health "ping" during idle/long operations** | CONCERNS.md's Architecture section suggests periodic connection probes to catch "silently disconnected" states before the next write attempt discovers it the hard way. | MEDIUM | Valuable but adds new background chatter to the device; given the author lacks BLE test hardware, an added polling mechanism increases the surface area needing hardware verification. Treat as a stretch goal, not baseline. |

### Anti-Features (Explicitly Do Not Build This Milestone)

Things that would be reasonable in a different context but are out of scope here — either because PROJECT.md already excludes them, or because they'd meaningfully expand risk/effort beyond this milestone's audit-and-fix framing.

| Feature | Why It Seems Appealing | Why Problematic Here | Alternative |
|---------|------------------------|-----------------------|-------------|
| **Full connection/command-layer redesign** (new state machine rebuilt from scratch) | Tempting once you see how many small issues trace back to the same "no watchdog, no error propagation" pattern — feels like "just do it right this time." | PROJECT.md explicitly scopes this milestone as audit + targeted fixes, not a rebuild; a redesign is untestable in CI (no automated coverage exists) and unverifiable on BLE (no test hardware), so a full rewrite risks introducing new, harder-to-diagnose regressions with no safety net. | Fix the specific root-cause pattern (silent exception swallowing + missing watchdog + missing pre-write validation) surgically in the existing services, per the Active requirements already defined. |
| **New settings/features beyond `/old` parity** | Once you're touching `SaberStateService`/`SaberCommandService`, it's tempting to add new capabilities while in there. | PROJECT.md explicitly excludes "new feature work beyond restoring parity — this milestone is stabilization, not net-new functionality." Scope creep here directly threatens the milestone's completion criteria. | Log any new-feature ideas surfaced during the audit as backlog items for a future milestone, not folded into this one. |
| **Automated CI test suite for USB/BLE flows** | Reliability work naturally raises "we should have tests for this." | WebUSB/BLE require a live browser + physical hardware; CI cannot exercise them. Building test infrastructure here is a different (larger) project than "fix 3 known bugs + audit for siblings." | Rely on manual verification against real hardware (USB) and code-review-level scrutiny (BLE), as PROJECT.md's Constraints section already establishes. Consider a follow-up milestone for structured manual test scripts/checklists if repeat regressions continue. |
| **Guaranteed BLE-hardware-verified fixes** | Symmetry with USB fixes feels right — "shouldn't every fix be verified the same way?" | The author has no BLE test hardware. Demanding hardware verification for BLE would block the milestone entirely or force purchasing hardware, which PROJECT.md already rules out. | Explicitly document BLE fixes as "code-review verified, lower confidence" in the audit output, matching PROJECT.md's stated Constraint. |
| **Elaborate reconnect-with-state-restoration UX** (auto-reconnect flows, session persistence across tab reloads, "resume where you left off") | Seen in some mature IoT tools (Espruino's discussion of avoiding re-pairing prompts) and feels like good polish. | High complexity, BLE-pairing-model-dependent, and not needed to fix the 3 reported bugs or close the parity gap — it's net-new UX beyond what `/old` did. | Note as a future differentiator idea; not in scope. Simple "disconnected — please reconnect" messaging (table stakes above) is sufficient for this milestone. |

## Feature Dependencies

```
Pre-write connection validation
    └──must land before──> Write/save confirmation feedback
                               (confirming a write that wasn't actually attempted is worse than no confirmation)

Command watchdog / hung-command detection
    └──shares root cause with──> Retry visibility
    └──must land before──> Adaptive per-connection-type timeout
                               (tune per-type timeouts against a watchdog that already exists, not a bare await)

Visible error surfacing for failed reads/loads
    └──requires──> Clear distinction between "not supported" vs "failed to read"
                       (can't surface a good error message without first classifying the failure reason)

Write/save confirmation feedback
    └──enhances──> Read-back write verification
                       (confirmation banner is v1; read-back is the stronger v2 built on top of it)

Graceful disconnect handling
    └──enables──> Persistent connection status indicator
                       (indicator is only trustworthy if disconnect events are actually caught and propagated)

Downloadable diagnostic log
    └──requires──> Visible error surfacing for failed reads/loads
                       (nothing to log/export until errors are actually captured instead of swallowed)
```

### Dependency Notes

- **Pre-write connection validation must land before write/save confirmation feedback:** if the app shows a "Saved" toast after a write that was never actually attempted (because the connection silently dropped), that's a worse UX regression than today's silence — it actively lies to the user. Sequence the guard first.
- **Command watchdog shares root cause with retry visibility:** both need the command layer to expose "this command is stalled" as a distinct, observable state (not just eventual timeout). Implement the watchdog's internal state as something the UI layer can also read/display for retry visibility, rather than building them as two unrelated changes.
- **Watchdog must land before adaptive per-connection-type timeout:** tuning USB=10s/BLE=15s only matters once there's a watchdog to enforce it meaningfully; today's flat 20s `await` timeout with no watchdog means "timeout value" and "detection of hang" are conflated — separate them, then tune.
- **Error classification must precede error surfacing:** you cannot show "board doesn't support this setting" vs. "read failed, retry?" without first threading through *why* `GetOptional()` returned null at each call site — this is foundational plumbing, not a UI-only change.
- **Read-back verification enhances write confirmation, doesn't replace it:** ship the cheap version (toast: "Saved"/"Save failed") first; read-back is a stronger guarantee but roughly doubles command traffic per save, so treat it as a later enhancement, gated on the timeout/watchdog rework being solid (otherwise verification reads inherit the same hang risk being fixed).
- **Diagnostic log export requires error surfacing to exist first:** there's nothing meaningful to export until failures are captured with context (which command, which setting, what error) rather than discarded in a bare `catch {}`.

## MVP Definition

Since this is a stabilization milestone (not a 0-to-1 product), "MVP" here means the minimum fix set that resolves the 3 reported bugs and restores trust, mapped against PROJECT.md's Active requirements.

### Launch With (This Milestone)

- [ ] Persistent, always-visible connection status indicator — cheap, high-leverage, prerequisite for trustworthy error messaging
- [ ] Stop silent exception swallowing in settings load; surface failures with a visible error/retry state — directly fixes Symptom 1
- [ ] Pre-write connection state validation before every JS interop write call — directly fixes Symptom 2
- [ ] Graceful catch + user-facing message for disconnect-during-write, instead of an uncaught JS exception — directly fixes Symptom 2
- [ ] Command watchdog for hung commands (restoring the `/old` app's 10s-check/20s-kill pattern) — directly fixes Symptom 3
- [ ] Write/save confirmation feedback (success or explicit failure, never silence) — directly fixes Symptom 3's "lost settings with no indication"
- [ ] Clear "not supported by firmware" vs. "read failed" distinction in settings display — closes the ambiguity that caused the original forum bug report
- [ ] Systematic parity audit comparing every `/old` feature against the new app (already an Active requirement) — catches any 4th/5th silent gap before it becomes another forum post

### Add After Validation (Only If Time Remains)

- [ ] Adaptive per-connection-type timeout (USB 10s / BLE 15s) — natural follow-on once watchdog exists
- [ ] Exponential backoff on retries — small, contained, complements watchdog work
- [ ] Retry visibility in UI ("Retrying 2/3...") — builds on watchdog state already being tracked
- [ ] Inter-chunk backpressure delay in chunked writes — root-cause hardening, low risk to bundle in

### Future Consideration (Explicitly Deferred, Not This Milestone)

- [ ] Read-back write verification — stronger guarantee, but sequence after timeout/watchdog rework is proven stable
- [ ] Downloadable diagnostic log/debug export — high value for a hobbyist audience's bug-report loop, but not required to close the 3 known bugs
- [ ] Connection health "ping" during idle/long operations — adds new device chatter; verify need after the core fixes ship
- [ ] Per-field inline retry affordances (vs. panel-level banner) — UI polish, defer to a future milestone
- [ ] Automated test coverage for USB/BLE flows — separate infrastructure project, blocked by CI's inability to run WebUSB/BLE
- [ ] Elaborate reconnect/session-restoration UX — net-new UX beyond `/old` parity, out of scope

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|----------------------|----------|
| Persistent connection status indicator | HIGH | LOW | P1 |
| Visible error surfacing for failed reads | HIGH | LOW–MEDIUM | P1 |
| Pre-write connection validation | HIGH | LOW | P1 |
| Graceful disconnect handling (no uncaught exceptions) | HIGH | LOW–MEDIUM | P1 |
| Command watchdog for hung commands | HIGH | MEDIUM | P1 |
| Write/save confirmation feedback | HIGH | LOW–MEDIUM | P1 |
| "Not supported" vs "failed" distinction | MEDIUM–HIGH | LOW | P1 |
| Parity audit vs. `/old` | HIGH | MEDIUM | P1 |
| Adaptive per-connection-type timeout | MEDIUM | LOW–MEDIUM | P2 |
| Exponential backoff on retry | MEDIUM | LOW | P2 |
| Retry visibility in UI | MEDIUM | LOW–MEDIUM | P2 |
| Inter-chunk backpressure delay | MEDIUM | LOW | P2 |
| Read-back write verification | MEDIUM–HIGH | MEDIUM–HIGH | P3 |
| Diagnostic log export | MEDIUM | LOW–MEDIUM | P3 |
| Connection health ping | LOW–MEDIUM | MEDIUM | P3 |
| Per-field inline retry affordances | LOW–MEDIUM | MEDIUM | P3 |

**Priority key:**
- P1: Must have — directly fixes one of the 3 reported bugs or is a prerequisite dependency for one that does
- P2: Should have — natural extension of P1 fixes, low incremental cost once P1 lands
- P3: Nice to have — defer to a future milestone unless trivially cheap once P1/P2 infrastructure exists

## Competitor Feature Analysis

| Feature | ESP Web Tools | Adafruit WebSerial ESPTool / micro:bit WebUSB | Google Web Bluetooth samples | Our Approach |
|---------|---------------|-----------------------------------------------|-------------------------------|--------------|
| Connection status visibility | Explicit dialog states (Dashboard → Connecting → Installing → Done/Error) | Connect button reflects paired/connected state | Battery-indicator-style live status element | Always-visible status indicator tied to actual `IsConnected` + disconnect events |
| Error surfacing | Dedicated error page/state in the install dialog with human-readable message | Console/alert-style error text (e.g., "failed to open serial port") | `.catch()` blocks recommended throughout; no prescribed UI, left to implementer | Panel-level error/retry banner per settings group, no bare swallowed exceptions |
| Diagnostic export | "Download Logs" button on error page | None found | None found | Deferred (P3) — noted as valuable but out of scope this milestone |
| Retry/backoff | Not prominently documented | Manual reload-the-page workaround (no automatic retry) | Documented exponential-backoff reconnect sample (3 retries, 2s delay pattern) | Adopt bounded retry + backoff at command layer (P2), surface count to user (P2) |
| Disconnect handling | Handles via internal state machine; "Back" button disconnects console explicitly | Relies on browser-level serial port errors; no proactive disconnect listener documented | `gattserverdisconnected` event explicitly recommended as the hook point | Register disconnect handlers proactively (don't wait for next failed write to reveal state) — P1 |
| Write verification | Verification is implicit in DFU/flash checksum process (firmware flashing, not settings) | N/A (flashing tool, not settings config) | N/A (sample code, not a full app) | Read-back verification noted as a differentiator (P3), not required this milestone |

## Sources

- ESP Web Tools official site and source (https://esphome.github.io/esp-web-tools/, https://github.com/esphome/esp-web-tools/blob/main/src/install-dialog.ts) — MEDIUM confidence (cross-checked via search + WebFetch of Chrome docs referencing same patterns)
- Chrome for Developers — Web Bluetooth capabilities guide (https://developer.chrome.com/docs/capabilities/bluetooth) — MEDIUM confidence (official vendor documentation)
- Google Chrome Web Bluetooth samples — Automatic Reconnect, Device Disconnect (https://googlechrome.github.io/samples/web-bluetooth/) — MEDIUM confidence (official vendor sample code)
- micro:bit WebUSB troubleshooting support article (https://support.microbit.org/support/solutions/articles/19000105428-webusb-troubleshooting) — MEDIUM confidence (official vendor support docs)
- Adafruit WebSerial ESPTool + community forum/GitHub issue reports (https://github.com/adafruit/Adafruit_WebSerial_ESPTool, adafruit forums) — LOW–MEDIUM confidence (mix of official repo and community reports)
- `/old/app.html` (project's own predecessor, lines ~440-490: `WatchDog()`/`RunWatchDog()`/`Send2()`) — HIGH confidence (primary source, directly inspected)
- `.planning/codebase/CONCERNS.md` (project's own codebase audit, 2026-07-01) — HIGH confidence (primary source, directly inspected)
- `.planning/PROJECT.md` — HIGH confidence (primary source, directly inspected)

---
*Feature research for: Browser-based WebUSB/Web Bluetooth hardware configuration tools — reliability/error-handling UX dimension*
*Researched: 2026-07-01*

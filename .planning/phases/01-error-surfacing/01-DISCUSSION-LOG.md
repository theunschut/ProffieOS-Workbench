# Phase 1: Error Surfacing - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-07-04
**Phase:** 1-Error Surfacing
**Areas discussed:** Retry behavior, Unsupported vs Failed, Cross-page visibility, Error detail level

---

## Retry behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Manual retry button | Panel shows an error banner listing failed settings with a 'Retry' button; user controls when to re-attempt. | ✓ |
| Automatic retry with backoff | Failed settings silently retry in the background a few times before surfacing as failed. | |
| Both — auto-retry once, then manual button | One automatic retry immediately, then manual button if still failing. | |

**User's choice:** Manual retry button
**Notes:** No follow-up questions requested; moved to next area.

---

## Unsupported vs Failed

| Option | Description | Selected |
|--------|-------------|----------|
| Hide unsupported, flag only real failures | Unsupported settings stay invisible (today's behavior, matches `/old`); only genuine read failures get an error indicator. | ✓ |
| Show unsupported as a grayed-out row | Every known setting always renders; unsupported ones show grayed out with a label. | |

**User's choice:** Hide unsupported, flag only real failures
**Notes:** No follow-up questions requested; moved to next area.

---

## Cross-page visibility

| Option | Description | Selected |
|--------|-------------|----------|
| Global snackbar/toast | A MudBlazor Snackbar fires app-wide the moment the background load fails. | ✓ |
| Persistent badge/indicator | A small persistent indicator shows an error count/badge until addressed. | |
| Queue and show on Settings visit only | Don't interrupt the user elsewhere; guarantee the banner appears on next Settings visit. | |

**User's choice:** Global snackbar/toast
**Notes:** Confirmed the panel-level banner (from Retry behavior area) still shows on next Settings visit in addition to the toast — not a replacement.

---

## Error detail level

| Option | Description | Selected |
|--------|-------------|----------|
| Specific reason | Distinguish timeout vs. disconnected vs. unexpected response; requires `Send()`/`Send2()` to stop conflating these into an empty-string return. | ✓ |
| Generic message | Just "Failed to load [setting]" for every failure type. | |

**User's choice:** Specific reason
**Notes:** Follow-up question asked whether the user was OK with Phase 1 including the `Send()`/`Send2()` breaking-change audit (flagged as a blocker in STATE.md) required to make specific reasons possible. User confirmed: **yes, include the audit** — this is the real fix underlying ERR-01/ERR-02, not just a UI layer on top of the same ambiguous signal.

User also added a general standing principle at the end of discussion (free-text, not tied to a specific option): the rewrite must be **functionally identical to `/old`** — not a literal code copy, but matching functional behavior/capability. Captured in CONTEXT.md `<domain>` as a guiding principle for this phase and beyond.

---

## Claude's Discretion

- Exact mechanism for carrying failure reason through `Send()`/`Send2()` (typed result vs. exception hierarchy vs. out-parameter).
- Exact wording/copy of error messages and retry button.
- Whether logging is added at the `Send()` layer, `SaberStateService` layer, or both.

## Deferred Ideas

None raised during this discussion.

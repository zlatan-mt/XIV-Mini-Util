# Cold-Start Character Select Visibility Task

## Goal
Investigate and fix the rare first Character Select / first-login path where the FRU background loads but the selected character is missing or rendered abnormally.

Do not require deliberate reproduction. First harden the passive recorder so the next naturally occurring failure is captured automatically; then implement only the root-cause repair justified by evidence.

Canonical repository: `zlatan-mt/XIV-Mini-Util`.

## Current baseline — 2026-09-04
- latest `main`: `5cb960d3c86b75025276301f389b86d5d84c4863`
- public release: `v0.4.3`
- platform: Dalamud API 15 / .NET 10
- task branch: `task/cold-start-character-select-visibility`
- Draft PR: `#8`
- original Phase A reviewed HEAD: `70a46a6655611a94c2830b4ae34b21bee4a76f86`
- target candidate: `custom:fru-clear-stage`

The branch originated at `c69579db02775cce4916eb2558df703b4f61ffab` and is behind current `main`. Before production-code edits, synchronize this same branch with latest `main` and preserve v0.4.3 / pre-release diagnostic-cleanup changes. Do not recreate removed diagnostics merely because the old PR branch contains earlier references.

Only PR #8 is active for this task. Do not create a duplicate or replacement PR.

## New real-game evidence — 2026-09-04
A rare failure was reproduced once:
- FRU background/scene override was visibly active;
- selected character was not visible;
- ordinary Dalamud log showed successful Character Select placement evidence, including repeated stable capture (`stableSamples=5`);
- the loaded dev plugin identified itself only as `XivMiniUtil.Dev v0.4.2.0`;
- the available log contains no `Cold-start diagnostic armed` / recorder-init evidence and no dedicated PR #8 report.

Important evidence limit: the log does **not** prove that the exact PR #8 diagnostic binary was deployed. Therefore the missing report must not yet be classified as a recorder arm bug; it can also be build/deployment mismatch. Phase A2 must make recorder presence and arm/skip state unambiguous.

This evidence does not authorize an H1 owner migration fix, draw/camera fix, or other Phase B repair.

## Phase A2 — rare-event passive recorder hardening
### Objective
Capture the first eligible pre-login FRU Character Select lifecycle during normal use without settings changes, button presses, preset re-selection, probe operations, or an intentional reproduction attempt.

### Required behavior
1. Emit one privacy-safe recorder-presence marker at plugin/service startup regardless of arm eligibility (for example `coldStart.recorderSchema=2`). This must not mutate runtime/config state.
2. Preserve the startup owner/config snapshot even if the diagnostic does not arm immediately.
3. Record compact startup arm result / skip or block reason.
4. If startup arm was missed, permit a passive first-scene fallback arm when the first eligible pre-login FRU Character Select is reached, but only if OneClick/probe/config-mutation state would not make evidence ambiguous.
5. Observation is bounded/run-scoped and stops on login, Character Select session end, dispose, or timeout.
6. Reuse existing service/update/diagnostic paths. No new native hook or generic logging framework.
7. Save a compact terminal report to a deterministic bounded/rolling diagnostic file so normal gameplay does not depend on reproducing the bug again. Avoid unbounded files or per-frame logging.
8. Existing automatic clipboard handoff may be reused for anomalous/explicit diagnostic completion, but do not require repeated user actions or manual diagnostic-key lookup.

### Evidence
Retain existing startup/scene/static-anchor/placement/V2 evidence and add only:
- recorder schema/presence;
- arm mode (`startup` / `first-scene-fallback`) and startup skip/block reason;
- first scene generation/owner;
- resolver attempt/success counters and resolve source;
- `DrawReady` at first valid resolve, ever-true flag, transition count;
- pointer-free actor identity epoch/recreation count (report counts only; never emit ContentId/address/raw identity fields);
- placement capture completion;
- placement write attempts + position/rotation readback confirmation;
- whether confirmed placement occurred before/after identity epoch change;
- login/session-end cleanup state.

Prefer transition/counter evidence to per-frame logging. Any history/ring storage must be small and bounded.

### Classification
Allowed technical-stage labels include `owner-migration-gap`, `scene-generation`, `actor-resolver`, `draw-readiness`, `static-anchor-authorization`, `capture`, `placement-write`, `actor-recreation`, `post-placement-visual-candidate`, `insufficient-evidence`.

Do not overclaim visual root cause from diagnostic state alone. The user-visible missing-character observation is external evidence for that run.

## Phase B — evidence only
Do not implement Phase B as part of recorder hardening unless evidence establishes the failing stage.

Decision order:
- H1 owner migration mismatch -> narrow saved-config reconciliation;
- H2 scene generation/attach -> lifecycle repair;
- H3 resolver never valid -> resolver readiness/recreation repair;
- H4 static anchor failure -> FRU scene/layout authorization repair;
- H5 capture unstable -> transient capture repair;
- H6 write/readback failure -> placement write repair;
- H7 write/readback succeeds then identity changes without reapply -> actor-recreation trigger repair;
- H8 resolver/placement/write passes while external visual failure occurs -> draw/model/camera lifecycle investigation before any write change.

## Safety / invariants
Do not weaken pre-login/Character Select gates, scene-generation checks, approved FRU static-anchor authorization, non-persistent probe rules, `PersistentApplyEnabled`, login-stop/dispose cleanup, prohibition on post-login world ObjectTable as Character Select source, or coordinate-semantics restrictions.

Do not log/persist character names, ContentId, raw pointers/addresses, credentials, local paths, or account-identifying data. No new end-user settings, developer UI, release/version changes, native hooks, or unrelated refactors.

## Acceptance criteria — Phase A2
1. Sync PR #8 branch with latest `main` before production edits without undoing v0.4.3 cleanup.
2. Logs/report can distinguish `recorder not present` from `present but not armed` and include a concrete arm/skip reason.
3. Recorder cannot silently miss the first eligible pre-login FRU Character Select merely because startup arm did not occur.
4. Startup snapshot remains available for H1 classification.
5. Report includes draw-ready, actor-epoch, placement/write/readback evidence.
6. Recorder mutates no config/candidate/camera/placement/scene/probe state.
7. No raw identity/private output.
8. Bounded completion on login/session-end/dispose/timeout and bounded file/history retention.
9. No Phase B production behavior change without evidence.

## Validation
```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only focused tests for recorder-presence/arm reason, startup snapshot retention, fallback eligibility/blocking, draw-ready/actor-epoch counters, bounded cleanup/retention, and privacy-safe report output.

Real-game validation for Phase A2 is **not** “reproduce the bug again”. Deploy the exact validated HEAD and return to normal use; the next naturally occurring failure should leave a usable report automatically.

## Planning self-review — 3 passes
### Review 1 — scope/current state
Found stale assumptions from the original task: v0.4.2 and blocked PR #7 are no longer current. Revised the plan to use v0.4.3 `main`, reuse PR #8 only, and require latest-main synchronization before production edits.

### Review 2 — evidence quality
Found an overclaim: absence of the cold-start report does not prove the recorder failed to arm because the Dalamud log only identifies `XivMiniUtil.Dev v0.4.2.0`, not exact PR #8 HEAD. Revised Phase A2 to emit an unconditional recorder-presence/schema marker and explicit arm/skip reasons before diagnosing an arm bug.

### Review 3 — safety/operability
Rejected deliberate reproduction, unbounded logging, new hooks, broad visual fixes, and per-login heavy diagnostics. Revised the recorder to remain passive/bounded, use transition counters and a bounded terminal report, preserve all existing safety gates, and defer Phase B until evidence identifies the failing stage.

## Review / merge
Same task spec + branch + Draft PR #8 throughout. No replacement PR. Final implementation review is ChatGPT/manual exact-HEAD review; Sol Review is not used. Do not merge Phase B until root cause and real-game behavior are established.

## External / paid API
None required. Do not call Universalis or Discord for this task.

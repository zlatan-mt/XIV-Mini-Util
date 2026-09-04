# Cold-Start Character Select Visibility Task

## Goal

Investigate and fix the rare first Character Select / first-login path where the FRU background loads but the selected character is missing or rendered abnormally.

This task must not require the user to deliberately reproduce the rare failure again. First make the passive recorder robust enough that the next naturally occurring failure is captured automatically, then implement only the root-cause repair justified by that evidence.

Canonical repository: `zlatan-mt/XIV-Mini-Util`.

## Current baseline — 2026-09-04

- latest `main`: `5cb960d3c86b75025276301f389b86d5d84c4863`
- public release: `v0.4.3`
- platform: Dalamud API 15 / .NET 10
- task branch: `task/cold-start-character-select-visibility`
- existing Draft PR: `#8`
- original Phase A reviewed HEAD: `70a46a6655611a94c2830b4ae34b21bee4a76f86`
- target candidate: `custom:fru-clear-stage`

The task branch was created from `c69579db02775cce4916eb2558df703b4f61ffab` and is currently behind `main`. Before production-code edits, bring this same branch forward to current `main` and preserve the v0.4.3 / pre-release diagnostic-cleanup changes. Do not recreate diagnostics removed by the cleanup task merely because the old PR branch still contains older references.

Only PR #8 is active for this task. Do not create a duplicate or replacement PR.

## New real-game evidence — 2026-09-04

A rare failure was reproduced once:

- the FRU background/scene override was visibly active;
- the selected character was not visible;
- the ordinary Dalamud log contained successful Character Select placement evidence, including repeated stable capture (`stableSamples=5`);
- the dedicated PR #8 cold-start report was not produced, and the expected `Cold-start diagnostic armed` evidence was absent from the available log.

This is useful partial evidence, but it is not sufficient to choose a Phase B native/runtime fix. In particular, do not infer an H1 owner migration fix or a draw/camera fix solely from the screenshot/log.

The missing report is itself a Phase A defect: the current recorder can miss the rare event that it exists to diagnose.

## Phase A2 — rare-event passive recorder hardening

### Objective

Make the recorder capture the first eligible pre-login FRU Character Select lifecycle without requiring settings changes, a button press, a preset re-selection, a probe operation, or an intentional reproduction attempt.

### Required behavior

1. Preserve the startup owner/config snapshot needed for H1 classification even if the diagnostic does not arm immediately.
2. Record why the startup arm did or did not happen using a compact pointer-free reason.
3. If startup arming was missed but the first eligible pre-login Character Select scene is reached with the FRU candidate active, allow a **passive first-scene fallback arm** only when doing so does not mix in OneClick/probe/config mutation state.
4. Keep observation bounded and run-scoped. Stop on login, Character Select session end, dispose, or timeout.
5. Reuse existing service/update/diagnostic paths. Do not add a native hook or general-purpose logging framework.
6. Auto-save and queue one compact report for clipboard. The user must not search diagnostic keys or perform another capture workflow.

### Evidence to retain

Keep existing startup/scene/static-anchor/placement/V2 evidence and add only what is required to distinguish resolver success from visual/draw lifecycle failure:

- arm mode: startup / first-scene-fallback / not-armed;
- startup arm skip/block reason;
- first Character Select scene generation and owner;
- resolver attempt/success counters and resolve source;
- `DrawReady` at first valid resolve, whether it ever becomes true, and transition count;
- pointer-free actor identity epoch/recreation count (report counts only; never emit ContentId, address or raw identity fields);
- placement capture completion;
- placement write attempts and position/rotation readback confirmation;
- whether a confirmed placement write occurred before/after an actor identity epoch change;
- login/session-end cleanup state.

Prefer transition/counter evidence over per-frame text logging. Any ring/history storage must be small and bounded.

### Classification

Do not overclaim visual failure from diagnostic state alone. The report may classify technical stages such as:

- `owner-migration-gap`
- `scene-generation`
- `actor-resolver`
- `draw-readiness`
- `static-anchor-authorization`
- `capture`
- `placement-write`
- `actor-recreation`
- `post-placement-visual-candidate`
- `insufficient-evidence`

The external fact that the character was visually missing comes from the user's observation for that run; automatic `post-placement-visual-candidate` is not standalone proof of rendering root cause.

## Phase B — root-cause repair, evidence only

Do not implement Phase B as part of the recorder-hardening change unless newly available evidence is sufficient to establish the failing stage.

Decision order remains:

- H1: expected owner=placement, actual startup owner=v2 -> narrow saved-config owner reconciliation;
- H2: scene generation / attach failure -> lifecycle repair;
- H3: actor resolver never becomes valid -> resolver readiness/recreation repair;
- H4: static anchor authorization failure -> FRU scene/layout authorization repair;
- H5: capture never stabilizes -> transient capture repair;
- H6: native write/readback fails -> placement write repair;
- H7: write/readback succeeds, actor identity changes, placement not reapplied -> actor-recreation trigger repair;
- H8: resolver/placement/write evidence passes while visual failure is externally observed -> draw/model/camera lifecycle investigation before any write change.

No hypothesis is authorized merely because it is plausible.

## Safety / invariants

Do not weaken:

- pre-login / Character Select gates;
- run-scoped scene-generation checks;
- approved FRU static-anchor authorization;
- non-persistent probe rules;
- `PersistentApplyEnabled` contract;
- login-stop / dispose cleanup;
- prohibition on using post-login world ObjectTable as Character Select source;
- prohibition on reusing world coordinates as camera/placement values without verified semantics.

Do not log or persist character names, ContentId, raw pointers/addresses, credentials, local paths, or account-identifying data.

Do not add new end-user settings, developer UI, release/version changes, new native hooks, or unrelated refactors.

## Acceptance criteria — Phase A2

1. Existing PR #8 branch is synchronized with current `main` before production edits, without undoing v0.4.3/diagnostic-cleanup changes.
2. Passive recorder cannot silently miss the first eligible pre-login FRU Character Select merely because the initial startup arm path did not run.
3. Startup snapshot remains available for owner-migration classification.
4. Report explains arm mode/skip reason and includes draw-ready + pointer-free actor-recreation + placement/write/readback evidence.
5. Recorder performs no config/candidate/camera/placement/scene/probe mutation.
6. No raw identity/pointer/private data is emitted.
7. Capture is bounded and stops on login/session-end/dispose/timeout.
8. Final report is automatically saved/copied without additional user action.
9. No Phase B production behavior is changed without evidence.

## Validation

Minimum automated validation after implementation:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only focused tests for:

- startup snapshot retention;
- startup-arm skip reason;
- first-scene passive fallback eligibility / blocking when OneClick or mutation state would make evidence ambiguous;
- draw-ready transition / actor-epoch counter logic;
- bounded completion and login/session-end/dispose cleanup;
- privacy-safe report output.

Do not add a broad test matrix.

Real-game validation for Phase A2 is **not** "reproduce the bug again". Build/deploy the exact validated HEAD and return the plugin to normal use. The next naturally occurring failure should produce the report automatically. Automated validation must not be presented as real-game validation.

## Planning self-review

This plan was reviewed three times before handoff:

1. **Scope review** — confirmed existing PR #8 must be reused; no duplicate PR, Phase B guess, release work, UI work, or unrelated refactor was added.
2. **Safety / evidence review** — confirmed passive-only observation, bounded lifecycle, no pointer/ContentId/private output, no new native hook, and no weakening of pre-login/static-anchor/login-stop contracts.
3. **Integration / current-state review** — corrected the stale v0.4.2 / blocked-PR-#7 assumptions, made latest `main` synchronization mandatory, and explicitly prohibited reintroducing diagnostics removed by the v0.4.3 pre-release cleanup.

## Review / merge

- Same task spec + branch + Draft PR #8 through implementation, review and eventual repair.
- No replacement PR.
- Final implementation review: ChatGPT/manual exact-HEAD review; Sol Review is not used for this task.
- Do not merge Phase B until its root cause and real-game behavior are established.

## External / paid API

No paid/live external API is required. Do not call Universalis or Discord for this task.

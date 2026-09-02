# Cold-Start Character Select Visibility Task

## Goal

Investigate and fix the first Character Select / first login path where the FRU background can load while the selected character is missing or rendered abnormally.

The task must first capture the untouched cold-start production path passively, then apply only the root-cause fix justified by that evidence.

Canonical repository: `zlatan-mt/XIV-Mini-Util`.

## Baseline

- task baseline `main`: `c69579db02775cce4916eb2558df703b4f61ffab`
- current public release: `v0.4.2`
- blocked release task: PR #7 (`task/release-v0.4.3`), keep blocked/draft
- current platform: Dalamud API 15 / .NET 10
- target candidate: `custom:fru-clear-stage`

## Observed symptom

On the user's own account, the first Character Select / first login path can show the FRU scene with the selected character missing or in an abnormal visual state. A later attempt can appear normal.

PR #4 fixed the fresh/unpromoted normal preset-selection path, but review explicitly identified a migration gap: an existing saved FRU config with old owner flags such as `TitleBackgroundCharaSelectPlacementEnabled=false` / `TitleBackgroundV2Enabled=true` is not reconciled merely by plugin startup because startup `ApplyFromConfiguration()` preserves saved owner flags while `ApplySimpleAutoSetup()` performs the corrected FRU owner selection.

Treat that as hypothesis H1, not a proven root cause until cold-start evidence confirms it.

## Scope

### Phase A — passive cold-start diagnostic

Add a passive, run-scoped cold-start capture that observes the real startup state without calling OneClick setup, `ApplySimpleAutoSetup()`, preset re-selection, probe mutation, configuration reset, or any other action that can normalize the reproduction condition.

Capture only pointer-free / privacy-safe evidence needed to classify the failure:

1. startup before/after configuration application
   - selected candidate
   - override enabled
   - V2 enabled
   - placement enabled
   - placement candidate
   - PositionCaptured
   - actual engine owner
   - expected owner derived read-only from current candidate/setup logic
2. first CharaSelect scene
   - scene path / territory / layer
   - scene generation
   - actual owner
   - V2 active / placement active / legacy ownership state
3. selected-character resolver readiness
   - resolve source
   - mapping available / hit
   - client object index valid
   - object resolved
   - draw ready
   - bounded retry count
4. placement path, when active
   - gate reason
   - static-anchor authorization
   - capture status
   - placement write attempt
   - position / rotation readback confirmation
5. V2 framing attempt/applied state
6. login transition and cleanup
   - no post-login writes/leaks

Do not log character names, ContentId, raw addresses/pointers, credentials, or local paths.

### Phase B — root-cause fix

After one cold-start report establishes the failing stage, implement only the corresponding minimal fix in this same PR.

Expected decision order:

- H1: expected owner=placement, startup actual owner=v2 -> narrow saved-config owner migration
- H2: placement owner but scene generation/attach mismatch -> scene lifecycle fix
- H3: scene valid but actor resolver never becomes valid -> resolver readiness/recreation fix
- H4: resolver valid but static anchor authorization fails -> FRU scene/layout authorization fix
- H5: anchor valid but capture never stabilizes -> transient capture fix
- H6: write attempted but readback fails -> placement write path fix
- H7: write/readback succeeds then actor is recreated and not reapplied -> actor-recreation trigger fix
- H8: placement evidence all passes but character is still visually missing -> draw/camera lifecycle investigation

Do not implement H1 migration until Phase A evidence supports it.

## Phase A implementation approach

Reuse the existing Title Background diagnostic infrastructure instead of adding a new logging framework:

- existing `TitleBackgroundTransitionDiagnosticRecorder`
- existing placement proof/diagnostic snapshots
- existing clipboard handoff pattern

Add at most one small dedicated state/diagnostic component such as `TitleBackgroundColdStartDiagnostic.cs` plus narrow integration points in the existing partial service/plugin draw handoff.

The cold-start diagnostic must be passive: it must not change config, owner, camera, placement, scene, candidate, hooks, or probe state.

Capture should be bounded and self-terminating. It begins from plugin service startup when the saved active candidate is FRU and Title Background override is enabled, observes the first pre-login CharaSelect lifecycle, freezes the safe pre-login snapshots, and completes on login (or bounded failure/timeout). The report is written to a dedicated diagnostic file and queued for automatic clipboard copy using the existing UI-draw clipboard mechanism.

The report should include a compact classification line, for example:

```text
coldStart.diagnosis=owner-migration-gap
```

Possible classifications: `owner-migration-gap`, `scene-generation`, `actor-resolver`, `static-anchor-authorization`, `capture`, `placement-write`, `actor-recreation`, `post-placement-visual`, `insufficient-evidence`.

## Expected H1 fix if confirmed

Do not call `ApplySimpleAutoSetup()` wholesale from startup.

Add only a narrow migration/reconciliation in the existing normalization/startup path for the old FRU owner state when all safety conditions match:

- override enabled
- active curated candidate resolves exactly to FRU
- V2 enabled
- placement disabled
- no conflicting placement candidate/data
- `PositionCaptured == false` or otherwise no valid conflicting persistent placement

Then only:

- `TitleBackgroundV2Enabled = false`
- `TitleBackgroundCharaSelectPlacementEnabled = true`
- matching placement candidate = FRU

Do not fabricate `PositionCaptured`, captured XYZ/rotation, OneClick proof, saved view, facing calibration, or any unrelated settings.

Runtime FRU target position must continue to come only from current-session approved-static-anchor authorization for n4gw / territory 1238 / explicit layer 0. Do not persist `(100,0,100)` as captured placement.

Il Mheg, Elpis, manual/unknown candidates, wrong scene/territory/layer, override OFF, and conflicting placement state must not be migrated.

## Out of Scope

- v0.4.3 release/version/pluginmaster changes
- PR #7 implementation/merge
- FRU time/VFX/lighting retuning
- camera redesign
- new native hooks
- generic migration framework
- new end-user settings or developer UI
- weakening safety gates, run-scoped checks, pre-login snapshots, `PersistentApplyEnabled`, or login-stop behavior

## Acceptance Criteria

### Phase A

1. Cold-start diagnostic observes the saved production state without mutating it.
2. OneClick / `ApplySimpleAutoSetup()` is not invoked by the diagnostic path.
3. Report includes before/after startup owner state, expected owner, first scene, resolver readiness, placement/write/readback evidence when applicable, V2 evidence, and login-stop/cleanup evidence.
4. Report contains no character name, ContentId, raw pointer/address, credential, or local path.
5. Capture is bounded and stops on login/session end/dispose.
6. Report is automatically copied without manual diagnostic-key lookup or multiple buttons.

### Phase B

1. Fix is selected from Phase A evidence, not guessed in advance.
2. For H1, only stale FRU owner state is reconciled; `PositionCaptured` and captured XYZ/proof remain untouched.
3. Il Mheg / Elpis / wrong candidate / override OFF / conflicting placement remain unchanged.
4. FRU placement continues to require current pre-login scene/layout/static-anchor authorization.
5. No post-login write/leak is introduced.
6. Cold-start real-game run shows FRU background, selected character, camera and login normal.

## Validation

Minimum automated validation:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only targeted tests required to prove:

- passive diagnostic does not normalize/mutate saved owner state
- classification decision logic
- bounded completion/login stop
- H1 migration behavior only if H1 is confirmed and implemented

Real-game validation:

- Phase A: one untouched cold-start reproduction run; ordinary Character Select/login flow only, report auto-copied
- Phase B: one cold-start verification run after the fix

Do not ask the user to reset settings, re-select FRU, operate probes, search diagnostic keys, or repeat multiple manual capture steps.

Automated validation must not be presented as real-game validation.

## Review / merge

- Final implementation review: ChatGPT / manual review on the exact final HEAD. Sol Review is not used for this task.
- Same PR is used for diagnostic, evidence-driven repair, validation and review.
- Do not create a replacement PR due to review/repair iterations.
- Do not merge until the root cause is confirmed, the corresponding fix (if required) has a real-game cold-start PASS, and final reviewed HEAD matches the merge HEAD.

## External / paid API

- No paid/live external API is required.
- Do not call Universalis or Discord for this task.

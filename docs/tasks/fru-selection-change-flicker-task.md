# FRU selection-change flicker task

## Goal

Remove the transient FRU clear-stage geometry flicker observed when switching the selected character or changing the character/world selection context on the Character Select screen.

Observed real-game symptom:
- FRU clear-stage candidate is active.
- Character placement itself is normal.
- Immediately after a character/world selection change, large non-clear-stage geometry can briefly appear behind the selected character.
- The unwanted geometry then fades/disappears and the intended flower-field / clear-sky presentation remains.
- The separate cold-start missing-character symptom tracked by PR #8 has not reproduced in these runs.

Do not commit the user screenshot or character/account-identifying information. The screenshot is observation evidence only.

## Baseline

- canonical repository: `zlatan-mt/XIV-Mini-Util`
- base branch: `main`
- baseline main: `c69579db02775cce4916eb2558df703b4f61ffab`
- task branch: `task/fru-selection-change-flicker`
- PR #7 remains blocked release work and is out of scope.
- PR #8 remains the independent cold-start selected-character visibility investigation and must not be modified or replaced by this task.

## Current production behavior

FRU scene-object suppression already exists on `main`:
- `TitleScreenBackgroundService.SceneObjectSuppression.cs`
- `TitleBackgroundCharaSelectSceneObjectSuppression.cs`
- `TitleScreenBackgroundService.CharaSelectPlacement.cs`

Current behavior intentionally assumes that character selection changes may cause n4gw `SharedGroup` instances to become active again:
- `SelectedCharacterChanged` sets `_fruSuppressionSelectionChangePending` atomically.
- the next eligible Framework-thread suppression pass consumes that pending flag and force-rearms the bounded suppression window even within the same scene generation.
- suppression is allowed only after the existing pre-login / CharaSelect / hook-ready / static-anchor / loaded-layout identity gates pass.
- suppression uses bounded `ILayoutInstance.SetActive(false)` writes and per-instance retry budgets.

The current deny table primarily targets fight-specific gimmick / telegraph groups (`_gmc`, `_ice0`, `_mag0`, `_dst0`, `sgvf_w_lvd_b`, `sgvf_n4gw_b35`) while the flower field / floor / trees / distant scenery / lighting remain explicitly kept.

## Working hypotheses

Do not choose a fix by assumption. Distinguish these with one representative selection-change run:

1. `timing-gap`
   - a known suppressible SharedGroup is reactivated by the game during character/world switching and remains visible until the next safe suppression pass.

2. `coverage-gap`
   - the visible large structural geometry is a SharedGroup/path not covered by the current deny set, while it later becomes inactive indirectly as the scene settles.

3. `deactivation-semantics`
   - the object is matched and `SetActive(false)` is attempted/confirmed, but FFXIV renders a visible fade/teardown interval before it disappears.

4. `insufficient-evidence`
   - the visible object cannot be associated safely with the current SharedGroup suppression path.

## Scope

### Phase A — minimal diagnostic first

Reuse the existing suppression runtime and diagnostics. Add only the smallest missing evidence required to classify a single selection-change occurrence.

Capture, without persistent native pointers:
- selection-change observed / pending set
- scene generation at event and at first eligible suppression pass
- number of Framework frames or monotonic elapsed time from event to first eligible suppression pass
- gate status before the first pass
- active matched SharedGroup count before writes
- per-pass matched / already-inactive / write / confirmed-inactive / still-active counts
- safe primary-path identifiers for active candidates needed to distinguish deny coverage; game asset paths are allowed, but do not record character/account data
- pass where the suppression window first becomes stable

Prefer extending the existing Title Background diagnostic/report path rather than adding a new general diagnostics framework. The user workflow must remain one simple reproduction: switch character/world once, then copy/paste one automatically produced or existing compact report.

No new native hook is required unless current evidence proves the existing event cannot observe the necessary boundary. Do not add a hook speculatively.

### Phase B — evidence-driven repair in the same PR

Apply only the fix corresponding to Phase A evidence:

- `timing-gap`: move/reorder the existing suppression pass to the earliest already-safe Framework-thread point that preserves all current gates. Do not bypass scene identity or static-anchor authorization merely to suppress earlier.
- `coverage-gap`: add only the exact verified FRU-specific SharedGroup/path token(s) required to hide the unwanted structure. Keep existing allow/keep precedence; do not broadly hide structural geometry.
- `deactivation-semantics`: do not add unverified alpha/native writes. Use only semantics already verified safe (`SetActive(false)`) unless a separately verified safe mechanism is established within this task.
- `insufficient-evidence`: stop and report what additional passive evidence is required; do not guess.

## Out of scope

- cold-start missing/abnormal selected-character bug from PR #8
- placement coordinates, camera framing, emotes, actor resolver redesign
- Title Background V2 owner migration
- release/version/pluginmaster/changelog changes
- generic scene-object suppression framework
- generic VFX replay/alpha control
- post-login world ObjectTable as a CharaSelect source
- any weakening of pre-login, run-scoped, scene-generation, static-anchor, or login-stop gates

## Acceptance criteria

1. Diagnostic evidence from one representative selection/world change classifies the transient geometry as `timing-gap`, `coverage-gap`, `deactivation-semantics`, or `insufficient-evidence`.
2. Any fix remains FRU-specific and preserves the existing safety gates and bounded-write behavior.
3. No character names, ContentId, raw pointers/addresses, credentials, local paths, or screenshot/account-identifying data are committed or emitted in the report.
4. Existing flower field / floor / distant scenery / trees / lighting remain visible.
5. No new post-login writes are introduced.
6. PR #7 and PR #8 are untouched.
7. After the evidence-driven repair, one real-game representative character/world selection change shows no transient unwanted large geometry, or the task explicitly stops as `deactivation-semantics` / `insufficient-evidence` if the safe current API cannot remove the fade.

## Validation

Minimum automated validation after code changes:

```text
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only focused logic tests needed for changed suppression classification/token/timing behavior. Do not add a broad test matrix.

Real-game validation:
- use the Debug dev plugin built from the exact reviewed HEAD
- FRU candidate active
- perform one representative character or world-selection change that previously shows the transient structure
- collect the compact diagnostic if Phase A is still active
- after Phase B, repeat one representative selection change and confirm the unwanted transient geometry is no longer visible

Automated validation must not be presented as real-game validation.

## Risks / safety

Primary risks:
- hiding a clear-stage object that should remain visible
- writing against a stale/replaced layout instance during selection transitions
- expanding suppression too broadly
- attempting suppression after login/session end

Mitigations:
- preserve current FRU candidate + pre-login + CharaSelect + hook-ready + scene-generation + static-anchor + loaded-layout identity gates
- reacquire layout/instance data within each bounded pass; do not persist native pointers
- keep per-instance retry budget and bounded window
- fail closed when current layout identity cannot be verified

## Review / merge

- one task = this task spec = `task/fru-selection-change-flicker` = one Draft PR
- same PR is used for diagnostic, evidence-driven repair, validation and review
- no replacement PR due to review/repair iterations
- final review: ChatGPT / manual exact-HEAD review; Sol Review is not used while it is under repair
- do not merge until the final reviewed HEAD matches the validated HEAD

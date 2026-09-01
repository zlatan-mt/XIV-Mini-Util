# FRU Clear-Stage Visual Polish Task

## Goal

Character Select の `FRU クリア後ステージ` で欠けている可能性が高い **FRU本来の ambient VFX（花びら等）** を、既存の安全契約・配置・カメラ・戦闘ギミック抑止を維持したまま source-backed に特定し、安全に可能なら最小範囲だけ復元する。

この task では **明るさ・時刻調整は行わない**。VFX復元後の実機結果でまだ暗い場合のみ、別 task として lighting/time tuning を検討する。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は一切参照しない。

## Current baseline

Current public `main`:
`9327b171d0bcea0878c9a22d69a0e48828da299c`

Current task container:
- branch: `task/fru-clear-stage-visual-polish`
- PR: `#3`
- task branch planning baseline before sync: `448e6340084cc920b08846b5bd50c0e9bf4a4fad`

PR #4 fresh-install FRU character visibility hotfix is complete and released in v0.4.2. It was real-game validated before merge: normal FRU background selection showed the character correctly, login completed, and no post-login placement/background leakage was observed.

FRU candidate stable contract:
- id: `custom:fru-clear-stage`
- scene: `ex3/01_nvt_n4/goe/n4gw/level/n4gw`
- territory: `1238`
- layer: `0` explicit
- approved static anchor: `(100, 0, 100)`
- weather: Clear Skies / row 1
- time: 15:17 ET (`DayTimeSeconds=55020`)
- existing FRU placement / suppression real-game verification: PASS

Current 15:17 is the MiniUtil stable value adopted after noon produced white-out in real-game validation. Do not change it in this task.

## Checkpoint 1 — COMPLETE

A FRU-only read-only VFX inventory is already integrated into the existing OneClick flow.

Real-game OneClick evidence:
- pre-login weather requested/readback: `1 / 1`
- pre-login time requested/readback: `55020 / 55020`
- `verify.environment=PASS`
- VFX total: `248`
- active: `1`
- inactive: `247`
- primary path: `248`
- read failures: `0`
- only active representative: `bg/ex3/01_nvt_n4/common/vfx/eff/n4gw02lit1_y1.avfx`

This makes a failed time/weather override an unlikely primary explanation for the flat/dark appearance. Missing ambient/layout VFX state is the current first hypothesis.

Important: `248 total / 247 inactive` does **not** authorize enabling 247 VFX. n4gw contains encounter/mechanic VFX as well as ambient effects.

## Current external evidence

### Current TitleEdit reference

Reference repository:
`RokasKil/TitleEdit`

Reference commit:
`7bf92cfefbcd608ee1eba3b76d472e7c0fbb1d49`

Its built-in `TE_Futures Rewritten` preset uses the same FRU/n4gw scene concept and has:
- `SaveLayout=true`
- `UseVfx=true`
- large `Active` / `Inactive` UUID sets

TitleEdit stores layout identity as:
`UUID = ILayoutInstance.Id.InstanceKey + ((ulong)SubId << 32)`

FFXIVClientStructs documents the nested `LayoutManager.InstancesByType` key as:
`InstanceId << 32 | SubId`

Therefore MiniUtil can derive the TitleEdit UUID from its current typed runtime inventory without any new native read:
`titleEditUuid = instanceId + ((ulong)subId << 32)`

Use this only for read-only correlation. Do not import/replay TitleEdit's huge Active/Inactive lists.

TitleEdit VFX replay is not a safe write contract for MiniUtil. Its VFX path uses:
- custom vtable slot 54 (`SetActiveVf54`)
- a custom `VfxTriggerIndex` field / replay path

These are evidence about TitleEdit behavior, not authorization to copy those native calls.

### FFXIVClientStructs update

Current upstream FFXIVClientStructs exposes typed `VfxLayoutInstance.PathCrc`, Transform, GraphicsObject and other VFX fields.

However MiniUtil's production source of truth is the actual API15 dependency resolved by `Dalamud.NET.Sdk/15.0.0`.

Implementation must first inspect/compile against the **locally resolved API15 surface**:
- if typed `VfxLayoutInstance.PathCrc` is available there, it may be read after `InstanceType.Vfx` has been established;
- if not available, keep the existing managed primary-path hash fallback;
- do not introduce raw offsets merely because current upstream exposes a field.

`VfxTriggerIndex` and TitleEdit's vfunc54 remain unverified/untyped for this task and must not be written in Checkpoint 2.

## Checkpoint 2 — Sync + source-backed read-only classification

Checkpoint 2 is the next implementation checkpoint. **No VFX write is allowed in this checkpoint.**

### 2A. Sync PR #3 with v0.4.2 main

Reuse PR #3 and its branch. Do not create a replacement PR.

1. Verify repository identity and current GitHub state.
2. Merge latest `origin/main` into `task/fru-clear-stage-visual-polish` using a normal merge. Avoid history-rewriting rebase/force-push unless a concrete need is found.
3. Preserve PR #4/v0.4.2 behavior unchanged.
4. Resolve the known test number collision mechanically:
   - current main uses `Test(576)` through `Test(580)` for the fresh-install FRU placement hotfix;
   - PR #3's VFX tests currently use `576/577`;
   - renumber the PR #3 VFX tests to the next free IDs (expected `581/582`) without changing test semantics.
5. Do not mix release/version changes into PR #3.

After sync run the minimal baseline validation:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Stop if the v0.4.2 hotfix behavior is lost or safety gates are weakened.

### 2B. Refine the existing read-only inventory only as needed

The existing OneClick path remains the only user-facing diagnostic flow.

Keep existing gates:
- candidate == FRU
- pre-login
- CharaSelect session active
- hook Ready
- current lobby map == CharaSelect
- same scene generation
- FRU static-anchor authorization success for the same generation
- ActiveLayout `InitState == 7`
- territory `1238`
- explicit layer `0`
- native pointers valid only within the current pass
- write count = 0

For each VFX, retain/derive the smallest useful evidence set:
- `instanceId`
- `subId`
- derived `titleEditUuid`
- `IsActive`
- primary path
- managed path hash
- typed `PathCrc` only if actually available in the API15 build surface
- `IsPrimaryLoaded`
- GraphicsObject presence
- Transform only if necessary to distinguish a very small number of otherwise ambiguous ambient candidates

Do not add raw-offset reads, trigger-index reads, new native hooks, config switches, developer buttons, commands, or generic layout infrastructure.

### 2C. Correlate against TitleEdit + installed n4gw data

Use TitleEdit's `TE_Futures Rewritten` Active/Inactive sets as **read-only reference evidence**.

Correlate by derived TitleEdit UUID first. Use path / PathCrc / installed n4gw game-data as independent confirmation.

Classify candidate VFX into:
- `ambient-clear-stage`: petals/sparkles/atmospheric clear-stage effect
- `lighting-environment`: lighting/atmosphere-related VFX that may influence perceived visual richness
- `fight-mechanic`: telegraph/attack/encounter mechanic; must remain off
- `unknown`: insufficient evidence; do not write

A restore candidate should require all of:
- current n4gw runtime identity is known;
- currently inactive in MiniUtil;
- TitleEdit expects the same UUID active, or equivalent source-backed evidence exists;
- path/game-data semantics are consistent with ambient/clear-stage use;
- no overlap with existing fight-gimmick suppression intent;
- no evidence it requires unknown trigger-index state to be meaningful.

Do not treat name-only heuristics such as `lit`, `efx`, `petal` as sufficient evidence by themselves.

### 2D. Bounded report and mandatory stop

Do not dump all 248 rows into clipboard.

Produce a compact OneClick/report summary plus, if necessary, a bounded diagnostic-file detail section containing the unique/correlated candidates.

At the end of Checkpoint 2, stop and return a candidate table with approximately:
- TitleEdit UUID
- instanceId/subId
- path / typed PathCrc if available
- current active state
- TitleEdit expected state
- classification
- confidence/evidence
- whether trigger-index semantics appear required

The target is a **small candidate set (ideally 1–a few VFX)**. If evidence only supports a giant blanket Active-state replay, report `HUMAN_DECISION_REQUIRED` and stop.

Checkpoint 2 does not run Sol Review yet unless upstream explicitly asks. It does not merge and does not modify brightness/time/weather.

## Conditional Checkpoint 3 — Minimal VFX restore

Proceed only after upstream reviews Checkpoint 2 evidence and explicitly authorizes a candidate set.

A restore implementation is allowed only if current API15/client semantics for that specific candidate can be established safely.

Minimum write gate must be at least as strict as the existing FRU suppression/static-anchor path:
- FRU candidate only
- pre-login / CharaSelect only
- hook Ready
- same scene generation
- authorized n4gw path / territory 1238 / explicit layer 0
- finite bounded write window / retry budget
- no pointer persistence
- stop on login / session end / scene change / unload
- readback or equivalent bounded state confirmation where the API supports it

Do not:
- replay TitleEdit's full Active/Inactive state
- reactivate fight SharedGroups suppressed by MiniUtil
- add arbitrary decorative VFX not source-backed to FRU
- call unverified vfunc54
- write raw `VfxTriggerIndex` offsets
- create a generic VFX/layout replay engine

If a correct authentic effect demonstrably requires untyped vfunc54 or trigger-index mutation, stop as `HUMAN_DECISION_REQUIRED`; do not silently copy TitleEdit native offsets.

## Lighting/time follow-up

Only after a VFX restore is real-game validated should perceived brightness be reconsidered.

If the result is still too dark/flat, create a separate task. PR #3 does not change:
- FRU 15:17
- Clear Skies row 1
- exposure / EnvSet / unknown lighting fields

## Validation

Checkpoint 2 automated minimum:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add automated tests only for small pure logic introduced by this checkpoint, for example:
- TitleEdit UUID derivation from instanceId/subId
- deterministic bounded candidate de-dup/classification output
- fail-closed gate remains intact

Do not add coverage-driven matrices or full E2E.

Real-game validation for Checkpoint 2 is needed only if the report shape/typed runtime inventory changed materially; if required, preserve the existing one-click contract:
`1クリック → logout → login → 自動コピーされたreportを貼る`

## Out of scope

- brightness/time tuning
- generic layout Active/Inactive replay
- TitleEdit full preset engine port
- blanket Active UUID replay
- arbitrary/custom sparkle injection
- new normal UI or developer controls
- camera / placement / static-anchor redesign
- persistent data semantics changes
- release/version bump
- other background tuning
- broad native-hook infrastructure

## Review

Final implementation review is Sol Review only. Luna/Sonnet may implement but are not reviewers.

MUST FIX is limited to reachable crash/freeze, stale native pointer, cleanup leak, post-login write, weakened scene/safety gate, FRU suppression/placement regression, persistent-data damage, credential/privacy leak, or reviewed-HEAD inconsistency.

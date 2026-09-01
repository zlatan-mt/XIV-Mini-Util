# FRU Clear-Stage Visual Polish Task

## Goal

Character Select の `FRU クリア後ステージ` で欠けている可能性が高い **FRU本来の ambient VFX（花びら等）** を、既存の安全契約・配置・カメラ・戦闘ギミック抑止を維持したまま特定し、安全に可能なら最小範囲だけ復元する。

この task では **brightness/time tuning は行わない**。VFX復元後もまだ暗い場合のみ、別 task としてFRU lighting/timeを調整する。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は一切参照しない。

## Baseline

Task作成時 main:
`d086ee8d6ebf817f5b25073abbc9ab361d2cbeb6`

Container:
- branch: `task/fru-clear-stage-visual-polish`
- PR: `#3`

FRU candidate:
- id: `custom:fru-clear-stage`
- scene: `ex3/01_nvt_n4/goe/n4gw/level/n4gw`
- territory: `1238`
- layer: `0` explicit
- approved static anchor: `(100,0,100)`
- weather: Clear Skies / row 1
- time: 15:17 ET (`DayTimeSeconds=55020`)
- existing placement/background OneClick: PASS

15:17 は noon でwhite-outした過去実機結果を受けた安定値。このtaskでは変更しない。

## Evidence

### MiniUtil current path

MiniUtilは n4gw sceneを直接ロードし、floor / flower field / distant scenery / lightsを残しつつ fight-specific SharedGroupをboundedに抑止する。

従来 `InstanceType.Vfx` は active semantics未検証としてrestore対象外だった。

### TitleEdit evidence

Current TitleEdit `TE_Futures Rewritten` は同じn4gw / territory 1238 / layer 0 / `(100,0,100)` を使い、さらに `SaveLayout=true` / `UseVfx=true` と saved Active layout state を持つ。

TitleEditのVFX active処理は generic `ILayoutInstance.SetActive` と異なる specialized path（vfunc54 / trigger-index replay）を使うため、legacy offset/native callをwrite authorizationとしてコピーしない。

TitleEdit built-inのtime値はMiniUtilの実機調整済み15:17と一致しないため、lighting/timeの正本にはしない。

## Checkpoint 1 — Completed

PR #3 HEAD `e55ef6eecf57f6dd6137de54321d1b1ba31c376d` で read-only FRU VFX inventory をOneClick reportへ統合済み。

Safety:
- FRU only
- pre-login / CharaSelect / hook Ready
- same scene generation
- approved static-anchor authorization
- ActiveLayout InitState 7
- territory 1238 / explicit layer 0
- native pointerはpass内だけ
- VFX write = 0

Automated validation:
- `dotnet run --project tools/CharaSelectLogicTests`: PASS
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`: 0 warnings / 0 errors
- `git diff --check`: PASS

Real-game OneClick result:
- environment pre-login weather requested/readback: `1 / 1` (Clear Skies)
- environment pre-login time requested/readback: `55020 / 55020` (15:17)
- `verify.environment=PASS`
- VFX total: `248`
- active: `1`
- inactive: `247`
- primary path: `248`
- read failures: `0`
- only active representative: `bg/ex3/01_nvt_n4/common/vfx/eff/n4gw02lit1_y1.avfx`

Therefore the immediate hypothesis is **not “time override failed”**. Missing FRU ambient/layout VFX state remains the leading explanation for the flatter/darker visual.

## Current Checkpoint 2 — Source-backed VFX mapping

Before adding any VFX write, identify the authentic clear-stage ambient VFX from installed n4gw game-data and current runtime evidence.

Priority evidence:
1. installed client n4gw layout/game-data identity and primary VFX paths
2. current runtime inventory from Checkpoint 1
3. TitleEdit saved FRU layout/VFX state as comparison evidence
4. real-game visual result

Implementation agent should first inspect local installed n4gw game-data. Do not ask the user for another OneClick merely to obtain data that can be extracted locally.

If runtime inventory needs one more diagnostic pass, extend the existing read-only report only enough to expose **deduplicated useful VFX paths/identities** with a bounded output. Do not dump 248 raw entries and do not add UI/buttons/commands.

Checkpoint 2 stops with a compact mapping such as:
- candidate instance/path
- why it is likely clear-stage ambient rather than fight mechanic
- current active/inactive state
- evidence source
- whether safe activation semantics are known

No write in Checkpoint 2.

## Conditional Checkpoint 3 — Minimal FRU VFX restore

Proceed only after upstream confirms a source-backed minimal candidate set.

Allowed implementation:
- FRU candidate-specific only
- minimal explicit VFX instances/paths only
- existing pre-login/CharaSelect/same-generation/authorized n4gw territory 1238 layer 0 gates
- finite write window
- bounded per-instance retry/write budget
- readback/observable state where supported
- stop on login/session end/scene change/unload
- no pointer persistence

Do not:
- replay TitleEdit's huge Active UUID list
- create generic layout/VFX editor/framework
- re-enable suppressed fight SharedGroups
- copy unverified vfunc54/raw offset/trigger-index writes
- weaken scene identity

If current API15/client semantics cannot independently establish a safe activation path, stop `HUMAN_DECISION_REQUIRED` with evidence rather than writing.

## Brightness follow-up

After authentic ambient VFX restoration is real-game verified, ask only whether the scene is still visibly too dark.

If yes, create a **separate lighting/time task**. That task may compare a very small FRU-only set around the existing 15:17 baseline. Noon remains excluded because it previously white-outed. Do not touch unknown exposure/EnvSet/native lighting fields merely to make the image brighter.

## Acceptance Criteria

- FRU scene/static anchor/camera/placement remain unchanged
- existing fight-gimmick suppression remains effective
- no post-login VFX write
- no stale/cross-scene native pointer
- authentic ambient VFX is source-backed before write
- only verified minimal candidate-specific VFX is restored
- OneClick user contract does not gain extra operations
- if VFX semantics cannot be safely verified, task fails closed instead of guessing

## Validation

For each implementation checkpoint, minimum:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only tests that protect new reachable safety gates/write budgets.

Real-game validation after a restore implementation remains the existing one-click contract:
`1 click → logout → login → auto-copied report` plus simple visual feedback (`ambient effect visible?`, `brightness acceptable?`).

## Coordination with production hotfix

PR #4 handles fresh-install FRU character visibility and is higher priority because it affects public v0.4.1 users.

Do not merge PR #3 before PR #4 solely to avoid sequencing; instead pause PR #3 implementation while PR #4 is completed/reviewed/merged, then sync PR #3 to latest main and continue Checkpoint 2. Keep both task histories separate.

## Out of Scope

- fresh-install character visibility fix (PR #4)
- brightness/time tuning in this PR
- generic layout replay
- TitleEdit engine port
- arbitrary sparkle effect
- camera/placement/static-anchor redesign
- release/version bump
- other background visual tuning

## Review

Final implementation review: Sol Review only. Luna/Sonnet may implement but are not reviewers.

MUST FIX only concrete reachable crash/freeze, stale pointer, cleanup leak, post-login write, safety gate weakening, FRU suppression regression, unintended layout/VFX activation, persistent semantics corruption, or reviewed-HEAD mismatch.
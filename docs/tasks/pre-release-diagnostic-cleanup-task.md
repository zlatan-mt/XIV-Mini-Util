# Pre-release Diagnostic Cleanup Task

## Goal

v0.4.3 公開前に、調査目的で main に入った FRU VFX inventory 診断を production path から除去する。

ゲーム内の現在の挙動は維持する。特に FRU の 13:00 ET、Clear Skies、scene / camera / placement / static anchor / suppression、v0.4.2 の character visibility hotfix は変更しない。

## Baseline

- canonical repository: `zlatan-mt/XIV-Mini-Util`
- base main: `c69579db02775cce4916eb2558df703b4f61ffab`
- branch: `task/pre-release-diagnostic-cleanup`
- release PR #7 は別 task のまま維持する
- PR #8 / #9 の調査実装はこの task に取り込まない

## Scope

最小限、以下の FRU VFX inventory 診断専用経路を除去する。

- `TitleBackgroundCharaSelectVfxInventory` の read-only runtime state / helper
- `TitleScreenBackgroundService.VfxInventory` の毎フレーム VFX scan
- `OnFrameworkUpdate()` からの `MaintainFruVfxInventory()` 呼び出し
- OneClick / QuickCheck / diagnostic selector への `fru.vfx.*` 統合
- inventory 用 reset / detail-file write 経路
- inventory 専用 logic tests

歴史資料として既存 task docs を全面書き換えない。今回の production cleanup に必要なコード参照だけを外す。

## Preserve unchanged

- FRU fixed time: 13:00 ET (`DayTimeSeconds=46800`)
- Clear Skies row 1
- FRU scene / camera / placement / static anchor / suppression
- v0.4.2 fresh-install character visibility hotfix
- pre-login / run-scoped / scene-generation / login-stop safety gates
- `PersistentApplyEnabled`
- no post-login writes
- no pointer persistence

## Out of scope

- release version / `pluginmaster.json` / release asset / publish
- PR #8 cold-start fix or diagnostics
- PR #9 flicker diagnostics or fix
- Title Background redesign
- new logging framework
- new native hook
- VFX state writes
- time / weather retuning

## Acceptance Criteria

1. Normal runtime no longer scans loaded FRU VFX instances for diagnostic inventory.
2. `title-background-fru-vfx-inventory.txt` is no longer produced by the production path.
3. `fru.vfx.*` diagnostic report keys are removed from current production diagnostics.
4. FRU time remains 13:00 ET and weather remains Clear Skies.
5. Existing placement / camera / static-anchor / suppression behavior is unchanged.
6. v0.4.2 character visibility hotfix remains intact.
7. No PR #8 / #9 diagnostic implementation is introduced.
8. No release metadata changes are mixed into this PR.

## Validation

Minimum automated validation:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Do not add new tests merely for coverage. Remove tests that exist only for the deleted inventory diagnostic. Keep the FRU 13:00 / safety behavior tests.

A representative normal FRU Character Select smoke may be performed once before release if needed, but do not recreate diagnostic/probe workflows just to validate this cleanup.

## Review / handoff

Final review is ChatGPT/manual exact-HEAD review. Sol Review is not used for this task.

Implementation must stay on this branch and this single PR. Do not create a replacement PR.

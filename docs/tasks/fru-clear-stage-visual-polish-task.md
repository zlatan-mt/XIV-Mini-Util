# FRU Clear-Stage Visual Polish Task

## Goal

Character Select の `FRU クリア後ステージ` で欠けている可能性が高い **FRU本来の ambient VFX（花びら等）** を、既存の安全契約・配置・カメラ・戦闘ギミック抑止を維持したまま特定し、安全に可能なら最小範囲だけ復元する。

この task では **明るさ・時刻調整は行わない**。VFX復元後の実機結果でまだ暗い場合のみ、別 task として lighting/time tuning を検討する。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は一切参照しない。

## Baseline

Task 作成時 public `main`:
`d086ee8d6ebf817f5b25073abbc9ab361d2cbeb6`

Current task container:
- branch: `task/fru-clear-stage-visual-polish`
- PR: `#3`

FRU candidate:
- id: `custom:fru-clear-stage`
- scene: `ex3/01_nvt_n4/goe/n4gw/level/n4gw`
- territory: `1238`
- layer: `0` explicit
- approved static anchor: `(100, 0, 100)`
- weather: Clear Skies / row 1
- time: 15:17 ET (`DayTimeSeconds=55020`)
- existing FRU OneClick real-game verification: PASS

現在の 15:17 は、正午固定で white-out した実機結果を受けた MiniUtil 側の安定値なので、この task では変更しない。

## Investigation Findings

### MiniUtil current state

MiniUtil は n4gw scene を直接ロードし、floor / flower field / distant scenery / lights を残しつつ fight-specific SharedGroup のみ bounded window で抑止している。

一方 `InstanceType.Vfx` は active semantics 未検証として明示的に suppression/restore 対象外で、現在の runtime state も `VfxMode=excluded-semantics-unverified`。

### TitleEdit evidence

Current TitleEdit built-in `TE_Futures Rewritten` は同じ n4gw / territory 1238 / layer 0 / position `(100,0,100)` を使い、さらに:
- `SaveLayout=true`
- `UseVfx=true`
- saved Active layout state

を持つ。

TitleEdit は VFX に generic `ILayoutInstance.SetActive` とは別の specialized active path（vfunc 54）と trigger-index replay を使う。これは missing ambient VFX の有力な説明だが、write authorization にはしない。

また current TitleEdit built-in の `TimeOffset=0` は MiniUtil の 15:17 と一致しない。したがって TitleEdit を lighting/time の正本として扱わない。

### Current FFXIVClientStructs

Current `VfxLayoutInstance` は GraphicsObject / transform / PathCrc / fade / color / flags 等を公開するが、TitleEdit が使う specialized vfunc54 / trigger-index は標準の型付き API として公開されていない。

TitleEdit の古い offset / vfunc / native call をコピーして write しない。

## Decisions

### 1. First implementation is read-only only

最初の implementation checkpoint は **FRU-only VFX inventory の OneClick 自動収集**のみ。

この checkpoint では:
- VFX write を追加しない
- FRU time/weather を変更しない
- 通常 UI / config toggle / developer button を追加しない
- generic VFX/layout framework を作らない

実装・自動validation後、一度停止して上流へ報告する。VFX write へ自律的に進まない。

### 2. OneClick contractを再利用

ユーザー操作は既存契約どおり:

`1クリック → logout → login → 自動コピーされたreportを貼る`

だけ。

VFX inventory は既存 OneClick final report に自動統合する。大量 raw dump を clipboard 本文へ流さず、本文は件数・代表identity・状態・取得可否を要約する。詳細が必要なら既存の診断ファイル保存経路を再利用する。

### 3. Restore is conditional and same PR

OneClick report + installed n4gw game-data + current loaded layout + TitleEdit evidence から authentic ambient VFX を source-backed に特定できた後、上流が許可した場合のみ同じ PR #3 で最小復元へ進む。

次のどれかが必要ならその時点で `HUMAN_DECISION_REQUIRED` として停止する:
- current API15 にない untyped vfunc54 call
- 未検証 raw offset / trigger-index write
- blanket Active UUID replay
- generic VFX framework
- scene identity を弱める必要がある実装

### 4. Brightness is separate follow-up

VFX復元後にまだ暗い場合だけ別 task を作る。

この PR では:
- `TitleBackgroundEnvironmentTimePolicy` の FRU 15:17 を変えない
- Clear Skies row 1 を変えない
- exposure / EnvSet / unknown lighting field を触らない

## Current Implementation Checkpoint

### Checkpoint 1 — Read-only FRU VFX inventory

既存の authorized pre-login n4gw scene で `InstanceType.Vfx` を read-only に走査する。

最低限の情報:
- stable identity: `InstanceKey` + `SubId` 等、既存型から安全に得られるもの
- `IsActive`
- primary path（安全に取得できる場合）
- `PathCrc`
- `IsPrimaryLoaded`
- GraphicsObject presence
- transform は候補判定に必要な場合だけ

必須 gate:
- FRU candidate only
- pre-login only
- CharaSelect session only
- hook Ready
- current lobby map == CharaSelect
- same scene generation
- existing static-anchor authorization success
- ActiveLayout `InitState=7`
- territory `1238`
- explicit layer `0`
- native pointer は pass 内だけで使用し保持しない
- **write = 0**

既存 `LayoutWorld.Instance()->ActiveLayout`、FRU static-anchor authorization、OneClick report path を優先して再利用する。

## Checkpoint 1 Acceptance Criteria

- FRU以外では inventory を取得しない
- unauthorized / wrong scene / wrong generation / post-login では native layout を読みに行かず fail-closed
- VFX state を一切変更しない
- FRU time/weather/camera/placement/suppression を変更しない
- OneClick の通常UI操作を増やさない
- final auto-report に compact な VFX inventory summary が含まれる
- stale pointer / cross-scene pointer reuse なし
- existing FRU suppression / static anchor / `PersistentApplyEnabled` / pre-login snapshot contract を維持

## Validation for Checkpoint 1

Minimum:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

新規 automated test は必要最小限:
- inventory gate が FRU + pre-login + exact authorized scene 以外で成立しないこと
- report summary が bounded であること
- diagnostic path に write が存在しないことを既存構造で固定できる場合のみ追加

その後 real-game OneClick を1回行い、自動コピーreportを上流へ返す。

**Checkpoint 1 完了時点では Sol Review を実行しない。VFX write を実装しない。mergeしない。**

## Conditional Next Step — Minimal VFX Restore

Checkpoint 1 evidence を上流で確認後、同じ PR #3 で continuation 指示が出た場合のみ実施する。

実装する場合も FRU candidate-specific minimal set のみ。write gate は既存 FRU suppression と同等以上:
- pre-login / CharaSelect / hook Ready
- same scene generation
- authorized n4gw path / territory 1238 / layer 0
- finite write window / bounded retry
- readback
- login / session end / scene change / unload で停止
- pointer persistence なし

既存 fight-gimmick suppression を弱めない。消した SharedGroup を blanket に再active化しない。

## Out of Scope

- brightness/time tuning（別 task）
- generic layout Active/Inactive replay
- TitleEdit 全 preset engine 移植
- TitleEdit の巨大 Active UUID list の blanket replay
- arbitrary/custom sparkle追加
- 新しい通常UI / developer controls
- camera / placement / static anchor redesign
- persistent data semantics変更
- release/version bump
- 他 background tuning
- broad native hook infrastructure

## Review

最終 implementation 完了後の review は Sol Review (`sol-review-bridge`) のみ。Luna / Sonnet は implementation agent として使ってよいが reviewer として使わない。

MUST FIX は reachable な crash/freeze、stale pointer、cleanup漏れ、post-login write、安全gate弱体化、既存FRU suppression破壊、persistent semantics破壊、reviewed HEAD不整合等に限定する。
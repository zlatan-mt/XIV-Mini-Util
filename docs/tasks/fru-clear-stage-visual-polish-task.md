# FRU Clear-Stage Visual Polish Task

## Goal

Character Select の `FRU クリア後ステージ` を、現在の安全性・配置・カメラ・戦闘ギミック抑止を維持したまま、実際の FRU クリア後花畑に近い見た目へ改善する。

主な対象は次の2点。

1. 実際のクリア後画面で見える、花びらが空中を漂うような ambient VFX / layout state の欠落を調査し、安全に特定できたものだけ復元する。
2. VFX 復元後も暗く感じる場合のみ、FRU 固有の時刻を white-out しない範囲で調整する。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は参照しない。

## Baseline

Task 作成時 public `main`:
`d086ee8d6ebf817f5b25073abbc9ab361d2cbeb6`

Open PR / 同目的 active branch: なし。

現在の FRU candidate:

- id: `custom:fru-clear-stage`
- scene: `ex3/01_nvt_n4/goe/n4gw/level/n4gw`
- territory: `1238`
- layer: `0`（explicit）
- approved static anchor: `(100, 0, 100)`
- `BackgroundUsable=true`
- `CharacterExpectedVisible=true`
- `VerifiedInGame=true`
- weather: Clear Skies / row 1
- time: 15:17 ET (`DayTimeSeconds=55020`)
- existing FRU OneClick real-game verification: PASS

現在の 15:17 は、正午固定で FRU lighting が white-out した実機結果を受けて採用されている。単純に noon へ戻さない。

## Investigation Findings

### 1. 実際の FRU clear scene

公開されている FRU clear 後の画像では、明るい青空・花畑に加えて、ピンク系の花びらが空中を漂う ambient effect が確認できる。

プレイヤー武器/スキル由来の sparkle が混在する画像もあるため、「任意のキラキラを追加」ではなく、まず n4gw 本来の ambient petal / clear-stage VFX の再現を目標にする。

### 2. MiniUtil の現在の再現範囲

MiniUtil は n4gw scene を直接ロードし、FRU 戦闘中の gimmick / telegraph SharedGroup を candidate-specific に抑止している。

既存 suppression は floor / flower field / distant scenery / lights を keep し、fight-specific SharedGroup のみ bounded window で `SetActive(false)` にする。

一方、`InstanceType.Vfx` は active semantics 未検証として明示的に対象外で、現在の runtime state も `VfxMode=excluded-semantics-unverified` になっている。

### 3. TitleEdit current FRU presetとの差

TitleEdit の current built-in `TE_Futures Rewritten` は同じ n4gw / territory 1238 / layer 0 / position `(100,0,100)` を使うが、さらに:

- `SaveLayout=true`
- `UseVfx=true`
- 大量の saved Active layout UUID state

を持つ。

TitleEdit は VFX instance について generic `ILayoutInstance.SetActive` とは別に、specialized VFX active path（vfunc 54）と VFX trigger-index replay を使用する。

これは「MiniUtilでは背景と花畑は出るが ambient effect が不足する」ことの有力な説明になる。

ただし TitleEdit の巨大な Active UUID list をそのまま移植して generic layout replay を作ることは禁止する。

### 4. Current FFXIVClientStructs

現行 `VfxLayoutInstance` は GraphicsObject / transform / PathCrc / fade / color / flags 等を公開するが、TitleEdit が使用する trigger-index field / specialized vfunc54 は標準の型付き API として公開されていない。

したがって current client / API15 で独立検証せずに TitleEdit の offset / native call をコピーして write しない。

## Decisions

### A. VFX first, brightness second

暗さと ambient VFX 欠落を混同しない。

最初に本来の ambient VFX を観測・特定・可能なら復元する。FRU 時刻 15:17 / weather 1 は初期実装では変更しない。

VFX 復元後もユーザーが暗いと判断した場合だけ、FRU candidate-specific time を別 checkpoint で調整する。

### B. Investigation-first / fail-closed

最初の実装 checkpoint は **read-only VFX inventory** とする。未検証 native VFX write は入れない。

既存 OneClick flow に自動統合し、ユーザー操作は従来どおり:

`1クリック → logout → login → 自動コピーされたreportを貼る`

だけとする。

### C. Candidate-specific only

generic VFX framework、generic Active/Inactive replay、全 background 共通の layout editor は作らない。

実装対象は FRU `custom:fru-clear-stage` / n4gw の clear-stage visual に必要と source-backed に確認できた最小 set だけ。

## Approach

### Checkpoint 1 — Read-only FRU VFX inventory

既存の authorized pre-login n4gw scene で `InstanceType.Vfx` を read-only に走査する小さな path を追加する。

最低限収集する情報:

- instance key / SubId から安定して表現できる identity
- `IsActive`
- primary path（安全に取得できる場合）
- `PathCrc`
- `IsPrimaryLoaded` / graphics object presence など read-only 状態
- 必要な場合のみ transform

条件:

- FRU candidate のみ
- pre-login CharaSelect session
- hook Ready
- current lobby map == CharaSelect
- same scene generation
- existing static-anchor authorization 成功
- ActiveLayout InitState=7
- territory 1238 / explicit layer 0 一致
- native pointer は各 pass 内だけで使用し保持しない
- write なし

report は件数・状態・候補 path/identity を要約し、数百行の raw dump を clipboard 本文へ流さない。必要なら詳細は別 diagnostic file に出す。

OneClick final report に自動統合する。

### Checkpoint 2 — Map authentic ambient VFX

Checkpoint 1 の report と installed client game-data（n4gw `vfx.lgb` / related SharedGroup）を照合し、実際の花びら/ambient effect に対応する VFX instance を特定する。

TitleEdit の `TE_Futures Rewritten` は evidence として比較してよいが、巨大 Active list を write authorization として扱わない。

優先する証拠:

1. installed n4gw game-data の primary path / VFX identity
2. current loaded layout の read-only identity/readback
3. TitleEdit preset が active として保持する該当 identity
4. 実ゲーム visual result

### Checkpoint 3 — Minimal FRU VFX restore

ambient VFX が少数の明示的 instance として特定でき、current client/API15 で activation semantics が独立確認できた場合のみ実装する。

write gate は既存 FRU suppression と同等以上にする:

- FRU candidate only
- pre-login only
- CharaSelect only
- hook Ready
- same scene generation
- authorized n4gw path / territory 1238 / layer 0
- finite write window
- per-instance retry/write budget
- readback
- login / session end / scene-generation change / plugin unload で停止
- native pointer を跨いで保持しない

specialized vfunc54 / trigger-index write が必要な場合、current client/API15 で signature/offset/semantics を独立確認できるまで write を実装しない。確認不能なら fail-closed で Checkpoint 2 の調査結果を返す。

既存 fight-gimmick suppression を弱めたり、消した SharedGroup を blanket に再 active 化しない。

### Checkpoint 4 — Optional brightness tune

ambient VFX 復元後の実機確認で「まだ暗い」場合のみ実施する。

- weather は Clear Skies row 1 を維持するのを第一候補とする
- current 15:17 を baseline とする
- noon は既知 white-out のため候補に戻さない
- 14時台など、少数の FRU-only candidate time を code-side で比較できる最小 diagnostic path にする
- 最終値は real-game result で決める

単に明るくする目的で exposure / EnvSet / unknown lighting native field を新規 write しない。

## Acceptance Criteria

- 既存 FRU scene / static anchor `(100,0,100)` / camera / Clear Skies / placement semantics を壊さない
- 既存 fight-specific gimmick suppression が維持され、魔法陣・氷・telegraph clutter が再出現しない
- OneClick の操作契約を増やさない
- Checkpoint 1 で FRU VFX inventory が自動 report に含まれる
- ambient petal VFX を source-backed に特定できた場合、verified minimal path だけで visible にできる
- 未検証 VFX native write は fail-closed
- post-login write はゼロ
- stale pointer / cross-scene pointer reuse なし
- plugin unload / session change で write path が確実に停止する
- brightness変更を行う場合、current 15:17 より visual が改善し、既知 white-out を再発しないことを実機確認する

「ambient VFX を安全に特定できない / activation semantics を current client で確認できない」場合は、無理に write せず HUMAN_DECISION_REQUIRED として evidence を返すことを許容する。

## Validation

Automated minimum:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

追加する automated test は今回の新しい安全ゲートを固定する最小量だけ:

- FRU / pre-login / CharaSelect / exact scene authorization 以外で write 不可
- unknown/unverified VFX は write 不可
- post-login write 不可
- bounded write budget / scene-generation reset

Real-game validation が必要な checkpoint では既存 OneClick を使用し、ユーザーには追加 developer 操作を要求しない。

実機確認時の最小 user feedback:

- 花びら/ambient effect: 出た / 出ない
- 明るさ: 良い / まだ暗い
- 自動コピーされた report を貼る

## Out of Scope

- generic layout Active/Inactive replay framework
- TitleEdit 全 preset engine の移植
- TitleEdit の巨大 FRU Active UUID list の blanket replay
- arbitrary/custom sparkle effect の追加
- 新しい通常UIの developer controls
- camera / placement / static anchor 再設計
- persistent data semantics 変更
- release/version bump
- 他 background の visual tuning
- 新しい broad native hook infrastructure

## Review

Review は Sol Review (`sol-review-bridge`) のみ。Luna / Sonnet を reviewer として使用しない。

MUST FIX は reachable な crash/freeze、stale pointer、cleanup漏れ、post-login write、安全gate弱体化、既存FRU suppression破壊、persistent semantics破壊、reviewed HEAD不整合などに限定する。
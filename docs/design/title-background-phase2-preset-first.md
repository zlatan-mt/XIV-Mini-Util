<!-- Path: docs/design/title-background-phase2-preset-first.md -->
<!-- Description: Title Background Phase 2 を preset-first 方針へ切り替える設計整理 -->
<!-- Reason: 任意地点キャプチャ汎用化を後回しにし、事前定義 preset 適用へ開発対象を絞るため -->
# Title Background Phase 2 preset-first 設計整理

作成日: 2026-05-13

## 方針変更

Phase 2 の主目的を「現在地キャプチャから任意地点を汎用再現する機能」から、「事前定義した背景・キャラ位置・カメラ値を選んで適用する機能」へ切り替える。

実機確認では、`LobbyCameraFixOn` の override 自体は成立している。`lastCapturedCamera == lastAppliedCamera == postFixOnSceneCameraPosition`、`lastCapturedFocus == lastAppliedFocus == postFixOnLookAtVector`、`FovY` 一致までは確認済み。

ただし、`FixOn` 直後の値は最終表示まで維持されない。`postFixOnSceneCameraPosition` と `currentSceneCameraPosition` は後続処理で変化し、`Distance` も変化する。一方で `LookAtVector` と `FovY` は維持される。現在地をそのまま保存して任意地点で汎用再現するには、`FixOn` 以外の camera 更新経路、distance、look-at、scene load 後の再調整を追う必要があり、Phase 2 の実装範囲としては重い。

そのため、当面は「preset ごとに最終見た目が合う値を持つ」設計に寄せる。内部 field の意味を完全に一般化するより、built-in preset を少数用意し、実機で見た目を合わせた値を安全に適用できることを優先する。

この方針は、camera 問題が自動的に解決したという意味ではない。「任意地点汎用再現」から「限られた built-in preset を実機で成立させる」へ問題を縮小する判断である。成立性は最初に 1 preset だけで確認し、その結果が出るまで catalog / UI 実装へ進まない。

## 目標

- `TitleBackgroundPreset` を中心に、背景 scene と camera 値を 1 セットとして扱う。
- `CharacterPosition` / `CharacterRotation` は将来拡張用 field として保持するが、Phase 2 preset-first v1 の完了条件には含めない。
- UI は手入力や現在地保存ではなく、preset 選択と適用状態の確認を中心にする。
- 1 preset feasibility spike で最終構図の再現性を確認してから、複数 built-in preset を追加できる構造を作る。
- camera / focus / FOV は、最終表示を合わせるための preset field として扱う。
- 意味が未確定の値は汎用 API 名にせず、必要なら `VisualTuning` や `RawCamera` 相当の扱いで閉じ込める。
- `CaptureCurrentLocationAndCamera()` は dev / debug 補助に下げ、通常 UI の主導線から外す。
- `FixOn` ABI verified 化、任意地点の現在地再現、delayed reapply、別 hook / 別適用点探索、外部 JSON preset 管理は後回しにする。

## 非目標

- 任意の現在地をワンクリック保存し、キャラ選択画面で完全再現すること。
- `Camera Position`、`Distance`、`LookAtVector`、`Focus` の内部意味を Phase 2 で確定すること。
- patch 差分に耐える native ABI の完全保証を Phase 2 の完了条件に入れること。
- ユーザー編集可能な外部 preset ファイル仕様を同時に作ること。

## preset-first モデル

`TitleBackgroundPreset` は Phase 2 の中心 DTO として残す。現在の field は大きく次の扱いに分ける。

| field | Phase 2 の扱い |
| --- | --- |
| `Name` | 既存 DTO 上の名称。永続キーには使わない。 |
| `TerritoryPath` | 背景 scene の主要値。引き続き正規化と LVB 存在確認を行う。 |
| `TerritoryTypeId` | scene load / layout 補助値。0 は未指定として扱う。 |
| `LayoutTerritoryTypeId` | `CreateScene` に渡す territoryId の補正値。必要な preset のみ使う。 |
| `LayoutLayerFilterKey` | layer 選択補助値。意味を広げず、見た目調整用の任意値として扱う。 |
| `CharacterPosition` | 将来のキャラクター配置用。Focus の代用品にはしない。 |
| `CharacterRotation` | 将来のキャラクター向き用。 |
| `CameraX/Y/Z` | `FixOn` に渡す camera 値。内部意味より最終構図を優先する。 |
| `FocusX/Y/Z` | `FixOn` に渡す focus 値。現時点では注視点として一般化しない。 |
| `FovY` | 見た目調整値。clamp は維持する。 |
| `WeatherId` / `TimeOffset` / `BgmPath` | 後続接続用に保持するが Phase 2 の適用対象外。 |

追加するなら、まず `TitleBackgroundBuiltInPresetCatalog` のような静的 catalog を置く。保存形式を増やす前に、built-in preset の安定した `PresetId` だけを `Configuration` に持たせる構造が低リスク。

`Name` / 表示名は、文言変更、日本語化、UI 表示調整で変わる可能性があるため、設定上の永続キーに使わない。`TitleBackgroundSelectedPresetName` は採用しない。

想定構造:

```csharp
internal sealed class TitleBackgroundBuiltInPresetEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required TitleBackgroundPreset Preset { get; init; }
}

internal static class TitleBackgroundBuiltInPresetCatalog
{
    public static IReadOnlyList<TitleBackgroundBuiltInPresetEntry> Presets { get; } =
    [
        new TitleBackgroundBuiltInPresetEntry
        {
            Id = "preset-a",
            DisplayName = "Preset A",
            Preset = new TitleBackgroundPreset { ... }.Normalize(),
        },
    ];
}
```

`Configuration` には `TitleBackgroundSelectedPresetId` を追加する。既存の個別 field は、選択 preset の展開先として当面残す。これにより既存の apply / validation / diagnostic 経路を大きく壊さずに UI を preset-first にできる。

`TitleBackgroundSelectedPresetId` は、現在の raw `TitleBackground*` field が built-in preset と同期している場合のみ有効とみなす。debug capture、手入力、raw field の直接編集が行われた場合は `TitleBackgroundSelectedPresetId` を空文字へ戻す。UI 上は preset 未選択、または `Custom / Debug override` として扱う。

## 責務分離

- `TitleBackgroundBuiltInPresetCatalog`
  - built-in preset の定義元。
  - stable `Id`、UI 用 `DisplayName`、適用値 `Preset` を保持する。
  - 実機で成立確認した値だけを確定 preset として置く。
- `Configuration`
  - `TitleBackgroundSelectedPresetId` と、既存の展開済み `TitleBackground*` field を保持する。
  - 永続キーには `PresetId` だけを使い、表示名は保存しない。
  - raw `TitleBackground*` field が preset 展開値から外れた場合、`TitleBackgroundSelectedPresetId` は空文字に戻す。
- `TitleScreenBackgroundService`
  - selected preset を atomic に検証し、成功時だけ既存設定 field へ一括展開して現行の scene / camera override 経路へ渡す。
  - catalog の表示順や UI 文言は知らない。
  - debug capture の成否を、built-in preset の成立判定とは混ぜない。
- UI
  - preset の選択、適用、現在 status の確認を担当する。
  - camera / focus / character position の直接編集と capture は debug / diagnostic 折りたたみに隔離する。
  - raw edit / debug capture 後は preset 選択を解除し、`Custom / Debug override` として表示する。
- `TitleBackgroundCameraCaptureService`
  - dev / debug 用の観測値取得に限定する。
  - 任意地点再現の保証や built-in preset の自動生成を担当しない。

## UI 方針

通常導線:

1. `キャラクター選択画面背景を差し替える（実験）`
2. preset combo で built-in preset を選択
3. `preset を適用`
4. status / LVB 想定パス / runtime mode を確認

通常 UI からは、`TerritoryPath`、camera、focus、character position の手入力を前面に出さない。これらは `詳細設定 / 診断` または `dev/debug` 折りたたみに移す。

`CaptureCurrentLocationAndCamera()` は次の扱いに下げる。

- 表示名は「現在値を debug 保存」程度にし、汎用再現できる印象を避ける。
- 通常の preset 選択 UI とは分離する。
- 保存結果は preset 作成の参考値として扱い、built-in preset 品質の根拠にはしない。
- diag には残してよいが、ユーザー向けの主機能として説明しない。

## 実装順

0. 1 preset feasibility spike
   - 背景 1 件だけを対象にする。
   - `FixOn` に渡す camera / focus / FOV の preset 値を実機で手調整する。
   - キャラ選択画面の最終見た目が、意図した構図に概ね合うことを確認する。
   - 同一 preset を 3 回以上ロードし、最終構図の再現性を確認する。
   - 成立した場合のみ、built-in preset catalog / UI 実装へ進む。
   - 成立しない場合は、camera 後段補正の調査、delayed reapply、別 hook / 別適用点探索を別タスクとして切り出し、preset-first UI 実装には進まない。
1. `TitleBackgroundBuiltInPresetCatalog` と `TitleBackgroundBuiltInPresetEntry` を追加し、複数 preset を扱える構造にする。初期の user-visible preset は feasibility spike 済みの verified 1 件に限定する。
2. `Configuration` に `TitleBackgroundSelectedPresetId` を追加する。`SelectedPresetName` は追加しない。
3. `TitleScreenBackgroundService` に atomic な preset 適用処理を追加する。
   - preset を取得する。
   - `Normalize()` する。
   - `Validate()` する。
   - 必要な LVB 確認を行う。
   - すべて成功した場合のみ `Configuration` に一括展開する。
   - 失敗時は既存 `Configuration` を変更しない。
   - `ApplyPreset(TitleBackgroundPreset preset)` または既存 `ApplyCapturedPreset` を汎用化した private/public 境界として実装する。
4. UI を preset combo 中心に変更し、手入力と capture は debug 折りたたみへ移す。
5. 既存 logic tests に catalog の stable ID 重複なし、normalize / validate、selected preset の展開テストを追加する。
   - invalid preset 適用時に `Configuration` が変更されないこと。
   - 存在しない `TitleBackgroundSelectedPresetId` 読み込み時に安全に fallback すること。
   - debug capture / raw edit 実行時に `TitleBackgroundSelectedPresetId` が解除されること。
6. 実機で built-in preset を 1 件ずつ調整し、最終見た目が合う値だけを確定値として残す。

## 既存コードから残すもの

- `TitleBackgroundPreset`
  - preset-first の中心として残す。
  - normalize / validate / clamp / sanitize は継続利用する。
- `TitleBackgroundBuiltInPresetEntry`
  - 新規追加する場合、`Id` と `DisplayName` と `Preset` の分離境界にする。
- `TitleBackgroundPathHelper`
  - `TerritoryPath` 正規化と LVB path 組み立てに必要。
- `TitleBackgroundCameraOverridePlan`
  - selected preset を `Configuration` に展開した後の FixOn 適用 plan として残す。
- `TitleScreenBackgroundService`
  - hook lifecycle、fail-closed、status、diag、scene override、camera override の実行点を残す。
  - `ApplyCapturedPreset` は名前と公開範囲を見直し、preset 適用用に再利用する。
  - built-in catalog の選択 UI や表示名管理は持たせない。
- runtime mode / resolver mode
  - `ResolveOnly` / `HookProbe` / `CharaSelectOnly` の段階投入は残す。
- signature 入力と address 再解決
  - dev / diagnostic 用として残す。ただし通常 UI の主導線ではない。
- 既存 tests
  - `TitleBackgroundPreset`、path、camera override、runtime mode、resolver の純粋テストは維持する。

## 保留するもの

- `CaptureCurrentLocationAndCamera()`
  - 削除はしないが dev / debug 用へ降格する。
  - 「現在地を保存して再現」というユーザー向け約束には使わない。
- `TitleBackgroundCameraCaptureService`
  - built-in preset 調整の参考値取得として残す。
  - field 名やメッセージは、汎用再現を保証しない表現へ直す余地がある。
- `TitleBackgroundCameraCapturePresetBuilder`
  - debug capture の fail-closed 境界として残す。
  - built-in catalog の正規化とは分ける。
- `WeatherId` / `TimeOffset` / `BgmPath`
  - preset field として残すが、Phase 2 の実装対象からは外す。
- 任意地点再現
  - Camera Position / Distance 更新経路の調査が必要なため、別 Phase に分離する。
- camera 後段補正への対応
  - delayed reapply、別 hook、別適用点探索は、1 preset feasibility spike が不成立だった場合の別タスクにする。
- `FixOn` ABI verified 化
  - 安全性には重要だが、Phase 2 preset-first UI の完成条件には入れない。
  - 実機検証済み signature / delegate の確定は別作業として管理する。
- 外部 JSON preset 管理
  - built-in preset でデータ形が固まってから検討する。

## 完了条件

- 1 preset feasibility spike で、同一 preset 3 回以上のロードに対して最終構図が概ね再現することを確認している。
- 複数 built-in preset を追加可能な catalog 構造になっている。
- user-visible preset は実機確認済みの verified preset に限定する。
- built-in preset は stable `PresetId` と `DisplayName` を分離している。
- `SelectedPresetId` と raw field の同期ルールが実装され、raw edit / debug capture で preset 選択が解除される。
- preset 適用は all-or-nothing で、validate / LVB 確認失敗時に既存 `Configuration` を変更しない。
- UI の主導線が preset 選択と適用になっている。
- 手入力 camera / capture は通常導線から外れている。
- selected preset を既存 `Configuration` field に展開し、現行の scene / camera override 経路で適用できる。
- logic tests で preset catalog の ID 重複なし、normalize / validate、選択 preset の展開、invalid preset の非破壊 fallback、存在しない selected ID の fallback、raw edit / debug capture 時の selected ID 解除が確認できる。
- 実機確認では「内部 field の意味が正しい」ではなく「選択 preset の最終見た目が意図通り」を合否基準にする。

# Research & Design Decisions

## Summary
- **Feature**: dalamud-utility-plugin (XIV Mini Util)
- **Discovery Scope**: New Feature (Greenfield)
- **Key Findings**:
  - Dalamud API v14は.NET 10とC# 14を使用、Dalamud.NET.Sdkへの移行が推奨
  - FFXIVClientStructsを通じてInventoryManager、InventoryItemにアクセス可能
  - AutoDutyプラグインのDesynthHelper実装が参考パターンとして利用可能
  - マテリア精製・アイテム分解はゲームUIアドオン経由で操作する必要あり

## Research Log

### Dalamud API v14の技術要件
- **Context**: プラグイン開発に必要なランタイムとSDKの調査
- **Sources Consulted**:
  - [Dalamud v14 Release Notes](https://dalamud.dev/versions/v14/)
  - [Dalamud GitHub](https://github.com/goatcorp/Dalamud)
- **Findings**:
  - .NET 10.0.0 (C# 14)が必須
  - .NET SDK 10.0.101とVisual Studio 2026またはRider 2025.3が必要
  - Dalamud.NET.Sdk/14.0.1への移行が推奨
  - サービスインターフェースは`Dalamud.Plugin.Services`名前空間に統合
  - 新サービス: IPlayerState（プレイヤー固有データ）、IUnlockState（アンロック状態確認）
  - LocalPlayerは`IObjectTable.LocalPlayer`に移動
- **Implications**: プロジェクトはDalamud.NET.Sdkを使用し、.NET 10ターゲットで構成する

### インベントリアクセスAPI
- **Context**: 所持品・アーマリーチェストへのアクセス方法の調査
- **Sources Consulted**:
  - [FFXIVClientStructs GitHub](https://github.com/aers/FFXIVClientStructs)
  - [Dalamud API Documentation](https://dalamud.dev/api/)
- **Findings**:
  - `FFXIVClientStructs.FFXIV.Client.Game.InventoryManager`でインベントリ管理
  - `InventoryItem`構造体に以下のフィールド:
    - `ItemID` (uint): アイテムID
    - `Spiritbond` (ushort): スピリットボンド値（0-10000、100% = 10000）
    - `Materia` (ushort*): マテリアスロット
    - `MateriaGrade` (byte*): マテリアグレード
    - `Quantity` (uint): 数量
    - `Slot` (short): スロット位置
    - `Flags` (ItemFlags): アイテムフラグ
  - インベントリタイプ: Inventory (所持品), ArmoryMainHand/OffHand/Head/Body/etc (アーマリーチェスト)
- **Implications**: InventoryManagerを通じて全インベントリスロットをスキャン可能

### マテリア精製の実装方法
- **Context**: スピリットボンド100%アイテムからのマテリア精製自動化
- **Sources Consulted**:
  - [AutoDuty Plugin](https://github.com/ffxivcode/AutoDuty)
  - Dalamud ConditionFlag documentation
- **Findings**:
  - スピリットボンド100%は`InventoryItem.Spiritbond == 10000`で判定
  - マテリア精製中は`ConditionFlag.Materialize`がアクティブ
  - ゲームUIアドオン経由でマテリア精製を実行する必要あり
  - 既存プラグイン（Artisan、AutoDuty）が自動マテリア精製機能を実装済み
- **Implications**: UIアドオンへのコールバック実行でマテリア精製を実装

### アイテム分解（Desynthesis）の実装方法
- **Context**: アイテム分解の自動化実装方法
- **Sources Consulted**:
  - [AutoDuty DesynthHelper.cs](https://github.com/ffxivcode/AutoDuty/blob/master/AutoDuty/Helpers/DesynthHelper.cs)
- **Findings**:
  - `FFXIVClientStructs.FFXIV.Client.Game`、`Client.UI`、`Client.UI.Agent`名前空間を使用
  - 関連UIアドオン: "Desynth", "SalvageResult", "SalvageDialog", "SalvageItemSelector"
  - `AddonHelper.FireCallBack()`でUI操作を実行
  - アイテムレベルはLumina Excelシートから取得
  - 分解スキルレベルは`PlayerState`から取得可能
  - ギアセット装備品の除外には`RaptureGearsetModule`を使用
- **Implications**: DesynthHelperのパターンを参考にUI操作ベースの実装を行う

### ジョブ判定API
- **Context**: 現在のジョブがクラフターかどうかの判定方法
- **Sources Consulted**:
  - [IClientState Interface](https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/IClientState/)
  - [Dalamud Character.cs](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/ClientState/Objects/Types/Character.cs)
- **Findings**:
  - `IClientState.LocalPlayer.ClassJob`でRowRef<ClassJob>を取得
  - ClassJobシートからジョブカテゴリ（DoH/DoL/Battle）を判定可能
  - クラフタージョブ: CRP(8), BSM(9), ARM(10), GSM(11), LTW(12), WVR(13), ALC(14), CUL(15)
  - 戦闘職: GLA(1), PGL(2), MRD(3), LNC(4), ARC(5), CNJ(6), THM(7), その他ジョブ
- **Implications**: ClassJob.Rowから直接ジョブIDを比較してカテゴリ判定

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| **Service-Based** | サービスクラスで機能を分離 | 単一責任、テスト容易、DI対応 | やや構造が複雑 | Dalamud標準パターン |
| Monolithic | 単一クラスに全機能 | 実装簡易 | 保守性低下、テスト困難 | 小規模プラグイン向け |
| Event-Driven | イベントベースの疎結合 | 拡張性高い | 複雑性増加 | 大規模プラグイン向け |

**選択**: Service-Basedアーキテクチャ
- Dalamudの標準DIパターンに準拠
- 機能（マテリア精製、アイテム分解）を独立サービスとして実装
- UIとロジックの分離が容易

## Design Decisions

### Decision: UIアドオン操作によるゲームアクション実行
- **Context**: マテリア精製・アイテム分解のゲーム内実行方法
- **Alternatives Considered**:
  1. 直接メモリ操作 — 危険性が高く、BAN対象の可能性
  2. UIアドオン経由のコールバック — 既存プラグインで実績あり
- **Selected Approach**: UIアドオン経由でFireCallBackを使用
- **Rationale**: 既存の安定したパターンを踏襲、ゲームクライアントの正常なフローを使用
- **Trade-offs**: UI表示が必要、処理速度はゲームUIに依存
- **Follow-up**: ゲームパッチ後のアドオン名・パラメータ変更に注意

### Decision: 警告ダイアログの実装方式
- **Context**: 高レベルアイテム分解時の警告表示
- **Alternatives Considered**:
  1. ImGuiモーダルダイアログ — プラグインUI内で完結
  2. ゲーム内通知 — ユーザーの注意を引きやすい
- **Selected Approach**: ImGuiモーダルダイアログ
- **Rationale**: ユーザーの明示的な承認が必要、処理フローの制御が容易
- **Trade-offs**: ゲームUIとの一貫性がやや低下
- **Follow-up**: ダイアログデザインをゲームUIに近づける

### Decision: 設定の永続化方式
- **Context**: ユーザー設定の保存方法
- **Alternatives Considered**:
  1. Dalamud標準のConfiguration — 自動シリアライズ、推奨パターン
  2. カスタムJSON — 柔軟性は高いが実装コスト増
- **Selected Approach**: Dalamud標準のIPluginConfiguration
- **Rationale**: フレームワーク標準、実装簡易、自動保存機能
- **Trade-offs**: 構造変更時のマイグレーションが必要
- **Follow-up**: バージョン管理による設定マイグレーション対応

## Risks & Mitigations
- **Risk 1**: ゲームパッチによるアドオン構造変更 — FFXIVClientStructsの更新を追従、パッチノートを監視
- **Risk 2**: 自動化によるBAN懸念 — ゲームUI経由の操作に限定、人間らしい遅延を導入
- **Risk 3**: マルチスレッド競合 — ゲームスレッドでのみUI操作を実行（Framework.RunOnFrameworkThread）

## References
- [Dalamud API v14 Release Notes](https://dalamud.dev/versions/v14/) — API変更点とマイグレーションガイド
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) — ゲームメモリ構造の定義
- [AutoDuty Plugin](https://github.com/ffxivcode/AutoDuty) — マテリア精製・分解実装の参考
- [Dalamud Plugin Development](https://dalamud.dev/category/plugin-development/) — 公式開発ガイド
- [InventoryTools](https://github.com/Critical-Impact/InventoryTools) — インベントリ操作の参考

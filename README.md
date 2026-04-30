<!-- Path: README.md -->
<!-- Description: プラグインの概要と導入手順を説明する -->
<!-- Reason: 第三者が内容と導入方法を理解できるようにするため -->
<!-- RELEVANT FILES: projects/XIV-Mini-Util/XivMiniUtil.csproj, projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs -->
# XIV Mini Util

FFXIV用のDalamudプラグインです。

マテリア精製と分解の操作支援、NPC販売場所の検索、外部マーケット最安値確認、日課チェックリスト、シャキ通知を提供します。

## 機能

- マテリア精製の自動実行とオン・オフ
- 分解の条件設定と警告ダイアログ
- アイテム販売場所の検索（マップピン・テレポ）
- Universalis APIによる現在DC内のマーケット最安値確認
- 日課チェックリスト（Daily/Weekly、通知設定）
- コンテンツ突入確認時のWindows通知音

## インストール（カスタムプラグイン）

現在の配布系統は次の2つです。

- Stable: `0.3.0` / Dalamud API 14
- Testing準備中: `0.3.1` / Dalamud API 15

API15版はTesting先行の準備中です。`v0.3.1` のGitHub Release assetを公開するまでは、公開中の `pluginmaster.json` はStable/Testingともに既存のAPI14版を指します。

1. `pluginmaster.json` をRawで公開します。  
   例: `https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json`

2. Dalamud の「Custom Plugin Repositories」にURLを追加します。

3. `XivMiniUtil.zip` をGitHub Releasesに添付して配布します。

配布手順の詳細は `docs/release/custom-plugin-distribution.md` を参照してください。

## 使い方（基本）

- `/xivminiutil` または `/xmu` でメインウィンドウを開きます。
- `/xivminiutil config` または `/xmu config` で設定タブを開きます。
- `/xivminiutil diag` または `/xmu diag` で診断レポートを出力します。

## 販売場所検索の使い方

1. アイテムを右クリックして「販売場所を検索」を選びます。

2. 検索結果が複数ある場合、一覧ウィンドウが開きます。  
   上位10件のみ表示されます。

3. 行をクリックするとマップピンが刺さります。  
   テレポボタンは上位10件のみ表示されます。

4. 設定タブからショップデータの再構築と進捗確認ができます。

## 外部マーケット検索の使い方

1. アイテムを右クリックして「Universalisで最安値確認」を選びます。

2. 現在いるDC内の最安出品がチャットに表示されます。表示される価格は税抜の単価です。

## 設定のポイント

- 「検索時/マップピン時に自動テレポ」をONにすると、  
  最寄りのエーテライトへ自動テレポします。
- 「ショップデータを再構築」からキャッシュを手動更新できます。
- Checklistタブで日課項目の完了管理と通知時刻を設定できます。
- Settingsタブの「シャキ通知」から通知音と再生時間を設定できます。

## 開発者向け（必要な場合のみ）

```powershell
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release
```

現在のソースツリーはTesting API15版のビルド用です。Stable API14版を再生成する場合は、`v0.3.0` タグなどAPI14設定が残るソースからビルドします。

## 注意事項

- Stableは Dalamud API 14 / .NET 10 向けです。
- Testing API15版は準備中です。公開用 `pluginmaster.json` のTesting側は、`v0.3.1` のRelease asset公開後に切り替えます。
- 自動操作は自己責任で使用してください。

## License

MIT

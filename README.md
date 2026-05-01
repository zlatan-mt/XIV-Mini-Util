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

現在の配布版は次の通りです。

- Stable: `0.3.3` / Dalamud API 15
- Testing: `0.3.3` / Dalamud API 15

`0.3.0 / API14` は過去リリースとして残しています。

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

現在のソースツリーはStable API15版のビルド用です。旧Stable API14版を再生成する場合は、`v0.3.0` タグなどAPI14設定が残るソースからビルドします。

## 注意事項

- Stable / Testing ともに Dalamud API 15 / .NET 10 向けです。
- 自動操作は自己責任で使用してください。

## License

MIT

<!-- Path: README.md -->
<!-- Description: プラグインの概要と導入手順を説明する -->
<!-- Reason: 第三者が内容と導入方法を理解できるようにするため -->
<!-- RELEVANT FILES: projects/XIV-Mini-Util/XivMiniUtil.csproj, projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs -->
# XIV Mini Util

FFXIV用のDalamudプラグインです。

マテリア精製と分解の操作支援、NPC販売場所の検索を提供します。

## 機能

- マテリア精製の自動実行とオン・オフ
- 分解の条件設定と警告ダイアログ
- アイテム販売場所の検索（マップピン・テレポ）

## インストール（カスタムプラグイン）

1. `pluginmaster.json` をRawで公開します。  
   例: `https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json`

2. Dalamud の「Custom Plugin Repositories」にURLを追加します。

3. `XivMiniUtil.zip` をGitHub Releasesに添付して配布します。

配布手順の詳細は `docs/custom-plugin-distribution.md` を参照してください。

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

## 設定のポイント

- 「検索時/マップピン時に自動テレポ」をONにすると、  
  最寄りのエーテライトへ自動テレポします。

## 開発者向け（必要な場合のみ）

```bash
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release
```

ビルド後、`%APPDATA%\XIVLauncher\devPlugins\XivMiniUtil\` に出力されます。

## 注意事項

- Dalamud API 14 / .NET 10 向けです。
- 自動操作は自己責任で使用してください。

## License

MIT

<!-- Path: README.md -->
<!-- Description: プラグインの概要と導入手順を説明する -->
<!-- Reason: 第三者が内容と導入方法を理解できるようにするため -->
<!-- RELEVANT FILES: projects/XIV-Mini-Util/XivMiniUtil.csproj, projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs -->
# XIV Mini Util

FFXIV用のDalamudプラグインです。

マテリア精製と分解の自動操作、アイテム販売場所の検索を提供します。

## 機能

- マテリア精製の自動実行とオン・オフ
- 分解の条件設定と警告ダイアログ
- アイテム販売場所の検索（マップピン・テレポ）

## 使い方

- `/xivminiutil` または `/xmu` でメインウィンドウを開きます。
- `/xivminiutil config` または `/xmu config` で設定タブを開きます。
- `/xivminiutil diag` または `/xmu diag` で診断レポートを出力します。

## 開発ビルド

```bash
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release
```

ビルド後、`%APPDATA%\XIVLauncher\devPlugins\XivMiniUtil\` に出力されます。

## カスタムプラグイン配布

1. `pluginmaster.json` をRawで公開します。  
   例: `https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json`

2. Dalamud の「Custom Plugin Repositories」にURLを追加します。

3. `XivMiniUtil.zip` をGitHub Releasesに添付して配布します。

配布手順の詳細は `docs/custom-plugin-distribution.md` を参照してください。

## 注意事項

- Dalamud API 14 / .NET 10 向けです。
- 自動操作は自己責任で使用してください。

## License

MIT

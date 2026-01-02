<!-- Path: docs/custom-plugin-distribution.md -->
<!-- Description: カスタムプラグインとして配布する手順をまとめる -->
<!-- Reason: リリース作業を再現可能にするため -->
<!-- RELEVANT FILES: pluginmaster.json, README.md, projects/XIV-Mini-Util/XivMiniUtil.csproj -->
# カスタムプラグイン配布手順

## 前提

- GitHub Releases に配布zipを置く
- `pluginmaster.json` をRawで公開する

## 手順

1. Releaseビルドを行う  
   `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`

2. 出力物をzip化する  
   例: `projects/XIV-Mini-Util/bin/Release/net10.0-windows/` 内の  
   `XivMiniUtil.dll` と `XivMiniUtil.json` を `XivMiniUtil.zip` にまとめる

3. GitHub Releases に `XivMiniUtil.zip` を添付する  
   タグは `v0.1.0` のようにSemVerで運用する

4. `pluginmaster.json` の内容を更新する  
   - `AssemblyVersion`
   - `DownloadLink`
   - `Changelog`
   - `LastUpdate`

5. README の配布手順を更新する

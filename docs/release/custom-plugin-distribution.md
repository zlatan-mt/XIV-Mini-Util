# カスタムプラグイン配布手順

## 目的
`XIV Mini Util` を Dalamud のカスタムプラグインとして配布する際の最小手順をまとめます。

## 前提
- `projects/XIV-Mini-Util/XivMiniUtil.csproj` の `Version` と `AssemblyVersion` が配布対象の版になっている
  - 人間向けのリリース表記とGitタグは `0.3.2`
  - .NET / Dalamud が照合する `AssemblyVersion` は4要素の `0.3.2.0`
- `pluginmaster.json` を GitHub の Raw URL で公開できる
- GitHub Releases に `XivMiniUtil.zip` を添付できる
- Stable は `0.3.2 / Dalamud API 15` として扱う
- Testing は Stable と同じ `0.3.2 / Dalamud API 15` を指す

## 配布系統
- Stable
  - `pluginmaster.json` の `AssemblyVersion`, `DalamudApiLevel`, `DownloadLink`, `DownloadLinkInstall`, `DownloadLinkUpdate` を使う
  - 現在は `0.3.2 / API15`
  - 旧 `0.3.0 / API14` を再生成する場合は `v0.3.0` タグなどAPI14設定が残るソースからビルドする
- Testing
  - `pluginmaster.json` の `TestingAssemblyVersion`, `TestingDalamudApiLevel`, `DownloadLinkTesting` を使う
  - 現在はStableと同じ `0.3.2 / API15`

## Stable API15 手動手順
1. 必要なら `CHANGELOG.md` の `Unreleased` を整理する
2. `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release` でビルド確認する
3. Release 出力の `XivMiniUtil.dll`, `XivMiniUtil.json`, 追加依存DLL有無を確認する
4. 追加依存DLLがない場合は PowerShell で `XivMiniUtil.dll` と `XivMiniUtil.json` を zip 化する。追加依存DLLがある場合は成果物に含めるか SDK/packager 生成物を優先する
5. zip を展開し、`XivMiniUtil.json` の `InternalName`, `AssemblyVersion`, `DalamudApiLevel` が `XivMiniUtil`, `0.3.2.0`, `15` と一致することを確認する
6. GitHub Releases に対象バージョンのリリースを作成し、`XivMiniUtil.zip` を添付する
7. Release asset のURLが取得できることを確認してから、公開用 `pluginmaster.json` のStable/Testing側を更新する
   - `AssemblyVersion`: `0.3.2.0`
   - `TestingAssemblyVersion`: `0.3.2.0`
   - `DalamudApiLevel`: `15`
   - `TestingDalamudApiLevel`: `15`
   - `DownloadLink`: `https://github.com/zlatan-mt/XIV-Mini-Util/releases/download/v0.3.2/XivMiniUtil.zip`
   - `DownloadLinkInstall`: `https://github.com/zlatan-mt/XIV-Mini-Util/releases/download/v0.3.2/XivMiniUtil.zip`
   - `DownloadLinkUpdate`: `https://github.com/zlatan-mt/XIV-Mini-Util/releases/download/v0.3.2/XivMiniUtil.zip`
   - `DownloadLinkTesting`: `https://github.com/zlatan-mt/XIV-Mini-Util/releases/download/v0.3.2/XivMiniUtil.zip`
   - `Changelog`
   - `LastUpdate`
8. `pluginmaster.json` を push し、Raw URL で取得できることを確認する

## 旧Stable API14 手動手順
- 旧 `0.3.0 / API14` を再生成する場合は現在のAPI15ソースツリーを使わない。
- `v0.3.0` タグ、またはAPI14設定の保守ブランチをチェックアウトしてReleaseビルドする。
- API14版を再公開する場合は、`pluginmaster.json` のAPIレベルとURLをAPI14成果物に戻す必要がある。

## 補助スクリプト
- `scripts/release-build.sh`
  - WSL / Linux 互換の Release ビルドと `XivMiniUtil.zip` 生成用
  - Windows ネイティブ作業では PowerShell で Release 出力と manifest を確認してから zip 化する
- `/dalamud-release`
  - Claude 利用時に GitHub Release 作成と `pluginmaster.json` 更新をまとめて行う補助スキル
  - 実行前に `gh` CLI の認証状態を確認する

## 公開URL
- pluginmaster:
  - `https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json`
- Releases:
  - `https://github.com/zlatan-mt/XIV-Mini-Util/releases`

## 注意
- リリース前に `pluginmaster.json` のダウンロードURLが対象タグを指しているか確認する
- `DownloadLinkTesting` はGitHub Release assetが存在することを確認してから切り替える。先にpushしない
- Dalamud API 15 以降では zip 内 manifest が repository manifest で上書きされないため、zip 内の `XivMiniUtil.json` を必ず確認する
- リリース成果物やローカル検証記録は Git 管理せず、必要に応じて GitHub Releases や外部バックアップで扱う

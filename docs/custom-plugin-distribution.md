# カスタムプラグイン配布手順

## 目的
`XIV Mini Util` を Dalamud のカスタムプラグインとして配布する際の最小手順をまとめます。

## 前提
- `projects/XIV-Mini-Util/XivMiniUtil.csproj` の `Version` と `AssemblyVersion` が配布対象の版になっている
- `pluginmaster.json` を GitHub の Raw URL で公開できる
- GitHub Releases に `XivMiniUtil.zip` を添付できる

## 手動手順
1. 必要なら `CHANGELOG.md` の `Unreleased` を整理する
2. `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release` でビルド確認する
3. `bash scripts/release-build.sh` を実行し、リポジトリ直下に `XivMiniUtil.zip` を生成する
4. GitHub Releases に対象バージョンのリリースを作成し、`XivMiniUtil.zip` を添付する
5. `pluginmaster.json` の以下をリリース版に合わせて更新する
   - `AssemblyVersion`
   - `TestingAssemblyVersion`
   - `DownloadLink`
   - `DownloadLinkInstall`
   - `DownloadLinkUpdate`
   - `DownloadLinkTesting`
   - `Changelog`
   - `LastUpdate`
6. `pluginmaster.json` を push し、Raw URL で取得できることを確認する

## 補助スクリプト
- `scripts/release-build.sh`
  - Release ビルドを行い、`XivMiniUtil.zip` を生成する
- `.claude/skills/dalamud-release/scripts/release.sh`
  - GitHub Release 作成と `pluginmaster.json` 更新をまとめて行う補助スクリプト
  - 実行前に `gh` CLI の認証状態を確認する

## 公開URL
- pluginmaster:
  - `https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json`
- Releases:
  - `https://github.com/zlatan-mt/XIV-Mini-Util/releases`

## 注意
- リリース前に `pluginmaster.json` のダウンロードURLが対象タグを指しているか確認する
- `.kiro/` 配下は `.gitignore` 対象のため、仕様メモやローカル検証記録は別途共有手段を考える

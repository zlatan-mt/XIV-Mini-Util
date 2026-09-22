# カスタムプラグイン配布手順

## 配布先とバージョン

`XIV Mini Util` は、このリポジトリの `pluginmaster.json` と GitHub Releases の `XivMiniUtil.zip` で配布します。

- 通常版の識別名: `XivMiniUtil`
- 開発版の識別名: `XivMiniUtil.Dev`（Debugビルド）
- Stable / Testing: いずれも Dalamud API 15。現在は同じ成果物を配布する構成
- 公開済みの版: [最新のRelease](https://github.com/zlatan-mt/XIV-Mini-Util/releases/latest) と [mainの配布情報](https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json) を正とする
- 次回候補: 作業ブランチの `XivMiniUtil.csproj` と `pluginmaster.json` を参照。バージョンを更新しただけでは公開完了にならない

人間向けのバージョンとタグは `0.4.4` / `v0.4.4`、DLL・manifestは4要素の `0.4.4.0` のようにそろえます。

## 公開前の準備

1. 最新の公開タグからの差分を確認し、`CHANGELOG.md` とリリースノートへ利用者向けの変更点をまとめる。Devでの実機確認と、通常版・他の機能の未確認事項を区別する。
2. csprojの `Version` / `AssemblyVersion` と、`pluginmaster.json` のStable / Testingの版、APIレベル、4つのダウンロードURL、説明をそろえる。
3. Windowsでは次のコマンドでロジックテスト、Debug / Releaseビルド、差分検査をまとめて行う。

   ```powershell
   pwsh -NoProfile -File scripts/verify-refactor-phase.ps1
   ```

   Releaseパッケージだけを作る場合:

   ```powershell
   pwsh -NoProfile -File scripts/release-build.ps1
   ```

   これらのスクリプトでは `DevPluginOutputDir` を空にしてビルドするため、稼働中のプラグインは更新しない。通常のDebugビルドによるDev配置とは別の処理になる。

4. SDKが生成した `projects/XIV-Mini-Util/bin/Release/XivMiniUtil/latest.zip` を使用する。リリーススクリプトは同じ内容をルートの `XivMiniUtil.zip` にコピーする。DLLとJSONだけの手動ZIPを作り直さない。
5. ZIPのCRCと内容を検査する。最低限 `XivMiniUtil.dll`、`XivMiniUtil.json`、`XivMiniUtil.deps.json` が必要。追加依存DLLがある場合はSDK生成物に含まれていることも確認する。設定、ログ、診断レポート、Dev DLLを混入させない。
6. ZIP内のmanifest・DLL・配布情報で `InternalName=XivMiniUtil`、バージョン、`DalamudApiLevel=15` が一致することを確認する。配布ZIPのSHA-256とビルド対象commitを記録する。

Dalamud API 15ではZIP内manifest自体の整合が必要です。`pluginmaster.json` だけの修正で不一致を補わないでください。

## 公開する順序

準備が終わって公開を実施する際は、既存利用者が存在しないダウンロード先へ誘導されない順に進めます。

1. `CHANGELOG.md` の `Unreleased` を公開日に変更し、`pluginmaster.json` の `LastUpdate` を公開時のUnix秒へ更新する。最終commitとそのRelease ZIPを確定する。
2. 対象ブランチをpushし、mainへのPRを作成する。ZIPやローカル検証記録はcommitしない。
3. 最終commitにタグを付けてGitHub Releaseを公開し、検証済みの `XivMiniUtil.zip` を添付する。リリースノートは本文ファイルで渡す。

   ```powershell
   gh release create v0.4.4 XivMiniUtil.zip --target <最終commit> --title 'XIV Mini Util v0.4.4' --notes-file docs/release/v0.4.4-notes.md
   ```

4. Release assetを取得し、SHA-256とZIP内manifestがローカル成果物と一致することを確認する。
5. PRをmergeし、mainの `pluginmaster.json` をRaw URLから読み直す。Stable / Testingの版・APIレベル・4つのダウンロードURLを確認する。
6. 公開成果物の取得確認と、ゲーム内で通常版を導入・ロードした確認は別々に記録する。

`pluginmaster.json` のmainへの反映は、対象Release assetの取得確認後に行います。

## Dev版から通常版への切り替え

通常版とDev版は別の設定保存先を使います。通常版をインストールするだけではDev版の設定は引き継がれません。

1. 両方のプラグインを停止してから、`%APPDATA%/XIVLauncher/pluginConfigs` 内の `XivMiniUtil.Dev.json` / `XivMiniUtil.Dev` と、存在する場合は `XivMiniUtil.json` / `XivMiniUtil` をバックアップする。
2. Devの設定を引き継ぐ場合、`XivMiniUtil.Dev.json` を `XivMiniUtil.json` へコピーする。潜水艦データも引き継ぐ場合は `XivMiniUtil.Dev/submarine_data.json` を `XivMiniUtil/submarine_data.json` へコピーする。通常版の既存データと自動で合成はしないため、残す側を決めてから行う。
3. Devの診断ログや確認処理の復旧用journalは移さない。保存構図・エモート等の設定は設定JSONに含まれる。
4. カスタムリポジトリから `XIV Mini Util` を導入し、Dev版を無効のまま通常版を有効にする。Dev版の自動起動も無効にして、両方のhookが同時に動かないようにする。

この切り替えはパッケージ作成とは別作業です。元の設定とDev DLLは、通常版への切り替えが確認できるまで保持します。

## 補助スクリプトと旧版

- `scripts/release-build.ps1`: WindowsのReleaseビルドとSDK生成ZIPのコピー
- `scripts/release-build.sh`: WSL / Linuxの同じ処理。環境に応じて `DALAMUD_HOOKS_ROOT` を指定する
- 旧 `0.3.0 / API14` を再生成する場合は `v0.3.0` タグ等のAPI14ソースを使用する

## 公開URL

- [配布情報](https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json)
- [GitHub Releases](https://github.com/zlatan-mt/XIV-Mini-Util/releases)

# v0.4.1 Release Task

## Goal

`XIV Mini Util` の現在の public `main` を `v0.4.1` として GitHub Releases に公開し、Dalamud Custom Plugin Repository から Stable / Testing ともに `XivMiniUtil.zip` をダウンロード・更新できる状態にする。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は参照しない。

## Baseline

- task 作成時 public `main`: `2e71c9bc720057657345a60c2602c84652ef38a7`
- latest published release: `v0.4.0`
- current project version: `0.4.0` / `0.4.0.0`
- current Dalamud API level: `15`
- `v0.4.0` 以降の main 変更は PR #1 の通常 UI / developer entrypoint cleanup
- open PR: none at task creation

## Scope

1. `projects/XIV-Mini-Util/XivMiniUtil.csproj`
   - `Version` -> `0.4.1`
   - `AssemblyVersion` -> `0.4.1.0`
   - Dalamud API 15 / .NET 10 その他の build semantics は変更しない

2. `CHANGELOG.md`
   - `0.4.1 - 2026-09-01` を追加
   - 内容は v0.4.0 以降の利用者向け差分だけを簡潔に記載する
   - 主眼: 5タブの日本語化、設定画面の整理、重複/開発者向け導線の削減
   - Title Background / CharaSelect の安全ロジックや機能変更があったかのように書かない

3. `pluginmaster.json`
   - `AssemblyVersion` / `TestingAssemblyVersion` -> `0.4.1.0`
   - `DalamudApiLevel` / `TestingDalamudApiLevel` は `15` のまま
   - Stable / Testing の全 download URL -> `releases/download/v0.4.1/XivMiniUtil.zip`
   - `Changelog` を 0.4.1 の利用者向け内容へ更新
   - `LastUpdate` を release publish 時点に合わせて更新

4. `docs/release/custom-plugin-distribution.md`
   - 現在の Stable / Testing と手動手順の版表記・manifest確認例を `0.4.1 / 0.4.1.0` に更新
   - 既存 release workflow を維持し、新しい release framework / CI は追加しない

5. GitHub Release
   - tag: `v0.4.1`
   - release name: `XIV Mini Util v0.4.1`
   - asset: `XivMiniUtil.zip`
   - release notes は `CHANGELOG.md` の 0.4.1 と整合させる

## Release sequencing

`pluginmaster.json` の public `main` が存在しない asset を指す状態を避けるため、以下の順序を使う。

1. task branch 上で version / changelog / pluginmaster / release doc を完成させる。
2. minimal validation を完了する。
3. Sol Review で final PR HEAD を review し、A/B PASS を得る。
4. **review 済みの同一 HEAD** から Windows / Dalamud 環境で Release artifact を生成する。
5. `v0.4.1` GitHub Release を review 済み HEAD を target に作成し、`XivMiniUtil.zip` を upload する。
6. release asset の public download が成功し、zip / manifest が acceptance criteria を満たすことを確認する。
7. その後に同一 review HEAD の PR を merge する。可能なら通常 merge を使い、release tag の対象 commit を main ancestry に保持する。
8. post-merge で public `main` の `pluginmaster.json` と release download を再確認し、local `main` を同期する。

Release asset 作成後に PR HEAD が変わった場合、その artifact / review は stale とみなし、変更後 HEAD で validation・Sol Review・artifact をやり直す。

## Acceptance Criteria

- `XivMiniUtil.csproj` が `Version=0.4.1`, `AssemblyVersion=0.4.1.0`
- Release build が成功する
- release zip の実ファイル名が `XivMiniUtil.zip`
- zip 内に少なくとも `XivMiniUtil.dll` と `XivMiniUtil.json` が存在する
- 追加 runtime dependency が必要なら zip に含まれ、不必要な debug/local artifact は含まれない
- zip 内 `XivMiniUtil.json` の `InternalName=XivMiniUtil`, `AssemblyVersion=0.4.1.0`, `DalamudApiLevel=15`
- GitHub Release `v0.4.1` が公開され、`XivMiniUtil.zip` を認証なしでダウンロードできる
- public `main` の `pluginmaster.json` が Stable / Testing とも `0.4.1.0 / API15 / v0.4.1 asset` を指す
- `CHANGELOG.md`, `pluginmaster.json`, release notes, release doc の版・内容が整合する
- Title Background / CharaSelect / Materia / Desynth / Teleport / persistent data 等の production behavior は変更しない
- reviewed HEAD と release artifact source HEAD が一致する

## Validation

最小限として以下を実施する。

```powershell
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
powershell -ExecutionPolicy Bypass -File scripts/release-build.ps1
git diff --check
```

Release packageについて追加で確認する。

- `projects/XIV-Mini-Util/bin/Release/XivMiniUtil/latest.zip` を確認し、upload用に `XivMiniUtil.zip` として扱う
- zip 内容一覧
- zip 内 manifest の version / API level / internal name
- GitHub Release asset upload後の HTTP download 成功
- public Raw `pluginmaster.json` の download URL と version

今回の変更は release metadata / package publication が中心なので、TitleBackground real-game OneClick や通常UI smokeの再実行は、release prep中に production codeを変更しない限り要求しない。

## Out of Scope

- 新しい GitHub Actions / CI / CD の導入
- 自動 release framework の新設
- production feature / UI / native logic の変更
- Dalamud API level 更新
- Testing 専用版の分岐
- 旧 archive repository の参照
- v0.4.1 と無関係な refactor / docs cleanup

## Safety / Publication Rules

- credential、Webhook URL、character情報、local pathを artifact / log / release notesへ含めない
- zip は Release build 生成物だけを対象にする
- asset が存在しない URL を public pluginmaster が指す状態にしない
- Release publish / merge 前後で repository identity guard を再確認する
- Sol Review は `sol-review-bridge` を使用し、Luna / Sonnet を reviewer として使用しない

# XIV Mini Util UI / 開発用導線クリーンアップ Task

## Goal

Title Background / FRU 背景実装の開発過程で増えた診断UI、developer command、重複操作、不要な常時表示を整理し、通常利用しやすいUIへ戻す。新機能追加や全面再設計は行わない。

## Repository guard

- Canonical repository: `zlatan-mt/XIV-Mini-Util`
- Base: `main` at `f962c8932075f21de76508714aba3aaaf5c76b77`
- Task branch: `task/ui-development-cleanup`
- 実装・検証・PR操作の前に `git remote get-url origin`、`gh repo view --json nameWithOwner`、`git rev-parse origin/main` を確認する。

## Scope

- 5タブ構成を維持し、表示名とウィンドウタイトルの常時表示ノイズを整理する。
- Settingsを「分解」「ショップ検索」「チェックリスト」「シャキ通知」「ログイン背景」「キャラ選択」「潜水艦」「バックアップ」に整理する。
- Home、Checklist、Shop、Notification、Backupに重複している通常操作とdeveloper向け操作を削減する。
- Title Backgroundのcurated選択、OneClick verification、構図保存・初期化、最小限の状態表示を維持する。
- pre-login snapshot、safety gate、run-scoped判定、非永続probe、`PersistentApplyEnabled`、candidate一致、login/logout cleanup、native lifetime safetyを維持する。
- Character Select emoteの既存操作を日本語で整理し、backend semanticsとpreset機能は変更しない。
- 通常利用に不要なcommand entrypointと旧手動導線だけから参照されるdead helperを削除する。
- persisted config、Shop検索/teleport/Universalis、Materia/Desynth/Submarineの安全・保存semanticsは変更しない。

## Out of Scope

- 新機能、UI framework導入、全面的なImGui再設計
- Title Background native engineの再設計、FRU背景の再調査、新しいbackground preset
- Title Background production behavior、安全条件、OneClick契約の変更
- persisted data形式の変更、migration、Materia / Desynth / Teleportの安全条件変更
- Submarine保存・merge、Universalis / Discord通信、release infrastructureの変更
- 新しい網羅testやcoverage目的のtest追加
- 実ゲームでのFRU OneClick再検証

## Acceptance Criteria

- 通常SettingsからTitle Background developer診断ページが消えている。
- 通常利用しないTitle Background developer command群、Shop詳細ログ、シャキ通知遅延テスト、重複resetが通常導線から消えている。
- Settingsのバックアップが独立カテゴリにあり、既存export/import semanticsと確認フローが維持されている。
- Login Backgroundのcurated UI、OneClick安全経路、FRUの現在のproduction behaviorが維持されている。
- persisted data semanticsを変更せず、scope外の無関係refactorを含めない。
- public mainをbaseとしたDraft PRが作成される。

## Validation

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

実ゲームUI smokeとFRU OneClick再実行はこのtaskの自動検証には含めず、未確認範囲として報告する。

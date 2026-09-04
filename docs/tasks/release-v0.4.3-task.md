# v0.4.3 Release Task

## Goal

`XIV Mini Util` の最新 public `main` を `v0.4.3` として GitHub Releases に公開し、Dalamud Custom Plugin Repository の Stable / Testing を同版へ更新する。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。

## Baseline

- task 作成時 public `main`: `c69579db02775cce4916eb2558df703b4f61ffab`
- latest published release: `v0.4.2`
- current project version: `0.4.2` / `0.4.2.0`
- current Dalamud API level: `15`
- task branch: `task/release-v0.4.3`
- open PR at task creation: none

## Release content since v0.4.2

### User-facing

PR #3 `FRU clear-stage visual polish` is merged and already real-game validated / Sol-reviewed.

Release note should focus on:
- `FRU クリア後ステージ` の固定時刻を 15:17 ET から **13:00 ET** (`DayTimeSeconds=46800`) へ調整
- Clear Skies / character placement / camera / static anchor / suppression behavior is unchanged
- real-game validation: background / character / camera PASS, no white-out, login normal, no post-login write/scene leak
- VFX activation was not added; VFX writes remain 0

Do not describe read-only VFX diagnostics as a new end-user feature.

### Internal only

PR #6 reorganized `AGENTS.md` / Title Background agent documentation. It changed no production behavior and does not need a user-facing changelog bullet beyond optional internal maintenance wording.

## Scope

1. `projects/XIV-Mini-Util/XivMiniUtil.csproj`
   - `Version` -> `0.4.3`
   - `AssemblyVersion` -> `0.4.3.0`
   - keep Dalamud API 15 / .NET 10 unchanged

2. `CHANGELOG.md`
   - add `0.4.3 - 2026-09-02`
   - summarize only meaningful user-facing changes since v0.4.2
   - keep `Unreleased` as `- なし`

3. `pluginmaster.json`
   - `AssemblyVersion` / `TestingAssemblyVersion` -> `0.4.3.0`
   - API levels remain 15
   - Stable / Testing download URLs -> `releases/download/v0.4.3/XivMiniUtil.zip`
   - update `Changelog` for the FRU visual adjustment
   - set `LastUpdate` when the release is actually published

4. `docs/release/custom-plugin-distribution.md`
   - update current Stable / Testing version references and manifest examples to `0.4.3 / 0.4.3.0`
   - do not redesign the release process

5. GitHub Release
   - tag: `v0.4.3`
   - name: `XIV Mini Util v0.4.3`
   - asset: `XivMiniUtil.zip`
   - release notes consistent with `CHANGELOG.md`

## Out of Scope

- production C# behavior changes other than version metadata
- further FRU time/VFX/lighting tuning
- new UI/configuration
- API level changes
- release workflow / CI redesign
- unrelated refactoring

## Release sequencing

Preserve the existing safe release order so public `pluginmaster.json` never points to a missing asset.

1. Complete version/changelog/pluginmaster/release-doc changes on this branch.
2. Run minimal validation.
3. Final Sol Review against the exact PR HEAD; do not use Luna/Sonnet as reviewer.
4. From that exact reviewed HEAD, generate the Release artifact.
5. Verify zip contents/manifest.
6. Publish GitHub Release `v0.4.3` and upload `XivMiniUtil.zip`.
7. Verify the public asset is downloadable.
8. Merge the same reviewed HEAD to `main`.
9. Re-check public `main` pluginmaster and release asset after merge.

If PR HEAD changes after review or artifact generation, treat review/artifact as stale and redo the affected validation/review/artifact steps.

## Acceptance Criteria

- project version is `0.4.3` / `0.4.3.0`
- Dalamud API remains 15
- Release build succeeds
- `XivMiniUtil.zip` contains at least `XivMiniUtil.dll` and `XivMiniUtil.json` plus any required runtime dependencies
- zip manifest reports `InternalName=XivMiniUtil`, `AssemblyVersion=0.4.3.0`, `DalamudApiLevel=15`
- GitHub Release `v0.4.3` exists with downloadable `XivMiniUtil.zip`
- public `main` pluginmaster Stable / Testing both point to `v0.4.3`
- changelog, pluginmaster, release notes, release doc versions are consistent
- no production behavior change is introduced by the release task
- reviewed HEAD and release artifact source HEAD match

## Validation

Minimum:

```powershell
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
powershell -ExecutionPolicy Bypass -File scripts/release-build.ps1
git diff --check
```

Inspect the generated release zip and `XivMiniUtil.json` explicitly. Do not run a new real-game FRU validation solely for release metadata: PR #3 already has same-feature real-game PASS; this task must not change its production logic.

## External / paid API

- No paid API is involved.
- GitHub Release upload/download verification is authorized as part of this user-requested publication task.
- Universalis / Discord live calls are unnecessary and must not be used.

## Review / merge

Final implementation review: Sol Review only.

Do not create a replacement PR for implementation/review/repair. Continue this single task branch and PR through merge.

# Fresh-Install Character Visibility Task

## Goal

公開版 v0.4.1 の fresh install / 未promotion config で curated Title Background を選んだ際、FRU 背景は出るのに selected character が表示されない可能性がある問題を修正する。

対象は **既に実ゲームで検証済みの FRU approved static anchor production placement を、ユーザーごとの OneClick promotion を前提にせず通常preset選択から利用可能にすること**。

Canonical repository は `zlatan-mt/XIV-Mini-Util` のみ。旧 archive repository は一切参照しない。

## Baseline

Task作成時 main:
`d086ee8d6ebf817f5b25073abbc9ab361d2cbeb6`

関連active task:
- PR #3 `FRU clear-stage visual polish` は別目的。混ぜない。

Observed production symptom:
- friend using public `0.4.1.0`: background selected but character reportedly not visible.

Source-level explanation:
- `TitleBackgroundCharaSelectPlacementEnabled` default is false.
- normal curated preset selection calls `ApplySimpleAutoSetup`.
- current `ApplySimpleAutoSetup` enables placement only when an already-promoted persistent placement is preserved; otherwise it falls back to V2 and leaves placement disabled.
- FRU candidate itself is already `BackgroundUsable=true`, `CharacterExpectedVisible=true`, `VerifiedInGame=true`, has `ApprovedStaticAnchor=(100,0,100)`, explicit territory 1238 / layer 0, and prior OneClick real-game PASS.
- runtime static-anchor path already re-authorizes current pre-login n4gw scene/layout before using `(100,0,100)` and never falls back to that anchor for the wrong scene.

Therefore a fresh config can miss the production placement engine even though the FRU placement source is already globally verified and runtime-authorized.

## Decisions

### 1. Do not fake OneClick promotion

Do not set `TitleBackgroundCharaSelectPlacementPositionCaptured=true` merely because FRU is selected.

`PositionCaptured` remains user/run promotion semantics. Do not fabricate capture evidence or persist a fake proof.

### 2. Enable only verified approved-static placement

Add a small pure decision helper for normal preset setup. Approved-static production placement is eligible only when the selected candidate:

- is a curated candidate
- `BackgroundUsable == true`
- `CharacterExpectedVisible == true`
- `VerifiedInGame == true`
- `ApprovedStaticAnchor.HasValue == true`
- approved anchor is finite
- `RequiresSourceBackedLayout == false`

Current registry should make this true for FRU only.

Do **not** broadly enable placement for all curated backgrounds:
- Il Mheg remains on its existing path (`CharacterExpectedVisible=false`).
- Elpis remains source-backed / OneClick-dependent and unverified.

### 3. Reuse existing runtime safety

For eligible approved-static candidates, `ApplySimpleAutoSetup` may enable the existing TitleEdit-informed placement owner and set the matching placement candidate id, but the runtime target position must continue to come from `BuildCurrentCharaSelectLocationModel()` -> current-session `TryGetAuthorizedAnchor()`.

Do not persist `(100,0,100)` into fresh config as captured placement.

Rotation remains the existing config/default rotation path; fresh config default `0f` matches the previously verified FRU proof. Existing promoted config rotation must be preserved when already valid.

### 4. Existing persistent placement wins

If the current candidate already has a valid promoted persistent placement, preserve it exactly as today.

The new approved-static route is the fallback for a verified static-anchor candidate when user-specific promotion does not yet exist.

## Minimal implementation

Primary expected change:
- `TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(...)`
- optionally one small pure helper in the existing Title Background logic file
- targeted logic tests

Expected behavior:

```text
usePlacement = preservePersistentPlacement || approvedStaticProductionPlacement
TitleBackgroundCharaSelectPlacementEnabled = usePlacement
TitleBackgroundV2Enabled = !usePlacement
```

When `approvedStaticProductionPlacement` is used without promotion:
- keep `TitleBackgroundCharaSelectPlacementPositionCaptured == false`
- keep captured XYZ unset/zero
- set/retain matching `TitleBackgroundCharaSelectPlacementCandidateId`
- runtime authorizes and supplies approved anchor from candidate metadata

Do not add new UI, commands, config toggles, native hooks, or generic placement framework.

## Safety invariants

Do not weaken:
- pre-login only write
- CharaSelect map/session checks
- canonical actor resolver
- same scene generation checks
- static-anchor scene/layout authorization
- exact FRU n4gw territory 1238 / explicit layer 0 checks
- capture proof / write authorization / bounded retry / readback
- stop on login / session end / unload
- no native pointer persistence
- `PersistentApplyEnabled` semantics

Wrong scene, wrong territory/layer, unresolved character, hook-not-ready, or post-login must still produce no placement write.

## Acceptance Criteria

1. Fresh `Configuration` + FRU normal preset setup:
   - override enabled
   - placement enabled
   - V2 disabled
   - placement candidate = FRU
   - `PositionCaptured` remains false
   - no fake persisted XYZ/proof is created
2. Existing promoted FRU placement remains preserved.
3. Il Mheg behavior remains unchanged; no new approved-static placement is enabled.
4. Elpis remains source-backed/OneClick-dependent; no static placement is invented.
5. Switching away from FRU does not leave FRU placement owner active for another candidate.
6. Runtime static-anchor authorization remains the only source of FRU target position.
7. No post-login / wrong-scene write path is introduced.

## Validation

Minimum automated validation:

```powershell
dotnet run --project tools/CharaSelectLogicTests
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
git diff --check
```

Add only targeted tests for:
- fresh FRU preset enables approved-static production placement without fake promotion
- promoted FRU remains preserved
- Il Mheg / Elpis do not inherit FRU behavior
- candidate switch clears/disables mismatched placement as existing semantics require

Real-game:
- placement engine itself and FRU anchor have already passed OneClick in real game.
- if a new real-game check is needed, keep the existing 1-click contract; do not ask for developer toggles/manual probes.
- friend confirmation on a fresh/unpromoted 0.4.1-style config is useful after the code path is validated, but do not weaken merge safety based only on anecdotal confirmation.

## Out of Scope

- FRU VFX/brightness changes (PR #3)
- Elpis promotion
- Il Mheg character redesign
- camera redesign
- generic per-candidate placement framework
- configuration migration that marks users as OneClick-verified
- release/version bump (release task after fixes are merged)

## Review

Final review: Sol Review only.

MUST FIX only for reachable crash/freeze, wrong-scene/native writes, stale pointers, safety gate regression, persistent config corruption, character still not placed on the intended fresh FRU path, or reviewed-HEAD mismatch.
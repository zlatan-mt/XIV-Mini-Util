# Title Background Character Select Bright Candidates

## Current status

Phase2N background-only MVP is complete.

`custom:n4f4` is the verified Dark / BackgroundOnly baseline candidate. It is not a real preset; it is a synthetic compatibility id for the current custom n4f4 override target.

Expected behavior remains:

- Character Select UI and character name are visible.
- The selected character model is expected to remain hidden.
- The background is usable, but dark.

## Baseline candidate

Required fields for the current baseline:

```text
id=custom:n4f4
displayName=Custom n4f4 override target
territoryPath=ex3/01_nvt_n4/fld/n4f4/level/n4f4
territoryId=816
layerFilterKey=51
expectedCompatibility=BackgroundOnly
expectedBrightness=Dark
backgroundUsable=True
characterExpectedVisible=False
verifiedInGame=True
```

## Adding candidates

Add a new entry only when all of these are known from repository data or a real-game diagnostic capture:

- `id`
- `displayName`
- `territoryPath`
- `territoryId`
- `layerFilterKey`
- `expectedCompatibility`
- `expectedBrightness`
- `backgroundUsable`
- `characterExpectedVisible`
- `verifiedInGame`
- `warning`
- `knownIssue`
- `recommendedAction`

If the candidate has not been verified in game, set `verifiedInGame=False` and include `Unverified` or `Experimental` in the UI label. Do not make an unverified candidate the default.

Do not invent `territoryPath`, `territoryId`, or `layerFilterKey` from visual guesses.

## In-game test

1. Select the candidate from `Character Select background candidate`.
2. Keep `selectedPresetId=none` for custom candidates.
3. Enter Character Select and run `/xmutbgdiag`.
4. Confirm `phase2N.mvpStatus=complete-background-only`.
5. Confirm `phase2N.deliveryVerdict=working-background-only`.
6. Confirm `phase2N.overrideCandidate.selectedId=<candidate id>`.
7. Confirm `phase2N.overrideCandidate.verifiedInGame` matches the candidate metadata.
8. Confirm `phase2N.compatibility.backgroundUsable=True`.
9. Confirm `phase2N.compatibility.characterExpectedVisible=False`.
10. Confirm login transition safety remains safe.

## Screenshot comparison

Capture one screenshot with `custom:n4f4`, then one with the candidate being tested.

Compare:

- background readability behind the character list
- character name readability
- lobby UI readability
- absence of login-transition leakage
- whether the selected character model remains hidden as expected

If the candidate is brighter and stable across repeated runs, update `verifiedInGame=True`.

## Expected diagnostics

With only the baseline candidate:

```text
phase2N.overrideCandidate.selectedId=custom:n4f4
phase2N.overrideCandidate.displayName=Custom n4f4 override target
phase2N.overrideCandidate.verifiedInGame=True
phase2N.overrideCandidate.expectedCompatibility=BackgroundOnly
phase2N.overrideCandidate.expectedBrightness=Dark
phase2N.overrideCandidate.backgroundUsable=True
phase2N.overrideCandidate.characterExpectedVisible=False
phase2N.overrideCandidate.availableCount=1
phase2N.overrideCandidate.available[0].id=custom:n4f4
phase2N.lighting.brightLayerCandidates=none
phase2N.lighting.recommendedAction=add-bright-override-candidate
```

After safe bright candidates are added:

```text
phase2N.lighting.brightLayerCandidates=<candidate ids>
phase2N.lighting.recommendedAction=try-bright-custom-target
```

Always preserve:

```text
transition.verdict.loginTransitionSafety=safe
transition.phase2G.appliedAfterLogin=False
transition.sceneOverride.active=False
```

## Prohibited changes

Do not add or restore:

- `Framework.Update` camera maintenance
- direct `SceneCamera.Position` writes
- direct `SceneCamera.LookAtVector` writes
- direct `SceneCamera.FoV` writes
- per-frame yaw, pitch, or distance correction
- default-path actor `Position` writes
- default-path actor `Rotation` writes
- ObjectTable ambiguous candidates as actors
- ObjectTable zero-transform stubs as actors
- post-login ObjectTable as CharaSelect preview source
- unsafe lighting or environment writes
- default-on experimental writes

## Rollback

To rollback a bad candidate, remove it from `TitleBackgroundCharacterSelectOverrideCandidateRegistry.All` or set the UI selection back to `custom:n4f4`. The baseline candidate must remain available.

## Phase2P Manual Candidate Slots

Phase2N background-only MVP is complete, and Phase2O added the candidate registry. `custom:n4f4` is verified in game as Dark / BackgroundOnly and remains the default.

Phase2P adds a default-off manual candidate slot so a new bright background-only target can be tested without a rebuild. The selected character model remains hidden because full scene override replaces the lobby scene. ObjectTable remains rejected because it is not a valid CharaSelect preview actor source in the current diagnostics.

This phase intentionally avoids actor, camera, lighting, and environment writes. It only changes candidate metadata, config, UI selection, and diagnostics.

### Production candidate rules

Add production registry candidates only when a safe source provides all required fields:

- `territoryPath`
- `territoryId`
- `layerFilterKey`
- `expectedCompatibility`
- `expectedBrightness`
- `verifiedInGame`
- source/reason for the data

Do not guess a candidate from screenshots or visual similarity. If brightness is not confirmed, use `ExpectedBrightness=Unknown`. If the candidate is not tested in game, keep `VerifiedInGame=False` and label it as unverified.

No safe production bright candidate is currently present in this repository, so none is added.

### Manual slot usage

1. Open Title Background settings.
2. Enable `Manual candidate slot 1`.
3. Enter `Territory path`, `Territory id`, and `Layer filter key`.
4. Set `Expected brightness` to `Unknown`, `Dark`, or `Bright`.
5. Select `Manual candidate slot 1` in `Character Select background candidate`.
6. Re-enter Character Select if needed.
7. Run `/xmutbgdiag`.
8. Capture a screenshot.

Manual candidates are always unverified by default. If a manual candidate is good, promote it to the production registry in a later code change after human confirmation.

### One-pass verification checklist

```text
1. Select Custom n4f4 override target and capture baseline SS/log.
2. Enable manual slot if testing a new candidate.
3. Enter territoryPath / territoryId / layerFilterKey.
4. Select manual candidate.
5. Re-enter Character Select if needed.
6. Run /xmutbgdiag.
7. Capture SS.
8. Check:
   - background appears
   - UI appears
   - selected character remains hidden
   - no login transition leak
   - candidate id/source/brightness are correct
9. If good, mark candidate as VerifiedInGame only in a later code change after human confirmation.
```

### Phase2P diagnostics

Baseline:

```text
phase2N.mvpStatus=complete-background-only
phase2N.deliveryVerdict=working-background-only
phase2N.overrideCandidate.selectedId=custom:n4f4
phase2N.overrideCandidate.source=registry
phase2N.overrideCandidate.displayName=Custom n4f4 override target
phase2N.overrideCandidate.verifiedInGame=True
phase2N.overrideCandidate.expectedBrightness=Dark
phase2N.lighting.brightLayerCandidates=none
transition.verdict.loginTransitionSafety=safe
transition.phase2G.appliedAfterLogin=False
transition.sceneOverride.active=False
```

Manual candidate disabled:

```text
phase2N.overrideCandidate.manualSlot[1].enabled=False
phase2N.overrideCandidate.manualSlot[1].valid=False
phase2N.overrideCandidate.manualSlot[1].validationError=disabled
```

Manual candidate enabled and valid:

```text
phase2N.overrideCandidate.manualSlot[1].enabled=True
phase2N.overrideCandidate.manualSlot[1].valid=True
phase2N.overrideCandidate.manualSlot[1].id=manual:slot1
phase2N.overrideCandidate.manualSlot[1].verifiedInGame=False
```

Manual bright candidate selected:

```text
phase2N.overrideCandidate.selectedId=manual:slot1
phase2N.overrideCandidate.source=manual
phase2N.overrideCandidate.verifiedInGame=False
phase2N.overrideCandidate.expectedBrightness=Bright
phase2N.lighting.brightLayerCandidates=manual:slot1
phase2N.lighting.recommendedAction=verify-manual-bright-candidate
```

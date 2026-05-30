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

<!-- Path: docs/notes/title-background-phase2m-placement-ground-plan.md -->
<!-- Description: Phase 2M plan for character-select placement and ground-height correction -->
<!-- Reason: Plan Phase 2M without implementing behavior changes before review -->

# Title Background Phase 2M Placement/Ground Plan

## Status

This is a planning document only. Do not implement Phase 2M behavior changes until this plan is reviewed.

Phase 2L appears successful for login-transition cleanup. The latest post-login diagnostics show:

- `transition.sceneOverride.active=False`
- `transition.sceneOverride.cleanupReason=world-login-transition`
- `transition.sceneOverride.activeAfterLoginDetected=False`
- `transition.adapter.staleAfterLoginDetected=False`
- `transition.phase2G.appliedAfterLogin=False`
- `transition.verdict.postLoginPhase2GStillApplying=False`
- `transition.verdict.postLoginSceneReadyAccepted=False`
- `transition.verdict.staleCharaSelectStateAfterLogin=False`
- `transition.verdict.loginTransitionSafety=safe`

The remaining issue is visual: Il Mheg loads, but character height/ground alignment is wrong and the character is missing or out of frame.

## Most Likely Root Cause

The likely root cause is coordinate-space mismatch between the original CharaSelect actor/camera assumptions and the overridden Il Mheg scene.

Current behavior overrides the scene path/territory/layer and derives camera runtime pose from preset camera/focus coordinates, but it does not prove that the character-select actor is spawned at a valid Il Mheg floor position. The preset already stores `CharacterPosition` and `CharacterRotation`, but current Phase 2G work primarily uses the preset focus/camera to rebuild camera pose and generated LookAtY curve. It does not yet establish a reliable character placement strategy for arbitrary overridden maps.

Phase 2L makes this clearer: the broken visual is no longer explained by stale post-login CharaSelect state. It is more likely a placement/ground/camera-anchor problem inside the active character-select scene.

## Exact Files And Functions To Inspect

Primary runtime hub:

- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs`
  - `CreateSceneDetour(...)`: overrides CharaSelect scene path, territory id, and layer filter.
  - `LoadLobbySceneDetour(GameLobbyType mapId)`: records runtime camera state before scene reload and increments scene generation.
  - `LobbySceneLoadedDetour(nint thisPtr)`: sceneReady signal path; calls restore/apply/capture after acceptance.
  - `RecordCharaSelectRuntimeCameraStateBeforeSceneReload(GameLobbyType mapId)`: currently records preset-derived camera runtime state, not actor placement.
  - `TryBuildPresetCameraRuntimePose(...)`: derives yaw/pitch/distance/LookAtY from configuration camera/focus.
  - `RestoreCharaSelectRuntimeCameraStateAfterSceneLoad()`: one-shot camera restore path; do not convert this to per-frame maintenance.
  - `ApplyCharaSelectCameraCurveAfterSceneLoad()`: applies generated curve once after scene load.
  - `CapturePhase2CTimelineFrame(int frame)`: existing timeline capture point for active/lobby/expanded camera state.
  - `TryCaptureActiveCameraSnapshot(...)`, `TryCaptureLobbyCameraSnapshot(...)`, `TryCaptureExpandedLobbyCameraSnapshot(...)`: existing camera diagnostic read paths.
  - `BuildTransitionDiagnosticSummaryLines(...)` and `BuildFailureSummary(...)`: summary/failure-only reporting locations.

Preset and configuration model:

- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPreset.cs`
  - currently has `TerritoryPath`, `TerritoryTypeId`, `LayoutTerritoryTypeId`, `LayoutLayerFilterKey`, `CharacterPosition`, `CharacterRotation`, camera coordinates, focus coordinates, `FovY`, weather/time/BGM.
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraCaptureDraft.cs`
  - `TitleBackgroundCameraCapturePresetBuilder`: captures/keeps `CharacterPosition` and `CharacterRotation`.
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraCaptureService.cs`
  - `Capture(...)`: captures logged-in territory, active camera/focus, player position/rotation, and nearest layer filter key.
  - `TryCaptureCharacterPosition(...)`: likely source for preset character anchor.
  - `TryResolveNearestLayerFilterKey(...)`: relevant for map layer compatibility.
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPresetApplicator.cs`
  - applies preset values into `Configuration`.
- `projects/XIV-Mini-Util/Configuration.cs`
  - `TitleBackgroundCharacterPositionX/Y/Z`, `TitleBackgroundCharacterRotation`, camera/focus fields, territory/layer fields.

Pure logic and tests:

- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraLogic.cs`
  - keep Phase 2G gate constraints intact.
  - add any new anchor/diagnostic math here when possible.
- `tools/CharaSelectLogicTests/Program.cs`
  - add tests for anchor selection, ground-offset math, non-blocking diagnostics, and safety verdicts.

## Is Character Actor Transform Currently Controllable?

Not proven.

Current code captures a preset character position from the logged-in world and stores it in configuration/preset data. It also uses `CharacterPosition.Y` to build the generated curve via `TitleBackgroundCharaSelectCameraInput` and `TitleBackgroundCharaSelectCameraLogic.BuildCurve(...)`.

However, the current inspected path does not show an explicit write to the CharaSelect actor transform. The scene override path changes environment loading, and the camera path is preset-derived, but actor placement appears to remain controlled by the native character-select flow unless a separate transform hook/write path exists elsewhere.

Phase 2M should therefore start with diagnostics that prove:

- which game object represents the CharaSelect actor,
- whether it is visible,
- where it is at sceneReady and stabilization frames,
- whether its Y coordinate matches the overridden scene ground.

Only after that should Phase 2M decide whether actor transform is controllable directly, indirectly through native CharaSelect placement data, or not safely controllable.

Candidate actor identification should be conservative. Start by recording object-table candidates rather than writing to them:

- object id / entity id / address if available,
- object kind and name if available,
- distance from configured `CharacterPosition`,
- distance from active camera lookAt,
- whether the object is player-like or battle-character-like,
- whether the candidate appears and remains stable across frames `0, 30, 60, 120, 300, 600`.

If multiple candidates match, diagnostics should report `ambiguous` and Phase 2M should not write actor placement.

## Recommended Strategy

Use Option D: hybrid curated anchors.

1. For known curated maps such as the current custom n4f4 override target, use override-based anchors:
   - `characterAnchorPosition`
   - `characterAnchorRotation`
   - `cameraFocusAnchor`
   - optional `cameraYaw`, `cameraPitch`, `cameraDistance`
   - optional `groundYOffset`
2. Keep original behavior only for maps proven compatible with native CharaSelect placement.
3. If no reliable actor placement control is found, explicitly mark that override target as background-only or unsupported for character framing.
4. Do not rely on original CharaSelect actor coordinates for all target maps.

This is more predictable than trying to make arbitrary terrain-derived placement work first. A generic terrain sampler can be added later if a reliable API or native helper is found.

## Design Options

Option A: preset-based fixed anchor.

- Add per-map preset coordinates for character anchor and camera focus.
- Best fit for curated title backgrounds such as Il Mheg.
- Lowest runtime uncertainty.
- Requires manual anchor data for each supported map.
- Recommended as the first behavior path after diagnostics.

Option B: terrain-derived Y adjustment.

- Keep native or preset X/Z and derive the ground Y at that point.
- More generic if a reliable ground-height API exists.
- Higher risk because no stable ground-height source has been identified in current code.
- Should remain an investigation track, not the first behavior dependency.

Option C: unsupported-map/background-only mode.

- If actor placement cannot be controlled reliably, treat the scene override as background-only.
- Avoids claiming character framing when actor visibility cannot be guaranteed.
- Useful as a safe fallback for arbitrary maps.

Option D: hybrid.

- Use curated preset anchors for known maps.
- Use terrain-derived Y only when a reliable sampler is proven.
- Fall back to native behavior for original-compatible scenes.
- Fall back to background-only for unsupported maps.
- Recommended because it handles Il Mheg predictably while preserving safe fallback behavior.

## Minimal Implementation Plan

Phase 2M-A: diagnostics only.

- Add a small retained character-placement diagnostic snapshot model.
- Capture at sceneReady accepted and stabilization frames `0, 30, 60, 120, 300, 600`.
- Reuse the existing failure-only detailed dump pattern; keep normal `/xmutbgdiag` concise.
- Do not change camera/actor behavior in this phase.

Phase 2M-B: decide actor control path.

- If object table exposes the CharaSelect actor reliably, identify it and record position/rotation/visibility.
- If transform write is possible and safe, plan a one-shot scene-generation-gated actor placement write after sceneReady; do not use a per-frame correction loop.
- If transform write is not reliable, plan background-only/unsupported-map handling.

Phase 2M-C: preset model extension.

- Extend `TitleBackgroundPreset` and `Configuration` with explicit anchor fields, preserving backward compatibility:
  - `CharacterAnchorPosition`, defaulting from existing `CharacterPosition`
  - `CharacterAnchorRotation`, defaulting from existing `CharacterRotation`
  - `CameraFocusAnchor`, defaulting from existing `FocusX/Y/Z`
  - `CameraYaw`, defaulting from camera/focus-derived yaw
  - `CameraPitch`, defaulting from camera/focus-derived pitch
  - `CameraDistance`, defaulting from camera/focus-derived distance
  - `GroundYOffset`, defaulting to `0`
  - `CharacterPlacementMode` such as `Native`, `PresetAnchor`, `BackgroundOnly`
- Add applicator/capture/import/export handling.
- Keep existing fields as legacy/default source when new fields are absent.

Phase 2M-D: one-shot placement and camera anchor.

- Apply character anchor once per scene generation after sceneReady accepted.
- Build camera target from anchor-aware focus:
  - preferred: `cameraFocusAnchor`
  - fallback: character anchor plus preset focus delta
  - last resort: existing preset focus
- Keep Phase 2G generated curve path post-original and generation-gated.
- Do not write `SceneCamera.Position` or `SceneCamera.LookAtVector`.

Phase 2M-E: success verdicts.

- Add explicit visual success diagnostics separate from transition safety:
  - `verdict.phase2M.actorVisible`
  - `verdict.phase2M.actorGroundAligned`
  - `verdict.phase2M.cameraFramesActor`
  - `verdict.phase2M.visualPlacementSafety`
- Keep `loginTransitionSafety=safe` as necessary but not sufficient.

## Diagnostics To Add Before Behavior Changes

Because `/xmutbgdiag` cannot be run directly on the login screen, retain these during character-select and dump after login:

- scene identity:
  - override path, original path, territory id, layout territory id, layer filter key
  - current lobby map, resolved lobby map, scene generation
- character actor:
  - candidate object index/id/name kind if available
  - actor position at sceneReady accepted
  - actor position at frames `0, 30, 60, 120, 300, 600`
  - actor rotation at the same frames
  - actor visibility/draw/culling flags if detectable
  - actor-to-camera distance
  - actor-to-lookAt delta
  - actor Y vs preset character Y
  - actor Y vs focus Y
  - actor Y vs native/current LookAtY
- camera:
  - active camera position/lookAt/yaw/pitch/distance at frames `0, 30, 60, 120, 300, 600`
  - lobby camera DirH/DirV/Distance/InterpDistance at the same frames
  - generated curve Low/Mid/High
  - final native LookAtY
- ground/height:
  - whether any ground-height source was available
  - sampled/derived ground Y at actor X/Z if available
  - `actorYMinusGroundY`
  - configured `groundYOffset`

Normal diagnostics should only show a compact status summary, for example:

- `phase2M.actorDiagnostic.status=observed|partial|unavailable`
- `phase2M.actor.visible=observed|not-observed|unknown`
- `phase2M.actor.groundAligned=observed|not-observed|unknown`
- `phase2M.camera.framesActor=observed|not-observed|unknown`
- `verdict.phase2M.visualPlacementSafety=safe|unsafe|unknown`

Detailed per-frame rows should remain failure-only.

## Ground Height Investigation

Open question: no reliable ground-height sampling API has been identified from the current inspected code.

Investigate in this order:

1. Existing Dalamud/ClientStructs helpers for terrain height or navmesh height.
2. Whether the current object table actor position is already snapped to floor after scene stabilization.
3. Whether layout layer filter key changes produce a valid floor for the actor.
4. Whether a known Il Mheg preset anchor can solve the target case without a generic sampler.

If no safe ground sampler exists, use curated `characterAnchorPosition.Y` plus optional `groundYOffset` for known maps.

## Camera Anchor Decision

Do not treat yaw/pitch/distance mismatch as acceptable unless the actor is visible and framed.

Preferred anchor order:

1. `cameraFocusAnchor` from curated preset.
2. `characterAnchorPosition + presetCameraFocusDelta`, where the delta is captured from logged-in world preset data.
3. Existing preset focus point.
4. No framing claim; mark visual placement unknown/unsafe.

Phase 2G should still only fix generated curve points and native LookAtY source. It should not become a direct active camera maintenance system.

## Safety Constraints

- Do not reintroduce `Framework.Update` camera maintenance.
- Do not write `SceneCamera.Position` per frame.
- Do not write `SceneCamera.LookAtVector` per frame.
- Do not add yaw/pitch/distance correction loops.
- Do not weaken Phase 2L login cleanup.
- Do not treat `loginTransitionSafety=safe` as visual success.
- Do not treat Phase 2G generated curve success as sufficient if actor is invisible.
- Any actor transform write, if added later, must be one-shot, scene-generation-gated, and only after diagnostics prove the target object.

## Proposed Success Criteria

- Il Mheg scene loads.
- Character actor is visible.
- Character feet/body align with expected ground height.
- Camera frames the character.
- Phase 2G generated curve success remains observed.
- `transition.verdict.loginTransitionSafety=safe` remains true after login.
- No post-login Phase 2G apply.
- No sceneReady accepted after login.
- No direct camera maintenance loop.

## Validation Commands

For diagnostics-only implementation:

```powershell
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
git diff --check
Select-String -Path projects\XIV-Mini-Util\Services\TitleBackground\*.cs -Pattern "SceneCamera\.Position\s*=|SceneCamera\.LookAtVector\s*="
```

The final search must have no direct assignment hits. Diagnostic string output containing those names is acceptable.

## In-Game Validation Steps

Diagnostics-only Phase 2M-A:

1. Restart game.
2. Enable title background override with the current custom n4f4 override target.
3. Enter character-select flow and reproduce the broken visual.
4. Log in.
5. Run `/xmutbgdiag` once after login.
6. Confirm Phase 2L remains safe:
   - `transition.sceneOverride.active=False`
   - `transition.sceneOverride.cleanupReason=world-login-transition`
   - `transition.phase2G.appliedAfterLogin=False`
   - `transition.verdict.postLoginPhase2GStillApplying=False`
   - `transition.verdict.postLoginSceneReadyAccepted=False`
   - `transition.verdict.loginTransitionSafety=safe`
7. Inspect retained Phase 2M diagnostics:
   - actor position/visibility exists or is explicitly unavailable
   - actor/camera deltas are present
   - camera timeline frames are present
   - ground Y is present or explicitly unavailable
8. Save failure-only detailed diagnostics if `visualPlacementSafety` is unsafe/unknown.

Behavior Phase 2M-D, only after plan review and diagnostics:

1. Repeat the same flow.
2. Confirm actor is visible and ground-aligned in Il Mheg.
3. Confirm camera frames actor at sceneReady and stabilization frames.
4. Confirm Phase 2G and Phase 2L verdicts remain safe.
5. Confirm no direct camera maintenance loop was added.

## Recommendation

Approve Phase 2M-A diagnostics first. Do not jump directly to actor/camera behavior changes.

The most productive next implementation is a retained actor-placement diagnostic pass that proves whether actor transform is identifiable and whether the current Y mismatch is actor placement, camera focus, layer, or ground-height related. After that, implement curated preset anchors for Il Mheg as the first behavior change.

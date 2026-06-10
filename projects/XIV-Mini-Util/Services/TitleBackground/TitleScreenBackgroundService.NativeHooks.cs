// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.NativeHooks.cs
// Description: TitleBackground の native hook 初期化、detour、Phase2G curve 適用を提供する
// Reason: native hook 経路を TitleScreenBackgroundService の本体状態管理から分離するため
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System.Runtime.InteropServices;
using System.Text;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private void InitializeHooks()
    {
        try
        {
            if (!_addressResolver.Resolve(_sigScanner, _configuration))
            {
                _state = TitleBackgroundServiceState.AddressResolveFailed;
                _stateReason = _addressResolver.LastError;
                return;
            }

            if (!ShouldCreateSceneHooks())
            {
                _state = TitleBackgroundServiceState.Disabled;
                _stateReason = _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly
                    ? "resolver-only"
                    : "無効";
                return;
            }

            _createSceneHook = _gameInteropProvider.HookFromAddress<CreateSceneDelegate>(_addressResolver.CreateScene, CreateSceneDetour);
            _lobbyUpdateHook = _gameInteropProvider.HookFromAddress<LobbyUpdateDelegate>(_addressResolver.LobbyUpdate, LobbyUpdateDetour);
            _loadLobbySceneHook = _gameInteropProvider.HookFromAddress<LoadLobbySceneDelegate>(_addressResolver.LoadLobbyScene, LoadLobbySceneDetour);
            if (_addressResolver.UpdateLobbyUIStage != nint.Zero)
            {
                _lobbySceneLoadedHook = _gameInteropProvider.HookFromAddress<LobbySceneLoadedDelegate>(_addressResolver.UpdateLobbyUIStage, LobbySceneLoadedDetour);
            }

            if (_addressResolver.CalculateLobbyCameraLookAtY != nint.Zero)
            {
                _calculateLobbyCameraLookAtYHook = _gameInteropProvider.HookFromAddress<CalculateLobbyCameraLookAtYDelegate>(
                    _addressResolver.CalculateLobbyCameraLookAtY,
                    CalculateLobbyCameraLookAtYDetour);
            }

            if (_addressResolver.SetCameraCurveMidPoint != nint.Zero)
            {
                _setCameraCurveMidPointHook = _gameInteropProvider.HookFromAddress<SetCameraCurveMidPointDelegate>(
                    _addressResolver.SetCameraCurveMidPoint,
                    SetCameraCurveMidPointDetour);
            }

            if (_addressResolver.CalculateCameraCurveLowAndHighPoint != nint.Zero)
            {
                _calculateCameraCurveLowAndHighPointHook = _gameInteropProvider.HookFromAddress<CalculateCameraCurveLowAndHighPointDelegate>(
                    _addressResolver.CalculateCameraCurveLowAndHighPoint,
                    CalculateCameraCurveLowAndHighPointDetour);
            }

            _createSceneHook.Enable();
            _lobbyUpdateHook.Enable();
            _loadLobbySceneHook.Enable();
            _lobbySceneLoadedHook?.Enable();
            _calculateLobbyCameraLookAtYHook?.Enable();
            _setCameraCurveMidPointHook?.Enable();
            _calculateCameraCurveLowAndHighPointHook?.Enable();
            RecordTransitionEvent("hooks enabled", "InitializeHooks");

            _state = TitleBackgroundServiceState.Disabled;
            _stateReason = "無効";
        }
        catch (Exception ex)
        {
            _state = _createSceneHook == null ? TitleBackgroundServiceState.HookCreateFailed : TitleBackgroundServiceState.HookEnableFailed;
            _stateReason = ex.Message;
            _log.Warning(ex, "TitleBackground: native integration failed.");
            DisposeHooks();
        }
    }

    private void LoadLobbySceneDetour(GameLobbyType mapId)
    {
        RecordTransitionEvent("LoadLobbySceneDetour entered", mapId.ToString());
        RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind.LoadLobbyScene);
        if (TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(mapId))
        {
            ResetPhase2ECalculateLookAtYObservation();
        }

        _loadingLobbyType = mapId;
        RecordCharaSelectRuntimeCameraStateBeforeSceneReload(mapId);
        _charaSelectCameraAdapter.NotifySceneLoadStarted(mapId);
        if (TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(mapId))
        {
            _charaSelectTitleBackgroundSessionActive = true;
            _activeCharaSelectSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
            _sceneOverrideCleanupReason = "none";
            _loggedInWorldTransitionRecorded = false;
        }

        RecordTransitionEvent("scene generation incremented", $"generation={_charaSelectCameraAdapter.RuntimeState.SceneGeneration}");
        RecordProbeLoadLobbyScene(mapId);
        _log.Debug("[XMU BG] LoadLobbyScene mapId={MapId}", mapId);
        _loadLobbySceneHook?.Original(mapId);
        RecordTransitionEvent("LoadLobbySceneDetour original called", mapId.ToString());
    }

    private void LobbySceneLoadedDetour(nint thisPtr)
    {
        _lobbySceneLoadedHook?.Original(thisPtr);

        try
        {
            var stateBeforeHandle = _charaSelectCameraAdapter.State;
            var map = ResolveSceneReadySignalLobbyMap();
            _sceneReadySignalCallCount++;
            _sceneReadySignalLastAdapterStateBeforeHandle = stateBeforeHandle.ToString();
            _sceneReadySignalLastResolvedLobbyMap = map;
            var sceneReadySnapshot = BuildTransitionSnapshot("sceneReady");
            _transitionDiagnostics.RecordSceneReadyRaw(sceneReadySnapshot, $"map={map}; stateBefore={stateBeforeHandle}");

            // Phase 2-A uses AgentLobby.UpdateLobbyUIStage as a provisional scene-ready signal.
            // This is not a confirmed native LobbySceneLoaded-equivalent hook.
            if (ShouldHandleCharaSelectSceneReadySignal(stateBeforeHandle, map))
            {
                _sceneReadySignalAcceptedCount++;
                if (_quickCheckState.RunState == TitleBackgroundQuickCheckRunState.Armed)
                {
                    _quickCheckState = _quickCheckState with { RunState = TitleBackgroundQuickCheckRunState.CharaSelectObserved };
                }

                _transitionDiagnostics.RecordSceneReadyAccepted(
                    sceneReadySnapshot,
                    $"map={map}; stateBefore={stateBeforeHandle}",
                    _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
                    _clientState.IsLoggedIn);
                StartPhase2CTimelineObservation();
                CapturePhase2MPlacementFrame(0, "scene-ready-accepted");
                _charaSelectCameraAdapter.NotifySceneLoaded(map);
                RecordTransitionEvent("sceneLoadedNotification success", map.ToString());
                RestoreCharaSelectRuntimeCameraStateAfterSceneLoad();
                ApplyCharaSelectCameraCurveAfterSceneLoad();
                CapturePhase2CTimelineFrame(0);
            }
            else
            {
                _transitionDiagnostics.RecordSceneReadyRejected(
                    sceneReadySnapshot,
                    $"map={map}; stateBefore={stateBeforeHandle}");
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbySceneLoadedDetour));
            RecordTransitionEvent("sceneLoadedNotification failure", nameof(LobbySceneLoadedDetour), ex.Message);
            _lastCharaSelectCameraRuntimeRestoreStatus = "runtime-error";
            _lastCharaSelectCameraRuntimeRestoreFailureReason = ex.Message;
            _curveApplyLastStatus = "runtime-error";
            _curveApplyLastFailureReason = ex.Message;
        }
    }

    private int CreateSceneDetour(byte* territoryPath, uint territoryId, nint p3, uint layerFilterKey, nint festivals, int p6, uint contentFinderConditionId)
    {
        byte[]? overrideBytes = null;
        try
        {
            var lobbyType = EffectiveLobbyType;
            var originalPath = territoryPath == null ? string.Empty : Marshal.PtrToStringUTF8((nint)territoryPath) ?? string.Empty;
            RecordTransitionEvent("CreateSceneDetour entered", $"lobbyType={lobbyType}; path={originalPath}");
            RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind.CreateScene);
            _lastObservedCreateScenePath = originalPath;
            RecordProbeCreateScene(lobbyType, originalPath, territoryId, layerFilterKey);
            _log.Debug("[XMU BG] CreateScene lobbyType={LobbyType}, path={Path}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", lobbyType, originalPath, territoryId, layerFilterKey);

            if (IsHookProbeMode())
            {
                RecordTransitionEvent("CreateSceneDetour original path observed", "hook-probe");
                return _createSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
            }

            if (ShouldOverrideCharaSelect(lobbyType))
            {
                LayoutWorld.UnloadPrefetchLayout();
                var presetTerritoryId = GetEffectiveOverrideTerritoryId();
                if (presetTerritoryId != 0)
                {
                    territoryId = presetTerritoryId;
                }

                if (_configuration.TitleBackgroundLayoutLayerFilterKey != 0)
                {
                    layerFilterKey = _configuration.TitleBackgroundLayoutLayerFilterKey;
                }

                overrideBytes = Encoding.UTF8.GetBytes(_validatedTerritoryPath + '\0');
                _cameraApplyPending = false;
                _charaSelectCameraAdapter.NotifySceneOverrideApplied(lobbyType);
                _lastOverrideApplied = true;
                _lastOverrideLobbyType = lobbyType;
                _lastOverrideOriginalPath = originalPath;
                _lastOverrideNewPath = _validatedTerritoryPath;
                _lastOverrideTerritoryId = territoryId;
                _lastOverrideLayerFilterKey = layerFilterKey;
                _quickCheckOverrideAppliedCount++;
                _charaSelectTitleBackgroundSessionActive = true;
                _activeCharaSelectSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
                _activeSceneOverride = true;
                _activeSceneOverrideLobbyType = lobbyType;
                _activeSceneOverridePath = _validatedTerritoryPath;
                _lastHistoricalOverridePath = _validatedTerritoryPath;
                _sceneOverrideCleanupReason = "none";
                _loggedInWorldTransitionRecorded = false;
                RecordTransitionEvent("CreateSceneDetour override applied", $"lobbyType={lobbyType}");
                _log.Information("[XMU BG] Override CharaSelect scene lobbyType={LobbyType}, originalPath={OriginalPath}, newPath={NewPath}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", lobbyType, originalPath, _validatedTerritoryPath, territoryId, layerFilterKey);
            }
            else
            {
                RecordTransitionEvent("CreateSceneDetour override skipped", $"lobbyType={lobbyType}");
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(CreateSceneDetour));
            overrideBytes = null;
        }
        finally
        {
            _loadingLobbyType = GameLobbyType.None;
        }

        if (overrideBytes is { Length: > 0 })
        {
            fixed (byte* overridePath = overrideBytes)
            {
                return _createSceneHook?.Original(overridePath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
            }
        }

        return _createSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
    }

    private byte LobbyUpdateDetour(GameLobbyType mapId, int time)
    {
        RecordTransitionEvent("LobbyUpdateDetour entered", $"map={mapId}; time={time}");
        var frame = RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind.LobbyUpdate);
        try
        {
            RecordProbeLobbyUpdate(mapId, time);
            _charaSelectCameraAdapter.NotifyLobbyUpdate(mapId);
            RecordTransitionEvent("adapter state transition", $"event=LobbyUpdate; map={mapId}; state={_charaSelectCameraAdapter.State}");
            if (!TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(mapId))
            {
                RecordTransitionEvent("leaving title/character-select context if detectable", mapId.ToString());
                EndCharaSelectTitleBackgroundSessionIfNeeded(mapId, "lobby-update");
            }

            if (!IsHookProbeMode() && ShouldResetCurrentMapForReload(mapId))
            {
                _currentMapWriteAttempted = true;
                _lastCurrentMapWriteSucceeded = TryWriteCurrentLobbyMap(GameLobbyType.None);
                _lastCurrentLobbyMapResetReason = $"reload-transition-to-{mapId}";
                RecordTransitionEvent("CurrentLobbyMap reset", _lastCurrentLobbyMapResetReason);
                _log.Debug("[XMU BG] CurrentLobbyMap reset requested. next={NextMap}, success={Success}", mapId, _lastCurrentMapWriteSucceeded);
            }
            else
            {
                RecordTransitionEvent("CurrentLobbyMap set", mapId.ToString());
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyUpdateDetour));
        }
        finally
        {
            _lastLobbyUpdateMapId = mapId;
        }

        CaptureCameraProbeLobbyUpdateState(frame, beforeOriginal: true);
        var result = _lobbyUpdateHook?.Original(mapId, time) ?? 0;
        CaptureCameraProbeLobbyUpdateState(frame, beforeOriginal: false);
        return result;
    }

    private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)
    {
        RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind.FixOn);
        float[]? cameraOverride = null;
        float[]? focusOverride = null;
        var overrideFovY = fovY;
        var invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
        try
        {
            _lastObservedFixOnCamera = TryReadVector(cameraPos);
            _lastObservedFixOnFocus = TryReadVector(focusPos);
            _lastObservedFixOnFovY = fovY;
            if (ShouldOverrideCamera())
            {
                _cameraApplyPending = false;
                var plan = TitleBackgroundCameraOverridePlan.FromConfiguration(_configuration);
                cameraOverride =
                [
                    plan.Camera.X,
                    plan.Camera.Y,
                    plan.Camera.Z,
                ];
                focusOverride =
                [
                    plan.Focus.X,
                    plan.Focus.Y,
                    plan.Focus.Z,
                ];
                overrideFovY = plan.FovY;
                _lastCameraOverrideApplied = true;
                invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(true);
                _lastAppliedCamera = plan.Camera;
                _lastAppliedFocus = plan.Focus;
                _lastAppliedFovY = plan.FovY;
                _log.Information(
                    "[XMU BG] Camera override applied. camera={Camera}, focus={Focus}, fovY={FovY}",
                    FormatVector(plan.Camera),
                    FormatVector(plan.Focus),
                    plan.FovY);
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyCameraFixOnDetour));
            cameraOverride = null;
            focusOverride = null;
            overrideFovY = fovY;
            invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
        }

        _lastFixOnInvocationMode = invocationMode;
        nint result;
        if (cameraOverride != null && focusOverride != null)
        {
            fixed (float* cameraPointer = cameraOverride)
            fixed (float* focusPointer = focusOverride)
            {
                result = _cameraFixOnHook?.Original(self, cameraPointer, focusPointer, overrideFovY) ?? nint.Zero;
            }
        }
        else
        {
            result = _cameraFixOnHook?.Original(self, cameraPos, focusPos, fovY) ?? nint.Zero;
        }

        CapturePostFixOnCameraState();
        ScheduleCameraProbeTimelineCapture(cameraOverride != null && focusOverride != null);
        return result;
    }

    private float CalculateLobbyCameraLookAtYDetour(
        nint self,
        float distance,
        CurvePoint* lowPoint,
        CurvePoint* midPoint,
        CurvePoint* highPoint)
    {
        var callIndex = ++_phase2ECalculateLookAtYCallCount;
        RecordTransitionEvent("calculateLobbyCameraLookAtY hook called", $"callIndex={callIndex}");
        var frame = GetCurrentPhase2CFrame();
        var low = ReadCurvePoint(lowPoint);
        var mid = ReadCurvePoint(midPoint);
        var high = ReadCurvePoint(highPoint);
        var activeBefore = TryCaptureActiveCameraSnapshot(out var beforeSnapshot, out _)
            ? beforeSnapshot.LookAtVector.Y
            : (float?)null;
        float returnValue;
        try
        {
            returnValue = _calculateLobbyCameraLookAtYHook?.Original(self, distance, lowPoint, midPoint, highPoint) ?? 0f;
        }
        catch (Exception ex)
        {
            _phase2ECalculateLookAtYLastError = ex.Message;
            RecordPhase2ECalculateLookAtYCall(new TitleBackgroundPhase2ECalculateLookAtYCall(
                callIndex,
                frame,
                distance,
                low,
                mid,
                high,
                null,
                activeBefore,
                null,
                "original-error",
                ex.Message));
            throw;
        }

        var activeAfter = TryCaptureActiveCameraSnapshot(out var afterSnapshot, out var afterError)
            ? afterSnapshot.LookAtVector.Y
            : (float?)null;
        var status = activeAfter.HasValue ? "success" : "active-camera-unavailable";
        var error = activeAfter.HasValue ? string.Empty : afterError;
        _phase2ECalculateLookAtYLastError = error;
        RecordPhase2ECalculateLookAtYCall(new TitleBackgroundPhase2ECalculateLookAtYCall(
            callIndex,
            frame,
            distance,
            low,
            mid,
            high,
            float.IsFinite(returnValue) ? returnValue : null,
            activeBefore,
            activeAfter,
            status,
            error));
        return returnValue;
    }

    private void SetCameraCurveMidPointDetour(nint self, float value)
    {
        var callIndex = ++_phase2FSetCameraCurveMidPointCallCount;
        RecordTransitionEvent("SetCameraCurveMidPoint hook called", $"callIndex={callIndex}");
        var frame = GetCurrentPhase2CFrame();
        var beforeCaptured = TryCaptureExpandedLobbyCameraSnapshot(self, out var before, out var beforeError);
        var activeBefore = TryCaptureActiveCameraSnapshot(out var activeBeforeSnapshot, out _)
            ? activeBeforeSnapshot
            : (TitleBackgroundActiveCameraSnapshot?)null;
        string phase2GStatus;
        string phase2GError;
        try
        {
            _setCameraCurveMidPointHook?.Original(self, value);
            TryApplyPhase2GSetCameraCurveMidPointOverride(self, frame, out phase2GStatus, out phase2GError);
        }
        catch (Exception ex)
        {
            _phase2FSetCameraCurveMidPointLastError = ex.Message;
            RecordPhase2FSetCameraCurveMidPointCall(
                BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, null, activeBefore, null, "original-error", ex.Message));
            throw;
        }

        var afterCaptured = TryCaptureExpandedLobbyCameraSnapshot(self, out var after, out var afterError);
        var activeAfter = TryCaptureActiveCameraSnapshot(out var activeAfterSnapshot, out _)
            ? activeAfterSnapshot
            : (TitleBackgroundActiveCameraSnapshot?)null;
        var status = beforeCaptured && afterCaptured ? "success" : "partial";
        status = string.IsNullOrWhiteSpace(phase2GStatus) ? status : $"{status}; phase2G={phase2GStatus}";
        var error = JoinErrors(JoinErrors(beforeCaptured ? string.Empty : beforeError, afterCaptured ? string.Empty : afterError), phase2GError);
        _phase2FSetCameraCurveMidPointLastError = error;
        RecordPhase2FSetCameraCurveMidPointCall(
            BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, afterCaptured ? after : null, activeBefore, activeAfter, status, error));
    }

    private void CalculateCameraCurveLowAndHighPointDetour(nint self, float value)
    {
        var callIndex = ++_phase2FCalculateCameraCurveLowAndHighPointCallCount;
        RecordTransitionEvent("CalculateCameraCurveLowAndHighPoint hook called", $"callIndex={callIndex}");
        var frame = GetCurrentPhase2CFrame();
        var beforeCaptured = TryCaptureExpandedLobbyCameraSnapshot(self, out var before, out var beforeError);
        var activeBefore = TryCaptureActiveCameraSnapshot(out var activeBeforeSnapshot, out _)
            ? activeBeforeSnapshot
            : (TitleBackgroundActiveCameraSnapshot?)null;
        string phase2GStatus;
        string phase2GError;
        try
        {
            _calculateCameraCurveLowAndHighPointHook?.Original(self, value);
            TryApplyPhase2GLowHighCurveOverride(self, frame, out phase2GStatus, out phase2GError);
        }
        catch (Exception ex)
        {
            _phase2FCalculateCameraCurveLowAndHighPointLastError = ex.Message;
            RecordPhase2FCalculateCameraCurveLowAndHighPointCall(
                BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, null, activeBefore, null, "original-error", ex.Message));
            throw;
        }

        var afterCaptured = TryCaptureExpandedLobbyCameraSnapshot(self, out var after, out var afterError);
        var activeAfter = TryCaptureActiveCameraSnapshot(out var activeAfterSnapshot, out _)
            ? activeAfterSnapshot
            : (TitleBackgroundActiveCameraSnapshot?)null;
        var status = beforeCaptured && afterCaptured ? "success" : "partial";
        status = string.IsNullOrWhiteSpace(phase2GStatus) ? status : $"{status}; phase2G={phase2GStatus}";
        var error = JoinErrors(JoinErrors(beforeCaptured ? string.Empty : beforeError, afterCaptured ? string.Empty : afterError), phase2GError);
        _phase2FCalculateCameraCurveLowAndHighPointLastError = error;
        RecordPhase2FCalculateCameraCurveLowAndHighPointCall(
            BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, afterCaptured ? after : null, activeBefore, activeAfter, status, error));
    }

    private bool TryApplyPhase2GSetCameraCurveMidPointOverride(nint self, int? frame, out string status, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryGetPhase2GGenerationOverrideCurve(out var curve, out var skippedReason))
        {
            status = $"skipped:{skippedReason}";
            _phase2GGenerationOverrideLastStatus = status;
            _phase2GGenerationOverrideLastSkippedReason = skippedReason;
            _transitionDiagnostics.RecordPhase2GSkipped(skippedReason);
            RecordTransitionEvent("Phase 2G setMid skipped", skippedReason);
            return false;
        }

        _phase2GGenerationOverrideSetMidAttemptCount++;
        RecordTransitionEvent("Phase 2G setMid attempted", $"frame={FormatFrame(frame)}");
        try
        {
            // Original is intentionally called first so native generation can keep its side effects;
            // Phase 2G only replaces the generated MidPoint value after that generation step.
            // Do not add Framework.Update maintenance or direct SceneCamera writes here.
            WriteCurvePointY(self, LobbyCameraExpandedMidPointOffset, curve.Mid);
            MarkPhase2GGenerationOverrideApplied(frame, "set-mid-applied");
            _phase2GGenerationOverrideSetMidAppliedCount++;
            _charaSelectService?.MarkTitleBackgroundCharacterCompositionBridgeCameraApplied();
            _transitionDiagnostics.RecordPhase2GApply(
                BuildTransitionSnapshot("Phase 2G setMid applied"),
                _clientState.IsLoggedIn,
                IsCharaSelectOrTitleBackground(TryReadCurrentLobbyMap(out var applyMap) ? applyMap : GameLobbyType.None),
                "ShouldApplyGeneratedCurveOverride=true");
            RecordTransitionEvent("Phase 2G setMid applied", $"frame={FormatFrame(frame)}");
            status = "applied-post-original";
            return true;
        }
        catch (Exception ex)
        {
            status = "failed";
            errorMessage = ex.Message;
            _phase2GGenerationOverrideLastStatus = "set-mid-failed";
            _phase2GGenerationOverrideLastSkippedReason = string.Empty;
            RecordTransitionEvent("Phase 2G setMid skipped", "failed", ex.Message);
            _log.Warning(ex, "TitleBackground Phase2G SetCameraCurveMidPoint override failed.");
            return false;
        }
    }

    private bool TryApplyPhase2GLowHighCurveOverride(nint self, int? frame, out string status, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryGetPhase2GGenerationOverrideCurve(out var curve, out var skippedReason))
        {
            status = $"skipped:{skippedReason}";
            _phase2GGenerationOverrideLastStatus = status;
            _phase2GGenerationOverrideLastSkippedReason = skippedReason;
            _transitionDiagnostics.RecordPhase2GSkipped(skippedReason);
            RecordTransitionEvent("Phase 2G lowHigh skipped", skippedReason);
            return false;
        }

        _phase2GGenerationOverrideLowHighAttemptCount++;
        RecordTransitionEvent("Phase 2G lowHigh attempted", $"frame={FormatFrame(frame)}");
        try
        {
            // Original computes Low/High from the current generated inputs first; the post-original
            // write preserves native state changes and only pins the generated curve targets.
            // Final yaw/pitch/distance mismatch is expected and remains non-blocking.
            WriteCurvePointY(self, LobbyCameraExpandedLowPointOffset, curve.Low);
            WriteCurvePointY(self, LobbyCameraExpandedHighPointOffset, curve.High);
            MarkPhase2GGenerationOverrideApplied(frame, "low-high-applied");
            _phase2GGenerationOverrideLowHighAppliedCount++;
            _charaSelectService?.MarkTitleBackgroundCharacterCompositionBridgeCameraApplied();
            _transitionDiagnostics.RecordPhase2GApply(
                BuildTransitionSnapshot("Phase 2G lowHigh applied"),
                _clientState.IsLoggedIn,
                IsCharaSelectOrTitleBackground(TryReadCurrentLobbyMap(out var applyMap) ? applyMap : GameLobbyType.None),
                "ShouldApplyGeneratedCurveOverride=true");
            RecordTransitionEvent("Phase 2G lowHigh applied", $"frame={FormatFrame(frame)}");
            status = "applied-post-original";
            return true;
        }
        catch (Exception ex)
        {
            status = "failed";
            errorMessage = ex.Message;
            _phase2GGenerationOverrideLastStatus = "low-high-failed";
            _phase2GGenerationOverrideLastSkippedReason = string.Empty;
            RecordTransitionEvent("Phase 2G lowHigh skipped", "failed", ex.Message);
            _log.Warning(ex, "TitleBackground Phase2G CalculateCameraCurveLowAndHighPoint override failed.");
            return false;
        }
    }

    private bool TryGetPhase2GGenerationOverrideCurve(
        out TitleBackgroundCharaSelectCameraCurve curve,
        out string skippedReason)
    {
        curve = default;
        var currentMapAvailable = TryReadCurrentLobbyMap(out var currentMap);
        var resolvedMap = ResolveSceneReadySignalLobbyMap();
        if (!TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
                _state == TitleBackgroundServiceState.Ready,
                IsHookProbeMode(),
                IsSceneOverrideEnabled(),
                _charaSelectCameraAdapter.IsArmed,
                _clientState.IsLoggedIn,
                _charaSelectTitleBackgroundSessionActive,
                _activeCharaSelectSceneGeneration > 0
                    && _charaSelectCameraAdapter.RuntimeState.SceneGeneration == _activeCharaSelectSceneGeneration,
                _charaSelectCameraAdapter.State,
                _charaSelectCameraAdapter.RuntimeState,
                currentMapAvailable ? currentMap : GameLobbyType.None,
                resolvedMap))
        {
            skippedReason = BuildPhase2GGenerationOverrideSkippedReason();
            return false;
        }

        var baseCurve = _charaSelectCameraAdapter.RuntimeState.CurveAtRecord!.Value;
        var cameraProfile = ResolveCurrentTitleBackgroundCameraProfile();
        curve = cameraProfile.HasProfile
            ? TitleBackgroundCharaSelectCameraLogic.ApplyCameraProfileCurveOffset(baseCurve, cameraProfile)
            : TitleBackgroundCharaSelectCameraLogic.ApplyCameraFramingOffset(
                baseCurve,
                _configuration.TitleBackgroundCharaSelectCameraFramingMode);
        skippedReason = string.Empty;
        return true;
    }

    private string BuildPhase2GGenerationOverrideSkippedReason()
    {
        if (_state != TitleBackgroundServiceState.Ready)
        {
            return "service-not-ready";
        }

        if (IsHookProbeMode())
        {
            return "hook-probe";
        }

        if (!IsSceneOverrideEnabled())
        {
            return "scene-override-disabled";
        }

        if (_clientState.IsLoggedIn)
        {
            return "logged-in-context";
        }

        if (!_charaSelectTitleBackgroundSessionActive)
        {
            return "inactive-chara-select-session";
        }

        if (!_charaSelectCameraAdapter.IsArmed)
        {
            return "adapter-not-armed";
        }

        if (_charaSelectCameraAdapter.State is not TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
            and not TitleBackgroundCharaSelectCameraAdapterState.Active)
        {
            return $"adapter-state-{_charaSelectCameraAdapter.State}";
        }

        if (_charaSelectCameraAdapter.RuntimeState.SceneGeneration <= 0)
        {
            return "scene-generation-empty";
        }

        if (!_charaSelectCameraAdapter.RuntimeState.HasCameraPose)
        {
            return "runtime-pose-incomplete";
        }

        if (!_charaSelectCameraAdapter.RuntimeState.CurveAtRecord.HasValue)
        {
            return "runtime-curve-empty";
        }

        if (_activeCharaSelectSceneGeneration <= 0
            || _charaSelectCameraAdapter.RuntimeState.SceneGeneration != _activeCharaSelectSceneGeneration)
        {
            return "scene-generation-mismatch";
        }

        var currentMapAvailable = TryReadCurrentLobbyMap(out var currentMap);
        var resolvedMap = ResolveSceneReadySignalLobbyMap();
        if (!(currentMapAvailable && TitleBackgroundCharaSelectCameraLogic.IsCharaSelectOrTitleBackgroundMap(currentMap))
            && !TitleBackgroundCharaSelectCameraLogic.IsCharaSelectOrTitleBackgroundMap(resolvedMap))
        {
            return "not-chara-select-or-title-background";
        }

        return "unknown";
    }

    private void MarkPhase2GGenerationOverrideApplied(int? frame, string status)
    {
        _phase2GGenerationOverrideLastAppliedFrame = frame;
        _phase2GGenerationOverrideLastAppliedSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        _phase2GGenerationOverrideLastStatus = status;
        _phase2GGenerationOverrideLastSkippedReason = string.Empty;
    }

    private static void WriteCurvePointY(nint lobbyCameraAddress, int offset, float value)
    {
        var point = (CurvePoint*)((byte*)lobbyCameraAddress + offset);
        point->Y = TitleBackgroundPreset.SanitizeCoordinate(value);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (_disposed)
        {
            return;
        }

        if (TryReadCurrentLobbyMap(out var currentMap))
        {
            EndCharaSelectTitleBackgroundSessionIfNeeded(currentMap, "framework-update");
        }

        CapturePhase2CTimelineOnFrameworkUpdate();
        CaptureCameraProbeTimelineOnFrameworkUpdate();
        UpdateSelfTestOnFrameworkUpdate();
    }
}

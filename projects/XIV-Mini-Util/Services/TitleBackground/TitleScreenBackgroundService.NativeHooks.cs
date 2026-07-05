// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.NativeHooks.cs
// Description: TitleBackground の native hook 初期化、detour、Phase2G curve 適用を提供する
// Reason: native hook 経路を TitleScreenBackgroundService の本体状態管理から分離するため
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private void InitializeHooks(bool useKnownSignaturesForMissing = false)
    {
        try
        {
            if (!_addressResolver.Resolve(
                    _sigScanner,
                    _configuration,
                    useKnownSignaturesForMissing))
            {
                _hookLifecycle.State = TitleBackgroundServiceState.AddressResolveFailed;
                _hookLifecycle.StateReason = _addressResolver.LastError;
                return;
            }

            if (!ShouldCreateSceneHooks())
            {
                _hookLifecycle.State = TitleBackgroundServiceState.Disabled;
                _hookLifecycle.StateReason = _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly
                    ? "resolver-only"
                    : "無効";
                return;
            }

            _hookLifecycle.CreateSceneHook = _gameInteropProvider.HookFromAddress<CreateSceneDelegate>(_addressResolver.CreateScene, CreateSceneDetour);
            _hookLifecycle.LobbyUpdateHook = _gameInteropProvider.HookFromAddress<LobbyUpdateDelegate>(_addressResolver.LobbyUpdate, LobbyUpdateDetour);
            _hookLifecycle.LoadLobbySceneHook = _gameInteropProvider.HookFromAddress<LoadLobbySceneDelegate>(_addressResolver.LoadLobbyScene, LoadLobbySceneDetour);
            if (_addressResolver.UpdateLobbyUIStage != nint.Zero)
            {
                _hookLifecycle.LobbySceneLoadedHook = _gameInteropProvider.HookFromAddress<LobbySceneLoadedDelegate>(_addressResolver.UpdateLobbyUIStage, LobbySceneLoadedDetour);
            }

            if (_addressResolver.CalculateLobbyCameraLookAtY != nint.Zero)
            {
                _hookLifecycle.CalculateLobbyCameraLookAtYHook = _gameInteropProvider.HookFromAddress<CalculateLobbyCameraLookAtYDelegate>(
                    _addressResolver.CalculateLobbyCameraLookAtY,
                    CalculateLobbyCameraLookAtYDetour);
            }

            if (_addressResolver.SetCameraCurveMidPoint != nint.Zero)
            {
                _hookLifecycle.SetCameraCurveMidPointHook = _gameInteropProvider.HookFromAddress<SetCameraCurveMidPointDelegate>(
                    _addressResolver.SetCameraCurveMidPoint,
                    SetCameraCurveMidPointDetour);
            }

            if (_addressResolver.CalculateCameraCurveLowAndHighPoint != nint.Zero)
            {
                _hookLifecycle.CalculateCameraCurveLowAndHighPointHook = _gameInteropProvider.HookFromAddress<CalculateCameraCurveLowAndHighPointDelegate>(
                    _addressResolver.CalculateCameraCurveLowAndHighPoint,
                    CalculateCameraCurveLowAndHighPointDetour);
            }

            // FixOn は既定では装着しない（dead code 整理済み）。passive 観測フラグ、または
            // focus-anchor override フラグが ON の時だけ装着する。passive は診断専用（上書き無し）、
            // focus override は候補一致時のみ焦点だけを陸上アンカーへ差し替える。
            if (ShouldInstallFixOnHook())
            {
                _hookLifecycle.CameraFixOnHook = _gameInteropProvider.HookFromAddress<LobbyCameraFixOnDelegate>(
                    _addressResolver.FixOn,
                    LobbyCameraFixOnDetour);
            }

            _hookLifecycle.CreateSceneHook.Enable();
            _hookLifecycle.LobbyUpdateHook.Enable();
            _hookLifecycle.LoadLobbySceneHook.Enable();
            _hookLifecycle.LobbySceneLoadedHook?.Enable();
            _hookLifecycle.CalculateLobbyCameraLookAtYHook?.Enable();
            _hookLifecycle.SetCameraCurveMidPointHook?.Enable();
            _hookLifecycle.CalculateCameraCurveLowAndHighPointHook?.Enable();
            _hookLifecycle.CameraFixOnHook?.Enable();
            RecordTransitionEvent("hooks enabled", "InitializeHooks");

            _hookLifecycle.State = TitleBackgroundServiceState.Disabled;
            _hookLifecycle.StateReason = "無効";
        }
        catch (Exception ex)
        {
            _hookLifecycle.State = _hookLifecycle.CreateSceneHook == null ? TitleBackgroundServiceState.HookCreateFailed : TitleBackgroundServiceState.HookEnableFailed;
            _hookLifecycle.StateReason = ex.Message;
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
            // R 実験の値は必ずロード単位にする。FixOn は本ロード中（下の Original 内）で再発火し再populate される。
            ResetFixOnExperimentSnapshot();
        }

        RecordTransitionEvent("scene generation incremented", $"generation={_charaSelectCameraAdapter.RuntimeState.SceneGeneration}");
        RecordProbeLoadLobbyScene(mapId);
        _log.Debug("[XMU BG] LoadLobbyScene mapId={MapId}", mapId);
        _hookLifecycle.LoadLobbySceneHook?.Original(mapId);
        RecordTransitionEvent("LoadLobbySceneDetour original called", mapId.ToString());
    }

    private void LobbySceneLoadedDetour(nint thisPtr)
    {
        _hookLifecycle.LobbySceneLoadedHook?.Original(thisPtr);

        try
        {
            var stateBeforeHandle = _charaSelectCameraAdapter.State;
            var map = ResolveSceneReadySignalLobbyMap();
            _cameraRestoreCurve.SceneReadySignalCallCount++;
            _cameraRestoreCurve.SceneReadySignalLastAdapterStateBeforeHandle = stateBeforeHandle.ToString();
            _cameraRestoreCurve.SceneReadySignalLastResolvedLobbyMap = map;
            var sceneReadySnapshot = BuildTransitionSnapshot("sceneReady");
            _transitionDiagnostics.RecordSceneReadyRaw(sceneReadySnapshot, $"map={map}; stateBefore={stateBeforeHandle}");

            // Phase 2-A uses AgentLobby.UpdateLobbyUIStage as a provisional scene-ready signal.
            // This is not a confirmed native LobbySceneLoaded-equivalent hook.
            if (ShouldHandleCharaSelectSceneReadySignal(stateBeforeHandle, map))
            {
                _cameraRestoreCurve.SceneReadySignalAcceptedCount++;
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
                CaptureCharacterPlacementPlacementFrame(0, "scene-ready-accepted");
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
            _cameraRestoreCurve.LastCharaSelectCameraRuntimeRestoreStatus = "runtime-error";
            _cameraRestoreCurve.LastCharaSelectCameraRuntimeRestoreFailureReason = ex.Message;
            _cameraRestoreCurve.CurveApplyLastStatus = "runtime-error";
            _cameraRestoreCurve.CurveApplyLastFailureReason = ex.Message;
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
                return _hookLifecycle.CreateSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
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
                return _hookLifecycle.CreateSceneHook?.Original(overridePath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
            }
        }

        return _hookLifecycle.CreateSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
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
        var result = _hookLifecycle.LobbyUpdateHook?.Original(mapId, time) ?? 0;
        CaptureCameraProbeLobbyUpdateState(frame, beforeOriginal: false);
        return result;
    }

    private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)
    {
        _cameraObservation.FixOnPassiveCallCount++;
        // FixOn 発火「時点」の generation / context を保持する（報告は post-login で行われ active
        // generation は終了処理で 0、context は post-login になるため、その時の値では取り違える）。
        _cameraObservation.FixOnExperimentSceneGeneration = _activeCharaSelectSceneGeneration;
        _cameraObservation.FixOnExperimentCaptureContext = _clientState.IsLoggedIn ? "post-login" : "pre-login";
        _cameraObservation.FixOnExperimentCharaSelectSession = _charaSelectTitleBackgroundSessionActive;
        RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind.FixOn);
        float[]? cameraOverride = null;
        float[]? focusOverride = null;
        var overrideFovY = fovY;
        var invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
        // 保存view再現バグ診断: この呼び出しで view override が成立したかを一時保持する（catch で握り潰された
        // 場合は false のままなので trace は開始しない＝誤ったtarget値でtraceを走らせない安全側の挙動）。
        var viewOverrideAppliedThisInvocation = false;
        try
        {
            _cameraObservation.LastObservedFixOnCamera = TryReadVector(cameraPos);
            _cameraObservation.LastObservedFixOnFocus = TryReadVector(focusPos);
            _cameraObservation.LastObservedFixOnFovY = fovY;
            // passive 観測モードでは、将来 ShouldOverrideCamera() が復活しても絶対に
            // override せず passthrough を強制する（観測専用フックの不変条件）。
            if (!_configuration.TitleBackgroundFixOnPassiveObservationEnabled && ShouldOverrideCamera())
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
                _cameraObservation.LastCameraOverrideApplied = true;
                invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(true);
                _cameraObservation.LastAppliedCamera = plan.Camera;
                _cameraObservation.LastAppliedFocus = plan.Focus;
                _cameraObservation.LastAppliedFovY = plan.FovY;
                _log.Information(
                    "[XMU BG] Camera override applied. camera={Camera}, focus={Focus}, fovY={FovY}",
                    FormatVector(plan.Camera),
                    FormatVector(plan.Focus),
                    plan.FovY);
            }

            // 診断（read-only）: 適用可否の総合理由を毎回記録する。"ready" のときだけ下の override が走る。
            _cameraObservation.LastFixOnFocusOverrideGateReason = ComputeFixOnFocusOverrideGateReason();

            // view override（TitleEdit 方式）: 「今の見え方を保存」した camera+focus+fov を scene-local
            // 絶対値で「まとめて」上書きする。passive 観測 ON は最優先 passthrough、実行コンテキストは
            // 焦点 override と同じ FixOn 専用ゲート、候補一致時のみ。焦点だけの anchor override より優先する。
            // 同一 scene generation での再発火は _cameraObservation.LastViewOverrideAppliedGeneration で弾き、ネイティブ FixOn
            // への適用を generation あたり 1 回に制限する（再呼び出しの冪等性は保証されないため）。
            if (cameraOverride == null
                && focusOverride == null
                && _cameraObservation.LastViewOverrideAppliedGeneration != _activeCharaSelectSceneGeneration
                && TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(
                    _configuration.TitleBackgroundFixOnPassiveObservationEnabled,
                    _configuration.TitleBackgroundCharaSelectViewEnabled)
                && IsFixOnFocusOverrideContextActive())
            {
                // 自動確認 run 中は view override を抑止し、カメラを自然 FixOn（配置キャラ追従）に任せる。
                // 抑止した事実は LastFixOnViewOverrideSource=suppressed-by-run として run レポートで読める。
                // run 判定は _automaticCheck.Requested ベースで、run の完了・失敗・キャンセルで必ず解除される。
                if (IsSavedViewSuppressedByAutomaticRun())
                {
                    _cameraObservation.LastFixOnViewOverrideSource = "suppressed-by-run";
                }
                else
                {
                    var viewResolution = TitleBackgroundFixOnViewOverrideLogic.Resolve(
                        _configuration.TitleBackgroundCharaSelectViewEnabled,
                        BuildCharaSelectView(),
                        ResolveCurrentOverrideCandidate().Id,
                        _cameraObservation.LastObservedFixOnCamera ?? Vector3.Zero,
                        _cameraObservation.LastObservedFixOnFocus ?? Vector3.Zero,
                        fovY);
                    _cameraObservation.LastFixOnViewOverrideSource = viewResolution.Source;
                    if (viewResolution.ShouldOverride)
                    {
                        cameraOverride =
                        [
                            viewResolution.Camera.X,
                            viewResolution.Camera.Y,
                            viewResolution.Camera.Z,
                        ];
                        focusOverride =
                        [
                            viewResolution.Focus.X,
                            viewResolution.Focus.Y,
                            viewResolution.Focus.Z,
                        ];
                        overrideFovY = viewResolution.FovY;
                        _cameraObservation.LastCameraOverrideApplied = true;
                        _cameraObservation.LastAppliedCamera = viewResolution.Camera;
                        _cameraObservation.LastAppliedFocus = viewResolution.Focus;
                        _cameraObservation.LastAppliedFovY = viewResolution.FovY;
                        _cameraObservation.FixOnViewOverrideAppliedCount++;
                        _cameraObservation.LastViewOverrideAppliedGeneration = _activeCharaSelectSceneGeneration;
                        invocationMode = "view-override";
                        viewOverrideAppliedThisInvocation = true;
                        _log.Information(
                            "[XMU BG] FixOn view override applied. camera={Camera}, focus={Focus}, fovY={FovY}",
                            FormatVector(viewResolution.Camera),
                            FormatVector(viewResolution.Focus),
                            viewResolution.FovY);
                    }
                }
            }

            // 焦点 override は次を全て満たすときだけ。passive 観測 ON は最優先で passthrough を強制し、
            // 実行コンテキストは FixOn 専用ゲート（pre-login + Ready + bridge + session active + scene
            // generation 一致 + CharaSelect セッション）を必須にする。FixOn はシーン読み込み途中に発火し
            // CurrentLobbyMap が None に戻り得るため CurrentLobbyMap には依存しない。legacy override が
            // 発火していない場合に限り焦点だけを置き換える（camera/fovY は不変）。
            if (cameraOverride == null
                && focusOverride == null
                && TitleBackgroundFixOnFocusOverrideLogic.ShouldConsiderFocusOverride(
                    _configuration.TitleBackgroundFixOnPassiveObservationEnabled,
                    _configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled)
                && IsFixOnFocusOverrideContextActive())
            {
                var focusResolution = TitleBackgroundFixOnFocusOverrideLogic.Resolve(
                    _configuration.TitleBackgroundFixOnFocusAnchorOverrideEnabled,
                    BuildFixOnFocusAnchor(),
                    ResolveCurrentOverrideCandidate().Id,
                    _cameraObservation.LastObservedFixOnFocus ?? Vector3.Zero,
                    CharaSelectCharacterFocusBodyDrop);
                _cameraObservation.LastFixOnFocusOverrideSource = focusResolution.Source;
                if (focusResolution.ShouldOverride)
                {
                    focusOverride =
                    [
                        focusResolution.Focus.X,
                        focusResolution.Focus.Y,
                        focusResolution.Focus.Z,
                    ];
                    _cameraObservation.LastCameraOverrideApplied = true;
                    _cameraObservation.LastAppliedFocus = focusResolution.Focus;
                    _cameraObservation.FixOnFocusOverrideAppliedCount++;
                    invocationMode = "anchor-focus-override";
                    _log.Information(
                        "[XMU BG] FixOn focus anchor override applied. focus={Focus}",
                        FormatVector(focusResolution.Focus));
                }
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyCameraFixOnDetour));
            cameraOverride = null;
            focusOverride = null;
            overrideFovY = fovY;
            invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
            viewOverrideAppliedThisInvocation = false;
        }

        _cameraObservation.LastFixOnInvocationMode = invocationMode;
        nint result;
        if (cameraOverride != null || focusOverride != null)
        {
            // 上書きしない側はゲームの元ポインタをそのまま渡す（focus のみ差し替えにも対応）。
            fixed (float* cameraOverridePointer = cameraOverride)
            fixed (float* focusOverridePointer = focusOverride)
            {
                var cameraPointer = cameraOverride != null ? cameraOverridePointer : cameraPos;
                var focusPointer = focusOverride != null ? focusOverridePointer : focusPos;
                result = _hookLifecycle.CameraFixOnHook?.Original(self, cameraPointer, focusPointer, overrideFovY) ?? nint.Zero;
            }
        }
        else
        {
            result = _hookLifecycle.CameraFixOnHook?.Original(self, cameraPos, focusPos, fovY) ?? nint.Zero;
        }

        CapturePostFixOnCameraState();
        ScheduleCameraProbeTimelineCapture(cameraOverride != null || focusOverride != null);
        // 保存view再現バグ診断（read-only）: view override が成立したFixOn呼び出し直後だけtraceを開始する。
        // カメラには一切書き込まず、CapturePostFixOnCameraState() が既に読み取った実値をframe 0として使う。
        StartViewReplayTraceIfApplicable(viewOverrideAppliedThisInvocation);
        return result;
    }

    private float CalculateLobbyCameraLookAtYDetour(
        nint self,
        float distance,
        CurvePoint* lowPoint,
        CurvePoint* midPoint,
        CurvePoint* highPoint)
    {
        var callIndex = ++_phaseRecording.Phase2ECalculateLookAtYCallCount;
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
            returnValue = _hookLifecycle.CalculateLobbyCameraLookAtYHook?.Original(self, distance, lowPoint, midPoint, highPoint) ?? 0f;
        }
        catch (Exception ex)
        {
            _phaseRecording.Phase2ECalculateLookAtYLastError = ex.Message;
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
        _phaseRecording.Phase2ECalculateLookAtYLastError = error;
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
        var callIndex = ++_phaseRecording.Phase2FSetCameraCurveMidPointCallCount;
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
            _hookLifecycle.SetCameraCurveMidPointHook?.Original(self, value);
            TryApplyPhase2GSetCameraCurveMidPointOverride(self, frame, out phase2GStatus, out phase2GError);
        }
        catch (Exception ex)
        {
            _phaseRecording.Phase2FSetCameraCurveMidPointLastError = ex.Message;
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
        _phaseRecording.Phase2FSetCameraCurveMidPointLastError = error;
        RecordPhase2FSetCameraCurveMidPointCall(
            BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, afterCaptured ? after : null, activeBefore, activeAfter, status, error));
    }

    private void CalculateCameraCurveLowAndHighPointDetour(nint self, float value)
    {
        var callIndex = ++_phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointCallCount;
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
            _hookLifecycle.CalculateCameraCurveLowAndHighPointHook?.Original(self, value);
            TryApplyPhase2GLowHighCurveOverride(self, frame, out phase2GStatus, out phase2GError);
        }
        catch (Exception ex)
        {
            _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointLastError = ex.Message;
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
        _phaseRecording.Phase2FCalculateCameraCurveLowAndHighPointLastError = error;
        RecordPhase2FCalculateCameraCurveLowAndHighPointCall(
            BuildPhase2FGeneratedCurveCall(callIndex, frame, value, beforeCaptured ? before : null, afterCaptured ? after : null, activeBefore, activeAfter, status, error));
    }

    private bool TryApplyPhase2GSetCameraCurveMidPointOverride(nint self, int? frame, out string status, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryGetPhase2GGenerationOverrideCurve(out var curve, out var skippedReason))
        {
            status = $"skipped:{skippedReason}";
            _phaseRecording.Phase2GGenerationOverrideLastStatus = status;
            _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = skippedReason;
            _transitionDiagnostics.RecordPhase2GSkipped(skippedReason);
            RecordTransitionEvent("Phase 2G setMid skipped", skippedReason);
            return false;
        }

        _phaseRecording.Phase2GGenerationOverrideSetMidAttemptCount++;
        RecordTransitionEvent("Phase 2G setMid attempted", $"frame={FormatFrame(frame)}");
        try
        {
            // Original is intentionally called first so native generation can keep its side effects;
            // Phase 2G only replaces the generated MidPoint value after that generation step.
            // Do not add Framework.Update maintenance or direct SceneCamera writes here.
            WriteCurvePointY(self, LobbyCameraExpandedMidPointOffset, curve.Mid);
            MarkPhase2GGenerationOverrideApplied(frame, "set-mid-applied");
            _phaseRecording.Phase2GGenerationOverrideSetMidAppliedCount++;
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
            _phaseRecording.Phase2GGenerationOverrideLastStatus = "set-mid-failed";
            _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = string.Empty;
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
            _phaseRecording.Phase2GGenerationOverrideLastStatus = status;
            _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = skippedReason;
            _transitionDiagnostics.RecordPhase2GSkipped(skippedReason);
            RecordTransitionEvent("Phase 2G lowHigh skipped", skippedReason);
            return false;
        }

        _phaseRecording.Phase2GGenerationOverrideLowHighAttemptCount++;
        RecordTransitionEvent("Phase 2G lowHigh attempted", $"frame={FormatFrame(frame)}");
        try
        {
            // Original computes Low/High from the current generated inputs first; the post-original
            // write preserves native state changes and only pins the generated curve targets.
            // Final yaw/pitch/distance mismatch is expected and remains non-blocking.
            WriteCurvePointY(self, LobbyCameraExpandedLowPointOffset, curve.Low);
            WriteCurvePointY(self, LobbyCameraExpandedHighPointOffset, curve.High);
            MarkPhase2GGenerationOverrideApplied(frame, "low-high-applied");
            _phaseRecording.Phase2GGenerationOverrideLowHighAppliedCount++;
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
            _phaseRecording.Phase2GGenerationOverrideLastStatus = "low-high-failed";
            _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = string.Empty;
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
                _hookLifecycle.State == TitleBackgroundServiceState.Ready,
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
        if (_hookLifecycle.State != TitleBackgroundServiceState.Ready)
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
        _phaseRecording.Phase2GGenerationOverrideLastAppliedFrame = frame;
        _phaseRecording.Phase2GGenerationOverrideLastAppliedSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        _phaseRecording.Phase2GGenerationOverrideLastStatus = status;
        _phaseRecording.Phase2GGenerationOverrideLastSkippedReason = string.Empty;
    }

    private static void WriteCurvePointY(nint lobbyCameraAddress, int offset, float value)
    {
        var point = (CurvePoint*)((byte*)lobbyCameraAddress + offset);
        point->Y = TitleBackgroundPreset.SanitizeCoordinate(value);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (_hookLifecycle.Disposed)
        {
            return;
        }

        if (TryReadCurrentLobbyMap(out var currentMap))
        {
            EndCharaSelectTitleBackgroundSessionIfNeeded(currentMap, "framework-update");
        }

        CapturePhase2CTimelineOnFrameworkUpdate();
        CaptureCameraProbeTimelineOnFrameworkUpdate();
        CapturePreLoginCameraOnFrameworkUpdate();
        CaptureViewReplayTraceOnFrameworkUpdate();
        MaintainCharaSelectCharacterPlacement();
        MaintainCharaSelectEnvironmentNoon();
        MaintainCharaSelectEnvironmentClearSky();
        UpdateSelfTestOnFrameworkUpdate();
        UpdateAutomaticQuickCheck();
    }
}

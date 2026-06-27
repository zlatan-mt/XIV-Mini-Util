// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.Diagnostics.cs
// Description: TitleBackground の診断レポート生成と遷移診断記録を提供する
// Reason: /xmutbgdiag のレポート生成を TitleScreenBackgroundService の本体ロジックから分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    private bool TryGetLatestPhase2CTimelineSnapshot(out TitleBackgroundPhase2CTimelineSnapshot snapshot)
    {
        if (_phase2CTimelineSnapshots.Count == 0)
        {
            snapshot = default;
            return false;
        }

        snapshot = _phase2CTimelineSnapshots.Values
            .OrderByDescending(sample => sample.Frame)
            .First();
        return true;
    }

    private static string CharacterPlacementStatusToQuickCheckTriState(string status)
    {
        return status.Equals("observed", StringComparison.OrdinalIgnoreCase)
            ? "True"
            : status.Equals("not-observed", StringComparison.OrdinalIgnoreCase)
                ? "False"
                : "Unknown";
    }

    private static string VerdictToQuickCheckTriState(string verdict)
    {
        return verdict.Equals("observed", StringComparison.OrdinalIgnoreCase)
            ? "True"
            : verdict.Equals("not-observed", StringComparison.OrdinalIgnoreCase)
                ? "False"
                : "Unknown";
    }

    private void SaveQuickCheckResult(TitleBackgroundQuickCheckResult result)
    {
        _configuration.TitleBackgroundLastQuickCheckResult = result.Level;
        _configuration.TitleBackgroundLastQuickCheckCandidateId = result.CandidateId == "none" ? string.Empty : result.CandidateId;
        _configuration.TitleBackgroundLastQuickCheckReason = result.Reason;
        _configuration.TitleBackgroundLastQuickCheckNextAction = result.NextAction;
        _configuration.TitleBackgroundLastQuickCheckTime = result.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
        _configuration.TitleBackgroundLastQuickCheckDetailFileName = result.DetailFileName;
        _configuration.Save();
        SaveQuickCheckDetailFile(result);
    }

    private void SaveQuickCheckDetailFile(TitleBackgroundQuickCheckResult result)
    {
        try
        {
            var path = Path.Combine(_configDirectory, result.DetailFileName);
            File.WriteAllLines(path, result.DetailLines);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Failed to write QuickCheck detail.");
        }
    }

    private TitleBackgroundCharacterSelectOverrideCandidate ResolveCurrentOverrideCandidate()
    {
        return TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(BuildPhase2PManualCandidateSlots()));
    }

    private int GetPhase2GApplyCount()
    {
        return _phase2GGenerationOverrideSetMidAppliedCount + _phase2GGenerationOverrideLowHighAppliedCount;
    }

    private bool IsQuickCheckCharaSelectObserved()
    {
        return _quickCheckState.RunState is TitleBackgroundQuickCheckRunState.CharaSelectObserved
            or TitleBackgroundQuickCheckRunState.LoggedInObserved
            or TitleBackgroundQuickCheckRunState.Completed;
    }

    private IReadOnlyList<string> BuildTransitionDiagnosticSummaryLines(
        bool automaticInvocation,
        string currentCaptureStatus,
        float? currentDirH,
        float? currentDirV,
        float? currentDistance,
        Vector3? currentSceneCameraPosition,
        Vector3? currentLookAtVector)
    {
        // 自動確認時は loginTransitionSafety / sceneReadyAcceptedMultipleTimes / post-login 異常を
        // current run 内で発生したものだけで判定する。過去の累積・sticky 履歴で current run を
        // unsafe にしない。通常の /xmutbgdiag は累積値・累積履歴を維持して長期傾向を残す。
        var runScopedVerdict = automaticInvocation && IsRunScopedQuickCheckActive();
        var verdictEventSeqStart = _quickCheckState.TransitionEventSeqStart;
        var verdictSceneReadyAcceptedCount = runScopedVerdict
            ? GetVerdictSceneReadyAcceptedCount(automaticInvocation)
            : _transitionDiagnostics.SceneReadyAcceptedCount;
        var counters = new TitleBackgroundTransitionCounters(
            _phase2ECalculateLookAtYCallCount,
            _phase2FSetCameraCurveMidPointCallCount,
            _phase2FCalculateCameraCurveLowAndHighPointCallCount,
            _phase2GGenerationOverrideSetMidAttemptCount,
            _phase2GGenerationOverrideLowHighAttemptCount,
            _sceneReadySignalAcceptedCount,
            _sceneReadySignalCallCount);
        var delta = _transitionDiagnostics.ComputeDeltaSinceLastDiagnostic(counters);
        var currentMapAvailableBeforeCleanup = TryReadCurrentLobbyMap(out var currentMap);
        if (_clientState.IsLoggedIn || currentMapAvailableBeforeCleanup)
        {
            EndCharaSelectTitleBackgroundSessionIfNeeded(currentMap, "diagnostic");
        }

        var currentLobbyMap = TryReadCurrentLobbyMap(out currentMap)
            ? currentMap.ToString()
            : "unavailable";
        var isCharaSelectOrTitleBackground = IsCharaSelectOrTitleBackground(currentMap);
        var staleAdapterAfterLogin = _clientState.IsLoggedIn
            && _charaSelectCameraAdapter.State is not TitleBackgroundCharaSelectCameraAdapterState.Inactive
            and not TitleBackgroundCharaSelectCameraAdapterState.Stopping;
        var staleCurrentLobbyMapAfterLogin = _clientState.IsLoggedIn && isCharaSelectOrTitleBackground;
        var staleSceneOverrideAfterLogin = _clientState.IsLoggedIn && _activeSceneOverride;
        _transitionDiagnostics.MarkPostLoginStaleState(
            BuildTransitionSnapshot("report-time"),
            staleAdapterAfterLogin,
            staleCurrentLobbyMapAfterLogin,
            staleSceneOverrideAfterLogin);

        // 状態型異常（stale adapter / active scene override）: run-scoped 時は現時点の状態のみ。
        var adapterStaleForVerdict = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
            runScopedVerdict,
            _transitionDiagnostics.StaleAdapterStateAfterLogin,
            staleAdapterAfterLogin);
        var sceneOverrideActiveForVerdict = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
            runScopedVerdict,
            _transitionDiagnostics.StaleSceneOverrideStateAfterLogin,
            staleSceneOverrideAfterLogin);
        // イベント型異常（post-login Phase2G / post-login sceneReady）: run-scoped 時は run 開始 seq より後のみ。
        var phase2GAppliedAfterLoginForVerdict = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
            runScopedVerdict,
            _transitionDiagnostics.Phase2GAppliedAfterLogin,
            _transitionDiagnostics.LastPhase2GAppliedAfterLoginEventSeq,
            verdictEventSeqStart);
        var phase2GLastAppliedSeqForVerdict = phase2GAppliedAfterLoginForVerdict
            ? _transitionDiagnostics.LastPhase2GAppliedAfterLoginEventSeq
            : (runScopedVerdict ? 0L : _transitionDiagnostics.LastPhase2GAppliedAfterLoginEventSeq);
        var postLoginSceneReadyForVerdict = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
            runScopedVerdict,
            _transitionDiagnostics.PostLoginSceneReadyAccepted,
            _transitionDiagnostics.LastSceneReadyAcceptedAfterLoginEventSeq,
            verdictEventSeqStart);
        var sceneReadyLastAfterLoginSeqForVerdict = postLoginSceneReadyForVerdict
            ? _transitionDiagnostics.LastSceneReadyAcceptedAfterLoginEventSeq
            : (runScopedVerdict ? 0L : _transitionDiagnostics.LastSceneReadyAcceptedAfterLoginEventSeq);

        var input = new TitleBackgroundTransitionSummaryInput(
            new TitleBackgroundTransitionContext(
                _clientState.IsLoggedIn,
                isCharaSelectOrTitleBackground,
                _clientState.TerritoryType,
                _clientState.TerritoryType.ToString(),
                currentLobbyMap),
            new TitleBackgroundTransitionSceneOverrideState(
                _activeSceneOverride,
                _lastOverrideApplied,
                _activeSceneOverrideLobbyType.ToString(),
                FormatNone(_activeSceneOverridePath),
                FormatNone(_lastHistoricalOverridePath),
                currentLobbyMap,
                _lastCurrentLobbyMapResetReason,
                _sceneOverrideCleanupReason,
                sceneOverrideActiveForVerdict),
            new TitleBackgroundTransitionAdapterState(
                _charaSelectCameraAdapter.State.ToString(),
                _charaSelectCameraAdapter.LastEvent,
                _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
                adapterStaleForVerdict),
            new TitleBackgroundTransitionPhase2GState(
                _transitionDiagnostics.LastPhase2GApplyContext,
                phase2GAppliedAfterLoginForVerdict,
                phase2GLastAppliedSeqForVerdict,
                _transitionDiagnostics.Phase2GAppliedAfterLeavingCharaSelect,
                _transitionDiagnostics.LastPhase2GAllowedReason,
                string.IsNullOrWhiteSpace(_transitionDiagnostics.LastPhase2GSkippedReason)
                    ? FormatNone(_phase2GGenerationOverrideLastSkippedReason)
                    : _transitionDiagnostics.LastPhase2GSkippedReason),
            new TitleBackgroundTransitionCameraState(
                currentCaptureStatus,
                FormatFloat(currentDirH),
                FormatFloat(currentDirV),
                FormatFloat(currentDistance),
                FormatVector(currentSceneCameraPosition),
                FormatVector(currentLookAtVector)),
            counters,
            delta,
            _transitionDiagnostics.FirstEvent,
            _transitionDiagnostics.LastEvent,
            _transitionDiagnostics.EventCount,
            _transitionDiagnostics.SceneReadyRawCallCount,
            verdictSceneReadyAcceptedCount,
            _transitionDiagnostics.SceneReadyRejectedCount,
            verdictSceneReadyAcceptedCount > 1,
            _transitionDiagnostics.SceneReadyLastAcceptedReason,
            _transitionDiagnostics.SceneReadyLastRejectedReason,
            _transitionDiagnostics.SceneReadyLastAcceptedSceneGeneration,
            _transitionDiagnostics.AcceptedGenerations,
            postLoginSceneReadyForVerdict,
            sceneReadyLastAfterLoginSeqForVerdict);
        return TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(input);
    }

    public const string BulkDiagnosticFileName = "title-background-bulk-diag.txt";

    // 一括診断: 1回のボタン操作で診断束を取得し、ファイル保存＋呼び出し側で clipboard。
    // GetDiagnosticLines は保持済み snapshot を整形するだけで、post-login に CharaSelect の
    // native read（TryReadCurrentCharacterAim）を新たに行わないため、安全境界を踏み越えない。
    public IReadOnlyList<string> RunBulkDiagnostic()
    {
        // 一括診断は要約のみを返す。per-frame タイムラインや .objectCandidate[ などの大量行は
        // includeDetailedPhase2Diagnostics:false 経路が transition/placement/delivery の別 dump
        // ファイルへ退避するため、本体（17,000 行超）は数百行の要約へ縮小される。
        var lines = GetDiagnosticLines(includeDetailedPhase2Diagnostics: false);
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(_configDirectory, BulkDiagnosticFileName);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground bulk diagnostic save failed.");
        }

        return lines;
    }

    private string SaveTransitionDiagnosticDump(IReadOnlyList<string> transitionSummaryLines)
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(_configDirectory, "title-background-transitiondiag.txt");
            var detailedLines = GetDiagnosticLines(includeDetailedPhase2Diagnostics: true)
                .Where(line => !line.Contains(".objectCandidate[", StringComparison.Ordinal))
                .ToList();
            AddCharacterPlacementTopCandidateLines(detailedLines);
            var dump = _transitionDiagnostics.BuildDetailedDump(transitionSummaryLines, detailedLines);
            File.WriteAllText(path, dump);
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground transition diagnostic save failed.");
            return string.Empty;
        }
    }

    private string SaveCharacterPlacementPlacementDiagnosticDump()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(_configDirectory, "title-background-placementdiag.txt");
            var lines = new List<string>();
            AddCharacterPlacementPreLoginCaptureLines(lines);
            AddCharacterPlacementSummaryLines(lines, TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(_phase2MPlacementFrames.Values));
            foreach (var frame in _phase2MPlacementFrames.Values.OrderBy(frame => frame.Frame))
            {
                AddCharacterPlacementPlacementFrameLines(lines, frame);
            }

            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground placement diagnostic save failed.");
            return string.Empty;
        }
    }

    private string SaveDeliveryDeliveryDiagnosticDump(TitleBackgroundDeliverySummary summary)
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(_configDirectory, "title-background-deliverydiag.txt");
            var lines = new List<string>
            {
                "Delivery.delivery=character-select-background-mvp",
            };
            TitleBackgroundDeliveryDiagnostic.AddLines(lines, summary);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground delivery diagnostic save failed.");
            return string.Empty;
        }
    }

    private void RecordTransitionEvent(string eventName, string reason = "", string error = "")
    {
        _transitionDiagnostics.Record(eventName, BuildTransitionSnapshot(eventName), reason, error);
    }

    private Dictionary<string, string> BuildTransitionSnapshot(string eventName)
    {
        var currentMapAvailable = TryReadCurrentLobbyMap(out var currentMap);
        var isCharaSelectOrTitleBackground = IsCharaSelectOrTitleBackground(currentMap);
        return new Dictionary<string, string>
        {
            ["event"] = eventName,
            ["runtimeMode"] = _configuration.TitleBackgroundRuntimeMode.ToString(),
            ["probeMode"] = IsHookProbeMode().ToString(),
            ["hooksEnabled"] = AreAnyHooksEnabled().ToString(),
            ["sceneOverrideEnabled"] = IsSceneOverrideEnabled().ToString(),
            ["cameraOverrideEnabled"] = _configuration.TitleBackgroundCameraOverrideEnabled.ToString(),
            ["cameraOverrideApplyPending"] = _cameraApplyPending.ToString(),
            ["isLoggedIn"] = _clientState.IsLoggedIn.ToString(),
            ["isTitleScreen"] = (!_clientState.IsLoggedIn).ToString(),
            ["isCharacterSelect"] = (currentMapAvailable && currentMap == GameLobbyType.CharaSelect).ToString(),
            ["isLobby"] = (currentMapAvailable && currentMap != GameLobbyType.None).ToString(),
            ["isCharaSelectOrTitleBackground"] = isCharaSelectOrTitleBackground.ToString(),
            ["currentTerritoryId"] = _clientState.TerritoryType.ToString(),
            ["lastOverrideLobbyType"] = _lastOverrideLobbyType.ToString(),
            ["resolvedLobbyMap"] = ResolveSceneReadySignalLobbyMap().ToString(),
            ["CurrentLobbyMap"] = currentMapAvailable ? currentMap.ToString() : "unavailable",
            ["originalTerritoryPath"] = FormatNone(_lastOverrideOriginalPath),
            ["overrideTerritoryPath"] = FormatNone(_lastOverrideNewPath),
            ["overrideTerritoryId"] = _lastOverrideTerritoryId.ToString(),
            ["layerFilterKey"] = _lastOverrideLayerFilterKey.ToString(),
            ["lastOverrideApplied"] = _lastOverrideApplied.ToString(),
            ["activeSceneOverride"] = _activeSceneOverride.ToString(),
            ["activeCharaSelectSession"] = _charaSelectTitleBackgroundSessionActive.ToString(),
            ["activeSceneGeneration"] = _activeCharaSelectSceneGeneration.ToString(),
            ["lastOverrideOriginalPath"] = FormatNone(_lastOverrideOriginalPath),
            ["lastOverrideNewPath"] = FormatNone(_lastOverrideNewPath),
            ["lastOverrideTerritoryId"] = _lastOverrideTerritoryId.ToString(),
            ["lastOverrideLayerFilterKey"] = _lastOverrideLayerFilterKey.ToString(),
            ["overrideMutationBranchArmed"] = IsOverrideMutationBranchArmed().ToString(),
            ["adapterState"] = _charaSelectCameraAdapter.State.ToString(),
            ["adapterLastEvent"] = _charaSelectCameraAdapter.LastEvent,
            ["sceneGeneration"] = _charaSelectCameraAdapter.RuntimeState.SceneGeneration.ToString(),
            ["curveAppliedSceneGeneration"] = _charaSelectCameraAdapter.LastCurveAppliedSceneGeneration.ToString(),
            ["lookAtYAppliedSceneGeneration"] = _charaSelectCameraAdapter.LastLookAtYAppliedSceneGeneration.ToString(),
            ["runtimeRestoreSceneGeneration"] = _lastCharaSelectCameraRuntimeRestoreSceneGeneration.ToString(),
            ["shouldRestoreRuntimeCameraState"] = _charaSelectCameraAdapter.ShouldRestoreRuntimeCameraState().ToString(),
            ["runtimeRecordStatus"] = _lastCharaSelectCameraRuntimeRecordStatus,
            ["runtimeRecordFailureReason"] = FormatNone(_lastCharaSelectCameraRuntimeRecordError),
            ["runtimeRestoreFailureReason"] = FormatNone(_lastCharaSelectCameraRuntimeRestoreFailureReason),
            ["phase2G.setMid.attemptCount"] = _phase2GGenerationOverrideSetMidAttemptCount.ToString(),
            ["phase2G.setMid.appliedCount"] = _phase2GGenerationOverrideSetMidAppliedCount.ToString(),
            ["phase2G.lowHigh.attemptCount"] = _phase2GGenerationOverrideLowHighAttemptCount.ToString(),
            ["phase2G.lowHigh.appliedCount"] = _phase2GGenerationOverrideLowHighAppliedCount.ToString(),
            ["phase2G.lastStatus"] = FormatNone(_phase2GGenerationOverrideLastStatus),
            ["phase2G.lastSkippedReason"] = FormatNone(_phase2GGenerationOverrideLastSkippedReason),
            ["phase2G.lastAppliedSceneGeneration"] = _phase2GGenerationOverrideLastAppliedSceneGeneration.ToString(),
        };
    }

    private bool IsCharaSelectOrTitleBackground(GameLobbyType currentMap)
    {
        return !_clientState.IsLoggedIn
            || IsCharaSelectOrTitleBackgroundMap(currentMap);
    }

    private static bool IsCharaSelectOrTitleBackgroundMap(GameLobbyType currentMap)
    {
        return currentMap == GameLobbyType.CharaSelect
            || currentMap == GameLobbyType.Title;
    }

    public IReadOnlyList<string> GetDiagnosticLines(
        bool includeDetailedPhase2Diagnostics = false,
        bool automaticInvocation = false)
    {
        if (!includeDetailedPhase2Diagnostics)
        {
            RecordTransitionEvent(
                automaticInvocation ? "automatic verification diagnostic collected" : "command /xmutbgdiag executed",
                "normal");
            if (_clientState.IsLoggedIn && !_postLoginDiagnosticSeen)
            {
                _postLoginDiagnosticSeen = true;
                RecordTransitionEvent("entering logged-in world if detectable", "first logged-in diagnostic");
                RecordTransitionEvent(
                    automaticInvocation ? "first post-login automatic diagnostic" : "first post-login /xmutbgdiag",
                    "normal");
            }
        }

        var sceneHooksReady = AreSceneHooksReady();
        var cameraHookReady = AreCameraHookReady();
        var cameraHookRequired = IsCameraHookRequired();
        var hooksReady = sceneHooksReady && (!cameraHookRequired || cameraHookReady);
        var hooksEnabled = IsHookEnabled(_createSceneHook)
            || IsHookEnabled(_lobbyUpdateHook)
            || IsHookEnabled(_loadLobbySceneHook)
            || IsHookEnabled(_lobbySceneLoadedHook)
            || IsHookEnabled(_cameraFixOnHook)
            || IsHookEnabled(_calculateLobbyCameraLookAtYHook)
            || IsHookEnabled(_setCameraCurveMidPointHook)
            || IsHookEnabled(_calculateCameraCurveLowAndHighPointHook);
        var capturePreset = _lastCameraCaptureResult.Preset;
        var currentCameraCaptured = TryCaptureActiveCameraSnapshot(out var currentCamera, out var currentCaptureError);
        var currentCaptureStatus = currentCameraCaptured ? "success" : "failed";
        Vector3? currentSceneCameraPosition = currentCameraCaptured ? currentCamera.SceneCameraPosition : null;
        Vector3? currentLookAtVector = currentCameraCaptured ? currentCamera.LookAtVector : null;
        float? currentDirH = currentCameraCaptured ? currentCamera.DirH : null;
        float? currentDirV = currentCameraCaptured ? currentCamera.DirV : null;
        float? currentDistance = currentCameraCaptured ? currentCamera.Distance : null;
        float? currentInterpDistance = currentCameraCaptured ? currentCamera.InterpDistance : null;
        float? currentFovY = currentCameraCaptured ? currentCamera.FovY : null;
        var transitionSummaryLines = BuildTransitionDiagnosticSummaryLines(
            automaticInvocation,
            currentCaptureStatus,
            currentDirH,
            currentDirV,
            currentDistance,
            currentSceneCameraPosition,
            currentLookAtVector).ToList();
        AddCharacterPlacementPreLoginCaptureLines(transitionSummaryLines);
        var transitionSafety = transitionSummaryLines
            .FirstOrDefault(line => line.StartsWith("transition.verdict.loginTransitionSafety=", StringComparison.Ordinal))
            ?.Split('=')[1] ?? "unknown";
        var transitionDetailPath = !includeDetailedPhase2Diagnostics && _transitionDiagnostics.EventCount > 0
            ? SaveTransitionDiagnosticDump(transitionSummaryLines)
            : string.Empty;
        var phase2CTimelineSamples = BuildPhase2CTimelineSamples();
        var phase2CStableSample = phase2CTimelineSamples
            .Where(sample => sample.ActiveCameraCaptured || sample.LobbyCameraCaptured)
            .Where(sample => sample.Frame <= 60)
            .OrderByDescending(sample => sample.Frame)
            .FirstOrDefault();
        var phase2CVerdicts = BuildPhase2CVerdicts(phase2CStableSample);
        var phase2DLatestSample = phase2CTimelineSamples
            .Where(sample => sample.ActiveCameraCaptured || sample.LobbyCameraCaptured || sample.ExpandedLobbyCameraCaptured)
            .OrderByDescending(sample => sample.Frame)
            .FirstOrDefault();
        var phase2DVerdicts = BuildPhase2DVerdicts(phase2CTimelineSamples);
        var phase2ECallSamples = BuildPhase2EProbeSamples();
        var phase2EFinalStableLookAtY = phase2DLatestSample.ActiveCameraCaptured
            ? phase2DLatestSample.SceneCameraLookAtVector?.Y
            : null;
        var phase2EVerdicts = TitleBackgroundCameraProbeReport.AnalyzePhase2E(
            phase2ECallSamples,
            phase2EFinalStableLookAtY);
        var phase2FCurveSamples = BuildPhase2FCurveTimelineSamples(phase2CTimelineSamples);
        var phase2FVerdicts = TitleBackgroundCameraProbeReport.AnalyzePhase2F(phase2FCurveSamples);
        var phase2FGeneratedCurveTransitions = BuildPhase2FGeneratedCurveTransitionSummary();
        var phase2GGeneratedCurveOverrideEffective = BuildPhase2GGeneratedCurveOverrideEffectiveVerdict(phase2FCurveSamples);
        var phase2GFinalLookAtYMatchesGeneratedCurve = BuildPhase2GFinalLookAtYMatchesGeneratedCurveVerdict(phase2DLatestSample);
        var phase2GFinalYawPitchDistanceMatchesPreset = BuildPhase2GFinalCameraStateMatchesPresetVerdict(phase2DLatestSample);
        var phase2MSummary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(_phase2MPlacementFrames.Values);
        EvaluateCharacterPlacementExperimentalApply(phase2MSummary);
        var phase2MObjectTableStats = GetLatestCharacterPlacementObjectTableStats();
        var phase2MActorCandidateStatus = GetLatestCharacterPlacementActorCandidateStatus();
        var phase2MActorCandidateReason = GetLatestCharacterPlacementActorCandidateReason();
        var phase2MActorSource = GetLatestCharacterPlacementActorSource();
        var phase2MNextNativeSourceToInspect = GetLatestCharacterPlacementNextNativeSourceToInspect();
        var phase2NCurrentLobbyMapAvailable = TryReadCurrentLobbyMap(out var phase2NCurrentLobbyMap);
        var phase2NCurrentObjectTableValid = !_clientState.IsLoggedIn
            && phase2NCurrentLobbyMapAvailable
            && phase2NCurrentLobbyMap != GameLobbyType.None
            && IsCharaSelectOrTitleBackground(phase2NCurrentLobbyMap);
        var phase2NCurrentObjectTableInvalidReason = phase2NCurrentObjectTableValid
            ? "none"
            : "post-login-world-object-table-not-valid-for-chara-select";
        // Delivery 判定も自動確認時は run-scoped 値を使う。過去 run の sticky な scene override leak /
        // Phase2G 漏れや累積 placement count を今回 run の判定へ持ち込まない。通常診断は累積を維持する。
        var deliveryRunScoped = automaticInvocation && IsRunScopedQuickCheckActive();
        var phase2NSceneOverrideActiveAfterLoginDetected = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedStateAnomaly(
            deliveryRunScoped,
            _transitionDiagnostics.StaleSceneOverrideStateAfterLogin,
            _clientState.IsLoggedIn && _activeSceneOverride);
        var deliveryPhase2GAppliedAfterLogin = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedEventAnomaly(
            deliveryRunScoped,
            _transitionDiagnostics.Phase2GAppliedAfterLogin,
            _transitionDiagnostics.LastPhase2GAppliedAfterLoginEventSeq,
            _quickCheckState.TransitionEventSeqStart);
        var deliveryCharacterPlacementApplied = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
            deliveryRunScoped,
            _charaSelectCharacterPlacementCount,
            _quickCheckState.CharacterPlacementCountStart) > 0;
        // 背景適用も run-scoped 化する。今回 run で override が適用されたかを差分で判定し、
        // 0 回なら historical flag/path も Delivery 判定へ入れない（前回成功を引き継がない）。
        var deliveryOverrideAppliedThisRun = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedCount(
            deliveryRunScoped,
            _quickCheckOverrideAppliedCount,
            _quickCheckState.OverrideAppliedCountStart) > 0;
        var deliveryLastOverrideApplied = deliveryRunScoped
            ? deliveryOverrideAppliedThisRun
            : _lastOverrideApplied && _lastOverrideLobbyType == GameLobbyType.CharaSelect;
        var deliveryHistoricalOverrideApplied = deliveryRunScoped
            ? deliveryOverrideAppliedThisRun
            : _lastOverrideApplied;
        var deliveryHistoricalOverridePath = deliveryRunScoped && !deliveryOverrideAppliedThisRun
            ? string.Empty
            : _lastHistoricalOverridePath;
        var phase2NDeliverySummary = TitleBackgroundDeliveryDiagnostic.BuildSummary(
            _configuration.TitleBackgroundCharacterSelectBackgroundMode,
            _configuration.TitleBackgroundCharacterSelectLightingMode,
            _configuration.TitleBackgroundSelectedPresetId,
            _configuration.TitleBackgroundTerritoryPath,
            GetEffectiveOverrideTerritoryId(),
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            IsSceneOverrideEnabled(),
            deliveryLastOverrideApplied,
            phase2MSummary.Resolution,
            phase2MSummary.TransformValidity,
            phase2MSummary.ActorVisible,
            phase2MSummary.ZeroPositionCandidateCount,
            phase2MSummary.NonZeroPositionCandidateCount,
            phase2MSummary.DrawObjectNonNullCount,
            phase2MSummary.ModelLikeNonNullCount,
            phase2MSummary.BestCandidate,
            GetLatestCharacterPlacementSourceDiscovery(),
            transitionSafety,
            phase2NCurrentObjectTableValid,
            phase2NCurrentObjectTableInvalidReason,
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            BuildPhase2PManualCandidateSlots(),
            deliveryHistoricalOverrideApplied,
            deliveryHistoricalOverridePath,
            GetVerdictSceneReadyAcceptedCount(automaticInvocation) > 1,
            phase2NSceneOverrideActiveAfterLoginDetected,
            deliveryPhase2GAppliedAfterLogin,
            phase2MSummary.NativeCharacterSource,
            deliveryCharacterPlacementApplied);
        var deliveryDetailPath = !includeDetailedPhase2Diagnostics
            ? SaveDeliveryDeliveryDiagnosticDump(phase2NDeliverySummary)
            : string.Empty;
        var placementDetailPath = !includeDetailedPhase2Diagnostics && _phase2MPlacementFrames.Count > 0
            ? SaveCharacterPlacementPlacementDiagnosticDump()
            : string.Empty;
        if (!includeDetailedPhase2Diagnostics)
        {
            // Keep normal /xmutbgdiag as a long-term Phase 2G summary. Detailed timelines and call
            // traces remain failure-only so routine checks stay short.
            return
            [
                $"hooksEnabled={hooksEnabled}",
                $"calculateLobbyCameraLookAtYHookEnabled={IsHookEnabled(_calculateLobbyCameraLookAtYHook)}",
                $"setCameraCurveMidPointHookEnabled={IsHookEnabled(_setCameraCurveMidPointHook)}",
                $"calculateCameraCurveLowAndHighPointHookEnabled={IsHookEnabled(_calculateCameraCurveLowAndHighPointHook)}",
                $"sceneReadySignal.acceptedCount={_sceneReadySignalAcceptedCount}",
                $"phase2E.calculateLobbyCameraLookAtY.callCount={_phase2ECalculateLookAtYCallCount}",
                $"phase2E.calculateLobbyCameraLookAtY.lastError={FormatNone(_phase2ECalculateLookAtYLastError)}",
                $"phase2F.setCameraCurveMidPoint.callCount={_phase2FSetCameraCurveMidPointCallCount}",
                $"phase2F.setCameraCurveMidPoint.lastError={FormatNone(_phase2FSetCameraCurveMidPointLastError)}",
                $"phase2F.calculateCameraCurveLowAndHighPoint.callCount={_phase2FCalculateCameraCurveLowAndHighPointCallCount}",
                $"phase2F.calculateCameraCurveLowAndHighPoint.lastError={FormatNone(_phase2FCalculateCameraCurveLowAndHighPointLastError)}",
                $"phase2G.generationOverride.enabled={IsPhase2GGenerationOverrideConfigured()}",
                "phase2G.generationOverride.writeTiming=post-original",
                $"phase2G.generationOverride.setMid.attemptCount={_phase2GGenerationOverrideSetMidAttemptCount}",
                $"phase2G.generationOverride.setMid.appliedCount={_phase2GGenerationOverrideSetMidAppliedCount}",
                $"phase2G.generationOverride.lowHigh.attemptCount={_phase2GGenerationOverrideLowHighAttemptCount}",
                $"phase2G.generationOverride.lowHigh.appliedCount={_phase2GGenerationOverrideLowHighAppliedCount}",
                $"phase2G.generationOverride.lastStatus={FormatNone(_phase2GGenerationOverrideLastStatus)}",
                $"phase2G.generationOverride.lastSkippedReason={FormatNone(_phase2GGenerationOverrideLastSkippedReason)}",
                $"verdict.phase2G.generatedCurveOverrideEffective={phase2GGeneratedCurveOverrideEffective}",
                $"verdict.phase2G.finalLookAtYMatchesGeneratedCurve={phase2GFinalLookAtYMatchesGeneratedCurve}",
                $"verdict.phase2G.finalYawPitchDistanceMatchesPreset={phase2GFinalYawPitchDistanceMatchesPreset}",
                "verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False",
                $"verdict.phase2G.finalYawPitchDistanceMatchesPreset.loginTransitionConditional={TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe(phase2GFinalYawPitchDistanceMatchesPreset, transitionSafety)}",
                // Deprecated compatibility output. Prefer finalYawPitchDistanceMatchesPreset.
                $"verdict.phase2G.finalCameraStateMatchesPreset={phase2GFinalYawPitchDistanceMatchesPreset}",
                $"phase2M.actorDiagnostic.status={phase2MSummary.ActorDiagnosticStatus}",
                $"phase2M.actor.visible={phase2MSummary.ActorVisible}",
                $"phase2M.actor.groundAligned={phase2MSummary.ActorGroundAligned}",
                $"phase2M.camera.framesActor={phase2MSummary.CameraFramesActor}",
                $"phase2M.objectTable.totalScanned={phase2MObjectTableStats.TotalScanned}",
                $"phase2M.objectTable.namedCount={phase2MObjectTableStats.NamedCount}",
                $"phase2M.objectTable.playerLikeCount={phase2MObjectTableStats.PlayerLikeCount}",
                $"phase2M.objectTable.battleCharaCount={phase2MObjectTableStats.BattleCharaCount}",
                $"phase2M.objectTable.eventNpcCount={phase2MObjectTableStats.EventNpcCount}",
                $"phase2M.objectTable.nearCameraCount={phase2MObjectTableStats.NearCameraCount}",
                $"phase2M.objectTable.nearConfiguredCharacterCount={phase2MObjectTableStats.NearConfiguredCharacterCount}",
                $"phase2M.actorCandidate.status={phase2MActorCandidateStatus}",
                $"phase2M.actorCandidate.reason={phase2MActorCandidateReason}",
                $"phase2M.actorCandidate.zeroPositionCandidateCount={phase2MSummary.ZeroPositionCandidateCount}",
                $"phase2M.actorCandidate.nonZeroPositionCandidateCount={phase2MSummary.NonZeroPositionCandidateCount}",
                $"phase2M.actorCandidate.namedCandidateCount={phase2MSummary.NamedCandidateCount}",
                $"phase2M.actorCandidate.visibleHintTrueCount={phase2MSummary.VisibleHintTrueCount}",
                $"phase2M.actorCandidate.drawObjectNonNullCount={phase2MSummary.DrawObjectNonNullCount}",
                $"phase2M.actorCandidate.modelLikeNonNullCount={phase2MSummary.ModelLikeNonNullCount}",
                $"phase2M.actorCandidate.uniqueAddressCount={phase2MSummary.UniqueAddressCount}",
                $"phase2M.actorCandidate.uniqueObjectIdCount={phase2MSummary.UniqueObjectIdCount}",
                $"phase2M.actorCandidate.uniqueEntityIdCount={phase2MSummary.UniqueEntityIdCount}",
                $"phase2M.actorCandidate.samePositionGroupCount={phase2MSummary.SamePositionGroupCount}",
                $"phase2M.actorCandidate.objectTableIndexRange={phase2MSummary.ObjectTableIndexRange}",
                $"phase2M.actorCandidate.sourceBreakdown={phase2MSummary.SourceBreakdown}",
                $"phase2M.actorCandidate.transformValidity={phase2MSummary.TransformValidity}",
                $"phase2M.actorCandidate.identityConfidence={phase2MSummary.IdentityConfidence}",
                $"phase2M.actorCandidate.stubLikelihood={phase2MSummary.StubLikelihood}",
                $"phase2M.actorCandidate.bestCandidateIndex={phase2MSummary.BestCandidateIndex}",
                $"phase2M.actorCandidate.bestCandidateReason={phase2MSummary.BestCandidateReason}",
                $"phase2M.actorCandidate.scoring.enabled={phase2MSummary.ScoringEnabled}",
                $"phase2M.actorCandidate.bestScore={phase2MSummary.BestScore}",
                $"phase2M.actorCandidate.bestCandidate={phase2MSummary.BestCandidate}",
                $"phase2M.actorCandidate.bestCandidateStableAcrossFrames={phase2MSummary.BestCandidateStableAcrossFrames}",
                $"phase2M.actorCandidate.resolution={phase2MSummary.Resolution}",
                $"phase2M.actor.source={phase2MActorSource}",
                $"phase2M.actor.nextNativeSourceToInspect={phase2MNextNativeSourceToInspect}",
                $"phase2M.sourceDiscovery.bestSource={phase2MSummary.BestSource}",
                $"phase2M.sourceDiscovery.nextNativeSourceToInspect={phase2MSummary.NextNativeSourceToInspect}",
                $"phase2M.nextAction={phase2MSummary.NextAction}",
                $"phase2M.nextAction.reason={phase2MSummary.NextActionReason}",
                $"phase2M.experimental.applyMode={_configuration.TitleBackgroundCharacterPlacementExperimentalApplyMode}",
                $"phase2M.experimental.lastStatus={FormatNone(_phase2MExperimentalLastStatus)}",
                $"phase2M.experimental.writeCount={_phase2MExperimentalWriteCount}",
                $"phase2M.experimental.skippedCount={_phase2MExperimentalSkippedCount}",
                $"verdict.phase2M.visualPlacementSafety={phase2MSummary.VisualPlacementSafety}",
                ..TitleBackgroundDeliveryDiagnostic.BuildLineList(phase2NDeliverySummary),
                $"transition.detailDump={FormatNone(string.IsNullOrWhiteSpace(transitionDetailPath) ? string.Empty : Path.GetFileName(transitionDetailPath))}",
                $"placement.detailDump={FormatNone(string.IsNullOrWhiteSpace(placementDetailPath) ? string.Empty : Path.GetFileName(placementDetailPath))}",
                $"delivery.detailDump={FormatNone(string.IsNullOrWhiteSpace(deliveryDetailPath) ? string.Empty : Path.GetFileName(deliveryDetailPath))}",
                ..transitionSummaryLines,
            ];
        }

        var effectiveOverrideTerritoryId = GetEffectiveOverrideTerritoryId();
        var lines = new List<string>
        {
            $"runtimeMode={_configuration.TitleBackgroundRuntimeMode}",
            $"probeMode={IsHookProbeMode()}",
            $"probeMutatesScene=False",
            $"probeWritesCurrentMap=False",
            $"probeEnablesCameraHook=False",
            $"automaticProbeCountersEnabled={_automaticProbeCountersEnabled}",
            $"CreateSceneProbe.callCount={_automaticProbeCounters.CreateSceneCallCount}",
            $"CreateSceneProbe.lastPath={FormatNone(_automaticProbeCounters.LastCreateScenePath)}",
            $"CreateSceneProbe.lastTerritoryId={_automaticProbeCounters.LastCreateSceneTerritoryId}",
            $"CreateSceneProbe.lastLayerFilterKey={_automaticProbeCounters.LastCreateSceneLayerFilterKey}",
            $"LobbyUpdateProbe.callCount={_automaticProbeCounters.LobbyUpdateCallCount}",
            $"LobbyUpdateProbe.lastMapId={_automaticProbeCounters.LastLobbyUpdateMapId}",
            $"LobbyUpdateProbe.lastTime={_automaticProbeCounters.LastLobbyUpdateTime}",
            $"LoadLobbySceneProbe.callCount={_automaticProbeCounters.LoadLobbySceneCallCount}",
            $"LoadLobbySceneProbe.lastMapId={_automaticProbeCounters.LastLoadLobbySceneMapId}",
            $"sceneOverrideEnabled={IsSceneOverrideEnabled()}",
            $"overrideMutationBranchArmed={IsOverrideMutationBranchArmed()}",
            $"overrideTerritoryPath={FormatNone(_configuration.TitleBackgroundTerritoryPath)}",
            $"overrideTerritoryId={effectiveOverrideTerritoryId}",
            $"overrideLayerFilterKey={_configuration.TitleBackgroundLayoutLayerFilterKey}",
            $"lastOverrideApplied={_lastOverrideApplied}",
            $"lastOverrideLobbyType={_lastOverrideLobbyType}",
            $"lastOverrideOriginalPath={FormatNone(_lastOverrideOriginalPath)}",
            $"lastOverrideNewPath={FormatNone(_lastOverrideNewPath)}",
            $"lastOverrideTerritoryId={_lastOverrideTerritoryId}",
            $"lastOverrideLayerFilterKey={_lastOverrideLayerFilterKey}",
            $"failureSummary={BuildFailureSummary(phase2DLatestSample)}",
            $"hooksReady={hooksReady}",
            $"sceneHooksReady={sceneHooksReady}",
            $"cameraHookReady={cameraHookReady}",
            $"cameraHookRequired={cameraHookRequired}",
            $"cameraHookEnabled={IsHookEnabled(_cameraFixOnHook)}",
            $"calculateLobbyCameraLookAtYHookEnabled={IsHookEnabled(_calculateLobbyCameraLookAtYHook)}",
            $"setCameraCurveMidPointHookEnabled={IsHookEnabled(_setCameraCurveMidPointHook)}",
            $"calculateCameraCurveLowAndHighPointHookEnabled={IsHookEnabled(_calculateCameraCurveLowAndHighPointHook)}",
            "fixOnHookPolicy=disabled-in-phase1",
            "calculateLobbyCameraLookAtYHookPolicy=read-only-probe",
            "setCameraCurveMidPointHookPolicy=phase2g-post-original-generation-override",
            "calculateCameraCurveLowAndHighPointHookPolicy=phase2g-post-original-generation-override",
            $"cameraOverrideEnabled={_configuration.TitleBackgroundCameraOverrideEnabled}",
            $"charaSelectCameraAdapter.state={_charaSelectCameraAdapter.State}",
            $"charaSelectCameraAdapter.lastEvent={_charaSelectCameraAdapter.LastEvent}",
            $"charaSelectCameraAdapter.characterPosition={FormatVector(_charaSelectCameraAdapter.Input.CharacterPosition)}",
            $"charaSelectCameraAdapter.characterRotation={_charaSelectCameraAdapter.Input.CharacterRotation:0.###}",
            $"charaSelectCameraAdapter.curveLow={FormatFloat(_charaSelectCameraAdapter.Curve.Low)}",
            $"charaSelectCameraAdapter.curveMid={FormatFloat(_charaSelectCameraAdapter.Curve.Mid)}",
            $"charaSelectCameraAdapter.curveHigh={FormatFloat(_charaSelectCameraAdapter.Curve.High)}",
            $"charaSelectCameraAdapter.runtimeYaw={FormatFloat(_charaSelectCameraAdapter.RuntimeState.Yaw)}",
            $"charaSelectCameraAdapter.runtimeYawOffset={FormatFloat(_charaSelectCameraAdapter.RuntimeState.YawOffset)}",
            $"charaSelectCameraAdapter.restoredYaw={FormatFloat(_charaSelectCameraAdapter.GetRestoredYaw())}",
            $"charaSelectCameraAdapter.runtimePitch={FormatFloat(_charaSelectCameraAdapter.RuntimeState.Pitch)}",
            $"charaSelectCameraAdapter.runtimeDistance={FormatFloat(_charaSelectCameraAdapter.RuntimeState.Distance)}",
            $"charaSelectCameraAdapter.runtimeLookAtY={FormatFloat(_charaSelectCameraAdapter.RuntimeState.LookAtY)}",
            $"charaSelectCameraAdapter.runtimeLookAt={FormatVector(_charaSelectCameraAdapter.RuntimeState.LookAt)}",
            $"charaSelectCameraAdapter.runtimeCurveLow={FormatFloat(_charaSelectCameraAdapter.RuntimeState.CurveAtRecord?.Low)}",
            $"charaSelectCameraAdapter.runtimeCurveMid={FormatFloat(_charaSelectCameraAdapter.RuntimeState.CurveAtRecord?.Mid)}",
            $"charaSelectCameraAdapter.runtimeCurveHigh={FormatFloat(_charaSelectCameraAdapter.RuntimeState.CurveAtRecord?.High)}",
            $"charaSelectCameraAdapter.runtimeCharacterRotationAtRecord={FormatFloat(_charaSelectCameraAdapter.RuntimeState.CharacterRotationAtRecord)}",
            $"charaSelectCameraAdapter.shouldSetLookAtY={_charaSelectCameraAdapter.RuntimeState.ShouldSetLookAtY}",
            $"charaSelectCameraAdapter.sceneGeneration={_charaSelectCameraAdapter.RuntimeState.SceneGeneration}",
            $"charaSelectCameraAdapter.shouldRestoreRuntimeCameraState={_charaSelectCameraAdapter.ShouldRestoreRuntimeCameraState()}",
            $"charaSelectCameraAdapter.sceneLoadedNotification={_lastCharaSelectCameraRuntimeRestoreStatus}",
            $"charaSelectCameraAdapter.runtimeRecordStatus={_lastCharaSelectCameraRuntimeRecordStatus}",
            $"charaSelectCameraAdapter.runtimeRecordFailureReason={FormatNone(_lastCharaSelectCameraRuntimeRecordError)}",
            $"charaSelectCameraAdapter.runtimeRestoreFailureReason={FormatNone(_lastCharaSelectCameraRuntimeRestoreFailureReason)}",
            $"charaSelectCameraAdapter.runtimeRestoreSceneGeneration={_lastCharaSelectCameraRuntimeRestoreSceneGeneration}",
            $"charaSelectCameraAdapter.curveAppliedSceneGeneration={_charaSelectCameraAdapter.LastCurveAppliedSceneGeneration}",
            $"charaSelectCameraAdapter.lookAtYAppliedSceneGeneration={_charaSelectCameraAdapter.LastLookAtYAppliedSceneGeneration}",
            "charaSelectCameraAdapter.phase=Phase2B-runtime-restore-curve-lookAtY",
            "sceneReadySignalSource=UpdateLobbyUIStage",
            "sceneReadySignalPolicy=phase2b-generation-gated",
            $"sceneReadySignal.callCount={_sceneReadySignalCallCount}",
            $"sceneReadySignal.rawCallCount={_sceneReadySignalCallCount}",
            $"sceneReadySignal.acceptedCount={_sceneReadySignalAcceptedCount}",
            $"sceneReadySignal.lastAdapterStateBeforeHandle={_sceneReadySignalLastAdapterStateBeforeHandle}",
            $"sceneReadySignal.lastResolvedLobbyMap={_sceneReadySignalLastResolvedLobbyMap}",
            $"runtimeRestore.attemptCount={_runtimeRestoreAttemptCount}",
            $"runtimeRestore.successCount={_runtimeRestoreSuccessCount}",
            $"runtimeRestore.lastStatus={_lastCharaSelectCameraRuntimeRestoreStatus}",
            $"runtimeRestore.lastFailureReason={FormatNone(_lastCharaSelectCameraRuntimeRestoreFailureReason)}",
            "runtimeRestore.target=LobbyCamera.DirH/DirV/Distance/InterpDistance/FoV",
            $"runtimeRestore.restoredYaw={FormatFloat(_runtimeRestoreLastRestoredYaw)}",
            $"runtimeRestore.restoredPitch={FormatFloat(_runtimeRestoreLastRestoredPitch)}",
            $"runtimeRestore.restoredDistance={FormatFloat(_runtimeRestoreLastRestoredDistance)}",
            $"runtimeRestore.restoredFovY={FormatFloat(_runtimeRestoreLastRestoredFovY)}",
            $"runtimeRestore.appliedFrame={FormatFrame(_runtimeRestoreAppliedFrame)}",
            $"cameraOneShotApply.status={_lastCharaSelectCameraRuntimeRestoreStatus}",
            $"cameraOneShotApply.failureReason={FormatNone(_lastCharaSelectCameraRuntimeRestoreFailureReason)}",
            $"cameraOneShotApply.appliedYaw={FormatFloat(_runtimeRestoreLastRestoredYaw)}",
            $"cameraOneShotApply.appliedPitch={FormatFloat(_runtimeRestoreLastRestoredPitch)}",
            $"cameraOneShotApply.appliedDistance={FormatFloat(_runtimeRestoreLastRestoredDistance)}",
            $"cameraOneShotApply.finalYaw={FormatFloat(phase2DLatestSample.LobbyDirH)}",
            $"cameraOneShotApply.finalPitch={FormatFloat(phase2DLatestSample.LobbyDirV)}",
            $"cameraOneShotApply.finalDistance={FormatFloat(phase2DLatestSample.LobbyDistance)}",
            $"curveApply.attemptCount={_curveApplyAttemptCount}",
            $"curveApply.successCount={_curveApplySuccessCount}",
            $"curveApply.lastStatus={_curveApplyLastStatus}",
            $"curveApply.lastFailureReason={FormatNone(_curveApplyLastFailureReason)}",
            $"curveApply.appliedLow={FormatFloat(_curveApplyLastAppliedLow)}",
            $"curveApply.appliedMid={FormatFloat(_curveApplyLastAppliedMid)}",
            $"curveApply.appliedHigh={FormatFloat(_curveApplyLastAppliedHigh)}",
            $"curveApply.finalLow={FormatFloat(phase2DLatestSample.LowPoint?.Y)}",
            $"curveApply.finalMid={FormatFloat(phase2DLatestSample.MidPoint?.Y)}",
            $"curveApply.finalHigh={FormatFloat(phase2DLatestSample.HighPoint?.Y)}",
            "curveApply.target=LobbyCamera.SetTiltOffset(mid); low/mid/high are recorded because FFXIVClientStructs does not expose separate lobby camera curve fields",
            $"curveApply.appliedFrame={FormatFrame(_curveApplyAppliedFrame)}",
            $"curveApply.requestedMid={FormatFloat(_curveApplyRequestedMid)}",
            $"curveApply.readBackValueImmediatelyAfterWrite={FormatUnavailable(_curveApplyReadBackValueImmediatelyAfterWrite, _curveApplyImmediateReadBackStatus)}",
            $"curveApply.activeCameraBefore.status={FormatNone(_curveApplyActiveCameraBeforeStatus)}",
            $"curveApply.activeCameraBefore.DirV={FormatFloat(_curveApplyActiveCameraBefore?.DirV)}",
            $"curveApply.activeCameraBefore.Distance={FormatFloat(_curveApplyActiveCameraBefore?.Distance)}",
            $"curveApply.activeCameraBefore.InterpDistance={FormatFloat(_curveApplyActiveCameraBefore?.InterpDistance)}",
            $"curveApply.activeCameraBefore.SceneCamera.LookAtVector={FormatVector(_curveApplyActiveCameraBefore?.LookAtVector)}",
            $"curveApply.activeCameraAfter.status={FormatNone(_curveApplyActiveCameraAfterStatus)}",
            $"curveApply.activeCameraAfter.DirV={FormatFloat(_curveApplyActiveCameraAfter?.DirV)}",
            $"curveApply.activeCameraAfter.Distance={FormatFloat(_curveApplyActiveCameraAfter?.Distance)}",
            $"curveApply.activeCameraAfter.InterpDistance={FormatFloat(_curveApplyActiveCameraAfter?.InterpDistance)}",
            $"curveApply.activeCameraAfter.SceneCamera.LookAtVector={FormatVector(_curveApplyActiveCameraAfter?.LookAtVector)}",
            $"curveApply.activeCameraImmediateDelta.DirV={FormatFloatDelta(_curveApplyActiveCameraAfter?.DirV, _curveApplyActiveCameraBefore?.DirV)}",
            $"curveApply.activeCameraImmediateDelta.LookAtVector={FormatVectorDelta(_curveApplyActiveCameraAfter?.LookAtVector, _curveApplyActiveCameraBefore?.LookAtVector)}",
            $"phase2C.timelineStatus={_phase2CTimelineStatus}",
            $"phase2C.timelineError={FormatNone(_phase2CTimelineError)}",
            $"phase2D.timelineStatus={_phase2CTimelineStatus}",
            $"phase2D.timelineError={FormatNone(_phase2CTimelineError)}",
            $"phase2D.timelineLatestFrame={FormatFrame(phase2DLatestSample.ActiveCameraCaptured ? phase2DLatestSample.Frame : null)}",
            $"verdict.distancePostRestoreStability={phase2CVerdicts.DistancePostRestoreStability}",
            $"verdict.tiltOffsetPostApplyObservableEffect={phase2CVerdicts.TiltOffsetPostApplyObservableEffect}",
            $"verdict.finalCameraStabilizationObserved={phase2DVerdicts.FinalCameraStabilizationObserved}",
            $"verdict.distanceEventuallyOverwritten={phase2DVerdicts.DistanceEventuallyOverwritten}",
            $"verdict.sceneTransformShiftObserved={phase2DVerdicts.SceneTransformShiftObserved}",
            $"verdict.phase2E.nativeReturnMatchesActiveLookAtY={phase2EVerdicts.NativeReturnMatchesActiveLookAtY}",
            $"verdict.phase2E.nativeReturnMatchesFinalStableLookAtY={phase2EVerdicts.NativeReturnMatchesFinalStableLookAtY}",
            $"verdict.phase2E.comparedCallCount={phase2EVerdicts.ComparedCallCount}",
            $"phase2E.calculateLobbyCameraLookAtY.callCount={_phase2ECalculateLookAtYCallCount}",
            $"phase2E.calculateLobbyCameraLookAtY.recordedCallCount={_phase2ECalculateLookAtYCalls.Count}",
            $"phase2E.calculateLobbyCameraLookAtY.lastError={FormatNone(_phase2ECalculateLookAtYLastError)}",
            $"phase2E.calculateLobbyCameraLookAtY.finalStableLookAtY={FormatFloat(phase2EFinalStableLookAtY)}",
            $"phase2F.curveTimeline.firstCapturedFrame={FormatFrame(phase2FVerdicts.FirstCapturedFrame)}",
            $"phase2F.curveTimeline.lastChangedFrame={FormatFrame(phase2FVerdicts.LastChangedFrame)}",
            $"phase2F.curveTimeline.curveGeneratedEarly={phase2FVerdicts.CurveGeneratedEarly}",
            $"phase2F.curveTimeline.curveStableByFinalWindow={phase2FVerdicts.CurveStableByFinalWindow}",
            $"phase2F.curveTimeline.curveRegeneratedAfterEarlyFrame={phase2FVerdicts.CurveRegeneratedAfterEarlyFrame}",
            $"phase2F.curveTimeline.oneShotWriteViability={phase2FVerdicts.OneShotWriteViability}",
            $"verdict.phase2F.curvePointValuesChangedAfterEarlyFrame={phase2FVerdicts.CurvePointValuesChangedAfterEarlyFrame}",
            $"verdict.phase2F.cameraCurveEnabledTransitionObserved={phase2FVerdicts.CameraCurveEnabledTransitionObserved}",
            $"verdict.phase2F.cameraCurveEnabledFirstObservedFrame={FormatFrame(phase2FVerdicts.CameraCurveEnabledFirstObservedFrame)}",
            $"verdict.phase2F.oneShotCurvePointWriteValueStability={phase2FVerdicts.OneShotCurvePointWriteValueStability}",
            $"verdict.phase2F.oneShotCurvePointWriteTimingRisk={phase2FVerdicts.OneShotCurvePointWriteTimingRisk}",
            $"phase2F.curveTimeline.lastPointValueChangedFrame={FormatFrame(phase2FVerdicts.LastPointValueChangedFrame)}",
            $"phase2F.generatedCurveTransitions.count={phase2FGeneratedCurveTransitions.Count}",
            $"phase2F.generatedCurveTransitions.firstFrame={FormatFrame(phase2FGeneratedCurveTransitions.FirstFrame)}",
            $"phase2F.generatedCurveTransitions.lastFrame={FormatFrame(phase2FGeneratedCurveTransitions.LastFrame)}",
            $"phase2F.generatedCurveTransitions.setCameraCurveMidPointCount={phase2FGeneratedCurveTransitions.SetCameraCurveMidPointCount}",
            $"phase2F.generatedCurveTransitions.calculateCameraCurveLowAndHighPointCount={phase2FGeneratedCurveTransitions.CalculateCameraCurveLowAndHighPointCount}",
            $"phase2F.setCameraCurveMidPoint.callCount={_phase2FSetCameraCurveMidPointCallCount}",
            $"phase2F.setCameraCurveMidPoint.recordedCallCount={_phase2FSetCameraCurveMidPointCalls.Count}",
            $"phase2F.setCameraCurveMidPoint.recentCallCount={_phase2FSetCameraCurveMidPointCalls.Count}",
            $"phase2F.setCameraCurveMidPoint.interestingCallCount={_phase2FSetCameraCurveMidPointInterestingCalls.Count}",
            $"phase2F.setCameraCurveMidPoint.lastError={FormatNone(_phase2FSetCameraCurveMidPointLastError)}",
            $"phase2F.calculateCameraCurveLowAndHighPoint.callCount={_phase2FCalculateCameraCurveLowAndHighPointCallCount}",
            $"phase2F.calculateCameraCurveLowAndHighPoint.recordedCallCount={_phase2FCalculateCameraCurveLowAndHighPointCalls.Count}",
            $"phase2F.calculateCameraCurveLowAndHighPoint.recentCallCount={_phase2FCalculateCameraCurveLowAndHighPointCalls.Count}",
            $"phase2F.calculateCameraCurveLowAndHighPoint.interestingCallCount={_phase2FCalculateCameraCurveLowAndHighPointInterestingCalls.Count}",
            $"phase2F.calculateCameraCurveLowAndHighPoint.lastError={FormatNone(_phase2FCalculateCameraCurveLowAndHighPointLastError)}",
            $"phase2G.generationOverride.enabled={IsPhase2GGenerationOverrideConfigured()}",
            "phase2G.generationOverride.writeTiming=post-original",
            "phase2G.generationOverride.writeScope=generated-curve-points-only",
            $"phase2G.generationOverride.setMid.attemptCount={_phase2GGenerationOverrideSetMidAttemptCount}",
            $"phase2G.generationOverride.setMid.appliedCount={_phase2GGenerationOverrideSetMidAppliedCount}",
            $"phase2G.generationOverride.lowHigh.attemptCount={_phase2GGenerationOverrideLowHighAttemptCount}",
            $"phase2G.generationOverride.lowHigh.appliedCount={_phase2GGenerationOverrideLowHighAppliedCount}",
            $"phase2G.generationOverride.lastAppliedFrame={FormatFrame(_phase2GGenerationOverrideLastAppliedFrame)}",
            $"phase2G.generationOverride.lastAppliedSceneGeneration={_phase2GGenerationOverrideLastAppliedSceneGeneration}",
            $"phase2G.generationOverride.lastStatus={FormatNone(_phase2GGenerationOverrideLastStatus)}",
            $"phase2G.generationOverride.lastSkippedReason={FormatNone(_phase2GGenerationOverrideLastSkippedReason)}",
            $"verdict.phase2G.generatedCurveOverrideEffective={phase2GGeneratedCurveOverrideEffective}",
            $"verdict.phase2G.finalLookAtYMatchesGeneratedCurve={phase2GFinalLookAtYMatchesGeneratedCurve}",
            $"verdict.phase2G.finalYawPitchDistanceMatchesPreset={phase2GFinalYawPitchDistanceMatchesPreset}",
            "verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False",
            $"verdict.phase2G.finalYawPitchDistanceMatchesPreset.loginTransitionConditional={TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe(phase2GFinalYawPitchDistanceMatchesPreset, transitionSafety)}",
            // Deprecated compatibility output. Prefer finalYawPitchDistanceMatchesPreset.
            $"verdict.phase2G.finalCameraStateMatchesPreset={phase2GFinalYawPitchDistanceMatchesPreset}",
            $"phase2M.actorDiagnostic.status={phase2MSummary.ActorDiagnosticStatus}",
            $"phase2M.actor.visible={phase2MSummary.ActorVisible}",
            $"phase2M.actor.groundAligned={phase2MSummary.ActorGroundAligned}",
            $"phase2M.camera.framesActor={phase2MSummary.CameraFramesActor}",
            $"phase2M.objectTable.totalScanned={phase2MObjectTableStats.TotalScanned}",
            $"phase2M.objectTable.namedCount={phase2MObjectTableStats.NamedCount}",
            $"phase2M.objectTable.playerLikeCount={phase2MObjectTableStats.PlayerLikeCount}",
            $"phase2M.objectTable.battleCharaCount={phase2MObjectTableStats.BattleCharaCount}",
            $"phase2M.objectTable.eventNpcCount={phase2MObjectTableStats.EventNpcCount}",
            $"phase2M.objectTable.nearCameraCount={phase2MObjectTableStats.NearCameraCount}",
            $"phase2M.objectTable.nearConfiguredCharacterCount={phase2MObjectTableStats.NearConfiguredCharacterCount}",
            $"phase2M.actorCandidate.status={phase2MActorCandidateStatus}",
            $"phase2M.actorCandidate.reason={phase2MActorCandidateReason}",
            $"phase2M.actor.source={phase2MActorSource}",
            $"phase2M.actor.nextNativeSourceToInspect={phase2MNextNativeSourceToInspect}",
            $"verdict.phase2M.visualPlacementSafety={phase2MSummary.VisualPlacementSafety}",
            $"phase2N.detailDump={FormatNone(string.IsNullOrWhiteSpace(deliveryDetailPath) ? string.Empty : Path.GetFileName(deliveryDetailPath))}",
            $"selectedPresetId={FormatNone(_configuration.TitleBackgroundSelectedPresetId)}",
            $"cameraOverrideApplyPending={_cameraApplyPending}",
            $"cameraCapture.lastStatus={_lastCameraCaptureResult.Status}",
            $"cameraCapture.lastFailureReason={FormatNone(_lastCameraCaptureResult.FailureReason)}",
            $"cameraCapture.lastFovState={_lastCameraCaptureResult.FovState}",
            $"lastCapturedTerritoryPath={(capturePreset == null ? "none" : capturePreset.TerritoryPath)}",
            $"lastCapturedCamera={FormatVector(capturePreset == null ? null : new Vector3(capturePreset.CameraX, capturePreset.CameraY, capturePreset.CameraZ))}",
            $"lastCapturedFocus={FormatVector(capturePreset == null ? null : new Vector3(capturePreset.FocusX, capturePreset.FocusY, capturePreset.FocusZ))}",
            $"lastCapturedFovY={(capturePreset == null ? "none" : capturePreset.FovY.ToString("0.###"))}",
            $"lastObservedFixOnCamera={FormatVector(_lastObservedFixOnCamera)}",
            $"lastObservedFixOnFocus={FormatVector(_lastObservedFixOnFocus)}",
            $"lastObservedFixOnFovY={(_lastObservedFixOnFovY.HasValue ? _lastObservedFixOnFovY.Value.ToString("0.###") : "none")}",
            $"lastFixOnFovY={(_lastObservedFixOnFovY.HasValue ? _lastObservedFixOnFovY.Value.ToString("0.###") : "none")}",
            $"lastCameraOverrideApplied={_lastCameraOverrideApplied}",
            $"lastAppliedCamera={FormatVector(_lastAppliedCamera)}",
            "cameraInputMeaning=FixOn camera argument; not yet verified as final stable camera position",
            $"lastAppliedFocus={FormatVector(_lastAppliedFocus)}",
            "focusInputMeaning=FixOn focus argument; observed relation exists, but semantics remain under verification",
            $"lastAppliedFovY={(_lastAppliedFovY.HasValue ? _lastAppliedFovY.Value.ToString("0.###") : "none")}",
            $"lastFixOnInvocationMode={_lastFixOnInvocationMode}",
            $"postFixOnCameraCaptureStatus={_lastPostFixOnCameraCaptureStatus}",
            $"postFixOnCameraCaptureError={FormatNone(_lastPostFixOnCameraCaptureError)}",
            $"postFixOnSceneCameraPosition={FormatVector(_lastPostFixOnSceneCameraPosition)}",
            $"postFixOnLookAtVector={FormatVector(_lastPostFixOnLookAtVector)}",
            "postFixOnLookAtVectorMeaning=raw SceneCamera.LookAtVector; meaning unverified, but live observation showed FixOn focusPos matched this field",
            $"postFixOnDistance={(_lastPostFixOnDistance.HasValue ? _lastPostFixOnDistance.Value.ToString("0.###") : "none")}",
            $"postFixOnFovY={(_lastPostFixOnFovY.HasValue ? _lastPostFixOnFovY.Value.ToString("0.###") : "none")}",
            $"currentCameraCaptureStatus={currentCaptureStatus}",
            $"currentCameraCaptureError={FormatNone(currentCaptureError)}",
            $"currentDirH={FormatFloat(currentDirH)}",
            $"currentDirV={FormatFloat(currentDirV)}",
            $"currentSceneCameraPosition={FormatVector(currentSceneCameraPosition)}",
            $"currentLookAtVector={FormatVector(currentLookAtVector)}",
            "currentCameraObservation=report-time active camera; /xmutbgdiag normally runs after login, so this is not the character-select Phase2D camera",
            "currentLookAtVectorMeaning=raw SceneCamera.LookAtVector; meaning unverified and not directly comparable to Phase2D after login",
            $"currentDistance={FormatFloat(currentDistance)}",
            $"currentInterpDistance={FormatFloat(currentInterpDistance)}",
            $"currentFovY={FormatFloat(currentFovY)}",
            "reportTimeCurrentVsPhase2DLatest.meaning=report-time current camera vs latest character-select timeline sample; not a post-login overwrite verdict",
            $"reportTimeCurrentVsPhase2DLatest.DistanceDelta={FormatFloatDelta(currentDistance, phase2DLatestSample.Distance)}",
            $"reportTimeCurrentVsPhase2DLatest.SceneCameraPositionDelta={FormatVectorDelta(currentSceneCameraPosition, phase2DLatestSample.SceneCameraPosition)}",
            $"reportTimeCurrentVsPhase2DLatest.LookAtVectorDelta={FormatVectorDelta(currentLookAtVector, phase2DLatestSample.SceneCameraLookAtVector)}",
            $"reportTimeCurrentVsPhase2DLatest.DirHDelta={FormatFloatDelta(currentDirH, phase2DLatestSample.DirH)}",
            $"reportTimeCurrentVsPhase2DLatest.DirVDelta={FormatFloatDelta(currentDirV, phase2DLatestSample.DirV)}",
            $"cameraDelta.appliedToPostFixOn={FormatVectorDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera)}",
            $"cameraDelta.appliedToPostFixOn.x={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 0)}",
            $"cameraDelta.appliedToPostFixOn.y={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 1)}",
            $"cameraDelta.appliedToPostFixOn.z={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 2)}",
            $"cameraDelta.postFixOnToCurrent={FormatVectorDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition)}",
            $"cameraDelta.postFixOnToCurrent.x={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 0)}",
            $"cameraDelta.postFixOnToCurrent.y={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 1)}",
            $"cameraDelta.postFixOnToCurrent.z={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 2)}",
            $"focusDelta.appliedToPostFixOn={FormatVectorDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus)}",
            $"focusDelta.appliedToPostFixOn.x={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 0)}",
            $"focusDelta.appliedToPostFixOn.y={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 1)}",
            $"focusDelta.appliedToPostFixOn.z={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 2)}",
            $"focusDelta.postFixOnToCurrent={FormatVectorDelta(currentLookAtVector, _lastPostFixOnLookAtVector)}",
            $"focusDelta.postFixOnToCurrent.x={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 0)}",
            $"focusDelta.postFixOnToCurrent.y={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 1)}",
            $"focusDelta.postFixOnToCurrent.z={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 2)}",
            $"currentVsPostFixOnCameraDelta={FormatVectorDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition)}",
            $"currentVsPostFixOnLookAtDelta={FormatVectorDelta(currentLookAtVector, _lastPostFixOnLookAtVector)}",
            $"currentVsPostFixOnDistanceDelta={FormatFloatDelta(currentDistance, _lastPostFixOnDistance)}",
            $"currentVsPostFixOnFovYDelta={FormatFloatDelta(currentFovY, _lastPostFixOnFovY)}",
            $"titleOverrideImplemented={TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(_configuration.TitleBackgroundRuntimeMode)}",
            "fixOnAbiVerified=False",
            $"hooksEnabled={hooksEnabled}",
            "",
            "Phase2C.timeline=scene-ready-accepted-relative-frames; early reflection/stability probe for runtime restore, curve apply, and LobbyCamera.LastLookAtVector.Y",
            "Phase2D.timeline=extended-scene-ready-accepted-relative-frames; character-select ActiveCamera/LobbyCamera samples through frame 600",
            "Phase2D.verdictScope=timeline-only; finalCameraStabilizationObserved and distanceEventuallyOverwritten are based on character-select samples, not report-time current camera",
            "Phase2F.timeline=extended-scene-ready-accepted-relative-frames; read-only LobbyCameraExpanded curve point samples through frame 600",
            "CharacterPlacement.placement=diagnostics-only; actor/object-table, camera delta, and ground-height availability are retained for post-login dump",
        };

        TitleBackgroundDeliveryDiagnostic.AddLines(lines, phase2NDeliverySummary);
        lines.AddRange(transitionSummaryLines);
        lines.AddRange(_transitionDiagnostics.BuildTraceLines());

        foreach (var sample in phase2CTimelineSamples)
        {
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.captureStatus={(sample.ActiveCameraCaptured ? "success" : "failed")}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.error={FormatNone(sample.ActiveCameraError)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.DirH={FormatFloat(sample.DirH)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.DirV={FormatFloat(sample.DirV)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.Distance={FormatFloat(sample.Distance)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.InterpDistance={FormatFloat(sample.InterpDistance)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.SceneCamera.Position={FormatVector(sample.SceneCameraPosition)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].activeCamera.SceneCamera.LookAtVector={FormatVector(sample.SceneCameraLookAtVector)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.captureStatus={(sample.LobbyCameraCaptured ? "success" : "failed")}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.error={FormatNone(sample.LobbyCameraError)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.LastLookAtVector={FormatVector(sample.LobbyLastLookAtVector)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.DirH={FormatFloat(sample.LobbyDirH)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.DirV={FormatFloat(sample.LobbyDirV)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.Distance={FormatFloat(sample.LobbyDistance)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.InterpDistance={FormatFloat(sample.LobbyInterpDistance)}");
            lines.Add($"phase2C.timeline[{sample.Frame}].lobbyCamera.tiltOffsetReadback=readback unavailable");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.captureStatus={(sample.ActiveCameraCaptured ? "success" : "failed")}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.error={FormatNone(sample.ActiveCameraError)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.DirH={FormatFloat(sample.DirH)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.DirV={FormatFloat(sample.DirV)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.Distance={FormatFloat(sample.Distance)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.InterpDistance={FormatFloat(sample.InterpDistance)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.SceneCamera.Position={FormatVector(sample.SceneCameraPosition)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].activeCamera.SceneCamera.LookAtVector={FormatVector(sample.SceneCameraLookAtVector)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.captureStatus={(sample.LobbyCameraCaptured ? "success" : "failed")}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.error={FormatNone(sample.LobbyCameraError)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.LastLookAtVector={FormatVector(sample.LobbyLastLookAtVector)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.DirH={FormatFloat(sample.LobbyDirH)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.DirV={FormatFloat(sample.LobbyDirV)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.Distance={FormatFloat(sample.LobbyDistance)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.InterpDistance={FormatFloat(sample.LobbyInterpDistance)}");
            lines.Add($"phase2D.timeline[{sample.Frame}].lobbyCamera.tiltOffsetReadback=readback unavailable");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.captureStatus={(sample.ExpandedLobbyCameraCaptured ? "success" : "failed")}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.error={FormatNone(sample.ExpandedLobbyCameraError)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.CameraCurveEnabled={FormatBool(sample.CameraCurveEnabled)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.LowPoint.Position={FormatFloat(sample.LowPoint?.X)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.LowPoint.Value={FormatFloat(sample.LowPoint?.Y)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.MidPoint.Position={FormatFloat(sample.MidPoint?.X)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.MidPoint.Value={FormatFloat(sample.MidPoint?.Y)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.HighPoint.Position={FormatFloat(sample.HighPoint?.X)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.HighPoint.Value={FormatFloat(sample.HighPoint?.Y)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.lowPoint={FormatCurvePoint(sample.LowPoint)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.midPoint={FormatCurvePoint(sample.MidPoint)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].expandedLobbyCamera.highPoint={FormatCurvePoint(sample.HighPoint)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].activeCamera.Distance={FormatFloat(sample.Distance)}");
            lines.Add($"phase2F.timeline[{sample.Frame}].activeCamera.SceneCamera.LookAtVector.Y={FormatFloat(sample.SceneCameraLookAtVector?.Y)}");
        }

        foreach (var frame in _phase2MPlacementFrames.Values.OrderBy(frame => frame.Frame))
        {
            AddCharacterPlacementPlacementFrameLines(lines, frame);
        }

        foreach (var call in _phase2ECalculateLookAtYCalls)
        {
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].frame={FormatFrame(call.Frame)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].distance={FormatFloat(call.Distance)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].lowPoint={FormatCurvePoint(call.LowPoint)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].midPoint={FormatCurvePoint(call.MidPoint)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].highPoint={FormatCurvePoint(call.HighPoint)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].returnValue={FormatFloat(call.ReturnValue)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].activeLookAtY.before={FormatFloat(call.ActiveLookAtYBeforeOriginal)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].activeLookAtY.after={FormatFloat(call.ActiveLookAtYAfterOriginal)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].returnToActiveAfterDelta={FormatFloatDelta(call.ReturnValue, call.ActiveLookAtYAfterOriginal)}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].status={call.Status}");
            lines.Add($"phase2E.calculateLobbyCameraLookAtY.call[{call.CallIndex}].error={FormatNone(call.Error)}");
        }

        foreach (var call in _phase2FSetCameraCurveMidPointCalls)
        {
            AddGeneratedCurveCallLines(lines, "phase2F.setCameraCurveMidPoint", call);
        }

        foreach (var call in _phase2FSetCameraCurveMidPointInterestingCalls)
        {
            AddGeneratedCurveCallLines(lines, "phase2F.setCameraCurveMidPoint", "interestingCall", call);
        }

        foreach (var call in _phase2FCalculateCameraCurveLowAndHighPointCalls)
        {
            AddGeneratedCurveCallLines(lines, "phase2F.calculateCameraCurveLowAndHighPoint", call);
        }

        foreach (var call in _phase2FCalculateCameraCurveLowAndHighPointInterestingCalls)
        {
            AddGeneratedCurveCallLines(lines, "phase2F.calculateCameraCurveLowAndHighPoint", "interestingCall", call);
        }

        lines.AddRange(
        [
            "",
            BuildAddressLine("CreateScene.configured", _configuration.TitleBackgroundCreateSceneSignature),
            $"CreateScene.resolverMode={_configuration.TitleBackgroundCreateSceneResolverMode}",
            $"CreateScene.match={FormatAddress(_addressResolver.CreateSceneMatch)}",
            $"CreateScene.resolvedCandidate={FormatAddress(GetResolvedCandidate("CreateScene"))}",
            $"CreateScene.hookTarget={FormatAddress(_addressResolver.CreateScene)}",
            $"CreateScene.hookTargetVerified={GetHookTargetVerified("CreateScene")}",
            $"CreateScene.method={GetResolveMethod("CreateScene", "TryScanText+NearbyE8Rel32")}",
            $"CreateScene.candidateReadable={GetCandidateReadable("CreateScene")}",
            $"CreateScene.candidatePrologueHint={GetCandidatePrologueHint("CreateScene")}",
            $"CreateScene.candidateFirstBytes={GetCandidateFirstBytes("CreateScene")}",
            $"CreateScene.targetWithinText={GetTargetWithinText("CreateScene")}",
            "",
            BuildAddressLine("LobbyUpdate.configured", _configuration.TitleBackgroundLobbyUpdateSignature),
            $"LobbyUpdate.resolverMode={_configuration.TitleBackgroundLobbyUpdateResolverMode}",
            $"LobbyUpdate.match={FormatAddress(_addressResolver.LobbyUpdateMatch)}",
            $"LobbyUpdate.resolvedCandidate={FormatAddress(GetResolvedCandidate("LobbyUpdate"))}",
            $"LobbyUpdate.hookTarget={FormatAddress(_addressResolver.LobbyUpdate)}",
            $"LobbyUpdate.hookTargetVerified={GetHookTargetVerified("LobbyUpdate")}",
            $"LobbyUpdate.method={GetResolveMethod("LobbyUpdate", "TryScanText+NearbyE8Rel32")}",
            $"LobbyUpdate.candidateReadable={GetCandidateReadable("LobbyUpdate")}",
            $"LobbyUpdate.candidatePrologueHint={GetCandidatePrologueHint("LobbyUpdate")}",
            $"LobbyUpdate.candidateFirstBytes={GetCandidateFirstBytes("LobbyUpdate")}",
            $"LobbyUpdate.targetWithinText={GetTargetWithinText("LobbyUpdate")}",
            "",
            BuildAddressLine("LoadLobbyScene.configured", _configuration.TitleBackgroundLoadLobbySceneSignature),
            $"LoadLobbyScene.address={FormatAddress(_addressResolver.LoadLobbyScene)}",
            "LoadLobbyScene.method=TryScanText",
            $"LoadLobbyScene.hookTargetVerified={GetHookTargetVerified("LoadLobbyScene")}",
            $"LoadLobbyScene.targetWithinText={GetTargetWithinText("LoadLobbyScene")}",
            "",
            "LobbySceneLoaded.source=UpdateLobbyUIStage",
            "LobbySceneLoaded.policy=phase2a-provisional-scene-ready-signal",
            $"LobbySceneLoaded.address={FormatAddress(_addressResolver.UpdateLobbyUIStage)}",
            $"LobbySceneLoaded.hookTargetVerified={GetHookTargetVerified("UpdateLobbyUIStage")}",
            $"LobbySceneLoaded.targetWithinText={GetTargetWithinText("UpdateLobbyUIStage")}",
            "",
            BuildAddressLine("LobbyCurrentMap.configured", _configuration.TitleBackgroundLobbyCurrentMapSignature),
            $"LobbyCurrentMap.staticAddress={FormatAddress(_addressResolver.LobbyCurrentMap)}",
            $"LobbyCurrentMap.readShort={ReadCurrentLobbyMapRawText()}",
            $"LobbyCurrentMap.enumDecoded={ReadCurrentLobbyMapDecodedText()}",
            $"LobbyCurrentMap.writeAttempted={_currentMapWriteAttempted}",
            $"LobbyCurrentMap.lastWriteSucceeded={_lastCurrentMapWriteSucceeded}",
            "",
            BuildAddressLine("LobbyCameraFixOn.configured", _configuration.TitleBackgroundFixOnSignature),
            $"LobbyCameraFixOn.address={FormatAddress(_addressResolver.FixOn)}",
            "LobbyCameraFixOn.method=TryScanText",
            $"LobbyCameraFixOn.hookTargetVerified={GetHookTargetVerified("LobbyCameraFixOn")}",
            $"LobbyCameraFixOn.targetWithinText={GetTargetWithinText("LobbyCameraFixOn")}",
            "",
            BuildAddressLine("CalculateLobbyCameraLookAtY.configured", _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature),
            $"CalculateLobbyCameraLookAtY.address={FormatAddress(_addressResolver.CalculateLobbyCameraLookAtY)}",
            "CalculateLobbyCameraLookAtY.method=TryScanText",
            $"CalculateLobbyCameraLookAtY.hookTargetVerified={GetHookTargetVerified("CalculateLobbyCameraLookAtY")}",
            $"CalculateLobbyCameraLookAtY.targetWithinText={GetTargetWithinText("CalculateLobbyCameraLookAtY")}",
            "",
            BuildAddressLine("SetCameraCurveMidPoint.configured", _configuration.TitleBackgroundSetCameraCurveMidPointSignature),
            $"SetCameraCurveMidPoint.address={FormatAddress(_addressResolver.SetCameraCurveMidPoint)}",
            "SetCameraCurveMidPoint.method=TryScanText",
            $"SetCameraCurveMidPoint.hookTargetVerified={GetHookTargetVerified("SetCameraCurveMidPoint")}",
            $"SetCameraCurveMidPoint.targetWithinText={GetTargetWithinText("SetCameraCurveMidPoint")}",
            "",
            BuildAddressLine("CalculateCameraCurveLowAndHighPoint.configured", _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature),
            $"CalculateCameraCurveLowAndHighPoint.address={FormatAddress(_addressResolver.CalculateCameraCurveLowAndHighPoint)}",
            "CalculateCameraCurveLowAndHighPoint.method=TryScanText",
            $"CalculateCameraCurveLowAndHighPoint.hookTargetVerified={GetHookTargetVerified("CalculateCameraCurveLowAndHighPoint")}",
            $"CalculateCameraCurveLowAndHighPoint.targetWithinText={GetTargetWithinText("CalculateCameraCurveLowAndHighPoint")}",
            "",
            "UpdateLobbyUIStage.optional=True",
            $"UpdateLobbyUIStage.address={FormatAddress(_addressResolver.UpdateLobbyUIStage)}",
            $"UpdateLobbyUIStage.resolvedCandidate={FormatAddress(GetResolvedCandidate("UpdateLobbyUIStage"))}",
            $"UpdateLobbyUIStage.hookTargetVerified={GetHookTargetVerified("UpdateLobbyUIStage")}",
            $"Focus.reservedForCameraPhase={!TitleBackgroundRuntimeModeHelper.IsFocusUsed(_configuration.TitleBackgroundCameraOverrideEnabled)}",
            $"lastLobbyUpdateMapId={_lastLobbyUpdateMapId}",
            $"loadingLobbyType={_loadingLobbyType}",
            $"effectiveLobbyType={EffectiveLobbyType}",
            $"lastCreateScenePath={(_lastObservedCreateScenePath.Length == 0 ? "none" : _lastObservedCreateScenePath)}",
            $"resolverError={(_addressResolver.LastError.Length == 0 ? "none" : _addressResolver.LastError)}",
        ]);

        foreach (var result in _addressResolver.ScanResults)
        {
            lines.Add($"signatureScan: name={result.Name}, method={result.Method}, status={result.Status}, address={FormatAddress(result.Address)}, resolvedCandidate={FormatAddress(result.ResolvedCandidate)}, hookTarget={FormatAddress(result.HookTarget)}, hookTargetVerified={result.HookTargetVerified}, addressSource={result.AddressSource}, targetWithinText={result.TargetWithinText}, hookTargetWithinText={result.HookTargetWithinText}, candidateReadable={result.CandidateDiagnostics.Readable}, candidatePrologueHint={result.CandidateDiagnostics.PrologueHint}, candidateFirstBytes={(string.IsNullOrWhiteSpace(result.CandidateDiagnostics.FirstBytesHex) ? "none" : result.CandidateDiagnostics.FirstBytesHex)}, safetyNote={(string.IsNullOrWhiteSpace(result.SafetyNote) ? "none" : result.SafetyNote)}");
        }

        return lines;
    }
}

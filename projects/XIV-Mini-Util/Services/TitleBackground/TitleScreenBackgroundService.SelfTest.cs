// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.SelfTest.cs
// Description: TitleBackground self-test と Character Select reload 診断操作を提供する
// Reason: self-test 診断処理を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    public string StartSelfTest()
    {
        if (_selfTestSession != null)
        {
            return "TitleBackground self-test: already running";
        }

        ClearSelfTestObservationBuffers();
        var reloadMessage = RequestCharaSelectReload(startedBySelfTest: true);
        if (!string.Equals(reloadMessage, "reload requested", StringComparison.Ordinal))
        {
            _log.Information("[XMU BG] Self-test reload unavailable. detail={Detail}", reloadMessage);
            var failure = CompleteSelfTestFailure("reload-unavailable");
            return failure;
        }

        _selfTestSession = new TitleBackgroundSelfTestSession(
            _configuration.TitleBackgroundSelectedPresetId,
            _configuration.TitleBackgroundTerritoryPath,
            GetEffectiveOverrideTerritoryId(),
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            _charaSelectCameraAdapter.Curve);
        return string.Empty;
    }

    public string RequestCharaSelectReload()
    {
        return RequestCharaSelectReload(startedBySelfTest: false);
    }

    /// <summary>
    /// Integrated composition route: CharaSelect scene reload を要求して CreateScene を発火させる。
    /// RouteInvoked は "reload requested" が返った場合のみ true になる。
    /// </summary>
    private void TryInvokeIntegratedCompositionRoute()
    {
        var reason = RequestCharaSelectReload(startedBySelfTest: false);
        _integratedCompositionRouteLastReason = reason;
        _integratedCompositionRouteInvoked = string.Equals(reason, "reload requested", StringComparison.Ordinal);
        RecordTransitionEvent("integrated composition route", reason);
    }

    private string RequestCharaSelectReload(bool startedBySelfTest)
    {
        if (_state != TitleBackgroundServiceState.Ready
            || !IsSceneOverrideEnabled()
            || !TryReadCurrentLobbyMap(out var currentMap)
            || !TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(currentMap))
        {
            return "available only in CharaSelect lobby";
        }

        ConfigureCharaSelectCameraAdapter();
        RecordCharaSelectRuntimeCameraStateBeforeSceneReload(GameLobbyType.CharaSelect);
        _charaSelectCameraAdapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
        RecordTransitionEvent("scene generation incremented", $"generation={_charaSelectCameraAdapter.RuntimeState.SceneGeneration}");
        _currentMapWriteAttempted = true;
        _lastCurrentMapWriteSucceeded = TryWriteCurrentLobbyMap(GameLobbyType.None);
        _lastCurrentLobbyMapResetReason = "manual-reload";
        RecordTransitionEvent("CurrentLobbyMap reset", _lastCurrentLobbyMapResetReason);
        if (!_lastCurrentMapWriteSucceeded)
        {
            return "reload failed: CurrentLobbyMap write failed";
        }

        _lastOverrideApplied = false;
        _lastOverrideLobbyType = GameLobbyType.None;
        _lastOverrideOriginalPath = string.Empty;
        _lastOverrideNewPath = string.Empty;
        _lastOverrideTerritoryId = 0;
        _lastOverrideLayerFilterKey = 0;
        if (!startedBySelfTest)
        {
            ClearSelfTestObservationBuffers();
        }

        return "reload requested";
    }

    private void UpdateSelfTestOnFrameworkUpdate()
    {
        if (_selfTestSession == null)
        {
            return;
        }

        _selfTestSession.Frame++;
        if (_selfTestSession.Frame < SelfTestMaxFrame)
        {
            var earlyVerdict = BuildSelfTestVerdict();
            if (earlyVerdict.Pass)
            {
                CompleteSelfTest(earlyVerdict);
            }

            return;
        }

        CompleteSelfTest(BuildSelfTestVerdict());
    }

    private void CompleteSelfTest(TitleBackgroundSelfTestVerdict verdict)
    {
        var session = _selfTestSession;
        _selfTestSession = null;
        if (session == null)
        {
            return;
        }

        string message;
        if (verdict.Pass)
        {
            message = "TitleBackground self-test: PASS scene=observed curve=observed lookAtY=observed camera=non-blocking";
        }
        else
        {
            var detailPath = SaveSelfTestFailureDiagnostic();
            var detailName = string.IsNullOrWhiteSpace(detailPath)
                ? "diag-save-failed"
                : Path.GetFileName(detailPath);
            message = $"TitleBackground self-test: FAIL reason={verdict.Reason} detail={detailName}";
        }

        SelfTestCompleted?.Invoke(message);
    }

    private string CompleteSelfTestFailure(string reason)
    {
        var detailPath = SaveSelfTestFailureDiagnostic();
        var detailName = string.IsNullOrWhiteSpace(detailPath)
            ? "diag-save-failed"
            : Path.GetFileName(detailPath);
        _selfTestSession = null;
        return $"TitleBackground self-test: FAIL reason={reason} detail={detailName}";
    }

    private TitleBackgroundSelfTestVerdict BuildSelfTestVerdict()
    {
        var phase2CTimelineSamples = BuildPhase2CTimelineSamples();
        var phase2FCurveSamples = BuildPhase2FCurveTimelineSamples(phase2CTimelineSamples);
        var latest = phase2CTimelineSamples
            .Where(sample => sample.ActiveCameraCaptured || sample.ExpandedLobbyCameraCaptured)
            .OrderByDescending(sample => sample.Frame)
            .FirstOrDefault();
        var scene = BuildSelfTestSceneVerdict();
        var curve = BuildPhase2GGeneratedCurveOverrideEffectiveVerdict(phase2FCurveSamples, _selfTestSession?.Curve);
        var lookAtY = BuildPhase2GFinalLookAtYMatchesGeneratedCurveVerdict(latest);
        var yawPitchDistance = BuildPhase2GFinalCameraStateMatchesPresetVerdict(latest);

        // Phase 2G success is generated-curve based. Final yaw/pitch/distance mismatch is
        // reported for visibility but is not a self-test blocker.
        if (scene != "observed")
        {
            return TitleBackgroundSelfTestVerdict.Fail("scene-not-applied");
        }

        if (curve != "observed")
        {
            return TitleBackgroundSelfTestVerdict.Fail("curve-not-applied");
        }

        if (lookAtY != "observed")
        {
            return TitleBackgroundSelfTestVerdict.Fail("lookAtY-not-applied");
        }

        if (!TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            _phase2GGenerationOverrideSetMidAttemptCount,
            _phase2GGenerationOverrideSetMidAppliedCount,
            _phase2GGenerationOverrideLowHighAttemptCount,
            _phase2GGenerationOverrideLowHighAppliedCount,
            lookAtY))
        {
            return TitleBackgroundSelfTestVerdict.Fail("phase2g-counts-not-applied");
        }

        if (!TitleBackgroundCameraProbeReport.IsGeneratedCurveSelfTestSuccess(scene, curve, lookAtY, yawPitchDistance))
        {
            return TitleBackgroundSelfTestVerdict.Fail("phase2g-not-applied");
        }

        return TitleBackgroundSelfTestVerdict.Success();
    }

    private string BuildSelfTestSceneVerdict()
    {
        var session = _selfTestSession;
        var expectedPath = session?.TerritoryPath ?? _validatedTerritoryPath;
        var expectedTerritoryId = session?.TerritoryId ?? GetEffectiveOverrideTerritoryId();
        var expectedLayerFilterKey = session?.LayerFilterKey ?? _configuration.TitleBackgroundLayoutLayerFilterKey;
        return _lastOverrideApplied
            && _lastOverrideLobbyType == GameLobbyType.CharaSelect
            && string.Equals(_lastOverrideNewPath, expectedPath, StringComparison.Ordinal)
            && (expectedTerritoryId == 0 || _lastOverrideTerritoryId == expectedTerritoryId)
            && (expectedLayerFilterKey == 0 || _lastOverrideLayerFilterKey == expectedLayerFilterKey)
            ? "observed"
            : "not-observed";
    }

    private string BuildFailureSummary(TitleBackgroundPhase2CTimelineSnapshot latestSample)
    {
        var items = new List<string>();
        if (!_lastOverrideApplied)
        {
            items.Add("scene-not-overridden");
        }

        if (_sceneReadySignalAcceptedCount == 0)
        {
            items.Add("scene-ready-not-accepted");
        }

        if (_lastCharaSelectCameraRuntimeRecordStatus is "failed")
        {
            items.Add($"camera-pose-build-failed:{FormatNone(_lastCharaSelectCameraRuntimeRecordError)}");
        }

        if (_lastCharaSelectCameraRuntimeRestoreStatus is "failed")
        {
            items.Add($"camera-apply-failed:{FormatNone(_lastCharaSelectCameraRuntimeRestoreFailureReason)}");
        }

        if (_curveApplyLastStatus is "failed")
        {
            items.Add($"curve-apply-failed:{FormatNone(_curveApplyLastFailureReason)}");
        }

        if (!latestSample.LobbyCameraCaptured)
        {
            items.Add("final-lobby-camera-missing");
        }

        if (!latestSample.ExpandedLobbyCameraCaptured)
        {
            items.Add("final-curve-missing");
        }

        return items.Count == 0 ? "none" : string.Join(",", items);
    }

    private void ClearSelfTestObservationBuffers()
    {
        ResetSceneOverrideObservation();
        ResetCameraOverrideObservation();
        ResetPhase2ECalculateLookAtYObservation();
        _probeTimeline.Phase2CTimelineFrameCounter = -1;
        _probeTimeline.Phase2CTimelineStatus = "not-run";
        _probeTimeline.Phase2CTimelineError = string.Empty;
        _probeTimeline.Phase2CTimelineSnapshots.Clear();
    }

    private string SaveSelfTestFailureDiagnostic()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            var path = Path.Combine(_configDirectory, "title-background-lastdiag.txt");
            File.WriteAllLines(path, GetDiagnosticLines(includeDetailedPhase2Diagnostics: true).Select(line => $"[XIV Mini Util] {line}"));
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground self-test diagnostic save failed.");
            return string.Empty;
        }
    }
}
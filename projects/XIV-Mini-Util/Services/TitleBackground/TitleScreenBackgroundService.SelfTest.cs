// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.SelfTest.cs
// Description: Integrated composition の Character Select reload と失敗要約を提供する
// Reason: production route の reload と診断要約を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    /// <summary>
    /// Integrated composition route: CharaSelect scene reload を要求して CreateScene を発火させる。
    /// RouteInvoked は "reload requested" が返った場合のみ true になる。
    /// </summary>
    private void TryInvokeIntegratedCompositionRoute()
    {
        var reason = RequestCharaSelectReload();
        _integratedCompositionRouteLastReason = reason;
        _integratedCompositionRouteInvoked = string.Equals(reason, "reload requested", StringComparison.Ordinal);
        RecordTransitionEvent("integrated composition route", reason);
    }

    private string RequestCharaSelectReload()
    {
        if (_hookLifecycle.State != TitleBackgroundServiceState.Ready
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
        ResetSceneOverrideObservation();
        ResetCameraOverrideObservation();
        ResetPhase2ECalculateLookAtYObservation();
        _probeTimeline.Phase2CTimelineFrameCounter = -1;
        _probeTimeline.Phase2CTimelineStatus = "not-run";
        _probeTimeline.Phase2CTimelineError = string.Empty;
        _probeTimeline.Phase2CTimelineSnapshots.Clear();

        return "reload requested";
    }

    private string BuildFailureSummary(TitleBackgroundPhase2CTimelineSnapshot latestSample)
    {
        var items = new List<string>();
        if (!_lastOverrideApplied)
        {
            items.Add("scene-not-overridden");
        }

        if (_cameraRestoreCurve.SceneReadySignalAcceptedCount == 0)
        {
            items.Add("scene-ready-not-accepted");
        }

        if (_cameraRestoreCurve.LastCharaSelectCameraRuntimeRecordStatus is "failed")
        {
            items.Add($"camera-pose-build-failed:{FormatNone(_cameraRestoreCurve.LastCharaSelectCameraRuntimeRecordError)}");
        }

        if (_cameraRestoreCurve.LastCharaSelectCameraRuntimeRestoreStatus is "failed")
        {
            items.Add($"camera-apply-failed:{FormatNone(_cameraRestoreCurve.LastCharaSelectCameraRuntimeRestoreFailureReason)}");
        }

        if (_cameraRestoreCurve.CurveApplyLastStatus is "failed")
        {
            items.Add($"curve-apply-failed:{FormatNone(_cameraRestoreCurve.CurveApplyLastFailureReason)}");
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

}

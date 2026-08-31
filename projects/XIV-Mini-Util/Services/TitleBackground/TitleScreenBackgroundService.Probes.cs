// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.Probes.cs
// Description: TitleBackground の hook probe / camera probe 診断操作を提供する
// Reason: probe 診断処理を TitleScreenBackgroundService の本体ロジックから分離するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    public IReadOnlyList<string> StartProbe()
    {
        if (_probeTimeline.CameraProbeSession != null)
        {
            return ["[Probe] camera probe is armed; run /xmutbgcamprobe restore before starting hook probe."];
        }

        if (_probeTimeline.ActiveProbeSession != null)
        {
            return ["[Probe] already active; existing session was left unchanged."];
        }

        var session = new TitleBackgroundProbeSession(TitleBackgroundProbeSettingsSnapshot.Capture(_configuration));
        _probeTimeline.ActiveProbeSession = session;
        _probeTimeline.LastProbeSession = session;

        try
        {
            _configuration.TitleBackgroundOverrideEnabled = true;
            _configuration.TitleBackgroundCameraOverrideEnabled = false;
            _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.HookProbe;
            _configuration.TitleBackgroundCreateSceneResolverMode = TitleBackgroundResolverMode.ManualDirectTextProbe;
            _configuration.TitleBackgroundLobbyUpdateResolverMode = TitleBackgroundResolverMode.ManualDirectTextProbe;
            ReloadNativeIntegration();
            session.HookEnabledAtStart = AreAnyHooksEnabled();

            return
            [
                "[Probe] started.",
                ..GetProbeReportLines(session, isActive: true),
                "",
                ..GetDiagnosticLines(),
            ];
        }
        catch (Exception ex)
        {
            session.RuntimeErrorOccurred = true;
            session.LastError = ex.Message;
            var rollbackMessage = TryRestoreProbeSettings(session.OriginalSettings);
            _probeTimeline.ActiveProbeSession = null;
            return
            [
                "[Probe] failed to start.",
                rollbackMessage,
                $"[Probe] error={ex.Message}",
            ];
        }
    }

    public IReadOnlyList<string> StopProbe()
    {
        if (_probeTimeline.ActiveProbeSession == null)
        {
            return ["[Probe] no active session; nothing to stop."];
        }

        var session = _probeTimeline.ActiveProbeSession;
        _probeTimeline.ActiveProbeSession = null;
        session.HookEnabledAtEnd = AreAnyHooksEnabled();
        var restoreMessage = TryRestoreProbeSettings(session.OriginalSettings);

        return
        [
            restoreMessage,
            ..GetProbeReportLines(session, isActive: false),
        ];
    }

    public IReadOnlyList<string> GetProbeReportLines()
    {
        var activeSession = _probeTimeline.ActiveProbeSession;
        var lastSession = activeSession ?? _probeTimeline.LastProbeSession;
        var reportInput = BuildProbeReportInput(lastSession, activeSession != null);
        var summaryLines = BuildProbeReportSummaryLines(reportInput);

        if (_probeTimeline.ActiveProbeSession != null)
        {
            return
            [
                ..summaryLines,
                "",
                "[Probe] Raw session",
                ..GetProbeReportLines(_probeTimeline.ActiveProbeSession, isActive: true),
            ];
        }

        if (_probeTimeline.LastProbeSession != null)
        {
            return
            [
                ..summaryLines,
                "",
                "[Probe] Raw session",
                ..GetProbeReportLines(_probeTimeline.LastProbeSession, isActive: false),
            ];
        }

        return summaryLines;
    }

    public IReadOnlyList<string> ArmCameraYProbe()
    {
        if (_probeTimeline.ActiveProbeSession != null)
        {
            return ["[CameraProbe] hook probe is active; run /xmutbgprobe off before arming camera probe."];
        }

        if (_probeTimeline.CameraProbeSession != null)
        {
            return ["[CameraProbe] already armed; run /xmutbgcamprobe restore before arming again."];
        }

        var baselineCamera = new Vector3(
            _configuration.TitleBackgroundCameraX,
            _configuration.TitleBackgroundCameraY,
            _configuration.TitleBackgroundCameraZ);
        var baselineFocus = new Vector3(
            _configuration.TitleBackgroundFocusX,
            _configuration.TitleBackgroundFocusY,
            _configuration.TitleBackgroundFocusZ);
        var probeCamera = new Vector3(
            baselineCamera.X,
            TitleBackgroundPreset.SanitizeCoordinate(baselineCamera.Y + 50f),
            baselineCamera.Z);
        var probeFocus = new Vector3(
            baselineFocus.X,
            TitleBackgroundPreset.SanitizeCoordinate(baselineFocus.Y - 50f),
            baselineFocus.Z);
        var session = new TitleBackgroundCameraProbeSession(
            TitleBackgroundCameraProbeSettingsSnapshot.Capture(_configuration),
            baselineCamera,
            baselineFocus,
            probeCamera,
            probeFocus);

        _probeTimeline.CameraProbeSession = session;
        try
        {
            _configuration.TitleBackgroundSelectedPresetId = string.Empty;
            _configuration.TitleBackgroundCameraOverrideEnabled = true;
            _configuration.TitleBackgroundCameraX = probeCamera.X;
            _configuration.TitleBackgroundCameraY = probeCamera.Y;
            _configuration.TitleBackgroundCameraZ = probeCamera.Z;
            _configuration.TitleBackgroundFocusX = probeFocus.X;
            _configuration.TitleBackgroundFocusY = probeFocus.Y;
            _configuration.TitleBackgroundFocusZ = probeFocus.Z;
            _configuration.Save();
            ApplyFromConfiguration();
            ResetCameraOverrideObservation();
            ResetCameraProbeTimelineObservation();

            return
            [
                "[CameraProbe] armed-y.",
                $"[CameraProbe] baselineCamera={FormatVector(baselineCamera)}",
                $"[CameraProbe] baselineFocus={FormatVector(baselineFocus)}",
                $"[CameraProbe] probeCamera={FormatVector(probeCamera)}",
                $"[CameraProbe] probeFocus={FormatVector(probeFocus)}",
                $"[CameraProbe] expectedCameraYDelta={FormatFloat(probeCamera.Y - baselineCamera.Y)}",
                $"[CameraProbe] expectedFocusYDelta={FormatFloat(probeFocus.Y - baselineFocus.Y)}",
                "[CameraProbe] next=logout -> character select -> login -> /xmutbgcamprobe report",
            ];
        }
        catch (Exception ex)
        {
            _probeTimeline.CameraProbeSession = null;
            session.OriginalSettings.ApplyTo(_configuration);
            _configuration.Save();
            ApplyFromConfiguration();
            _log.Warning(ex, "TitleBackground camera probe failed to arm.");
            return
            [
                "[CameraProbe] failed to arm-y.",
                $"[CameraProbe] error={ex.Message}",
            ];
        }
    }

    public IReadOnlyList<string> GetCameraProbeReportLines()
    {
        var currentCameraCaptured = TryCaptureActiveCameraSnapshot(out var currentCamera, out var currentCaptureError);
        Vector3? reportTimeSceneCameraPosition = currentCameraCaptured ? currentCamera.SceneCameraPosition : null;
        Vector3? reportTimeLookAtVector = currentCameraCaptured ? currentCamera.LookAtVector : null;
        var timelineSamples = BuildCameraProbeTimelineSamples();
        var latestTimelineSample = timelineSamples
            .Where(sample => sample.SceneCameraPosition.HasValue || sample.LookAtVector.HasValue)
            .OrderByDescending(sample => sample.Frame)
            .FirstOrDefault();
        var hasTimelineStabilitySample = latestTimelineSample.SceneCameraPosition.HasValue || latestTimelineSample.LookAtVector.HasValue;
        var stabilitySceneCameraPosition = hasTimelineStabilitySample
            ? latestTimelineSample.SceneCameraPosition
            : reportTimeSceneCameraPosition;
        var stabilityLookAtVector = hasTimelineStabilitySample
            ? latestTimelineSample.LookAtVector
            : reportTimeLookAtVector;
        var stabilitySampleSource = hasTimelineStabilitySample
            ? $"timeline[{latestTimelineSample.Frame}]"
            : "report-time-current";
        var session = _probeTimeline.CameraProbeSession;
        var input = new TitleBackgroundCameraProbeReportInput(
            session != null,
            session?.BaselineCamera ?? default,
            session?.BaselineFocus ?? default,
            session?.ProbeCamera ?? default,
            session?.ProbeFocus ?? default,
            _cameraObservation.LastAppliedCamera,
            _cameraObservation.LastPostFixOnSceneCameraPosition,
            stabilitySceneCameraPosition,
            _cameraObservation.LastAppliedFocus,
            _cameraObservation.LastPostFixOnLookAtVector,
            stabilityLookAtVector);
        var result = TitleBackgroundCameraProbeReport.Evaluate(input);
        var timelineAnalysis = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
            timelineSamples,
            _cameraObservation.LastPostFixOnSceneCameraPosition,
            _cameraObservation.LastPostFixOnLookAtVector);

        var lines = new List<string>
        {
            "[CameraProbe] Report",
            $"[CameraProbe] armed={input.Armed}",
        };
        if (session != null)
        {
            lines.Add($"[CameraProbe] armedAt={session.ArmedAt:yyyy-MM-dd HH:mm:ss zzz}");
            lines.Add($"[CameraProbe] baselineCamera={FormatVector(session.BaselineCamera)}");
            lines.Add($"[CameraProbe] baselineFocus={FormatVector(session.BaselineFocus)}");
            lines.Add($"[CameraProbe] probeCamera={FormatVector(session.ProbeCamera)}");
            lines.Add($"[CameraProbe] probeFocus={FormatVector(session.ProbeFocus)}");
            lines.Add($"[CameraProbe] expectedCameraYDelta={FormatFloat(session.ProbeCamera.Y - session.BaselineCamera.Y)}");
            lines.Add($"[CameraProbe] expectedFocusYDelta={FormatFloat(session.ProbeFocus.Y - session.BaselineFocus.Y)}");
        }
        else
        {
            lines.Add("[CameraProbe] baselineCamera=none");
            lines.Add("[CameraProbe] baselineFocus=none");
            lines.Add("[CameraProbe] probeCamera=none");
            lines.Add("[CameraProbe] probeFocus=none");
        }

        lines.AddRange(
        [
            $"[CameraProbe] lastAppliedCamera={FormatVector(_cameraObservation.LastAppliedCamera)}",
            $"[CameraProbe] postFixOnSceneCameraPosition={FormatVector(_cameraObservation.LastPostFixOnSceneCameraPosition)}",
            $"[CameraProbe] currentSceneCameraPosition={FormatVector(reportTimeSceneCameraPosition)}",
            $"[CameraProbe] lastAppliedFocus={FormatVector(_cameraObservation.LastAppliedFocus)}",
            $"[CameraProbe] postFixOnLookAtVector={FormatVector(_cameraObservation.LastPostFixOnLookAtVector)}",
            $"[CameraProbe] currentLookAtVector={FormatVector(reportTimeLookAtVector)}",
            $"[CameraProbe] stabilitySampleSource={stabilitySampleSource}",
            $"[CameraProbe] timelineStatus={_probeTimeline.CameraProbeTimelineStatus}",
            $"[CameraProbe] timelineError={FormatNone(_probeTimeline.CameraProbeTimelineError)}",
            $"[CameraProbe] currentCameraCaptureStatus={(currentCameraCaptured ? "success" : "failed")}",
            $"[CameraProbe] currentCameraCaptureError={FormatNone(currentCaptureError)}",
            $"[CameraProbe] cameraY.appliedToPostFixOn.delta={FormatVectorAxisDelta(_cameraObservation.LastPostFixOnSceneCameraPosition, _cameraObservation.LastAppliedCamera, 1)}",
            $"[CameraProbe] cameraY.postFixOnToStabilitySample.delta={FormatVectorAxisDelta(stabilitySceneCameraPosition, _cameraObservation.LastPostFixOnSceneCameraPosition, 1)}",
            $"[CameraProbe] focusY.appliedToPostFixOn.delta={FormatVectorAxisDelta(_cameraObservation.LastPostFixOnLookAtVector, _cameraObservation.LastAppliedFocus, 1)}",
            $"[CameraProbe] focusY.postFixOnToStabilitySample.delta={FormatVectorAxisDelta(stabilityLookAtVector, _cameraObservation.LastPostFixOnLookAtVector, 1)}",
            "[CameraProbe] Timeline",
        ]);

        foreach (var sample in timelineSamples)
        {
            var snapshotFound = _probeTimeline.CameraProbeTimelineSnapshots.TryGetValue(sample.Frame, out var snapshot);
            var events = GetCameraProbeTimelineEventCounts(sample.Frame);
            var lobbyUpdate = GetCameraProbeLobbyUpdateSnapshot(sample.Frame);
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].sceneCamera={FormatVector(sample.SceneCameraPosition)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lookAt={FormatVector(sample.LookAtVector)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].cameraY={FormatVectorAxis(sample.SceneCameraPosition, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].focusY={FormatVectorAxis(sample.LookAtVector, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].cameraYDeltaFromPostFixOn={FormatVectorAxisDelta(sample.SceneCameraPosition, _cameraObservation.LastPostFixOnSceneCameraPosition, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].focusYDeltaFromPostFixOn={FormatVectorAxisDelta(sample.LookAtVector, _cameraObservation.LastPostFixOnLookAtVector, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].fixOnCalls={events.FixOnCalls}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lobbyUpdateCalls={events.LobbyUpdateCalls}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].loadLobbySceneCalls={events.LoadLobbySceneCalls}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].createSceneCalls={events.CreateSceneCalls}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].preLobbyUpdateSceneCamera={FormatVector(lobbyUpdate.PreSceneCameraPosition)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].preLobbyUpdateLookAt={FormatVector(lobbyUpdate.PreLookAtVector)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].preLobbyUpdateCameraY={FormatVectorAxis(lobbyUpdate.PreSceneCameraPosition, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].preLobbyUpdateFocusY={FormatVectorAxis(lobbyUpdate.PreLookAtVector, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].postLobbyUpdateSceneCamera={FormatVector(lobbyUpdate.PostSceneCameraPosition)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].postLobbyUpdateLookAt={FormatVector(lobbyUpdate.PostLookAtVector)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].postLobbyUpdateCameraY={FormatVectorAxis(lobbyUpdate.PostSceneCameraPosition, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].postLobbyUpdateFocusY={FormatVectorAxis(lobbyUpdate.PostLookAtVector, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lobbyUpdateDelta.cameraY={FormatVectorAxisDelta(lobbyUpdate.PostSceneCameraPosition, lobbyUpdate.PreSceneCameraPosition, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lobbyUpdateDelta.focusY={FormatVectorAxisDelta(lobbyUpdate.PostLookAtVector, lobbyUpdate.PreLookAtVector, 1)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lobbyUpdateCaptureStatus={FormatLobbyUpdateCaptureStatus(lobbyUpdate)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].lobbyUpdateCaptureError={FormatLobbyUpdateCaptureError(lobbyUpdate)}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].status={(snapshotFound ? snapshot.Status : "missing")}");
            lines.Add($"[CameraProbe] timeline[{sample.Frame}].error={(snapshotFound ? FormatNone(snapshot.Error) : "none")}");
        }

        var cameraCoincidentEvents = TitleBackgroundCameraProbeReport.DescribeCoincidentEvents(
            timelineAnalysis.CameraOverwriteFirstObservedFrame,
            GetCameraProbeTimelineEventCounts(timelineAnalysis.CameraOverwriteFirstObservedFrame));
        var focusCoincidentEvents = TitleBackgroundCameraProbeReport.DescribeCoincidentEvents(
            timelineAnalysis.FocusOverwriteFirstObservedFrame,
            GetCameraProbeTimelineEventCounts(timelineAnalysis.FocusOverwriteFirstObservedFrame));
        var focusDriftEvents = TitleBackgroundCameraProbeReport.DescribeFocusDriftEvents(
            timelineSamples,
            GetCameraProbeTimelineEventCountsInRange,
            _cameraObservation.LastPostFixOnLookAtVector);

        lines.AddRange(
        [
            $"[CameraProbe] cameraOverwriteFirstObservedFrame={FormatNullableInt(timelineAnalysis.CameraOverwriteFirstObservedFrame)}",
            $"[CameraProbe] focusOverwriteFirstObservedFrame={FormatNullableInt(timelineAnalysis.FocusOverwriteFirstObservedFrame)}",
            $"[CameraProbe] cameraOverwriteCoincidentEvents={cameraCoincidentEvents}",
            $"[CameraProbe] focusOverwriteCoincidentEvents={focusCoincidentEvents}",
            $"[CameraProbe] focusDriftObservedEvents={focusDriftEvents}",
            $"[CameraProbe] cameraOverwritePattern={TitleBackgroundCameraProbeReport.FormatOverwritePattern(timelineAnalysis.CameraOverwritePattern)}",
            $"[CameraProbe] focusOverwritePattern={TitleBackgroundCameraProbeReport.FormatOverwritePattern(timelineAnalysis.FocusOverwritePattern)}",
            $"[CameraProbe] verdict.cameraYFixOnReflection={TitleBackgroundCameraProbeReport.FormatVerdict(result.CameraYFixOnReflection)}",
            $"[CameraProbe] verdict.cameraYPostFixOnStability={TitleBackgroundCameraProbeReport.FormatVerdict(result.CameraYPostFixOnStability)}",
            $"[CameraProbe] verdict.focusYFixOnReflection={TitleBackgroundCameraProbeReport.FormatVerdict(result.FocusYFixOnReflection)}",
            $"[CameraProbe] verdict.focusYPostFixOnStability={TitleBackgroundCameraProbeReport.FormatVerdict(result.FocusYPostFixOnStability)}",
            $"[CameraProbe] {result.LikelyConclusion}",
        ]);

        return lines;
    }

    public IReadOnlyList<string> RestoreCameraProbe()
    {
        if (_probeTimeline.CameraProbeSession == null)
        {
            return ["[CameraProbe] no armed session; nothing to restore."];
        }

        var session = _probeTimeline.CameraProbeSession;
        _probeTimeline.CameraProbeSession = null;
        session.OriginalSettings.ApplyTo(_configuration);
        _configuration.Save();
        ApplyFromConfiguration();
        ResetCameraOverrideObservation();
        ResetCameraProbeTimelineObservation();

        return
        [
            "[CameraProbe] restored original camera settings.",
            $"[CameraProbe] restoredCamera={FormatVector(session.BaselineCamera)}",
            $"[CameraProbe] restoredFocus={FormatVector(session.BaselineFocus)}",
            $"[CameraProbe] restoredCameraOverrideEnabled={session.OriginalSettings.CameraOverrideEnabled}",
        ];
    }

    private string TryRestoreProbeSettings(TitleBackgroundProbeSettingsSnapshot snapshot)
    {
        try
        {
            snapshot.ApplyTo(_configuration);
            ReloadNativeIntegration();
            return "[Probe] original settings restored.";
        }
        catch (Exception ex)
        {
            if (_probeTimeline.LastProbeSession != null)
            {
                _probeTimeline.LastProbeSession.RuntimeErrorOccurred = true;
                _probeTimeline.LastProbeSession.LastError = $"restore failed: {ex.Message}";
            }

            _log.Warning(ex, "TitleBackground probe failed to restore original settings.");
            return $"[Probe] failed to restore original settings: {ex.Message}";
        }
    }

    private TitleBackgroundProbeReportInput BuildProbeReportInput(TitleBackgroundProbeSession? session, bool isActive)
    {
        var useAutomaticCounters = _probeTimeline.AutomaticProbeCountersEnabled;
        var hooksEnabled = isActive || session == null ? AreAnyHooksEnabled() : session.HookEnabledAtEnd;
        var runtimeError = (isActive || session == null) && _hookLifecycle.State == TitleBackgroundServiceState.RuntimeError;
        if (session != null)
        {
            runtimeError |= session.RuntimeErrorOccurred;
        }

        var lastError = session?.LastError ?? string.Empty;
        var createSceneCallCount = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.CreateSceneCallCount : session?.CreateSceneCallCount ?? 0;
        var lobbyUpdateCallCount = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LobbyUpdateCallCount : session?.LobbyUpdateCallCount ?? 0;
        var loadLobbySceneCallCount = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LoadLobbySceneCallCount : session?.LoadLobbySceneCallCount ?? 0;
        var lastCreateScenePath = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastCreateScenePath : session?.LastCreateScenePath ?? string.Empty;
        var lastCreateSceneTerritoryId = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastCreateSceneTerritoryId : session?.LastCreateSceneTerritoryId ?? 0;
        var lastCreateSceneLayerFilterKey = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastCreateSceneLayerFilterKey : session?.LastCreateSceneLayerFilterKey ?? 0;
        var lastLobbyUpdateMapId = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastLobbyUpdateMapId : session?.LastLobbyUpdateMapId ?? GameLobbyType.None;
        var lastLobbyUpdateTime = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastLobbyUpdateTime : session?.LastLobbyUpdateTime ?? 0;
        var lastLoadLobbySceneMapId = useAutomaticCounters ? _probeTimeline.AutomaticProbeCounters.LastLoadLobbySceneMapId : session?.LastLoadLobbySceneMapId ?? GameLobbyType.None;

        return new TitleBackgroundProbeReportInput(
            ProbeActive: isActive,
            OverrideEnabled: _configuration.TitleBackgroundOverrideEnabled,
            RuntimeMode: _configuration.TitleBackgroundRuntimeMode,
            CreateSceneResolverMode: _configuration.TitleBackgroundCreateSceneResolverMode,
            LobbyUpdateResolverMode: _configuration.TitleBackgroundLobbyUpdateResolverMode,
            AutomaticCountersEnabled: _probeTimeline.AutomaticProbeCountersEnabled,
            HooksEnabled: hooksEnabled,
            RuntimeError: runtimeError,
            ResolverError: _addressResolver.LastError,
            LastError: lastError,
            CreateSceneCallCount: createSceneCallCount,
            LobbyUpdateCallCount: lobbyUpdateCallCount,
            LoadLobbySceneCallCount: loadLobbySceneCallCount,
            LastCreateScenePath: lastCreateScenePath,
            LastCreateSceneTerritoryId: lastCreateSceneTerritoryId,
            LastCreateSceneLayerFilterKey: lastCreateSceneLayerFilterKey,
            LastLobbyUpdateMapId: lastLobbyUpdateMapId,
            LastLobbyUpdateTime: lastLobbyUpdateTime,
            LastLoadLobbySceneMapId: lastLoadLobbySceneMapId);
    }

    private static IReadOnlyList<string> BuildProbeReportSummaryLines(TitleBackgroundProbeReportInput input)
    {
        var attentionItems = TitleBackgroundProbeReportHelper.GetAttentionItems(input);
        var lines = new List<string>
        {
            "[Probe] Summary",
            $"[Probe] active={input.ProbeActive}",
            $"[Probe] status={TitleBackgroundProbeReportHelper.GetOverallStatus(input)}",
            $"[Probe] modeStatus={TitleBackgroundProbeReportHelper.GetModeStatus(input)}",
            $"[Probe] runtimeMode={input.RuntimeMode}",
            $"[Probe] overrideEnabled={input.OverrideEnabled}",
            $"[Probe] createSceneResolver={input.CreateSceneResolverMode}",
            $"[Probe] lobbyUpdateResolver={input.LobbyUpdateResolverMode}",
            $"[Probe] automaticCountersEnabled={input.AutomaticCountersEnabled}",
            $"[Probe] hooksEnabled={input.HooksEnabled}",
            "",
            "[Probe] Observed",
            $"[Probe] CreateScene.callCount={input.CreateSceneCallCount}",
            $"[Probe] LobbyUpdate.callCount={input.LobbyUpdateCallCount}",
            $"[Probe] LoadLobbyScene.callCount={input.LoadLobbySceneCallCount}",
            "",
            "[Probe] Latest Values",
            $"[Probe] CreateScene.lastPath={FormatNone(input.LastCreateScenePath)}",
            $"[Probe] CreateScene.lastTerritoryId={input.LastCreateSceneTerritoryId}",
            $"[Probe] CreateScene.lastLayerFilterKey={input.LastCreateSceneLayerFilterKey}",
            $"[Probe] LobbyUpdate.lastMapId={input.LastLobbyUpdateMapId}",
            $"[Probe] LobbyUpdate.lastTime={input.LastLobbyUpdateTime}",
            $"[Probe] LoadLobbyScene.lastMapId={input.LastLoadLobbySceneMapId}",
            "",
            "[Probe] Not Yet Observed / Attention",
        };

        if (attentionItems.Count == 0)
        {
            lines.Add("[Probe] none");
        }
        else
        {
            foreach (var item in attentionItems)
            {
                lines.Add($"[Probe] {item}");
            }
        }

        lines.Add("");
        lines.Add("[Probe] Next Check");
        lines.Add($"[Probe] {TitleBackgroundProbeReportHelper.GetNextCheck(input)}");
        return lines;
    }

    private IReadOnlyList<string> GetProbeReportLines(TitleBackgroundProbeSession session, bool isActive)
    {
        var hooksEnabled = isActive ? AreAnyHooksEnabled() : session.HookEnabledAtEnd;
        var runtimeError = session.RuntimeErrorOccurred || (isActive && _hookLifecycle.State == TitleBackgroundServiceState.RuntimeError);
        var observedAnyDetour = session.CreateSceneCallCount > 0 || session.LobbyUpdateCallCount > 0;
        var result = !hooksEnabled || runtimeError
            ? "Failure"
            : observedAnyDetour
                ? "Success"
                : "Warning";

        return
        [
            $"[Probe] active={isActive}",
            $"[Probe] startedAt={session.StartedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"[Probe] result={result}",
            $"[Probe] hooksEnabled={hooksEnabled}",
            $"[Probe] hookEnabledAtStart={session.HookEnabledAtStart}",
            $"[Probe] runtimeError={runtimeError}",
            $"[Probe] createSceneCalls={session.CreateSceneCallCount}",
            $"[Probe] createSceneCharaSelectCalls={session.CreateSceneCharaSelectCallCount}",
            $"[Probe] lobbyUpdateCalls={session.LobbyUpdateCallCount}",
            $"[Probe] loadLobbySceneCalls={session.LoadLobbySceneCallCount}",
            $"[Probe] lastCreateSceneLobbyType={session.LastCreateSceneLobbyType}",
            $"[Probe] lastCreateScenePath={(string.IsNullOrWhiteSpace(session.LastCreateScenePath) ? "none" : session.LastCreateScenePath)}",
            $"[Probe] lastCreateSceneTerritoryId={session.LastCreateSceneTerritoryId}",
            $"[Probe] lastCreateSceneLayerFilterKey={session.LastCreateSceneLayerFilterKey}",
            $"[Probe] lastLobbyUpdateMapId={session.LastLobbyUpdateMapId}",
            $"[Probe] lastLobbyUpdateTime={session.LastLobbyUpdateTime}",
            $"[Probe] lastLoadLobbySceneMapId={session.LastLoadLobbySceneMapId}",
            $"[Probe] createSceneHistory={FormatProbeHistory(session.CreateSceneHistory)}",
            $"[Probe] lastError={(string.IsNullOrWhiteSpace(session.LastError) ? "none" : session.LastError)}",
        ];
    }

    private void RecordProbeCreateScene(GameLobbyType lobbyType, string path, uint territoryId, uint layerFilterKey)
    {
        if (_probeTimeline.AutomaticProbeCountersEnabled)
        {
            _probeTimeline.AutomaticProbeCounters.CreateSceneCallCount++;
            _probeTimeline.AutomaticProbeCounters.LastCreateScenePath = path;
            _probeTimeline.AutomaticProbeCounters.LastCreateSceneTerritoryId = territoryId;
            _probeTimeline.AutomaticProbeCounters.LastCreateSceneLayerFilterKey = layerFilterKey;
        }

        if (_probeTimeline.ActiveProbeSession == null)
        {
            return;
        }

        _probeTimeline.ActiveProbeSession.CreateSceneCallCount++;
        if (lobbyType == GameLobbyType.CharaSelect)
        {
            _probeTimeline.ActiveProbeSession.CreateSceneCharaSelectCallCount++;
        }

        _probeTimeline.ActiveProbeSession.LastCreateSceneLobbyType = lobbyType;
        _probeTimeline.ActiveProbeSession.LastCreateScenePath = path;
        _probeTimeline.ActiveProbeSession.LastCreateSceneTerritoryId = territoryId;
        _probeTimeline.ActiveProbeSession.LastCreateSceneLayerFilterKey = layerFilterKey;
        _probeTimeline.ActiveProbeSession.CreateSceneHistory.Add(
            $"lobbyType={lobbyType},path={(string.IsNullOrWhiteSpace(path) ? "none" : path)},territoryId={territoryId},layerFilterKey={layerFilterKey}");
        if (_probeTimeline.ActiveProbeSession.CreateSceneHistory.Count > 5)
        {
            _probeTimeline.ActiveProbeSession.CreateSceneHistory.RemoveAt(0);
        }
    }

    private static string FormatProbeHistory(IReadOnlyList<string> history)
    {
        return history.Count == 0 ? "none" : string.Join(" | ", history);
    }

    private void RecordProbeLobbyUpdate(GameLobbyType mapId, int time)
    {
        if (_probeTimeline.AutomaticProbeCountersEnabled)
        {
            _probeTimeline.AutomaticProbeCounters.LobbyUpdateCallCount++;
            _probeTimeline.AutomaticProbeCounters.LastLobbyUpdateMapId = mapId;
            _probeTimeline.AutomaticProbeCounters.LastLobbyUpdateTime = time;
        }

        if (_probeTimeline.ActiveProbeSession == null)
        {
            return;
        }

        _probeTimeline.ActiveProbeSession.LobbyUpdateCallCount++;
        _probeTimeline.ActiveProbeSession.LastLobbyUpdateMapId = mapId;
        _probeTimeline.ActiveProbeSession.LastLobbyUpdateTime = time;
    }

    private void RecordProbeLoadLobbyScene(GameLobbyType mapId)
    {
        if (_probeTimeline.AutomaticProbeCountersEnabled)
        {
            _probeTimeline.AutomaticProbeCounters.LoadLobbySceneCallCount++;
            _probeTimeline.AutomaticProbeCounters.LastLoadLobbySceneMapId = mapId;
        }

        if (_probeTimeline.ActiveProbeSession == null)
        {
            return;
        }

        _probeTimeline.ActiveProbeSession.LoadLobbySceneCallCount++;
        _probeTimeline.ActiveProbeSession.LastLoadLobbySceneMapId = mapId;
    }

    private bool AreAnyHooksEnabled()
    {
        return IsHookEnabled(_hookLifecycle.CreateSceneHook)
            || IsHookEnabled(_hookLifecycle.LobbyUpdateHook)
            || IsHookEnabled(_hookLifecycle.LoadLobbySceneHook)
            || IsHookEnabled(_hookLifecycle.LobbySceneLoadedHook)
            || IsHookEnabled(_hookLifecycle.CameraFixOnHook)
            || IsHookEnabled(_hookLifecycle.CalculateLobbyCameraLookAtYHook)
            || IsHookEnabled(_hookLifecycle.SetCameraCurveMidPointHook)
            || IsHookEnabled(_hookLifecycle.CalculateCameraCurveLowAndHighPointHook);
    }

    private void CaptureCameraProbeTimelineOnFrameworkUpdate()
    {
        if (_probeTimeline.CameraProbeTimelineFrameCounter < 0)
        {
            return;
        }

        _probeTimeline.CameraProbeTimelineFrameCounter++;
        if (Array.IndexOf(CameraProbeTimelineFrames, _probeTimeline.CameraProbeTimelineFrameCounter) < 0)
        {
            return;
        }

        if (_probeTimeline.CameraProbeTimelineSnapshots.ContainsKey(_probeTimeline.CameraProbeTimelineFrameCounter))
        {
            return;
        }

        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            _probeTimeline.CameraProbeTimelineSnapshots[_probeTimeline.CameraProbeTimelineFrameCounter] = new TitleBackgroundCameraProbeTimelineSnapshot(
                snapshot.SceneCameraPosition,
                snapshot.LookAtVector,
                "success",
                string.Empty);
            _probeTimeline.CameraProbeTimelineStatus = _probeTimeline.CameraProbeTimelineFrameCounter >= CameraProbeTimelineFrames[^1]
                ? "complete"
                : "collecting";
            _probeTimeline.CameraProbeTimelineError = string.Empty;
        }
        else
        {
            _probeTimeline.CameraProbeTimelineSnapshots[_probeTimeline.CameraProbeTimelineFrameCounter] = new TitleBackgroundCameraProbeTimelineSnapshot(
                null,
                null,
                "failed",
                string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : errorMessage);
            _probeTimeline.CameraProbeTimelineStatus = "partial";
            _probeTimeline.CameraProbeTimelineError = $"frame {_probeTimeline.CameraProbeTimelineFrameCounter}: {_probeTimeline.CameraProbeTimelineSnapshots[_probeTimeline.CameraProbeTimelineFrameCounter].Error}";
        }

        if (_probeTimeline.CameraProbeTimelineFrameCounter >= CameraProbeTimelineFrames[^1])
        {
            _probeTimeline.CameraProbeTimelineFrameCounter = -1;
        }
    }

    private void ScheduleCameraProbeTimelineCapture(bool overrideAppliedInThisInvocation)
    {
        if (_probeTimeline.CameraProbeSession == null || !overrideAppliedInThisInvocation)
        {
            return;
        }

        _probeTimeline.CameraProbeTimelineFrameCounter = 0;
        _probeTimeline.CameraProbeTimelineStatus = "collecting";
        _probeTimeline.CameraProbeTimelineError = string.Empty;
        _probeTimeline.CameraProbeTimelineSnapshots.Clear();
        _probeTimeline.CameraProbeTimelineSnapshots[0] = new TitleBackgroundCameraProbeTimelineSnapshot(
            _cameraObservation.LastPostFixOnSceneCameraPosition,
            _cameraObservation.LastPostFixOnLookAtVector,
            _cameraObservation.LastPostFixOnCameraCaptureStatus,
            _cameraObservation.LastPostFixOnCameraCaptureError);
    }

    private void ResetCameraProbeTimelineObservation()
    {
        _probeTimeline.CameraProbeTimelineFrameCounter = -1;
        _probeTimeline.CameraProbeTimelineStatus = "not-run";
        _probeTimeline.CameraProbeTimelineError = string.Empty;
        _probeTimeline.CameraProbeTimelineSnapshots.Clear();
        _probeTimeline.CameraProbeTimelineEventCounts.Clear();
        _probeTimeline.CameraProbeLobbyUpdateSnapshots.Clear();
    }

    private int? RecordCameraProbeTimelineEvent(TitleBackgroundCameraProbeTimelineEventKind kind)
    {
        if (_probeTimeline.CameraProbeSession == null)
        {
            return null;
        }

        var frame = GetCurrentCameraProbeTimelineEventFrame();
        if (!frame.HasValue)
        {
            return null;
        }

        var counts = GetCameraProbeTimelineEventCounts(frame);
        counts = kind switch
        {
            TitleBackgroundCameraProbeTimelineEventKind.FixOn => counts with { FixOnCalls = counts.FixOnCalls + 1 },
            TitleBackgroundCameraProbeTimelineEventKind.LobbyUpdate => counts with { LobbyUpdateCalls = counts.LobbyUpdateCalls + 1 },
            TitleBackgroundCameraProbeTimelineEventKind.LoadLobbyScene => counts with { LoadLobbySceneCalls = counts.LoadLobbySceneCalls + 1 },
            TitleBackgroundCameraProbeTimelineEventKind.CreateScene => counts with { CreateSceneCalls = counts.CreateSceneCalls + 1 },
            _ => counts,
        };
        _probeTimeline.CameraProbeTimelineEventCounts[frame.Value] = counts;
        return frame;
    }

    private int? GetCurrentCameraProbeTimelineEventFrame()
    {
        if (_probeTimeline.CameraProbeTimelineFrameCounter < 0)
        {
            return _probeTimeline.CameraProbeTimelineStatus == "not-run" ? 0 : null;
        }

        return _probeTimeline.CameraProbeTimelineFrameCounter;
    }

    private TitleBackgroundCameraProbeTimelineEventCounts GetCameraProbeTimelineEventCounts(int? frame)
    {
        return frame.HasValue && _probeTimeline.CameraProbeTimelineEventCounts.TryGetValue(frame.Value, out var counts)
            ? counts
            : default;
    }

    private TitleBackgroundCameraProbeTimelineEventCounts GetCameraProbeTimelineEventCountsInRange(int startFrame, int endFrame)
    {
        var totals = new TitleBackgroundCameraProbeTimelineEventCounts();
        foreach (var (frame, events) in _probeTimeline.CameraProbeTimelineEventCounts)
        {
            if (frame < startFrame || frame > endFrame)
            {
                continue;
            }

            totals = new TitleBackgroundCameraProbeTimelineEventCounts(
                totals.FixOnCalls + events.FixOnCalls,
                totals.LobbyUpdateCalls + events.LobbyUpdateCalls,
                totals.LoadLobbySceneCalls + events.LoadLobbySceneCalls,
                totals.CreateSceneCalls + events.CreateSceneCalls);
        }

        return totals;
    }

    private void CaptureCameraProbeLobbyUpdateState(int? frame, bool beforeOriginal)
    {
        if (!frame.HasValue || _probeTimeline.CameraProbeSession == null)
        {
            return;
        }

        var current = GetCameraProbeLobbyUpdateSnapshot(frame.Value);
        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            current = beforeOriginal
                ? current with
                {
                    PreSceneCameraPosition = current.PreSceneCameraPosition ?? snapshot.SceneCameraPosition,
                    PreLookAtVector = current.PreLookAtVector ?? snapshot.LookAtVector,
                    PreStatus = "success",
                    PreError = string.Empty,
                }
                : current with
                {
                    PostSceneCameraPosition = snapshot.SceneCameraPosition,
                    PostLookAtVector = snapshot.LookAtVector,
                    PostStatus = "success",
                    PostError = string.Empty,
                };
        }
        else
        {
            current = beforeOriginal
                ? current with
                {
                    PreStatus = "failed",
                    PreError = string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : errorMessage,
                }
                : current with
                {
                    PostStatus = "failed",
                    PostError = string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : errorMessage,
                };
        }

        _probeTimeline.CameraProbeLobbyUpdateSnapshots[frame.Value] = current;
    }

    private TitleBackgroundCameraProbeLobbyUpdateSnapshot GetCameraProbeLobbyUpdateSnapshot(int frame)
    {
        return _probeTimeline.CameraProbeLobbyUpdateSnapshots.TryGetValue(frame, out var snapshot)
            ? snapshot
            : default;
    }

    private static string FormatLobbyUpdateCaptureStatus(TitleBackgroundCameraProbeLobbyUpdateSnapshot snapshot)
    {
        var preStatus = string.IsNullOrWhiteSpace(snapshot.PreStatus) ? "missing" : snapshot.PreStatus;
        var postStatus = string.IsNullOrWhiteSpace(snapshot.PostStatus) ? "missing" : snapshot.PostStatus;
        return $"pre={preStatus},post={postStatus}";
    }

    private static string FormatLobbyUpdateCaptureError(TitleBackgroundCameraProbeLobbyUpdateSnapshot snapshot)
    {
        var preError = string.IsNullOrWhiteSpace(snapshot.PreError) ? "none" : snapshot.PreError;
        var postError = string.IsNullOrWhiteSpace(snapshot.PostError) ? "none" : snapshot.PostError;
        return $"pre={preError},post={postError}";
    }

    private void RestoreCameraProbeSettingsOnDispose()
    {
        if (_probeTimeline.CameraProbeSession == null)
        {
            return;
        }

        try
        {
            _probeTimeline.CameraProbeSession.OriginalSettings.ApplyTo(_configuration);
            _configuration.Save();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground camera probe failed to restore settings on dispose.");
        }
        finally
        {
            _probeTimeline.CameraProbeSession = null;
            ResetCameraProbeTimelineObservation();
        }
    }

    private IReadOnlyList<TitleBackgroundCameraProbeTimelineSample> BuildCameraProbeTimelineSamples()
    {
        var samples = new List<TitleBackgroundCameraProbeTimelineSample>(CameraProbeTimelineFrames.Length);
        foreach (var frame in CameraProbeTimelineFrames)
        {
            if (_probeTimeline.CameraProbeTimelineSnapshots.TryGetValue(frame, out var snapshot))
            {
                samples.Add(new TitleBackgroundCameraProbeTimelineSample(
                    frame,
                    snapshot.SceneCameraPosition,
                    snapshot.LookAtVector));
            }
            else
            {
                samples.Add(new TitleBackgroundCameraProbeTimelineSample(frame, null, null));
            }
        }

        return samples;
    }
}
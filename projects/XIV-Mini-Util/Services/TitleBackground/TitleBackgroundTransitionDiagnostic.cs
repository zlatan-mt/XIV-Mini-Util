// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundTransitionDiagnostic.cs
// Description: Title Background character-select-to-login transition diagnostics
// Reason: One reproduction run should capture lifecycle, stale-state, and counter-delta evidence without changing runtime behavior
using System.Text;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundTransitionCounters(
    int Phase2ELookAtYCallCount,
    int Phase2FSetMidCallCount,
    int Phase2FLowHighCallCount,
    int Phase2GSetMidAttemptCount,
    int Phase2GLowHighAttemptCount,
    int SceneReadyAcceptedCount,
    int SceneReadyRawCallCount);

internal readonly record struct TitleBackgroundTransitionDelta(
    bool BaselineEstablished,
    bool FirstReport,
    int Phase2ELookAtYCallCount,
    int Phase2FSetMidCallCount,
    int Phase2FLowHighCallCount,
    int Phase2GSetMidAttemptCount,
    int Phase2GLowHighAttemptCount,
    int SceneReadyAcceptedCount,
    int SceneReadyRawCallCount);

internal readonly record struct TitleBackgroundTransitionContext(
    bool IsLoggedIn,
    bool IsCharaSelectOrTitleBackground,
    uint CurrentTerritoryId,
    string CurrentTerritoryType,
    string CurrentLobbyMap);

internal readonly record struct TitleBackgroundTransitionSceneOverrideState(
    bool Active,
    bool HistoricalLastOverrideApplied,
    string ActiveLobbyType,
    string ActiveOverridePath,
    string LastHistoricalOverridePath,
    string CurrentLobbyMap,
    string LastCurrentLobbyMapResetReason,
    string CleanupReason,
    bool ActiveAfterLoginDetected);

internal readonly record struct TitleBackgroundTransitionAdapterState(
    string State,
    string LastEvent,
    int SceneGeneration,
    bool StaleAfterLoginDetected);

internal readonly record struct TitleBackgroundTransitionPhase2GState(
    string LastApplyContext,
    bool AppliedAfterLogin,
    long LastAppliedAfterLoginEventSeq,
    bool AppliedAfterLeavingCharaSelect,
    string LastAllowedReason,
    string LastSkippedReason);

internal readonly record struct TitleBackgroundTransitionCameraState(
    string CurrentCaptureStatus,
    string CurrentDirH,
    string CurrentDirV,
    string CurrentDistance,
    string CurrentPosition,
    string CurrentLookAt);

internal readonly record struct TitleBackgroundTransitionSummaryInput(
    TitleBackgroundTransitionContext Context,
    TitleBackgroundTransitionSceneOverrideState SceneOverride,
    TitleBackgroundTransitionAdapterState Adapter,
    TitleBackgroundTransitionPhase2GState Phase2G,
    TitleBackgroundTransitionCameraState Camera,
    TitleBackgroundTransitionCounters Counters,
    TitleBackgroundTransitionDelta Delta,
    string FirstEvent,
    string LastEvent,
    int EventCount,
    int SceneReadyRawCallCount,
    int SceneReadyAcceptedCount,
    int SceneReadyRejectedCount,
    bool SceneReadyAcceptedSuspicious,
    string SceneReadyLastAcceptedReason,
    string SceneReadyLastRejectedReason,
    int SceneReadyLastAcceptedSceneGeneration,
    string SceneReadyAcceptedGenerations,
    bool PostLoginSceneReadyAcceptedDetected,
    long SceneReadyLastAcceptedAfterLoginEventSeq);

internal sealed class TitleBackgroundTransitionDiagnosticRecorder
{
    public const int RingCapacity = 128;

    private readonly Queue<TitleBackgroundTransitionDiagnosticEvent> _events = new(RingCapacity);
    private TitleBackgroundTransitionCounters _lastDiagnosticCounters;
    private long _nextEventSeq;
    private bool _diagnosticBaselineEstablished;
    private readonly Dictionary<string, int> _suppressedSpamKeys = [];

    public int SceneReadyRawCallCount { get; private set; }
    public int SceneReadyAcceptedCount { get; private set; }
    public int SceneReadyRejectedCount { get; private set; }
    public string SceneReadyLastAcceptedReason { get; private set; } = "none";
    public string SceneReadyLastRejectedReason { get; private set; } = "none";
    public int SceneReadyLastAcceptedSceneGeneration { get; private set; }
    public string FirstEvent => _events.Count == 0 ? "none" : _events.Peek().Name;
    public string LastEvent { get; private set; } = "none";
    public string LastPhase2GApplyContext { get; private set; } = "none";
    public string LastPhase2GAllowedReason { get; private set; } = "none";
    public string LastPhase2GSkippedReason { get; private set; } = "none";
    public bool Phase2GAppliedAfterLogin { get; private set; }
    public bool Phase2GAppliedAfterLeavingCharaSelect { get; private set; }
    public long LastPhase2GAppliedAfterLoginEventSeq { get; private set; }
    public bool PostLoginSceneReadyAccepted { get; private set; }
    public long LastSceneReadyAcceptedAfterLoginEventSeq { get; private set; }
    public bool StaleAdapterStateAfterLogin { get; private set; }
    public bool StaleCurrentLobbyMapAfterLogin { get; private set; }
    public bool StaleSceneOverrideStateAfterLogin { get; private set; }
    public int EventCount => _events.Count;
    public IReadOnlyList<TitleBackgroundTransitionDiagnosticEvent> Events => _events.ToArray();
    public string AcceptedGenerations => string.Join(",", _acceptedGenerations.OrderBy(value => value));

    private readonly HashSet<int> _acceptedGenerations = [];

    private static bool IsImportantEvent(string name)
    {
        return name.Contains("accepted", StringComparison.OrdinalIgnoreCase)
            || name.Contains("override applied", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CurrentLobbyMap", StringComparison.Ordinal)
            || name.Contains("adapter state transition", StringComparison.Ordinal)
            || name.Contains("runtime restore", StringComparison.Ordinal)
            || name.Contains("Phase 2G", StringComparison.Ordinal)
            || name.Contains("logged-in world", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cleanup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("stale", StringComparison.OrdinalIgnoreCase)
            || name.Contains("leak", StringComparison.OrdinalIgnoreCase);
    }

    public long Record(string name, IReadOnlyDictionary<string, string>? snapshot = null, string reason = "", string error = "")
    {
        var seq = ++_nextEventSeq;
        var compactSnapshot = snapshot == null || snapshot.Count == 0
            ? string.Empty
            : string.Join("; ", snapshot.Select(static pair => $"{pair.Key}={pair.Value}"));
        var diagnosticEvent = new TitleBackgroundTransitionDiagnosticEvent(
            seq,
            name,
            DateTimeOffset.Now,
            Environment.CurrentManagedThreadId,
            string.IsNullOrWhiteSpace(reason) ? "none" : reason,
            string.IsNullOrWhiteSpace(error) ? "none" : error,
            compactSnapshot);

        if (_events.Count >= RingCapacity)
        {
            DropOldestEvent(name);
        }

        _events.Enqueue(diagnosticEvent);
        LastEvent = name;
        return seq;
    }

    private void RecordSpamAware(string name, IReadOnlyDictionary<string, string> snapshot, string reason)
    {
        var key = $"{name}|{reason}|{GetSnapshotValue(snapshot, "isLoggedIn")}|{GetSnapshotValue(snapshot, "CurrentLobbyMap")}|{GetSnapshotValue(snapshot, "adapterState")}";
        if (_suppressedSpamKeys.TryGetValue(key, out var count))
        {
            _suppressedSpamKeys[key] = count + 1;
            LastEvent = name;
            return;
        }

        _suppressedSpamKeys[key] = 1;
        Record(name, snapshot, reason);
    }

    private void DropOldestEvent(string incomingName)
    {
        if (!IsImportantEvent(incomingName))
        {
            _events.Dequeue();
            return;
        }

        var retained = _events.ToList();
        var removeIndex = retained.FindIndex(static item => !IsImportantEvent(item.Name));
        if (removeIndex < 0)
        {
            _events.Dequeue();
            return;
        }

        retained.RemoveAt(removeIndex);
        _events.Clear();
        foreach (var item in retained)
        {
            _events.Enqueue(item);
        }
    }

    private static string GetSnapshotValue(IReadOnlyDictionary<string, string> snapshot, string key)
    {
        return snapshot.TryGetValue(key, out var value) ? value : "none";
    }

    public void RecordSceneReadyRaw(IReadOnlyDictionary<string, string> snapshot, string reason)
    {
        SceneReadyRawCallCount++;
        RecordSpamAware("sceneReady raw signal received", snapshot, reason);
    }

    public void RecordSceneReadyAccepted(IReadOnlyDictionary<string, string> snapshot, string reason, int sceneGeneration, bool isLoggedIn)
    {
        SceneReadyAcceptedCount++;
        SceneReadyLastAcceptedReason = string.IsNullOrWhiteSpace(reason) ? "accepted" : reason;
        SceneReadyLastAcceptedSceneGeneration = sceneGeneration;
        if (sceneGeneration > 0)
        {
            _acceptedGenerations.Add(sceneGeneration);
        }

        if (isLoggedIn)
        {
            PostLoginSceneReadyAccepted = true;
            LastSceneReadyAcceptedAfterLoginEventSeq = Record("post-login sceneReady accepted detected", snapshot, reason);
        }

        Record("sceneReady accepted", snapshot, reason);
        if (SceneReadyAcceptedCount > 1)
        {
            Record("sceneReady accepted more than once", snapshot, $"acceptedCount={SceneReadyAcceptedCount}");
        }
    }

    public void RecordSceneReadyRejected(IReadOnlyDictionary<string, string> snapshot, string reason)
    {
        SceneReadyRejectedCount++;
        SceneReadyLastRejectedReason = string.IsNullOrWhiteSpace(reason) ? "rejected" : reason;
        RecordSpamAware("sceneReady rejected", snapshot, reason);
    }

    public void RecordPhase2GApply(IReadOnlyDictionary<string, string> snapshot, bool isLoggedIn, bool isCharaSelectOrTitleBackground, string allowedReason)
    {
        LastPhase2GApplyContext = isLoggedIn
            ? "logged-in"
            : isCharaSelectOrTitleBackground ? "chara-select-or-title-background" : "outside-chara-select";
        LastPhase2GAllowedReason = string.IsNullOrWhiteSpace(allowedReason) ? "allowed" : allowedReason;
        if (isLoggedIn)
        {
            Phase2GAppliedAfterLogin = true;
            LastPhase2GAppliedAfterLoginEventSeq = Record("post-login Phase 2G apply detected", snapshot, allowedReason);
        }

        if (!isCharaSelectOrTitleBackground)
        {
            Phase2GAppliedAfterLeavingCharaSelect = true;
        }
    }

    public void RecordPhase2GSkipped(string reason)
    {
        LastPhase2GSkippedReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    public void MarkPostLoginStaleState(
        IReadOnlyDictionary<string, string> snapshot,
        bool staleAdapter,
        bool staleCurrentLobbyMap,
        bool staleSceneOverride)
    {
        if (staleAdapter && !StaleAdapterStateAfterLogin)
        {
            StaleAdapterStateAfterLogin = true;
            Record("stale adapter state detected after login", snapshot);
        }

        if (staleCurrentLobbyMap && !StaleCurrentLobbyMapAfterLogin)
        {
            StaleCurrentLobbyMapAfterLogin = true;
            Record("stale CurrentLobbyMap detected after login", snapshot);
        }

        if (staleSceneOverride && !StaleSceneOverrideStateAfterLogin)
        {
            StaleSceneOverrideStateAfterLogin = true;
            Record("stale scene override state detected after login", snapshot);
        }
    }

    public TitleBackgroundTransitionDelta ComputeDeltaSinceLastDiagnostic(TitleBackgroundTransitionCounters counters)
    {
        var firstReport = !_diagnosticBaselineEstablished;
        var delta = new TitleBackgroundTransitionDelta(
            BaselineEstablished: !firstReport,
            FirstReport: firstReport,
            firstReport ? 0 : counters.Phase2ELookAtYCallCount - _lastDiagnosticCounters.Phase2ELookAtYCallCount,
            firstReport ? 0 : counters.Phase2FSetMidCallCount - _lastDiagnosticCounters.Phase2FSetMidCallCount,
            firstReport ? 0 : counters.Phase2FLowHighCallCount - _lastDiagnosticCounters.Phase2FLowHighCallCount,
            firstReport ? 0 : counters.Phase2GSetMidAttemptCount - _lastDiagnosticCounters.Phase2GSetMidAttemptCount,
            firstReport ? 0 : counters.Phase2GLowHighAttemptCount - _lastDiagnosticCounters.Phase2GLowHighAttemptCount,
            firstReport ? 0 : counters.SceneReadyAcceptedCount - _lastDiagnosticCounters.SceneReadyAcceptedCount,
            firstReport ? 0 : counters.SceneReadyRawCallCount - _lastDiagnosticCounters.SceneReadyRawCallCount);
        _lastDiagnosticCounters = counters;
        _diagnosticBaselineEstablished = true;
        return delta;
    }

    public static IReadOnlyList<string> BuildSummaryLines(TitleBackgroundTransitionSummaryInput input)
    {
        var verdicts = BuildVerdicts(input);
        return
        [
            $"transition.eventCount={input.EventCount}",
            $"transition.firstEvent={input.FirstEvent}",
            $"transition.lastEvent={input.LastEvent}",
            $"transition.sceneReady.rawCallCount={input.SceneReadyRawCallCount}",
            $"transition.sceneReady.acceptedCount={input.SceneReadyAcceptedCount}",
            $"transition.sceneReady.rejectedCount={input.SceneReadyRejectedCount}",
            $"transition.sceneReady.acceptedCount.suspicious={input.SceneReadyAcceptedSuspicious}",
            $"transition.sceneReady.lastAcceptedReason={input.SceneReadyLastAcceptedReason}",
            $"transition.sceneReady.lastRejectedReason={input.SceneReadyLastRejectedReason}",
            $"transition.sceneReady.lastAcceptedSceneGeneration={input.SceneReadyLastAcceptedSceneGeneration}",
            $"transition.sceneReady.acceptedGenerations={FormatNone(input.SceneReadyAcceptedGenerations)}",
            $"transition.currentContext.isLoggedIn={input.Context.IsLoggedIn}",
            $"transition.currentContext.isCharaSelectOrTitleBackground={input.Context.IsCharaSelectOrTitleBackground}",
            $"transition.currentContext.currentTerritoryId={input.Context.CurrentTerritoryId}",
            $"transition.currentContext.currentTerritoryType={input.Context.CurrentTerritoryType}",
            $"transition.currentContext.currentLobbyMap={input.Context.CurrentLobbyMap}",
            $"transition.sceneOverride.active={input.SceneOverride.Active}",
            $"transition.sceneOverride.historicalLastOverrideApplied={input.SceneOverride.HistoricalLastOverrideApplied}",
            $"transition.sceneOverride.activeLobbyType={input.SceneOverride.ActiveLobbyType}",
            $"transition.sceneOverride.activeOverridePath={input.SceneOverride.ActiveOverridePath}",
            $"transition.sceneOverride.lastHistoricalOverridePath={input.SceneOverride.LastHistoricalOverridePath}",
            $"transition.sceneOverride.currentLobbyMap={input.SceneOverride.CurrentLobbyMap}",
            $"transition.sceneOverride.lastCurrentLobbyMapResetReason={input.SceneOverride.LastCurrentLobbyMapResetReason}",
            $"transition.sceneOverride.cleanupReason={input.SceneOverride.CleanupReason}",
            $"transition.sceneOverride.activeAfterLoginDetected={input.SceneOverride.ActiveAfterLoginDetected}",
            $"transition.adapter.state={input.Adapter.State}",
            $"transition.adapter.lastEvent={input.Adapter.LastEvent}",
            $"transition.adapter.sceneGeneration={input.Adapter.SceneGeneration}",
            $"transition.adapter.staleAfterLoginDetected={input.Adapter.StaleAfterLoginDetected}",
            $"transition.phase2G.lastApplyContext={input.Phase2G.LastApplyContext}",
            $"transition.phase2G.appliedAfterLogin={input.Phase2G.AppliedAfterLogin}",
            $"transition.phase2G.lastAppliedAfterLoginEventSeq={input.Phase2G.LastAppliedAfterLoginEventSeq}",
            $"transition.phase2G.appliedAfterLeavingCharaSelect={input.Phase2G.AppliedAfterLeavingCharaSelect}",
            $"transition.phase2G.lastAllowedReason={input.Phase2G.LastAllowedReason}",
            $"transition.phase2G.lastSkippedReason={input.Phase2G.LastSkippedReason}",
            $"transition.phase2G.setMid.deltaSinceLastDiag={input.Delta.Phase2GSetMidAttemptCount}",
            $"transition.phase2G.lowHigh.deltaSinceLastDiag={input.Delta.Phase2GLowHighAttemptCount}",
            $"transition.camera.currentCaptureStatus={input.Camera.CurrentCaptureStatus}",
            $"transition.camera.currentDirH={input.Camera.CurrentDirH}",
            $"transition.camera.currentDirV={input.Camera.CurrentDirV}",
            $"transition.camera.currentDistance={input.Camera.CurrentDistance}",
            $"transition.camera.currentPosition={input.Camera.CurrentPosition}",
            $"transition.camera.currentLookAt={input.Camera.CurrentLookAt}",
            $"transition.verdict.postLoginPhase2GStillApplying={verdicts.PostLoginPhase2GStillApplying}",
            $"transition.verdict.postLoginSceneReadyAccepted={verdicts.PostLoginSceneReadyAccepted}",
            $"transition.verdict.staleCharaSelectStateAfterLogin={verdicts.StaleCharaSelectStateAfterLogin}",
            $"transition.verdict.sceneReadyAcceptedMultipleTimes={verdicts.SceneReadyAcceptedMultipleTimes}",
            $"transition.verdict.loginTransitionSafety={verdicts.LoginTransitionSafety}",
            $"diagDelta.baselineEstablished={input.Delta.BaselineEstablished}",
            $"diagDelta.firstReport={input.Delta.FirstReport}",
            $"diagDelta.phase2E.calculateLobbyCameraLookAtY.callCount={input.Delta.Phase2ELookAtYCallCount}",
            $"diagDelta.phase2F.setCameraCurveMidPoint.callCount={input.Delta.Phase2FSetMidCallCount}",
            $"diagDelta.phase2F.calculateCameraCurveLowAndHighPoint.callCount={input.Delta.Phase2FLowHighCallCount}",
            $"diagDelta.phase2G.setMid.attemptCount={input.Delta.Phase2GSetMidAttemptCount}",
            $"diagDelta.phase2G.lowHigh.attemptCount={input.Delta.Phase2GLowHighAttemptCount}",
            $"diagDelta.sceneReady.acceptedCount={input.Delta.SceneReadyAcceptedCount}",
            $"diagDelta.sceneReady.rawCallCount={input.Delta.SceneReadyRawCallCount}",
        ];
    }

    public static TitleBackgroundTransitionVerdicts BuildVerdicts(TitleBackgroundTransitionSummaryInput input)
    {
        var postLoginPhase2GStillApplying = input.Context.IsLoggedIn
            && input.Phase2G.AppliedAfterLogin
            && input.Phase2G.LastAppliedAfterLoginEventSeq > 0;
        var postLoginSceneReadyAccepted = input.Context.IsLoggedIn
            && input.PostLoginSceneReadyAcceptedDetected
            && input.SceneReadyLastAcceptedAfterLoginEventSeq > 0;
        var staleCharaSelectStateAfterLogin = input.Context.IsLoggedIn
            && (input.Adapter.StaleAfterLoginDetected
                || input.SceneOverride.ActiveAfterLoginDetected
                || input.Context.IsCharaSelectOrTitleBackground);
        var sceneReadyAcceptedMultipleTimes = input.SceneReadyAcceptedCount > 1;
        var safe = input.Context.IsLoggedIn
            && !postLoginPhase2GStillApplying
            && !postLoginSceneReadyAccepted
            && !staleCharaSelectStateAfterLogin
            && !sceneReadyAcceptedMultipleTimes;

        return new TitleBackgroundTransitionVerdicts(
            postLoginPhase2GStillApplying,
            postLoginSceneReadyAccepted,
            staleCharaSelectStateAfterLogin,
            sceneReadyAcceptedMultipleTimes,
            safe ? "safe" : "unsafe");
    }

    public static bool IsFinalYawPitchDistanceSafe(string finalYawPitchDistanceMatchesPreset, string loginTransitionSafety)
    {
        return finalYawPitchDistanceMatchesPreset == "observed" || loginTransitionSafety == "safe";
    }

    public IReadOnlyList<string> BuildTraceLines(string prefix = "transition.event")
    {
        return _events
            .Select((item, index) => $"{prefix}[{index}].seq={item.Sequence}; name={item.Name}; timestamp={item.Timestamp:O}; thread={item.ThreadId}; reason={item.Reason}; error={item.Error}; snapshot={item.Snapshot}")
            .ToArray();
    }

    public string BuildDetailedDump(IReadOnlyList<string> summaryLines, IReadOnlyList<string> existingDetailedLines)
    {
        var builder = new StringBuilder();
        foreach (var line in summaryLines)
        {
            builder.AppendLine(line);
        }

        foreach (var line in existingDetailedLines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string FormatNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }
}

internal readonly record struct TitleBackgroundTransitionDiagnosticEvent(
    long Sequence,
    string Name,
    DateTimeOffset Timestamp,
    int ThreadId,
    string Reason,
    string Error,
    string Snapshot);

internal readonly record struct TitleBackgroundTransitionVerdicts(
    bool PostLoginPhase2GStillApplying,
    bool PostLoginSceneReadyAccepted,
    bool StaleCharaSelectStateAfterLogin,
    bool SceneReadyAcceptedMultipleTimes,
    string LoginTransitionSafety);

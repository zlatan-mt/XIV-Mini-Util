// Path: tools/CharaSelectLogicTests/TestHelpers.cs
// Description: Provides shared console runner and test-data helpers
// Reason: Avoids duplication across responsibility-specific test files
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Text;
using System.Text.Json;
using XivMiniUtil;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Market;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

internal readonly record struct LogicTestCase(int Order, string Name, Func<bool> Assertion);

internal sealed class TestContext
{
    private readonly List<string> failures = [];

    public void Run(LogicTestCase test)
    {
        try
        {
            if (!test.Assertion())
            {
                failures.Add($"FAILED: {test.Name}");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"ERROR: {test.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public int Complete()
    {
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine(failure);
            }

            return 1;
        }

        Console.WriteLine("CharaSelect logic tests passed.");
        return 0;
    }
}

internal static partial class TestRunner
{
    private static TitleBackgroundCharacterSelectOverrideCandidate TestBrightCandidate()
{
    return new TitleBackgroundCharacterSelectOverrideCandidate(
        "custom:test-bright",
        "Test bright custom target",
        "ex5/test/bright/level/bright",
        999,
        10,
        TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
        TitleBackgroundCharacterSelectExpectedBrightness.Bright,
        true,
        false,
        false,
        "registry",
        "test-only bright candidate",
        "test-only",
        "try-bright-custom-target");
}

    private static TitleBackgroundCharacterSelectManualCandidateSlot ManualSlot(
    bool enabled = true,
    string path = "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
    uint territoryId = 1234,
    uint layerFilterKey = 7,
    TitleBackgroundCharacterSelectExpectedBrightness brightness = TitleBackgroundCharacterSelectExpectedBrightness.Unknown)
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildManualSlot(
        1,
        enabled,
        "Manual test candidate",
        path,
        territoryId,
        layerFilterKey,
        brightness);
}

    private static string CandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
{
    var verified = candidate.VerifiedInGame ? "Verified" : "Unverified";
    var source = candidate.Source == "manual" ? "Manual / " : string.Empty;
    var compatibility = candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        ? "Background-only"
        : candidate.ExpectedCompatibility.ToString();
    return $"{candidate.DisplayName} [{source}{verified} / {candidate.ExpectedBrightness} / {compatibility}]";
}

    private static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "projects", "XIV-Mini-Util", "XivMiniUtil.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

    private static string ExtractMethodBody(string source, string signature)
{
    var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
    if (signatureIndex < 0)
    {
        return string.Empty;
    }

    var bodyStart = source.IndexOf('{', signatureIndex);
    if (bodyStart < 0)
    {
        return string.Empty;
    }

    var depth = 0;
    for (var index = bodyStart; index < source.Length; index++)
    {
        if (source[index] == '{')
        {
            depth++;
        }
        else if (source[index] == '}')
        {
            depth--;
            if (depth == 0)
            {
                return source.Substring(bodyStart, index - bodyStart + 1);
            }
        }
    }

    return source[bodyStart..];
}

    private static TitleBackgroundDeliverySummary Delivery(
    TitleBackgroundCharacterPlacementSummary summary,
    TitleBackgroundCharacterSelectBackgroundMode backgroundMode = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
    TitleBackgroundCharacterSelectLightingMode lightingMode = TitleBackgroundCharacterSelectLightingMode.Default,
    string selectedPresetId = "",
    bool lastOverrideApplied = false,
    string transitionSafety = "safe",
    bool currentObjectTableValidForCharaSelect = true,
    string selectedOverrideCandidateId = "",
    string overrideTerritoryPath = "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
    uint overrideTerritoryId = 816,
    uint overrideLayerFilterKey = 51,
    IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot>? manualCandidateSlots = null,
    bool historicalLastOverrideApplied = false,
    string historicalLastOverridePath = "",
    bool sceneReadyAcceptedMultipleTimes = false,
    bool activeAfterLoginDetected = false,
    bool phase2GAppliedAfterLogin = false)
{
    return TitleBackgroundDeliveryDiagnostic.BuildSummary(
        backgroundMode,
        lightingMode,
        selectedPresetId,
        overrideTerritoryPath,
        overrideTerritoryId,
        overrideLayerFilterKey,
        sceneOverrideEnabled: true,
        lastOverrideApplied,
        summary.Resolution,
        summary.TransformValidity,
        summary.ActorVisible,
        summary.ZeroPositionCandidateCount,
        summary.NonZeroPositionCandidateCount,
        summary.DrawObjectNonNullCount,
        summary.ModelLikeNonNullCount,
        summary.BestCandidate,
        [
            new TitleBackgroundCharacterPlacementSourceDiscovery(
                "ObjectTable",
                true,
                summary.ZeroPositionCandidateCount + summary.NonZeroPositionCandidateCount,
                summary.ZeroPositionCandidateCount + summary.NonZeroPositionCandidateCount,
                string.Empty,
                summary.NonZeroPositionCandidateCount,
                summary.DrawObjectNonNullCount,
                summary.ModelLikeNonNullCount),
        ],
        transitionSafety,
        currentObjectTableValidForCharaSelect,
        currentObjectTableValidForCharaSelect ? "none" : "post-login-world-object-table-not-valid-for-chara-select",
        selectedOverrideCandidateId,
        manualCandidateSlots,
        historicalLastOverrideApplied,
        historicalLastOverridePath,
        sceneReadyAcceptedMultipleTimes,
        activeAfterLoginDetected,
        phase2GAppliedAfterLogin);
}

    private static TitleBackgroundDeliverySummary DeliveryFromRaw(
    string phase2MResolution,
    string phase2MTransformValidity,
    string phase2MActorVisible,
    int zeroPositionCandidateCount,
    int nonZeroPositionCandidateCount,
    int drawObjectNonNullCount,
    int modelLikeNonNullCount,
    IReadOnlyList<TitleBackgroundCharacterPlacementSourceDiscovery> sourceDiscovery,
    bool lastOverrideApplied = false,
    bool currentObjectTableValidForCharaSelect = true,
    TitleBackgroundCharacterSourceSummary nativeCharacterSource = default,
    bool characterCompositedApplied = false)
{
    return TitleBackgroundDeliveryDiagnostic.BuildSummary(
        TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
        TitleBackgroundCharacterSelectLightingMode.Default,
        string.Empty,
        "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        816,
        51,
        sceneOverrideEnabled: true,
        lastOverrideApplied,
        phase2MResolution,
        phase2MTransformValidity,
        phase2MActorVisible,
        zeroPositionCandidateCount,
        nonZeroPositionCandidateCount,
        drawObjectNonNullCount,
        modelLikeNonNullCount,
        "ObjectTable:1",
        sourceDiscovery,
        "safe",
        currentObjectTableValidForCharaSelect,
        currentObjectTableValidForCharaSelect ? "none" : "post-login-world-object-table-not-valid-for-chara-select",
        phase2GAppliedAfterLogin: false,
        nativeCharacterSource: nativeCharacterSource,
        characterCompositedApplied: characterCompositedApplied);
}

    private static TitleBackgroundCharacterSourceSnapshot NativeCharacterSnapshot(
    int frame,
    nint address,
    Vector3 position,
    bool drawObjectNonNull = true,
    string captureContext = "pre-login")
{
    return new TitleBackgroundCharacterSourceSnapshot(
        frame,
        captureContext,
        "read",
        address,
        new nint(0x100),
        0x1234,
        200,
        200,
        0xE0000001,
        "Pc",
        position,
        0.5f,
        1f,
        0.5f,
        drawObjectNonNull ? new nint(0x7000) : nint.Zero,
        "race=1;tribe=1;sex=1",
        "none");
}

    private static TitleBackgroundCharacterPlacementFrame CharacterPlacementFrame(
    int frame,
    TitleBackgroundCharacterPlacementActorMatchKind matchKind,
    bool visibleHint = false,
    bool withCameraDeltas = false,
    string groundStatus = "unavailable",
    int candidateCount = 1)
{
    var actor = matchKind == TitleBackgroundCharacterPlacementActorMatchKind.Single
        ? new TitleBackgroundCharacterPlacementActorCandidate(
            SourceIndex: 1,
            Source: "test",
            ObjectIndex: 1,
            ObjectKind: "Pc",
            Name: "candidate",
            GameObjectId: 0x100,
            EntityId: 0x200,
            Address: new nint(0x300),
            Position: new Vector3(1f, 2f, 3f),
            Rotation: 0.5f,
            Scale: 1f,
            HitboxRadius: 0.5f,
            CurrentHp: 100,
            MaxHp: 100,
            Targetable: visibleHint,
            VisibilityHint: visibleHint ? "targetable" : "not-targetable",
            SelectableHint: visibleHint ? "selectable-hint" : "unknown",
            Flags: "none",
            Customize: "none",
            Model: "model",
            DrawObject: "0x400",
            DrawObjectNonNull: true,
            ModelLikePointer: "model",
            ModelLikeNonNull: true,
            SafeReadError: "none",
            Named: true,
            PlayerLike: true,
            BattleCharacterLike: true,
            EventNpcLike: false,
            CompanionLike: false,
            VisibleHint: visibleHint,
            DistanceFromConfiguredCharacter: 0f,
            DistanceFromActiveLookAt: withCameraDeltas ? 1f : null,
            DistanceFromActiveCameraPosition: withCameraDeltas ? 3f : null,
            YDeltaFromConfiguredCharacter: 0f,
            NearConfiguredCharacter: true,
            NearCameraLookAt: withCameraDeltas,
            NearCameraPosition: withCameraDeltas,
            CategoryReason: "PlayerCharacter,BattleChara,Named",
            Score: 100,
            ScoreReason: "test")
        : (TitleBackgroundCharacterPlacementActorCandidate?)null;
    var objectCandidates = actor.HasValue
        ? new[] { actor.Value }
        : Array.Empty<TitleBackgroundCharacterPlacementActorCandidate>();

    return new TitleBackgroundCharacterPlacementFrame(
        Frame: frame,
        Reason: frame == 0 ? "scene-ready-accepted" : "timeline",
        ActiveCameraCaptured: withCameraDeltas,
        ActiveCameraPosition: withCameraDeltas ? Vector3.Zero : null,
        ActiveCameraLookAt: withCameraDeltas ? new Vector3(1f, 2f, 2f) : null,
        ActiveCameraYaw: withCameraDeltas ? 0f : null,
        ActiveCameraPitch: withCameraDeltas ? 0f : null,
        ActiveCameraDistance: withCameraDeltas ? 3f : null,
        LobbyCameraCaptured: withCameraDeltas,
        LobbyCameraLookAt: withCameraDeltas ? new Vector3(1f, 2f, 2f) : null,
        LobbyDirH: withCameraDeltas ? 0f : null,
        LobbyDirV: withCameraDeltas ? 0f : null,
        LobbyDistance: withCameraDeltas ? 3f : null,
        LobbyInterpDistance: withCameraDeltas ? 3f : null,
        ActorMatchKind: matchKind,
        Actor: actor,
        ObjectCandidates: objectCandidates,
        SourceDiscovery:
        [
            new TitleBackgroundCharacterPlacementSourceDiscovery(
                "ObjectTable",
                true,
                candidateCount,
                objectCandidates.Length,
                string.Empty,
                objectCandidates.Count(candidate => candidate.Position != Vector3.Zero),
                objectCandidates.Count(candidate => candidate.DrawObjectNonNull),
                objectCandidates.Count(candidate => candidate.ModelLikeNonNull)),
            new TitleBackgroundCharacterPlacementSourceDiscovery("CharacterManagerObjects", true, candidateCount, 0, string.Empty),
        ],
        CandidateCount: candidateCount,
        ActorStatus: matchKind switch
        {
            TitleBackgroundCharacterPlacementActorMatchKind.Single => "observed",
            TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous => "ambiguous",
            _ => "not-observed",
        },
        ObjectTableStats: new TitleBackgroundCharacterPlacementObjectTableStats(
            TotalScanned: candidateCount,
            NamedCount: actor.HasValue ? 1 : 0,
            PlayerLikeCount: actor.HasValue ? 1 : 0,
            BattleCharaCount: actor.HasValue ? 1 : 0,
            EventNpcCount: 0,
            CompanionLikeCount: 0,
            NearCameraCount: withCameraDeltas && actor.HasValue ? 1 : 0,
            NearConfiguredCharacterCount: actor.HasValue ? 1 : 0),
        ActorCandidateStatus: matchKind switch
        {
            TitleBackgroundCharacterPlacementActorMatchKind.Single => "single",
            TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous => "ambiguous",
            _ => "none",
        },
        ActorCandidateReason: matchKind switch
        {
            TitleBackgroundCharacterPlacementActorMatchKind.Single => "single-candidate",
            TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous => "multiple-candidates:2",
            _ => "objectTable-unavailable-or-not-exposed",
        },
        ActorSource: matchKind == TitleBackgroundCharacterPlacementActorMatchKind.Single ? "test" : "objectTable-unavailable-or-not-exposed",
        NextNativeSourceToInspect: matchKind == TitleBackgroundCharacterPlacementActorMatchKind.None ? "native character-select actor manager" : "none",
        GroundHeightStatus: groundStatus,
        GroundY: null,
        ActorToCameraDistance: withCameraDeltas ? 3f : null,
        ActorToLookAtDelta: withCameraDeltas ? new Vector3(0f, 0f, 1f) : null,
        ConfiguredCharacterPosition: new Vector3(1f, 2f, 3f),
        ConfiguredCharacterRotation: 0.5f,
        CurveLow: 1f,
        CurveMid: 2f,
        CurveHigh: 3f,
        ActorYMinusPresetCharacterY: actor.HasValue ? 0f : null,
        ActorYMinusFocusY: actor.HasValue ? 0f : null,
        ActorYMinusNativeLookAtY: actor.HasValue ? 0f : null);
}

    private static TitleBackgroundCharacterPlacementActorCandidate CharacterPlacementCandidate(
    int index,
    Vector3 position,
    bool named,
    bool drawObject,
    bool visible)
{
    var zero = position == Vector3.Zero;
    return new TitleBackgroundCharacterPlacementActorCandidate(
        SourceIndex: index,
        Source: "ObjectTable",
        ObjectIndex: 200 + index,
        ObjectKind: "Pc",
        Name: named ? $"candidate-{index}" : string.Empty,
        GameObjectId: (ulong)(0x100 + index),
        EntityId: visible ? (uint)(0x200 + index) : 0xE0000000,
        Address: new nint(0x300 + index),
        Position: position,
        Rotation: 0f,
        Scale: 1f,
        HitboxRadius: 0.5f,
        CurrentHp: visible ? 100u : null,
        MaxHp: visible ? 100u : null,
        Targetable: visible,
        VisibilityHint: visible ? "targetable" : "not-targetable",
        SelectableHint: visible ? "selectable-hint" : "unknown",
        Flags: "none",
        Customize: named ? "customize" : "none",
        Model: drawObject ? "model" : "none",
        DrawObject: drawObject ? "0x400" : "none",
        DrawObjectNonNull: drawObject,
        ModelLikePointer: drawObject ? "model" : "none",
        ModelLikeNonNull: drawObject,
        SafeReadError: "none",
        Named: named,
        PlayerLike: true,
        BattleCharacterLike: true,
        EventNpcLike: false,
        CompanionLike: false,
        VisibleHint: visible,
        DistanceFromConfiguredCharacter: zero ? 551f : 1f,
        DistanceFromActiveLookAt: zero ? 1f : 2f,
        DistanceFromActiveCameraPosition: zero ? 1f : 3f,
        YDeltaFromConfiguredCharacter: position.Y - 2f,
        NearConfiguredCharacter: !zero,
        NearCameraLookAt: true,
        NearCameraPosition: true,
        CategoryReason: "PlayerCharacter,BattleChara",
        Score: zero ? -20 : 100,
        ScoreReason: zero ? "all-zero-transform-penalty:-40" : "non-zero-world-position:+30");
}

    private static TitleBackgroundCharacterPlacementFrame CharacterPlacementFrameFromCandidates(
    IReadOnlyList<TitleBackgroundCharacterPlacementActorCandidate> candidates,
    TitleBackgroundCharacterPlacementActorMatchKind matchKind = TitleBackgroundCharacterPlacementActorMatchKind.Ambiguous)
{
    var actor = candidates.Count == 1
        ? candidates[0]
        : candidates.FirstOrDefault();
    var hasActor = candidates.Count > 0;
    return new TitleBackgroundCharacterPlacementFrame(
        Frame: 0,
        Reason: "scene-ready-accepted",
        ActiveCameraCaptured: true,
        ActiveCameraPosition: Vector3.Zero,
        ActiveCameraLookAt: new Vector3(0f, 0f, 0f),
        ActiveCameraYaw: 0f,
        ActiveCameraPitch: 0f,
        ActiveCameraDistance: 3f,
        LobbyCameraCaptured: true,
        LobbyCameraLookAt: Vector3.Zero,
        LobbyDirH: 0f,
        LobbyDirV: 0f,
        LobbyDistance: 3f,
        LobbyInterpDistance: 3f,
        ActorMatchKind: hasActor ? matchKind : TitleBackgroundCharacterPlacementActorMatchKind.None,
        Actor: hasActor ? actor : null,
        ObjectCandidates: candidates,
        SourceDiscovery:
        [
            new TitleBackgroundCharacterPlacementSourceDiscovery(
                "ObjectTable",
                true,
                candidates.Count,
                candidates.Count,
                string.Empty,
                candidates.Count(candidate => candidate.Position != Vector3.Zero),
                candidates.Count(candidate => candidate.DrawObjectNonNull),
                candidates.Count(candidate => candidate.ModelLikeNonNull)),
            new TitleBackgroundCharacterPlacementSourceDiscovery("CharacterManagerObjects", false, 0, 0, "not-exposed"),
        ],
        CandidateCount: candidates.Count,
        ActorStatus: hasActor ? matchKind == TitleBackgroundCharacterPlacementActorMatchKind.Single ? "observed" : "ambiguous" : "not-observed",
        ObjectTableStats: new TitleBackgroundCharacterPlacementObjectTableStats(
            TotalScanned: candidates.Count,
            NamedCount: candidates.Count(candidate => candidate.Named),
            PlayerLikeCount: candidates.Count(candidate => candidate.PlayerLike),
            BattleCharaCount: candidates.Count(candidate => candidate.BattleCharacterLike),
            EventNpcCount: 0,
            CompanionLikeCount: 0,
            NearCameraCount: candidates.Count(candidate => candidate.NearCameraLookAt || candidate.NearCameraPosition),
            NearConfiguredCharacterCount: candidates.Count(candidate => candidate.NearConfiguredCharacter)),
        ActorCandidateStatus: hasActor ? matchKind == TitleBackgroundCharacterPlacementActorMatchKind.Single ? "single" : "ambiguous" : "none",
        ActorCandidateReason: hasActor ? $"multiple-candidates:{candidates.Count}" : "objectTable-unavailable-or-not-exposed",
        ActorSource: hasActor ? "ObjectTable" : "objectTable-unavailable-or-not-exposed",
        NextNativeSourceToInspect: hasActor ? "CharacterManager" : "native character-select actor manager",
        GroundHeightStatus: "unavailable",
        GroundY: null,
        ActorToCameraDistance: hasActor ? Vector3.Distance(actor.Position, Vector3.Zero) : null,
        ActorToLookAtDelta: hasActor ? actor.Position : null,
        ConfiguredCharacterPosition: new Vector3(1f, 2f, 3f),
        ConfiguredCharacterRotation: 0f,
        CurveLow: 1f,
        CurveMid: 2f,
        CurveHigh: 3f,
        ActorYMinusPresetCharacterY: hasActor ? actor.Position.Y - 2f : null,
        ActorYMinusFocusY: hasActor ? actor.Position.Y - 2f : null,
        ActorYMinusNativeLookAtY: hasActor ? actor.Position.Y : null);
}

    private static TitleBackgroundTransitionSummaryInput BuildTransitionSummaryInput(
    TitleBackgroundTransitionDiagnosticRecorder recorder,
    TitleBackgroundTransitionDelta delta,
    bool isLoggedIn,
    bool staleAdapter = false,
    bool phase2GAppliedAfterLogin = false,
    bool activeSceneOverride = false,
    bool historicalLastOverrideApplied = false,
    bool activeSceneOverrideAfterLogin = false,
    bool sceneReadyAcceptedAfterLogin = false,
    string cleanupReason = "none")
{
    return new TitleBackgroundTransitionSummaryInput(
        new TitleBackgroundTransitionContext(
            isLoggedIn,
            IsCharaSelectOrTitleBackground: !isLoggedIn,
            CurrentTerritoryId: isLoggedIn ? 777u : 0u,
            CurrentTerritoryType: isLoggedIn ? "777" : "0",
            CurrentLobbyMap: isLoggedIn ? "None" : "CharaSelect"),
        new TitleBackgroundTransitionSceneOverrideState(
            Active: activeSceneOverride,
            HistoricalLastOverrideApplied: historicalLastOverrideApplied,
            ActiveLobbyType: activeSceneOverride ? "CharaSelect" : "None",
            ActiveOverridePath: activeSceneOverride ? "ex5/test/level/test" : "none",
            LastHistoricalOverridePath: historicalLastOverrideApplied ? "ex5/test/level/test" : "none",
            CurrentLobbyMap: isLoggedIn ? "None" : "CharaSelect",
            LastCurrentLobbyMapResetReason: cleanupReason,
            CleanupReason: cleanupReason,
            ActiveAfterLoginDetected: activeSceneOverrideAfterLogin),
        new TitleBackgroundTransitionAdapterState(
            State: staleAdapter ? "Active" : "Inactive",
            LastEvent: "not-run",
            SceneGeneration: staleAdapter ? 1 : 0,
            StaleAfterLoginDetected: staleAdapter),
        new TitleBackgroundTransitionPhase2GState(
            LastApplyContext: phase2GAppliedAfterLogin ? "logged-in" : recorder.LastPhase2GApplyContext,
            AppliedAfterLogin: phase2GAppliedAfterLogin || recorder.Phase2GAppliedAfterLogin,
            LastAppliedAfterLoginEventSeq: phase2GAppliedAfterLogin
                ? Math.Max(1, recorder.LastPhase2GAppliedAfterLoginEventSeq)
                : recorder.LastPhase2GAppliedAfterLoginEventSeq,
            AppliedAfterLeavingCharaSelect: recorder.Phase2GAppliedAfterLeavingCharaSelect,
            LastAllowedReason: recorder.LastPhase2GAllowedReason,
            LastSkippedReason: recorder.LastPhase2GSkippedReason),
        new TitleBackgroundTransitionCameraState(
            CurrentCaptureStatus: "not-run",
            CurrentDirH: "none",
            CurrentDirV: "none",
            CurrentDistance: "none",
            CurrentPosition: "none",
            CurrentLookAt: "none"),
        new TitleBackgroundTransitionCounters(0, 0, 0, 0, 0, recorder.SceneReadyAcceptedCount, recorder.SceneReadyRawCallCount),
        delta,
        recorder.FirstEvent,
        recorder.LastEvent,
        recorder.EventCount,
        recorder.SceneReadyRawCallCount,
        recorder.SceneReadyAcceptedCount,
        recorder.SceneReadyRejectedCount,
        recorder.SceneReadyAcceptedCount > 1,
        recorder.SceneReadyLastAcceptedReason,
        recorder.SceneReadyLastRejectedReason,
        recorder.SceneReadyLastAcceptedSceneGeneration,
        recorder.AcceptedGenerations,
        sceneReadyAcceptedAfterLogin || recorder.PostLoginSceneReadyAccepted,
        sceneReadyAcceptedAfterLogin
            ? Math.Max(1, recorder.LastSceneReadyAcceptedAfterLoginEventSeq)
            : recorder.LastSceneReadyAcceptedAfterLoginEventSeq);
}

    private static TitleBackgroundTransitionDelta TrustedDelta(
    int phase2ELookAtYCallCount,
    int phase2FSetMidCallCount,
    int phase2FLowHighCallCount,
    int phase2GSetMidAttemptCount,
    int phase2GLowHighAttemptCount,
    int sceneReadyAcceptedCount,
    int sceneReadyRawCallCount)
{
    return new TitleBackgroundTransitionDelta(
        BaselineEstablished: true,
        FirstReport: false,
        phase2ELookAtYCallCount,
        phase2FSetMidCallCount,
        phase2FLowHighCallCount,
        phase2GSetMidAttemptCount,
        phase2GLowHighAttemptCount,
        sceneReadyAcceptedCount,
        sceneReadyRawCallCount);
}

    private static TitleBackgroundQuickCheckInput QuickCheckInput(
    bool runScoped = true,
    string candidateId = "custom:n4f4",
    TitleBackgroundCharacterSelectExpectedBrightness expectedBrightness = TitleBackgroundCharacterSelectExpectedBrightness.Normal,
    bool isLoggedIn = true,
    bool startedLoggedIn = false,
    bool charaSelectObserved = true,
    TitleBackgroundQuickCheckRunState runState = TitleBackgroundQuickCheckRunState.LoggedInObserved,
    int sceneReadyAcceptedCount = 1,
    int overrideAppliedCount = 1,
    bool backgroundApplied = true,
    bool backgroundObserved = true,
    bool characterExpectedVisible = false,
    string characterObserved = "hidden",
    bool sceneOverrideActiveAfterLogin = false,
    bool activeAfterLoginDetected = false,
    bool phase2GAppliedAfterLogin = false,
    bool actorSourceAmbiguous = false,
    bool objectTableZeroTransformStubs = false,
    TitleBackgroundCharacterVisualStatus characterVisualStatus = TitleBackgroundCharacterVisualStatus.Unknown,
    TitleBackgroundCharaSelectCameraFramingMode cameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.Default,
    TitleBackgroundCharaSelectCameraFramingMode candidateRecommendedFraming = TitleBackgroundCharaSelectCameraFramingMode.Default,
    string candidateRecommendedAction = "",
    bool titleBackgroundOverrideEnabled = true,
    bool titleBackgroundCameraOverrideEnabled = true,
    bool legacySceneCompositionEnabled = false,
    bool integratedCompositionEnabled = true,
    bool shouldArmAdapter = true,
    string shouldArmAdapterReason = "",
    bool integratedCompositionRouteInvoked = false,
    string integratedCompositionRouteReason = "",
    bool cameraFramingApplied = false,
    bool sceneOverrideApplyObserved = false,
    TitleBackgroundCharacterCompositionBridgeSnapshot characterCompositionBridge = default,
    string cameraProfileId = "",
    string cameraProfileSource = "",
    string cameraYaw = "",
    string cameraPitch = "",
    string cameraDistance = "",
    string cameraFramesCharacter = "",
    string cameraFinalYawPitchDistanceMatchesProfile = "",
    bool cameraVisibleProfileResolved = false,
    bool cameraVisibleProfileApplied = false,
    string cameraVisibleProfileAppliedState = "",
    string cameraProfileApplyRoute = "",
    bool cameraCapturedProfileEnabled = false,
    bool bridgeCharacterCompositionApplied = false,
    bool bridgeCameraProfileApplied = false,
    bool characterCompositedApplied = false,
    bool passiveCameraObservationActive = false,
    bool characterPlacedViaCameraFocusFallback = false,
    bool characterGroundPlacementVerified = false)
{
    return new TitleBackgroundQuickCheckInput(
        RunScoped: runScoped,
        RunState: runState,
        StartedAt: DateTimeOffset.Now.AddMinutes(-2),
        CompletedAt: DateTimeOffset.Now,
        StartedLoggedIn: startedLoggedIn,
        CharaSelectObserved: charaSelectObserved,
        SceneGenerationStart: 1,
        SceneGenerationEnd: 2,
        SceneReadyAcceptedCount: sceneReadyAcceptedCount,
        OverrideAppliedCount: overrideAppliedCount,
        Phase2GApplyCount: 0,
        PluginOrHookError: false,
        PluginOrHookErrorReason: "none",
        IsLoggedIn: isLoggedIn,
        CurrentLobbyMap: "None",
        CurrentLobbyMapRemainedAfterLogin: false,
        BackgroundMode: TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
        LightingMode: TitleBackgroundCharacterSelectLightingMode.Default,
        CandidateId: candidateId,
        CandidateDisplayName: string.IsNullOrWhiteSpace(candidateId) ? string.Empty : "Custom n4f4 override target",
        CandidateVerifiedInGame: true,
        CandidateSource: "registry",
        ExpectedCompatibility: TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
        ExpectedBrightness: expectedBrightness,
        OverrideTerritoryPath: "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        OverrideTerritoryId: 816,
        OverrideLayerFilterKey: 51,
        CandidateFieldsValid: true,
        BackgroundApplied: backgroundApplied,
        BackgroundObserved: backgroundObserved,
        VisualConfirmationRequired: false,
        CharacterExpectedVisible: characterExpectedVisible,
        CharacterObserved: characterObserved,
        CharacterKnownLimitation: !characterExpectedVisible,
        SceneOverrideActiveAfterLogin: sceneOverrideActiveAfterLogin,
        ActiveAfterLoginDetected: activeAfterLoginDetected,
        StaleCharaSelectStateAfterLogin: false,
        Phase2GAppliedAfterLogin: phase2GAppliedAfterLogin,
        ForegroundPreserveUnavailable: true,
        ActorSourceAmbiguous: actorSourceAmbiguous,
        ObjectTableZeroTransformStubs: objectTableZeroTransformStubs,
        CharacterVisualStatus: characterVisualStatus,
        CameraFramingMode: cameraFramingMode,
        CandidateRecommendedFraming: candidateRecommendedFraming,
        CandidateRecommendedAction: candidateRecommendedAction,
        TitleBackgroundOverrideEnabledAtCheck: titleBackgroundOverrideEnabled,
        TitleBackgroundCameraOverrideEnabledAtCheck: titleBackgroundCameraOverrideEnabled,
        LegacySceneCompositionEnabledAtCheck: legacySceneCompositionEnabled,
        TitleBackgroundIntegratedCompositionEnabledAtCheck: integratedCompositionEnabled,
        ShouldArmAdapterAtCheck: shouldArmAdapter,
        ShouldArmAdapterReasonAtCheck: shouldArmAdapterReason,
        IntegratedCompositionRouteInvoked: integratedCompositionRouteInvoked,
        IntegratedCompositionRouteReason: integratedCompositionRouteReason,
        CameraFramingApplied: cameraFramingApplied,
        SceneOverrideApplyObserved: sceneOverrideApplyObserved,
        CharacterCompositionBridge: characterCompositionBridge,
        CameraProfileId: cameraProfileId,
        CameraProfileSource: cameraProfileSource,
        CameraYaw: cameraYaw,
        CameraPitch: cameraPitch,
        CameraDistance: cameraDistance,
        CameraLookAtOffset: "",
        CameraPositionOffset: "",
        CameraFramesCharacter: cameraFramesCharacter,
        CameraFinalYawPitchDistanceMatchesProfile: cameraFinalYawPitchDistanceMatchesProfile,
        CameraVisibleProfileResolved: cameraVisibleProfileResolved,
        CameraVisibleProfileApplied: cameraVisibleProfileApplied,
        CameraVisibleProfileAppliedState: cameraVisibleProfileAppliedState,
        CameraProfileApplyRoute: cameraProfileApplyRoute,
        CameraCapturedProfileEnabled: cameraCapturedProfileEnabled,
        CameraCapturedProfileDirH: "",
        CameraCapturedProfileDirV: "",
        CameraCapturedProfileDistance: "",
        CameraCapturedProfilePosition: "",
        CameraCapturedProfileLookAt: "",
        CameraCurrentDirH: "",
        CameraCurrentDirV: "",
        CameraCurrentDistance: "",
        CameraCurrentPosition: "",
        CameraCurrentLookAt: "",
        BridgeCharacterCompositionApplied: bridgeCharacterCompositionApplied,
        BridgeCameraProfileApplied: bridgeCameraProfileApplied,
        CharacterCompositedApplied: characterCompositedApplied,
        PassiveCameraObservationActive: passiveCameraObservationActive,
        CharacterPlacedViaCameraFocusFallback: characterPlacedViaCameraFocusFallback,
        CharacterGroundPlacementVerified: characterGroundPlacementVerified);
}

    private static string ReadTitleBackgroundNormalBody()
{
    var root = FindRepositoryRoot();
    var settingsText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    return ExtractMethodBody(settingsText, "private void DrawTitleBackgroundSettings()");
}

    private static string ReadServiceMethodBody(string fileName, string signature)
{
    var root = FindRepositoryRoot();
    var text = File.ReadAllText(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", fileName));
    return ExtractMethodBody(text, signature);
}

    private static TitleBackgroundWorldCoordinateSample MakeWorldSample(int idx, Vector3 world, Vector3 lookAt)
    => new(
        idx, "run", "t", "custom:n4f4", 816, 816,
        world, world, "world-experimental", "world", 100,
        world, world, lookAt, 5, true, "pre-login");

    private static int CountOccurrences(string text, string value)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

}

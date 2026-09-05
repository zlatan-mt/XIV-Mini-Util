// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.ColdStartDiagnostic.cs
// Description: FRU cold-start の first Character Select を production state のまま受動観測する。
// Reason: OneClick / preset再選択で owner state を正規化すると再現条件を消すため、startup snapshot と
//         first CharaSelect lifecycle を mutation なしで取得し、1回の実機runから原因を分類する。
using Dalamud.Plugin.Services;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

internal enum ColdStartArmStatus
{
    NotEvaluated,
    Armed,
    Skipped,
    Blocked,
}

internal enum ColdStartArmMode
{
    None,
    Startup,
    FirstSceneFallback,
}

internal readonly record struct TitleBackgroundColdStartOwnerSnapshot(
    string CandidateId,
    bool OverrideEnabled,
    bool V2Enabled,
    bool PlacementEnabled,
    string PlacementCandidateId,
    bool PositionCaptured,
    string ActualOwner,
    string ExpectedOwner);

internal readonly record struct TitleBackgroundColdStartDiagnosisInput(
    TitleBackgroundColdStartOwnerSnapshot Before,
    TitleBackgroundColdStartOwnerSnapshot After,
    bool CharaSelectObserved,
    int PlacementSceneGeneration,
    int ActiveSceneGeneration,
    bool ResolverEverValid,
    bool DrawReadyEverTrue,
    bool StaticAnchorEvaluated,
    bool StaticAnchorAuthorized,
    string StaticAnchorReason,
    bool CaptureCompleted,
    bool CaptureTimedOut,
    int PlacementWriteAttemptCount,
    bool PlacementWriteConfirmed,
    int UniqueResolvedActorCount,
    int ConfirmedWriteKeyCount,
    bool ActorEpochChangedAfterConfirmedWrite,
    bool LoginObserved,
    // Latest-or-terminal read-only actor visual-state evidence (H8 extension). Captured=false means
    // the visual state was never successfully read for this run; classification must not treat that
    // as "hidden". LatestVisualHidden comes from the documented GameObject.Visibility byte only
    // (true=hidden/raw==1, false=visible/raw==0, null=any other raw value / unknown) — RenderFlags is
    // deliberately not used here (ChatGPT exact-HEAD review 5118977128 MUST FIX).
    bool LatestVisualCaptured = false,
    bool? LatestVisualHidden = null,
    bool LatestVisualScaleFinitePositive = false,
    bool LatestVisualDrawOffsetFinite = false);

internal static class TitleBackgroundColdStartDiagnosticLogic
{
    public const int RecorderSchema = 2;
    public const int ResolverAttemptBudget = 120;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    public static TitleBackgroundColdStartOwnerSnapshot CaptureOwnerSnapshot(
        Configuration configuration,
        string? actualOwnerOverride = null)
    {
        var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            configuration.TitleBackgroundTerritoryPath,
            configuration.TitleBackgroundTerritoryTypeId,
            configuration.TitleBackgroundLayoutLayerFilterKey);
        var candidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(candidate.Id);
        var placementCandidateId = TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(
            configuration.TitleBackgroundCharaSelectPlacementCandidateId);

        var actualOwner = string.IsNullOrWhiteSpace(actualOwnerOverride)
            ? TitleBackgroundCharaSelectEngineOwnerLogic.Describe(
                TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
                    configuration.TitleBackgroundOverrideEnabled,
                    automaticPlacementProofArmed: false,
                    configuration.TitleBackgroundCharaSelectPlacementEnabled,
                    configuration.TitleBackgroundV2Enabled))
            : actualOwnerOverride;

        // Read-only expectation: what normal curated preset setup would choose for the current candidate.
        var expectedPlacement =
            TitleBackgroundQuickCheckUiPresenter.IsPersistentCharaSelectPlacementConfigured(configuration)
            || TitleBackgroundQuickCheckUiPresenter.IsApprovedStaticProductionPlacementEligible(candidate);
        var expectedOwner = TitleBackgroundCharaSelectEngineOwnerLogic.Describe(
            TitleBackgroundCharaSelectEngineOwnerLogic.Resolve(
                configuration.TitleBackgroundOverrideEnabled,
                automaticPlacementProofArmed: false,
                persistentPlacementEnabled: expectedPlacement,
                v2Enabled: !expectedPlacement));

        return new TitleBackgroundColdStartOwnerSnapshot(
            candidateId,
            configuration.TitleBackgroundOverrideEnabled,
            configuration.TitleBackgroundV2Enabled,
            configuration.TitleBackgroundCharaSelectPlacementEnabled,
            placementCandidateId,
            configuration.TitleBackgroundCharaSelectPlacementPositionCaptured,
            actualOwner,
            expectedOwner);
    }

    // The recorder is local-dev-plugin-only (Implementation plan — dev-only always-on flight recorder).
    // Production/release plugin behavior must stay unchanged, so this gate is checked before any other
    // startup/fallback arm evaluation and short-circuits both without touching runtime/config state.
    public static (bool Allowed, string Reason) EvaluateDevGate(bool isDevPlugin)
        => isDevPlugin ? (true, "ok") : (false, "release-build");

    public static (ColdStartArmStatus Status, string Reason) EvaluateStartupArm(
        bool isLoggedIn,
        in TitleBackgroundColdStartOwnerSnapshot before,
        bool automaticCheckRequested,
        bool placementProofArmed)
    {
        if (isLoggedIn)
        {
            return (ColdStartArmStatus.Skipped, "already-logged-in");
        }

        if (!before.OverrideEnabled)
        {
            return (ColdStartArmStatus.Skipped, "override-disabled");
        }

        if (!string.Equals(
                before.CandidateId,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                StringComparison.Ordinal))
        {
            return (ColdStartArmStatus.Skipped, "candidate-not-fru");
        }

        if (automaticCheckRequested || placementProofArmed)
        {
            return (ColdStartArmStatus.Blocked, "automatic-check-active");
        }

        return (ColdStartArmStatus.Armed, "ok");
    }

    public static bool ShouldArm(bool isLoggedIn, in TitleBackgroundColdStartOwnerSnapshot before)
        => EvaluateStartupArm(isLoggedIn, before, automaticCheckRequested: false, placementProofArmed: false).Status == ColdStartArmStatus.Armed;

    public static (bool Eligible, string Reason) EvaluateFallbackArm(
        bool isLoggedIn,
        GameLobbyType lobbyType,
        in TitleBackgroundColdStartOwnerSnapshot startup,
        in TitleBackgroundColdStartOwnerSnapshot current,
        bool automaticCheckActive,
        bool probeTransactionActive)
    {
        if (isLoggedIn)
        {
            return (false, "already-logged-in");
        }

        if (lobbyType != GameLobbyType.CharaSelect)
        {
            return (false, "not-chara-select");
        }

        if (!current.OverrideEnabled)
        {
            return (false, "override-disabled");
        }

        if (!string.Equals(
                current.CandidateId,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.FruCandidateId,
                StringComparison.Ordinal))
        {
            return (false, "candidate-not-fru");
        }

        if (automaticCheckActive || probeTransactionActive)
        {
            return (false, "unsafe-transaction-active");
        }

        if (!IsStartupStateConsistent(startup, current))
        {
            return (false, "startup-state-changed");
        }

        return (true, "ok");
    }

    public static bool IsStartupStateConsistent(
        in TitleBackgroundColdStartOwnerSnapshot startup,
        in TitleBackgroundColdStartOwnerSnapshot current)
    {
        if (string.IsNullOrEmpty(startup.CandidateId))
        {
            return false;
        }

        return string.Equals(startup.CandidateId, current.CandidateId, StringComparison.Ordinal)
            && startup.OverrideEnabled == current.OverrideEnabled
            && startup.V2Enabled == current.V2Enabled
            && startup.PlacementEnabled == current.PlacementEnabled
            && string.Equals(startup.PlacementCandidateId, current.PlacementCandidateId, StringComparison.Ordinal)
            && startup.PositionCaptured == current.PositionCaptured;
    }

    public static string Classify(in TitleBackgroundColdStartDiagnosisInput input)
    {
        // H1: PR #4 reviewで既知の保存済みowner migration gap。最優先で判定する。
        if (string.Equals(input.After.ExpectedOwner, "placement", StringComparison.Ordinal)
            && string.Equals(input.After.ActualOwner, "v2", StringComparison.Ordinal))
        {
            return "owner-migration-gap";
        }

        if (!input.CharaSelectObserved
            || input.PlacementSceneGeneration <= 0
            || (string.Equals(input.After.ActualOwner, "placement", StringComparison.Ordinal)
                && input.ActiveSceneGeneration <= 0))
        {
            return input.LoginObserved ? "scene-generation" : "insufficient-evidence";
        }

        if (!string.Equals(input.After.ActualOwner, "placement", StringComparison.Ordinal))
        {
            return "insufficient-evidence";
        }

        if (!input.ResolverEverValid)
        {
            return "actor-resolver";
        }

        if (input.StaticAnchorEvaluated && !input.StaticAnchorAuthorized)
        {
            return "static-anchor-authorization";
        }

        if (input.CaptureTimedOut || (!input.CaptureCompleted && input.PlacementWriteAttemptCount == 0))
        {
            return "capture";
        }

        if (input.PlacementWriteAttemptCount > 0 && !input.PlacementWriteConfirmed)
        {
            return "placement-write";
        }

        if (input.PlacementWriteConfirmed
            && (input.ActorEpochChangedAfterConfirmedWrite
                || (input.UniqueResolvedActorCount > input.ConfirmedWriteKeyCount && input.UniqueResolvedActorCount > 1)))
        {
            return "actor-recreation";
        }

        if (!input.DrawReadyEverTrue)
        {
            return "draw-readiness";
        }

        if (!input.PlacementWriteConfirmed)
        {
            return "insufficient-evidence";
        }

        // H8 extension: refine the generic visual candidate into a pointer-free technical label only
        // when the typed visual-state read actually succeeded for this run. Only the documented
        // GameObject.Visibility byte (LatestVisualHidden == true) is treated as "hidden" evidence — an
        // unconfirmed/unknown reading (null) must not be overclassified (review 5118977128 MUST FIX).
        if (input.LatestVisualCaptured && input.LatestVisualHidden == true)
        {
            return "actor-visibility-hidden";
        }

        if (input.LatestVisualCaptured
            && (!input.LatestVisualScaleFinitePositive || !input.LatestVisualDrawOffsetFinite))
        {
            return "actor-visual-transform-candidate";
        }

        return "post-placement-visual-candidate";
    }
}

internal sealed class TitleBackgroundColdStartDiagnosticRuntimeState
{
    public bool Active { get; private set; }
    public bool Subscribed { get; set; }
    public bool Completed { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public ColdStartArmMode ArmMode { get; private set; } = ColdStartArmMode.None;
    public ColdStartArmStatus StartupArmStatus { get; private set; } = ColdStartArmStatus.NotEvaluated;
    public string StartupArmReason { get; private set; } = "not-evaluated";
    public TitleBackgroundColdStartOwnerSnapshot StartupBefore { get; private set; }
    public TitleBackgroundColdStartOwnerSnapshot Before { get; private set; }
    public TitleBackgroundColdStartOwnerSnapshot After { get; private set; }

    public bool CharaSelectObserved { get; private set; }
    public string ScenePath { get; private set; } = "none";
    public uint SceneTerritoryId { get; private set; }
    public uint SceneLayerFilterKey { get; private set; }
    public int PlacementSceneGeneration { get; private set; }
    public int ActiveSceneGeneration { get; private set; }
    public string SceneOwner { get; private set; } = "none";
    public bool V2Active { get; private set; }
    public bool PlacementActive { get; private set; }
    public bool LegacyOwnershipInactive { get; private set; }

    public int ResolverAttemptCount { get; private set; }
    public bool ResolverEverValid { get; private set; }
    public string ResolverSource { get; private set; } = "None";
    public bool CurrentCharacterAvailable { get; private set; }
    public bool EntryAvailable { get; private set; }
    public bool SelectedContentAvailable { get; private set; }
    public bool MappingAvailable { get; private set; }
    public bool MappingHit { get; private set; }
    public bool ClientObjectIndexValid { get; private set; }
    public bool ObjectResolved { get; private set; }
    public bool IdentityConsistent { get; private set; }
    public bool DrawReady { get; private set; }

    public bool? DrawReadyAtFirstValid { get; private set; }
    public bool DrawReadyEverTrue { get; private set; }
    public int DrawReadyTransitionCount { get; private set; }
    private bool? _lastObservedDrawReady;

    // H8 extension: pointer-free typed actor visual-state checkpoints. "FirstValid" is captured once,
    // at the first resolver attempt whose visual read succeeded; "Latest" is overwritten every attempt
    // and therefore also represents the terminal/last-observed state used for classification.
    // Hidden/Visible come only from the documented GameObject.Visibility byte (null = unknown raw
    // value); RenderFlags is kept only as a raw neutral fact (ModelBitSet), never a visibility verdict
    // (ChatGPT exact-HEAD review 5118977128 MUST FIX).
    public bool FirstValidVisualCaptured { get; private set; }
    public bool? FirstValidVisualHidden { get; private set; }
    public bool LatestVisualCaptured { get; private set; }
    public byte LatestVisualVisibilityRaw { get; private set; }
    public bool? LatestVisualHidden { get; private set; }
    public bool LatestVisualReadyToDrawFlag { get; private set; }
    public bool LatestVisualRenderFlagsModelBitSet { get; private set; }
    public bool LatestVisualDrawObjectPresent { get; private set; }
    public bool LatestVisualScaleFinitePositive { get; private set; }
    public bool LatestVisualDrawOffsetFinite { get; private set; }
    public bool LatestVisualDrawOffsetNonZero { get; private set; }
    public int VisualVisibilityTransitionCount { get; private set; }
    private bool _visualHiddenObserved;
    private bool? _lastObservedVisualHidden;

    public int ActorIdentityEpoch { get; private set; }
    public int ActorRecreationCount { get; private set; }
    private CharaSelectActorIdentityKey _lastValidIdentityKey;

    public bool StaticAnchorEvaluated { get; private set; }
    public bool StaticAnchorAuthorized { get; private set; }
    public string StaticAnchorReason { get; private set; } = "not-run";
    public bool CaptureCompleted { get; private set; }
    public bool CaptureTimedOut { get; private set; }
    public int CaptureStableSamples { get; private set; }
    public int PlacementApplyCount { get; private set; }
    public int PlacementWriteAttemptCount { get; private set; }
    public bool PlacementWriteConfirmed { get; private set; }
    public bool PositionReadbackConfirmed { get; private set; }
    public bool RotationReadbackConfirmed { get; private set; }
    public string PlacementWriteStatus { get; private set; } = "not-attempted";
    public string PlacementLastReason { get; private set; } = "not-run";
    public int UniqueResolvedActorCount { get; private set; }
    public int ConfirmedWriteKeyCount { get; private set; }
    public bool ActorEpochChangedAfterConfirmedWrite { get; private set; }

    public int V2FramingAttemptCount { get; private set; }
    public int V2FramingAppliedCount { get; private set; }
    public string V2LastFramingStatus { get; private set; } = "not-run";
    public bool V2WindowClosed { get; private set; }

    public bool LoginObserved { get; private set; }
    public bool PostLoginSceneOverrideActive { get; private set; }
    public bool V2PostLoginWritesStopped { get; private set; }
    public bool PlacementLoginStopped { get; private set; }

    public string Diagnosis { get; private set; } = "not-completed";
    public string PendingClipboardText { get; set; } = string.Empty;

    public void RecordStartupSnapshot(in TitleBackgroundColdStartOwnerSnapshot startupBefore)
    {
        StartupBefore = startupBefore;
    }

    public void RecordStartupArmResult(ColdStartArmStatus status, string reason)
    {
        StartupArmStatus = status;
        StartupArmReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    public void Arm(
        in TitleBackgroundColdStartOwnerSnapshot before,
        in TitleBackgroundColdStartOwnerSnapshot after,
        ColdStartArmMode mode)
    {
        Before = before;
        After = after;
        ArmMode = mode;
        StartedAt = DateTimeOffset.UtcNow;
        Active = true;
        Completed = false;
        Diagnosis = "collecting";
        PendingClipboardText = string.Empty;
    }

    public void RecordScene(
        string scenePath,
        uint territoryId,
        uint layerFilterKey,
        int placementSceneGeneration,
        int activeSceneGeneration,
        string owner,
        bool v2Active,
        bool placementActive,
        bool legacyOwnershipInactive)
    {
        CharaSelectObserved = true;
        ScenePath = string.IsNullOrWhiteSpace(scenePath) ? "none" : scenePath;
        SceneTerritoryId = territoryId;
        SceneLayerFilterKey = layerFilterKey;
        PlacementSceneGeneration = Math.Max(PlacementSceneGeneration, placementSceneGeneration);
        ActiveSceneGeneration = Math.Max(ActiveSceneGeneration, activeSceneGeneration);
        SceneOwner = string.IsNullOrWhiteSpace(owner) ? "none" : owner;
        V2Active |= v2Active;
        PlacementActive |= placementActive;
        LegacyOwnershipInactive |= legacyOwnershipInactive;
    }

    public bool CanAttemptResolver
        => ResolverAttemptCount < TitleBackgroundColdStartDiagnosticLogic.ResolverAttemptBudget;

    public void RecordResolverAttempt(in CharaSelectResolvedActorContext actor)
    {
        ResolverAttemptCount++;
        ResolverEverValid |= actor.Valid;
        ResolverSource = actor.Source.ToString();
        CurrentCharacterAvailable |= actor.CurrentCharacterAvailable;
        EntryAvailable |= actor.EntryAvailable;
        SelectedContentAvailable |= actor.SelectedContentAvailable;
        MappingAvailable |= actor.MappingAvailable;
        MappingHit |= actor.MappingHit;
        ClientObjectIndexValid |= actor.ClientObjectIndexValid;
        ObjectResolved |= actor.ObjectResolved;
        IdentityConsistent |= actor.IdentityConsistent;
        DrawReady |= actor.DrawReady;

        if (actor.Valid && !DrawReadyAtFirstValid.HasValue)
        {
            DrawReadyAtFirstValid = actor.DrawReady;
        }

        if (actor.DrawReady)
        {
            DrawReadyEverTrue = true;
        }

        if (!_lastObservedDrawReady.HasValue)
        {
            _lastObservedDrawReady = actor.DrawReady;
        }
        else if (_lastObservedDrawReady.Value != actor.DrawReady)
        {
            DrawReadyTransitionCount++;
            _lastObservedDrawReady = actor.DrawReady;
        }

        if (actor.Valid)
        {
            if (!_lastValidIdentityKey.Valid)
            {
                _lastValidIdentityKey = actor.IdentityKey;
                ActorIdentityEpoch = 1;
            }
            else if (_lastValidIdentityKey != actor.IdentityKey)
            {
                _lastValidIdentityKey = actor.IdentityKey;
                ActorIdentityEpoch++;
                ActorRecreationCount++;

                if (PlacementWriteConfirmed)
                {
                    ActorEpochChangedAfterConfirmedWrite = true;
                }
            }
        }

        if (actor.VisualStateCaptured)
        {
            if (!FirstValidVisualCaptured)
            {
                FirstValidVisualCaptured = true;
                FirstValidVisualHidden = actor.VisibilityHidden;
            }

            LatestVisualCaptured = true;
            LatestVisualVisibilityRaw = actor.VisibilityRaw;
            LatestVisualHidden = actor.VisibilityHidden;
            LatestVisualReadyToDrawFlag = actor.ReadyToDrawFlag;
            LatestVisualRenderFlagsModelBitSet = actor.RenderFlagsModelBitSet;
            LatestVisualDrawObjectPresent = actor.DrawObjectPresent;
            LatestVisualScaleFinitePositive = actor.ScaleFinitePositive;
            LatestVisualDrawOffsetFinite = actor.DrawOffsetFinite;
            LatestVisualDrawOffsetNonZero = actor.DrawOffsetNonZero;

            if (!_visualHiddenObserved)
            {
                _visualHiddenObserved = true;
                _lastObservedVisualHidden = actor.VisibilityHidden;
            }
            else if (_lastObservedVisualHidden != actor.VisibilityHidden)
            {
                VisualVisibilityTransitionCount++;
                _lastObservedVisualHidden = actor.VisibilityHidden;
            }
        }
    }

    public void RecordRuntimeEvidence(
        TitleBackgroundCharaSelectPlacementRuntimeState placement,
        TitleBackgroundCharaSelectStaticAnchorSnapshot anchor,
        TitleBackgroundV2RuntimeState v2)
    {
        if (!string.Equals(anchor.AuthorizationReason, "not-run", StringComparison.Ordinal))
        {
            StaticAnchorEvaluated = true;
            StaticAnchorAuthorized = anchor.Authorized;
            StaticAnchorReason = anchor.AuthorizationReason;
        }

        CaptureCompleted |= placement.CaptureCompleted;
        CaptureTimedOut |= placement.CaptureTimedOut;
        CaptureStableSamples = Math.Max(CaptureStableSamples, placement.CaptureStableSamplesAtPersist);
        PlacementApplyCount = Math.Max(PlacementApplyCount, placement.PlacementApplyCount);
        PlacementWriteAttemptCount = Math.Max(PlacementWriteAttemptCount, placement.PlacementWriteAttemptCount);
        var writeConfirmed = placement.LastWriteReadbackConfirmed;
        PlacementWriteConfirmed |= writeConfirmed;
        PositionReadbackConfirmed |= placement.LastWritePositionReadbackConfirmed;
        RotationReadbackConfirmed |= placement.LastWriteRotationReadbackConfirmed;
        if (!string.Equals(placement.LastWriteStatus, "not-attempted", StringComparison.Ordinal))
        {
            PlacementWriteStatus = placement.LastWriteStatus;
        }
        if (!string.Equals(placement.LastReason, "not-run", StringComparison.Ordinal))
        {
            PlacementLastReason = placement.LastReason;
        }
        UniqueResolvedActorCount = Math.Max(UniqueResolvedActorCount, placement.UniqueResolvedActorCount);
        ConfirmedWriteKeyCount = Math.Max(ConfirmedWriteKeyCount, placement.ConfirmedWriteKeyCount);

        V2FramingAttemptCount = Math.Max(V2FramingAttemptCount, v2.FramingAttemptCount);
        V2FramingAppliedCount = Math.Max(V2FramingAppliedCount, v2.FramingAppliedCount);
        if (!string.Equals(v2.LastFramingStatus, "not-run", StringComparison.Ordinal))
        {
            V2LastFramingStatus = v2.LastFramingStatus;
        }
        V2WindowClosed |= v2.WindowClosed;
    }

    public void RecordLoginEvidence(
        bool sceneOverrideActive,
        bool v2PostLoginWritesStopped,
        bool placementLoginStopped)
    {
        LoginObserved = true;
        PostLoginSceneOverrideActive = sceneOverrideActive;
        V2PostLoginWritesStopped = v2PostLoginWritesStopped;
        PlacementLoginStopped = placementLoginStopped;
    }

    public string Complete(string diagnosis)
    {
        Active = false;
        Completed = true;
        Diagnosis = string.IsNullOrWhiteSpace(diagnosis) ? "insufficient-evidence" : diagnosis;
        return BuildReport();
    }

    public void Stop() => Active = false;

    private string BuildReport()
    {
        static string B(bool value) => value ? "True" : "False";
        static string N(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value;
        static string TB(bool? value) => value.HasValue ? B(value.Value) : "none";

        var lines = new List<string>
        {
            "[XIV Mini Util] Title Background cold-start diagnostic",
            $"coldStart.recorderSchema={TitleBackgroundColdStartDiagnosticLogic.RecorderSchema}",
            $"coldStart.diagnosis={Diagnosis}",
            $"coldStart.completed={B(Completed)}",
            $"coldStart.armMode={ArmMode}",
            $"coldStart.startupArmStatus={StartupArmStatus}",
            $"coldStart.startupArmReason={StartupArmReason}",
            $"startup.before.candidate={N(Before.CandidateId)}",
            $"startup.before.overrideEnabled={B(Before.OverrideEnabled)}",
            $"startup.before.v2Enabled={B(Before.V2Enabled)}",
            $"startup.before.placementEnabled={B(Before.PlacementEnabled)}",
            $"startup.before.placementCandidate={N(Before.PlacementCandidateId)}",
            $"startup.before.positionCaptured={B(Before.PositionCaptured)}",
            $"startup.before.owner={N(Before.ActualOwner)}",
            $"startup.before.expectedOwner={N(Before.ExpectedOwner)}",
            $"startup.after.candidate={N(After.CandidateId)}",
            $"startup.after.overrideEnabled={B(After.OverrideEnabled)}",
            $"startup.after.v2Enabled={B(After.V2Enabled)}",
            $"startup.after.placementEnabled={B(After.PlacementEnabled)}",
            $"startup.after.placementCandidate={N(After.PlacementCandidateId)}",
            $"startup.after.positionCaptured={B(After.PositionCaptured)}",
            $"startup.after.owner={N(After.ActualOwner)}",
            $"startup.after.expectedOwner={N(After.ExpectedOwner)}",
            $"startup.ownerMismatch={B(!string.Equals(After.ActualOwner, After.ExpectedOwner, StringComparison.Ordinal))}",
            $"firstScene.observed={B(CharaSelectObserved)}",
            $"firstScene.path={N(ScenePath)}",
            $"firstScene.territoryId={SceneTerritoryId}",
            $"firstScene.layerFilterKey={SceneLayerFilterKey}",
            $"firstScene.placementSceneGeneration={PlacementSceneGeneration}",
            $"firstScene.activeSceneGeneration={ActiveSceneGeneration}",
            $"firstScene.owner={N(SceneOwner)}",
            $"firstScene.v2Active={B(V2Active)}",
            $"firstScene.placementActive={B(PlacementActive)}",
            $"firstScene.legacyOwnershipInactive={B(LegacyOwnershipInactive)}",
            $"resolver.attemptCount={ResolverAttemptCount}",
            $"resolver.everValid={B(ResolverEverValid)}",
            $"resolver.source={ResolverSource}",
            $"resolver.currentCharacterAvailable={B(CurrentCharacterAvailable)}",
            $"resolver.entryAvailable={B(EntryAvailable)}",
            $"resolver.selectedContentAvailable={B(SelectedContentAvailable)}",
            $"resolver.mappingAvailable={B(MappingAvailable)}",
            $"resolver.mappingHit={B(MappingHit)}",
            $"resolver.clientObjectIndexValid={B(ClientObjectIndexValid)}",
            $"resolver.objectResolved={B(ObjectResolved)}",
            $"resolver.identityConsistent={B(IdentityConsistent)}",
            $"resolver.drawReady={B(DrawReady)}",
            $"resolver.drawReadyAtFirstValid={(DrawReadyAtFirstValid.HasValue ? B(DrawReadyAtFirstValid.Value) : "none")}",
            $"resolver.drawReadyEverTrue={B(DrawReadyEverTrue)}",
            $"resolver.drawReadyTransitionCount={DrawReadyTransitionCount}",
            $"actor.identityEpoch={ActorIdentityEpoch}",
            $"actor.recreationCount={ActorRecreationCount}",
            $"actor.visual.firstValidCaptured={B(FirstValidVisualCaptured)}",
            $"actor.visual.firstValidHidden={TB(FirstValidVisualHidden)}",
            $"actor.visual.latestCaptured={B(LatestVisualCaptured)}",
            $"actor.visual.latestVisibilityRaw={LatestVisualVisibilityRaw}",
            $"actor.visual.latestHidden={TB(LatestVisualHidden)}",
            $"actor.visual.latestReadyToDrawFlag={B(LatestVisualReadyToDrawFlag)}",
            // Raw/neutral evidence only — the Model bit's direction is not documented strongly enough
            // to be a "disabled" verdict (review 5118977128 MUST FIX). Do not use for classification.
            $"actor.visual.latestRenderFlagsModelBitSet={B(LatestVisualRenderFlagsModelBitSet)}",
            $"actor.visual.latestDrawObjectPresent={B(LatestVisualDrawObjectPresent)}",
            $"actor.visual.latestScaleFinitePositive={B(LatestVisualScaleFinitePositive)}",
            $"actor.visual.latestDrawOffsetFinite={B(LatestVisualDrawOffsetFinite)}",
            $"actor.visual.latestDrawOffsetNonZero={B(LatestVisualDrawOffsetNonZero)}",
            $"actor.visual.visibilityTransitionCount={VisualVisibilityTransitionCount}",
            $"staticAnchor.evaluated={B(StaticAnchorEvaluated)}",
            $"staticAnchor.authorized={B(StaticAnchorAuthorized)}",
            $"staticAnchor.reason={StaticAnchorReason}",
            $"placement.captureCompleted={B(CaptureCompleted)}",
            $"placement.captureTimedOut={B(CaptureTimedOut)}",
            $"placement.captureStableSamples={CaptureStableSamples}",
            $"placement.applyCount={PlacementApplyCount}",
            $"placement.writeAttemptCount={PlacementWriteAttemptCount}",
            $"placement.writeConfirmed={B(PlacementWriteConfirmed)}",
            $"placement.positionReadbackConfirmed={B(PositionReadbackConfirmed)}",
            $"placement.rotationReadbackConfirmed={B(RotationReadbackConfirmed)}",
            $"placement.writeStatus={PlacementWriteStatus}",
            $"placement.lastReason={PlacementLastReason}",
            $"placement.uniqueResolvedActorCount={UniqueResolvedActorCount}",
            $"placement.confirmedWriteKeyCount={ConfirmedWriteKeyCount}",
            $"placement.actorEpochChangedAfterConfirmedWrite={B(ActorEpochChangedAfterConfirmedWrite)}",
            $"v2.framingAttemptCount={V2FramingAttemptCount}",
            $"v2.framingAppliedCount={V2FramingAppliedCount}",
            $"v2.lastFramingStatus={V2LastFramingStatus}",
            $"v2.windowClosed={B(V2WindowClosed)}",
            $"login.observed={B(LoginObserved)}",
            $"login.sceneOverrideActiveAfterLogin={B(PostLoginSceneOverrideActive)}",
            $"login.v2PostLoginWritesStopped={B(V2PostLoginWritesStopped)}",
            $"login.placementLoginStopped={B(PlacementLoginStopped)}",
        };
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed unsafe partial class TitleScreenBackgroundService
{
    internal const string ColdStartDiagnosticFileName = "title-background-cold-start-diag.txt";
    internal const string ColdStartDiagnosticPreviousFileName = "title-background-cold-start-diag.prev.txt";
    internal const string ColdStartDiagnosticPreviousFileName2 = "title-background-cold-start-diag.prev2.txt";
    internal const string ColdStartDiagnosticPreviousFileName3 = "title-background-cold-start-diag.prev3.txt";
    internal const string ColdStartDiagnosticPreviousFileName4 = "title-background-cold-start-diag.prev4.txt";

    // Bounded rolling retention: current + up to 4 previous runs (5 total) so a rare failed run is not
    // lost immediately by the next natural Character Select occurrence. Oldest slot is dropped on rotation.
    private static readonly string[] ColdStartDiagnosticRotationFileNames =
    [
        ColdStartDiagnosticFileName,
        ColdStartDiagnosticPreviousFileName,
        ColdStartDiagnosticPreviousFileName2,
        ColdStartDiagnosticPreviousFileName3,
        ColdStartDiagnosticPreviousFileName4,
    ];

    private readonly TitleBackgroundColdStartDiagnosticRuntimeState _coldStartDiagnostic = new();

    internal void StartColdStartDiagnostic(in TitleBackgroundColdStartOwnerSnapshot before)
    {
        // Dev-plugin-only gate checked FIRST: a release/public plugin must show no cold-start recorder
        // behavior at all — not even the presence-marker log line or the startup snapshot (review
        // 5118977128 small cleanup).
        var (devAllowed, _) = TitleBackgroundColdStartDiagnosticLogic.EvaluateDevGate(_isDevPlugin);
        if (!devAllowed)
        {
            return;
        }

        _log.Information(
            "[XMU BG] Title Background cold-start recorder loaded. coldStart.recorderSchema={Schema}, isDevPlugin={IsDevPlugin}",
            TitleBackgroundColdStartDiagnosticLogic.RecorderSchema,
            _isDevPlugin);

        _coldStartDiagnostic.RecordStartupSnapshot(before);

        if (_coldStartDiagnostic.Active || _coldStartDiagnostic.Completed)
        {
            return;
        }

        var (status, reason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateStartupArm(
            _clientState.IsLoggedIn,
            before,
            _automaticCheck.Requested,
            _automaticCheck.PlacementProofArmed);

        _coldStartDiagnostic.RecordStartupArmResult(status, reason);

        _log.Information(
            "[XMU BG] Cold-start diagnostic startup arm result: {Status}. reason={Reason}, ownerBefore={OwnerBefore}, expected={ExpectedOwner}",
            status,
            reason,
            before.ActualOwner,
            before.ExpectedOwner);

        if (status != ColdStartArmStatus.Armed)
        {
            return;
        }

        var after = TitleBackgroundColdStartDiagnosticLogic.CaptureOwnerSnapshot(
            _configuration,
            TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner));
        _coldStartDiagnostic.Arm(before, after, ColdStartArmMode.Startup);
        _framework.Update += OnColdStartDiagnosticFrameworkUpdate;
        _coldStartDiagnostic.Subscribed = true;
    }

    internal void TryFallbackArmColdStartDiagnostic()
    {
        if (!_isDevPlugin)
        {
            return;
        }

        if (_coldStartDiagnostic.Active || _coldStartDiagnostic.Completed)
        {
            return;
        }

        if (!TryReadCurrentLobbyMap(out var currentMap))
        {
            return;
        }

        var currentSnapshot = TitleBackgroundColdStartDiagnosticLogic.CaptureOwnerSnapshot(
            _configuration,
            TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner));

        var automaticCheckActive = _automaticCheck.Requested
            || _automaticCheck.PlacementProofArmed
            || _automaticCheck.State != TitleBackgroundAutomaticCheckState.Idle;
        var probeTransactionActive = _probeTimeline.ActiveProbeSession != null;
        var startupSnapshot = _coldStartDiagnostic.StartupBefore;

        var (eligible, reason) = TitleBackgroundColdStartDiagnosticLogic.EvaluateFallbackArm(
            _clientState.IsLoggedIn,
            currentMap,
            startupSnapshot,
            currentSnapshot,
            automaticCheckActive,
            probeTransactionActive);

        if (!eligible)
        {
            return;
        }

        // MUST FIX: StartupBefore is immutable evidence; never replace it with currentSnapshot.
        _coldStartDiagnostic.Arm(startupSnapshot, currentSnapshot, ColdStartArmMode.FirstSceneFallback);
        if (!_coldStartDiagnostic.Subscribed)
        {
            _framework.Update += OnColdStartDiagnosticFrameworkUpdate;
            _coldStartDiagnostic.Subscribed = true;
        }

        _log.Information(
            "[XMU BG] Cold-start diagnostic armed via first-scene fallback. reason={Reason}, ownerBefore={OwnerBefore}, ownerAfter={OwnerAfter}, expected={ExpectedOwner}",
            reason,
            startupSnapshot.ActualOwner,
            currentSnapshot.ActualOwner,
            currentSnapshot.ExpectedOwner);
    }

    internal void StopColdStartDiagnostic()
    {
        if (_coldStartDiagnostic.Subscribed)
        {
            _framework.Update -= OnColdStartDiagnosticFrameworkUpdate;
            _coldStartDiagnostic.Subscribed = false;
        }

        _coldStartDiagnostic.Stop();
    }

    // Reuse the existing CharaSelect Title Background session-end semantics: once the first CharaSelect
    // was observed, a session that ends without login means the cold-start window is over. Finishing here
    // as insufficient-evidence prevents a later CharaSelect from being mixed into this single run and
    // avoids the diagnostic idling until the bounded timeout.
    internal void NotifyColdStartDiagnosticTitleBackgroundSessionEnded()
    {
        if (!_coldStartDiagnostic.Active || !_coldStartDiagnostic.CharaSelectObserved)
        {
            return;
        }

        FinishColdStartDiagnostic("insufficient-evidence");
    }

    internal bool TryConsumeColdStartDiagnosticClipboardText(out string text)
    {
        text = _coldStartDiagnostic.PendingClipboardText;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = string.Empty;
            return false;
        }

        _coldStartDiagnostic.PendingClipboardText = string.Empty;
        return true;
    }

    private void OnColdStartDiagnosticFrameworkUpdate(IFramework _)
    {
        if (!_coldStartDiagnostic.Active)
        {
            return;
        }

        try
        {
            if (_hookLifecycle.Disposed
                || DateTimeOffset.UtcNow - _coldStartDiagnostic.StartedAt >= TitleBackgroundColdStartDiagnosticLogic.MaxDuration)
            {
                FinishColdStartDiagnostic("insufficient-evidence");
                return;
            }

            if (_clientState.IsLoggedIn)
            {
                _coldStartDiagnostic.RecordLoginEvidence(
                    _activeSceneOverride,
                    _v2.PostLoginWritesStopped,
                    _charaSelectPlacement.LoginStopped);
                FinishColdStartDiagnostic(ClassifyColdStartDiagnostic());
                return;
            }

            if (!TryReadCurrentLobbyMap(out var currentMap)
                || currentMap != GameLobbyType.CharaSelect)
            {
                return;
            }

            _coldStartDiagnostic.RecordScene(
                _lastOverrideNewPath,
                _lastOverrideTerritoryId,
                _lastOverrideLayerFilterKey,
                _charaSelectPlacement.SceneGeneration,
                _activeCharaSelectSceneGeneration,
                TitleBackgroundCharaSelectEngineOwnerLogic.Describe(CharaSelectEngineOwner),
                IsV2Active,
                IsCharaSelectPlacementActive,
                IsNewCharaSelectEngineActive);

            if (_coldStartDiagnostic.CanAttemptResolver)
            {
                var actor = default(CharaSelectResolvedActorContext);
                _charaSelectService?.TryResolveCurrentCharaSelectActor(out actor);
                _coldStartDiagnostic.RecordResolverAttempt(actor);
            }

            // Read only existing runtime evidence. Do not evaluate/arm an anchor or write a native value here.
            _coldStartDiagnostic.RecordRuntimeEvidence(
                _charaSelectPlacement,
                _charaSelectStaticAnchor.Snapshot,
                _v2);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Cold-start diagnostic observation failed.");
            FinishColdStartDiagnostic("insufficient-evidence");
        }
    }

    private string ClassifyColdStartDiagnostic()
    {
        var input = new TitleBackgroundColdStartDiagnosisInput(
            _coldStartDiagnostic.Before,
            _coldStartDiagnostic.After,
            _coldStartDiagnostic.CharaSelectObserved,
            _coldStartDiagnostic.PlacementSceneGeneration,
            _coldStartDiagnostic.ActiveSceneGeneration,
            _coldStartDiagnostic.ResolverEverValid,
            _coldStartDiagnostic.DrawReadyEverTrue,
            _coldStartDiagnostic.StaticAnchorEvaluated,
            _coldStartDiagnostic.StaticAnchorAuthorized,
            _coldStartDiagnostic.StaticAnchorReason,
            _coldStartDiagnostic.CaptureCompleted,
            _coldStartDiagnostic.CaptureTimedOut,
            _coldStartDiagnostic.PlacementWriteAttemptCount,
            _coldStartDiagnostic.PlacementWriteConfirmed,
            _coldStartDiagnostic.UniqueResolvedActorCount,
            _coldStartDiagnostic.ConfirmedWriteKeyCount,
            _coldStartDiagnostic.ActorEpochChangedAfterConfirmedWrite,
            _coldStartDiagnostic.LoginObserved,
            _coldStartDiagnostic.LatestVisualCaptured,
            _coldStartDiagnostic.LatestVisualHidden,
            _coldStartDiagnostic.LatestVisualScaleFinitePositive,
            _coldStartDiagnostic.LatestVisualDrawOffsetFinite);
        return TitleBackgroundColdStartDiagnosticLogic.Classify(input);
    }

    private void FinishColdStartDiagnostic(string diagnosis)
    {
        if (!_coldStartDiagnostic.Active)
        {
            return;
        }

        var report = _coldStartDiagnostic.Complete(diagnosis);
        if (_coldStartDiagnostic.Subscribed)
        {
            _framework.Update -= OnColdStartDiagnosticFrameworkUpdate;
            _coldStartDiagnostic.Subscribed = false;
        }

        // Queue clipboard before best-effort file I/O so a filesystem failure cannot lose the one-run report.
        _coldStartDiagnostic.PendingClipboardText = report;
        try
        {
            Directory.CreateDirectory(_configDirectory);

            // Bounded rolling retention: shift each slot into the next-older one, oldest first, so a
            // rare failed run survives a few more natural Character Select occurrences instead of being
            // overwritten by the very next run.
            for (var i = ColdStartDiagnosticRotationFileNames.Length - 1; i > 0; i--)
            {
                var olderPath = Path.Combine(_configDirectory, ColdStartDiagnosticRotationFileNames[i - 1]);
                if (!File.Exists(olderPath))
                {
                    continue;
                }

                var newerSlotPath = Path.Combine(_configDirectory, ColdStartDiagnosticRotationFileNames[i]);
                try
                {
                    File.Copy(olderPath, newerSlotPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "[XMU BG] Cold-start diagnostic rolling retention copy skipped.");
                }
            }

            var currentPath = Path.Combine(_configDirectory, ColdStartDiagnosticFileName);
            File.WriteAllText(currentPath, report + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[XMU BG] Cold-start diagnostic file save failed.");
        }

        _log.Information("[XMU BG] Cold-start diagnostic completed. diagnosis={Diagnosis}", diagnosis);
    }
}

// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.ColdStartDiagnostic.cs
// Description: FRU cold-start の first Character Select を production state のまま受動観測する。
// Reason: OneClick / preset再選択で owner state を正規化すると再現条件を消すため、startup snapshot と
//         first CharaSelect lifecycle を mutation なしで取得し、1回の実機runから原因を分類する。
using System.Numerics;
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
    bool LatestVisualDrawOffsetFinite = false,
    // Placement retention (schema 3). RetentionTerminalComparable is true only when the last
    // pre-login sample had the same resolved actor identity + same scene generation as the last
    // confirmed placement apply. RetentionTerminalDriftMeters is then the scalar distance between
    // the actor's current Character.Position and the last confirmed applied position (never a
    // coordinate); it is NaN when the terminal sample was not comparable. A NaN / not-comparable
    // value must never be read as "retained" (fix 3/4). RetentionDriftExceededEpsilonEver is
    // evidence only and is deliberately NOT a classification input (mirrors the ever-anomaly rule,
    // review 5119158365).
    bool RetentionTerminalComparable = false,
    float RetentionTerminalDriftMeters = float.NaN,
    bool RetentionDriftExceededEpsilonEver = false);

// Same-tick (A+B) correlation inputs for the placement-retention observation. Built once per
// observed pre-login Character Select framework tick from the placement runtime state plus a single
// in-frame Character.Position read. Carries no pointers; the identity keys are used only for
// run-local anonymous slot mapping and component-change booleans and are never emitted.
internal readonly record struct TitleBackgroundColdStartPlacementTickSnapshot(
    int SceneGeneration,
    bool ResolvedActorValid,
    CharaSelectActorIdentityKey ResolvedActorKey,
    bool TransformReadOk,
    int PlacementApplyCount,
    bool PlacementLastWriteReadbackConfirmed,
    string PlacementLastWriteStatus,
    bool PlacementLastWritePositionReadback,
    bool PlacementLastWriteRotationReadback,
    bool PlacementLastWriteSetterCompleted,
    int PlacementWriteAttemptCount,
    string PlacementLastTrigger,
    CharaSelectActorIdentityKey PlacementLastAppliedActorKey,
    int PlacementLastAppliedSceneGeneration,
    float RetentionDriftMeters);

internal static class TitleBackgroundColdStartDiagnosticLogic
{
    // Schema 3: adds per-attempt placement-write observations, same-tick placement-retention drift
    // (scalar only), bounded event checkpoints, and a split login-stop representation.
    public const int RecorderSchema = 3;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    // Retention drift is compared with the SAME tolerance the placement write path itself uses to
    // confirm a position readback (TitleBackgroundCharaSelectPlacementLogic.CapturePositionEpsilon;
    // world units, yalms ≈ metres). "Retained" therefore means "still within the write path's own
    // confirmation epsilon" — no independently invented threshold (fix 4).
    public const float RetentionDriftEpsilonMeters =
        TitleBackgroundCharaSelectPlacementLogic.CapturePositionEpsilon;

    // Pure: scalar distance between the actor's current Character.Position and the last confirmed
    // applied position. Returns NaN for any non-finite input so callers never treat a bad read as 0.
    public static float ComputeRetentionDrift(Vector3 currentPosition, Vector3 appliedPosition)
    {
        if (!IsFiniteVector(currentPosition) || !IsFiniteVector(appliedPosition))
        {
            return float.NaN;
        }

        return Vector3.Distance(currentPosition, appliedPosition);
    }

    private static bool IsFiniteVector(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    // Split login-stop representation (fix 6). The proof-run LoginStopped latch is only meaningful
    // once a proof run has observed a logout -> Character Select transition; a passive cold-start run
    // never sets that precondition, so the latch is "not applicable", not "failed to stop".
    public static string LoginStopInterpretation(bool logoutTransitionObserved, bool loginStopLatch)
        => !logoutTransitionObserved
            ? "latch-not-applicable-requires-proof-run-logout-observation"
            : loginStopLatch
                ? "latch-set"
                : "latch-expected-but-unset";

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

        // Placement wrote and read back within epsilon, but by the last comparable pre-login sample
        // the actor's own Character.Position had drifted beyond that same epsilon from the last
        // confirmed applied position (same resolved actor + same scene generation). This is a
        // distinct stage between H6 (write/readback) and H8 (external visual): the value did not
        // stick. Not-comparable / NaN terminal samples never reach here (fix 3/4). Terminal only —
        // a mid-run drift that recovered is carried as RetentionDriftExceededEpsilonEver evidence,
        // not a classification (mirrors the ever-anomaly rule).
        if (input.PlacementWriteConfirmed
            && input.RetentionTerminalComparable
            && !float.IsNaN(input.RetentionTerminalDriftMeters)
            && input.RetentionTerminalDriftMeters > RetentionDriftEpsilonMeters)
        {
            return "placement-retention-drift";
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

        // H7 (write/readback succeeds, then a resolved identity never receives its own confirmed
        // write). Evidence and label are separated (fix 1): a bare post-write identity-epoch change
        // is NOT sufficient — ActorEpochChangedAfterConfirmedWrite alone stays reported evidence but
        // is not classified, because the placement path re-applies on identity change and the sample
        // log showed every resolved identity did get a confirmed write. Only an unmatched resolved
        // identity (uniqueResolved > confirmedWriteKeys) is H7.
        if (input.PlacementWriteConfirmed
            && input.UniqueResolvedActorCount > input.ConfirmedWriteKeyCount
            && input.UniqueResolvedActorCount > 1)
        {
            return "actor-recreation";
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

    // Persistent "ever" anomaly facts: once true, stay true for the run, so a transient anomaly that
    // recovers before the terminal/latest sample is not lost. Evidence only — not used for
    // classification (review 5119158365 MUST FIX).
    public bool VisibilityHiddenEverTrue { get; private set; }
    public bool DrawObjectEverAbsentWhileCaptured { get; private set; }
    public bool ReadyToDrawEverFalseWhileCaptured { get; private set; }

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

    // ---- schema 3: bounded event checkpoints (fix 5) ----
    // Cap + explicit dropped count. The latest valid state is always separately available in the
    // dedicated retention.* / actor.* / placement.latest* fields, so overflow never loses it.
    private const int MaxCheckpoints = 24;
    private const int MaxWriteObservations = 8;
    private const int MaxAnonActorSlots = 8;
    private readonly List<string> _checkpoints = [];
    private readonly List<string> _writeObservations = [];
    private readonly Dictionary<CharaSelectActorIdentityKey, int> _anonActorSlots = [];
    public int CheckpointDroppedCount { get; private set; }
    public int WriteObservationDroppedCount { get; private set; }
    public IReadOnlyList<string> Checkpoints => _checkpoints;
    public IReadOnlyList<string> WriteObservations => _writeObservations;

    private int _observationOrdinal;
    public int LastObservationOrdinal { get; private set; }
    public int LastObservationSceneGeneration { get; private set; }
    public int LastObservationActorEpoch { get; private set; }
    public int LastObservationPlacementApplyCount { get; private set; }

    // Pending identity-transition facts stashed by RecordResolverAttempt, flushed to a checkpoint by
    // the next RecordPlacementCorrelation so the checkpoint carries the shared run-local ordinal.
    private bool _pendingIdentityFirst;
    private bool _pendingIdentityChange;
    private CharaSelectActorIdentityKey _pendingIdentityKey;
    private (bool Content, bool ClientIndex, bool ObjectIndex, bool Entity) _pendingIdentityChangedFields;

    private int _lastSeenWriteAttemptCount;
    private int _lastSeenApplyCount;
    // Latest per-attempt write result, kept distinct from the cumulative PlacementWriteConfirmed OR
    // (fix 2): a cumulative True must not be read as "every attempt succeeded".
    public string LatestWriteStatus { get; private set; } = "not-attempted";
    public bool LatestWritePositionReadback { get; private set; }
    public bool LatestWriteRotationReadback { get; private set; }

    // ---- schema 3: same-tick placement retention (fix 3/4). Scalars only, never coordinates. ----
    private int _retentionBaselineApplyCount = -1;
    private CharaSelectActorIdentityKey _retentionBaselineActorKey;
    private int _retentionBaselineSceneGeneration;
    private bool _retentionIntervalHasSample;
    private bool _retentionIntervalDriftExceeded;
    private float _retentionIntervalLastValidDrift = float.NaN;
    private float _retentionIntervalMaxDrift = float.NaN;
    private int _retentionIntervalComparableCount;
    // Set when the currently observed actor/scene leaves the open interval's target without a new
    // confirmed placement (P2 review). Returning to the same key does NOT reuse the stale baseline:
    // comparison stays suppressed until a fresh confirmed placement event (baselineChanged) clears it.
    private bool _retentionAwaitingNewConfirmedPlacement;
    private float _retentionClosedIntervalsMaxDrift = float.NaN;
    public int RetentionClosedIntervalCount { get; private set; }
    public int RetentionComparableSampleCountTotal { get; private set; }
    public int RetentionNotComparableCount { get; private set; }
    public float RetentionLastValidDriftMeters { get; private set; } = float.NaN;
    public float RetentionMaxDriftMeters { get; private set; } = float.NaN;
    public bool RetentionDriftExceededEpsilonEverTrue { get; private set; }
    public bool RetentionTerminalComparable { get; private set; }
    public float RetentionTerminalDriftMeters { get; private set; } = float.NaN;
    public float RetentionIntervalLastValidDriftMeters => _retentionIntervalLastValidDrift;
    public float RetentionIntervalMaxDriftMeters => _retentionIntervalMaxDrift;
    public int RetentionIntervalComparableSampleCount => _retentionIntervalComparableCount;
    public bool RetentionAwaitingNewConfirmedPlacement => _retentionAwaitingNewConfirmedPlacement;
    public float RetentionClosedIntervalsMaxDriftMeters => _retentionClosedIntervalsMaxDrift;
    // Stats of the most recently closed interval, kept separate from the current interval and from
    // the run-wide aggregates so a stale over-epsilon spike is never mixed into a later interval.
    public string LastClosedIntervalReason { get; private set; } = "none";
    public float LastClosedIntervalMaxDriftMeters { get; private set; } = float.NaN;
    public float LastClosedIntervalLastValidDriftMeters { get; private set; } = float.NaN;
    public int LastClosedIntervalComparableSampleCount { get; private set; }

    // ---- schema 3: split login-stop representation (fix 6) ----
    public bool PlacementLoginStopLatch { get; private set; }
    public bool PlacementLogoutTransitionObserved { get; private set; }
    public int PlacementWriteAttemptDeltaAtLoginFrame { get; private set; }

    public int V2FramingAttemptCount { get; private set; }
    public int V2FramingAppliedCount { get; private set; }
    public string V2LastFramingStatus { get; private set; } = "not-run";
    public bool V2WindowClosed { get; private set; }

    public bool LoginObserved { get; private set; }
    public bool PostLoginSceneOverrideActive { get; private set; }
    public bool V2PostLoginWritesStopped { get; private set; }

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
                _pendingIdentityFirst = true;
                _pendingIdentityKey = actor.IdentityKey;
            }
            else if (_lastValidIdentityKey != actor.IdentityKey)
            {
                // Component-level change flags only — never the values (fix 1/5). The diff is done
                // inside the key type so this recorder never touches a raw id field.
                _pendingIdentityChangedFields = _lastValidIdentityKey.DiffComponents(actor.IdentityKey);
                _pendingIdentityChange = true;
                _pendingIdentityKey = actor.IdentityKey;

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

            // Persistent "ever" anomaly facts (evidence only, not a root-cause classification input):
            // a transient anomaly that recovers before the terminal/latest sample must not be erased by
            // it (review 5119158365 MUST FIX).
            if (actor.VisibilityHidden == true)
            {
                VisibilityHiddenEverTrue = true;
            }

            if (!actor.DrawObjectPresent)
            {
                DrawObjectEverAbsentWhileCaptured = true;
            }

            if (!actor.ReadyToDrawFlag)
            {
                ReadyToDrawEverFalseWhileCaptured = true;
            }

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

    // Same-tick (A+B) correlation: run-local ordinal, anonymous actor slot, per-attempt write
    // observation, and placement-retention drift — all from one framework tick so the recorder-side
    // identity epoch and the placement state are correlated at capture rather than reconciled from
    // separate aggregates afterwards (fix 3/5). Never stores pointers or coordinates.
    public void RecordPlacementCorrelation(in TitleBackgroundColdStartPlacementTickSnapshot snap)
    {
        _observationOrdinal++;
        LastObservationOrdinal = _observationOrdinal;
        LastObservationSceneGeneration = snap.SceneGeneration;
        LastObservationActorEpoch = ActorIdentityEpoch;
        LastObservationPlacementApplyCount = snap.PlacementApplyCount;

        if (_pendingIdentityFirst)
        {
            var firstSlot = ResolveAnonActorSlot(_pendingIdentityKey, out _);
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};identity-first;epoch={ActorIdentityEpoch}"
                + $";anonSlot={FormatSlot(firstSlot)}");
            _pendingIdentityFirst = false;
        }

        if (_pendingIdentityChange)
        {
            var slot = ResolveAnonActorSlot(_pendingIdentityKey, out var returned);
            var f = _pendingIdentityChangedFields;
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};identity-changed;epoch={ActorIdentityEpoch}"
                + $";changed=[content={Bool(f.Content)},clientIdx={Bool(f.ClientIndex)},objIdx={Bool(f.ObjectIndex)},entity={Bool(f.Entity)}]"
                + $";anonSlot={FormatSlot(slot)};returnedToSlot={Bool(returned)}");
            _pendingIdentityChange = false;
        }

        // Per-attempt placement write observation (fix 2): one entry per real attempt-count
        // increment. The placement state's Last* fields are set in the same call that increments the
        // counter, so this observes rather than fabricates; the cumulative PlacementWriteConfirmed OR
        // is kept separate. A counter jump > 1 records priorUnobservedAttempts rather than inventing
        // the skipped results.
        if (snap.PlacementWriteAttemptCount > _lastSeenWriteAttemptCount)
        {
            var priorUnobserved = snap.PlacementWriteAttemptCount - _lastSeenWriteAttemptCount - 1;
            AppendWriteObservation(
                $"ord={_observationOrdinal};idx={snap.PlacementWriteAttemptCount};status={NoneIfEmpty(snap.PlacementLastWriteStatus)}"
                + $";posReadback={Bool(snap.PlacementLastWritePositionReadback)};rotReadback={Bool(snap.PlacementLastWriteRotationReadback)}"
                + $";setterCompleted={Bool(snap.PlacementLastWriteSetterCompleted)}"
                + (priorUnobserved > 0 ? $";priorUnobservedAttempts={priorUnobserved}" : string.Empty));
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};placement-write-attempt;idx={snap.PlacementWriteAttemptCount}"
                + $";status={NoneIfEmpty(snap.PlacementLastWriteStatus)}");
            _lastSeenWriteAttemptCount = snap.PlacementWriteAttemptCount;
        }

        LatestWriteStatus = NoneIfEmpty(snap.PlacementLastWriteStatus);
        LatestWritePositionReadback = snap.PlacementLastWritePositionReadback;
        LatestWriteRotationReadback = snap.PlacementLastWriteRotationReadback;

        if (snap.PlacementApplyCount > _lastSeenApplyCount)
        {
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};placement-applied;applyCount={snap.PlacementApplyCount}"
                + $";confirmed={Bool(snap.PlacementLastWriteReadbackConfirmed)};trigger={NoneIfEmpty(snap.PlacementLastTrigger)}");
            _lastSeenApplyCount = snap.PlacementApplyCount;
        }

        // Retention interval management (fix 3 + P2 review):
        //  1. A new confirmed placement (apply count / last-applied actor / last-applied scene
        //     generation changed) closes the interval and re-baselines; it also clears any
        //     "awaiting new confirmed placement" suppression because a fresh confirmed target exists.
        //  2. Otherwise, if the interval has samples and the CURRENTLY OBSERVED (validly resolved)
        //     actor or scene generation has left the interval's baseline target, close the interval
        //     now and suppress comparison until a new confirmed placement — an A->B->A actor bounce
        //     (or a scene change) with no re-placement must not keep the old interval alive and must
        //     not emit a drift-recovered event against a stale over-epsilon spike. A read failure
        //     alone (actor not validly resolved) is NOT treated as a target change.
        var baselineChanged = snap.PlacementApplyCount != _retentionBaselineApplyCount
            || snap.PlacementLastAppliedActorKey != _retentionBaselineActorKey
            || snap.PlacementLastAppliedSceneGeneration != _retentionBaselineSceneGeneration;
        if (baselineChanged)
        {
            CloseRetentionInterval(snap.SceneGeneration, "placement-target-changed");
            _retentionBaselineApplyCount = snap.PlacementApplyCount;
            _retentionBaselineActorKey = snap.PlacementLastAppliedActorKey;
            _retentionBaselineSceneGeneration = snap.PlacementLastAppliedSceneGeneration;
            _retentionAwaitingNewConfirmedPlacement = false;
        }
        else if (_retentionIntervalHasSample
            && snap.ResolvedActorValid
            && (snap.ResolvedActorKey != _retentionBaselineActorKey
                || snap.SceneGeneration != _retentionBaselineSceneGeneration))
        {
            CloseRetentionInterval(snap.SceneGeneration, "observed-target-left");
            _retentionAwaitingNewConfirmedPlacement = true;
        }

        var comparable = snap.ResolvedActorValid
            && snap.TransformReadOk
            && snap.PlacementApplyCount > 0
            && snap.PlacementLastWriteReadbackConfirmed
            && !_retentionAwaitingNewConfirmedPlacement
            && snap.ResolvedActorKey == snap.PlacementLastAppliedActorKey
            && snap.SceneGeneration > 0
            && snap.SceneGeneration == snap.PlacementLastAppliedSceneGeneration
            && !float.IsNaN(snap.RetentionDriftMeters);

        RetentionTerminalComparable = comparable;

        if (!comparable)
        {
            RetentionNotComparableCount++;
            RetentionTerminalDriftMeters = float.NaN;
            // Read failure / unconfirmed placement / identity mismatch is never converted to 0 or to
            // a "retained" result (fix 3): the drift aggregates are left untouched.
            return;
        }

        var drift = snap.RetentionDriftMeters;
        _retentionIntervalHasSample = true;
        _retentionIntervalComparableCount++;
        _retentionIntervalLastValidDrift = drift;
        _retentionIntervalMaxDrift = float.IsNaN(_retentionIntervalMaxDrift)
            ? drift
            : Math.Max(_retentionIntervalMaxDrift, drift);
        RetentionComparableSampleCountTotal++;
        RetentionLastValidDriftMeters = drift;
        RetentionMaxDriftMeters = float.IsNaN(RetentionMaxDriftMeters)
            ? drift
            : Math.Max(RetentionMaxDriftMeters, drift);
        RetentionTerminalDriftMeters = drift;

        var exceeds = drift > TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters;
        if (exceeds && !_retentionIntervalDriftExceeded)
        {
            _retentionIntervalDriftExceeded = true;
            RetentionDriftExceededEpsilonEverTrue = true;
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};retention-drift-exceeded"
                + $";driftMeters={Distance(drift)};epsilonMeters={Distance(TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters)}");
        }
        else if (!exceeds && _retentionIntervalDriftExceeded)
        {
            _retentionIntervalDriftExceeded = false;
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={snap.SceneGeneration};retention-drift-recovered;driftMeters={Distance(drift)}");
        }
    }

    // Close the current retention interval. Only an interval that actually recorded a comparable
    // sample is counted / checkpointed; its max / last-valid / sample-count / reason are frozen into
    // the "last closed interval" fields (kept separate from the current interval and the run-wide
    // aggregates) and one bounded checkpoint is emitted. Per-interval accumulators are reset; the
    // baseline key itself is set by the caller.
    private void CloseRetentionInterval(int sceneGeneration, string reason)
    {
        if (_retentionIntervalHasSample)
        {
            RetentionClosedIntervalCount++;
            _retentionClosedIntervalsMaxDrift = float.IsNaN(_retentionClosedIntervalsMaxDrift)
                ? _retentionIntervalMaxDrift
                : Math.Max(_retentionClosedIntervalsMaxDrift, _retentionIntervalMaxDrift);
            LastClosedIntervalReason = reason;
            LastClosedIntervalMaxDriftMeters = _retentionIntervalMaxDrift;
            LastClosedIntervalLastValidDriftMeters = _retentionIntervalLastValidDrift;
            LastClosedIntervalComparableSampleCount = _retentionIntervalComparableCount;
            AppendCheckpoint(
                $"ord={_observationOrdinal};gen={sceneGeneration};retention-interval-closed;reason={reason}"
                + $";maxDriftMeters={Distance(_retentionIntervalMaxDrift)}"
                + $";lastValidDriftMeters={Distance(_retentionIntervalLastValidDrift)}"
                + $";comparableSamples={_retentionIntervalComparableCount}");
        }

        _retentionIntervalHasSample = false;
        _retentionIntervalDriftExceeded = false;
        _retentionIntervalLastValidDrift = float.NaN;
        _retentionIntervalMaxDrift = float.NaN;
        _retentionIntervalComparableCount = 0;
    }

    private int ResolveAnonActorSlot(CharaSelectActorIdentityKey key, out bool returned)
    {
        if (_anonActorSlots.TryGetValue(key, out var existing))
        {
            returned = true;
            return existing;
        }

        returned = false;
        if (_anonActorSlots.Count >= MaxAnonActorSlots)
        {
            return -1;
        }

        var slot = _anonActorSlots.Count;
        _anonActorSlots[key] = slot;
        return slot;
    }

    private void AppendCheckpoint(string line)
    {
        if (_checkpoints.Count >= MaxCheckpoints)
        {
            CheckpointDroppedCount++;
            return;
        }

        _checkpoints.Add(line);
    }

    private void AppendWriteObservation(string line)
    {
        if (_writeObservations.Count >= MaxWriteObservations)
        {
            WriteObservationDroppedCount++;
            return;
        }

        _writeObservations.Add(line);
    }

    private static string FormatSlot(int slot) => slot < 0 ? "overflow" : slot.ToString();
    private static string Bool(bool value) => value ? "True" : "False";
    private static string NoneIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value;
    private static string Distance(float value) => float.IsNaN(value) ? "none" : value.ToString("0.####");

    public void RecordLoginEvidence(
        bool sceneOverrideActive,
        bool v2PostLoginWritesStopped,
        bool placementLoginStopLatch,
        bool placementLogoutTransitionObserved,
        int placementWriteAttemptCountAtLoginFrame)
    {
        LoginObserved = true;
        PostLoginSceneOverrideActive = sceneOverrideActive;
        V2PostLoginWritesStopped = v2PostLoginWritesStopped;
        PlacementLoginStopLatch = placementLoginStopLatch;
        PlacementLogoutTransitionObserved = placementLogoutTransitionObserved;
        // Single bounded sample at the existing finish point. Says only whether a NEW placement write
        // attempt was recorded up to the login frame; observation ends here and this value is NOT
        // evidence about any later frame (fix 6). No post-login native re-read is done.
        PlacementWriteAttemptDeltaAtLoginFrame =
            Math.Max(0, placementWriteAttemptCountAtLoginFrame - _lastSeenWriteAttemptCount);
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
        static string FD(float value) => float.IsNaN(value) ? "none" : value.ToString("0.####");

        var lines = new List<string>
        {
            "[XIV Mini Util] Title Background cold-start diagnostic",
            $"coldStart.recorderSchema={TitleBackgroundColdStartDiagnosticLogic.RecorderSchema}",
            $"coldStart.diagnosis={Diagnosis}",
            // The diagnosis is a pipeline-stage observation, not a measured on-screen visual root
            // cause; the recorder does not directly measure whether the character is visible on
            // screen, and per-frame event order within a single tick is unknown (fix 1/5/7).
            "coldStart.evidenceNote=stage-observation-only;on-screen-visibility-not-directly-measured;intra-frame-event-order-unknown",
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
            $"actor.visual.visibilityHiddenEverTrue={B(VisibilityHiddenEverTrue)}",
            $"actor.visual.drawObjectEverAbsentWhileCaptured={B(DrawObjectEverAbsentWhileCaptured)}",
            $"actor.visual.readyToDrawEverFalseWhileCaptured={B(ReadyToDrawEverFalseWhileCaptured)}",
            $"staticAnchor.evaluated={B(StaticAnchorEvaluated)}",
            $"staticAnchor.authorized={B(StaticAnchorAuthorized)}",
            $"staticAnchor.reason={StaticAnchorReason}",
            $"placement.captureCompleted={B(CaptureCompleted)}",
            $"placement.captureTimedOut={B(CaptureTimedOut)}",
            $"placement.captureStableSamples={CaptureStableSamples}",
            $"placement.applyCount={PlacementApplyCount}",
            $"placement.writeAttemptCount={PlacementWriteAttemptCount}",
            // Cumulative OR across the run: True means "at least one confirmed attempt was observed",
            // NOT "every attempt succeeded" (fix 2). Per-attempt results are in placement.writeAttempt
            // below; the latest single result is placement.latestWrite*.
            $"placement.writeConfirmed={B(PlacementWriteConfirmed)}",
            "placement.writeConfirmedSemantics=cumulative-any-attempt",
            $"placement.positionReadbackConfirmed={B(PositionReadbackConfirmed)}",
            $"placement.rotationReadbackConfirmed={B(RotationReadbackConfirmed)}",
            $"placement.writeStatus={PlacementWriteStatus}",
            $"placement.latestWriteStatus={N(LatestWriteStatus)}",
            $"placement.latestWritePositionReadback={B(LatestWritePositionReadback)}",
            $"placement.latestWriteRotationReadback={B(LatestWriteRotationReadback)}",
            $"placement.writeAttemptObservations.count={_writeObservations.Count}",
            $"placement.writeAttemptObservations.dropped={WriteObservationDroppedCount}",
            $"placement.lastReason={PlacementLastReason}",
            $"placement.uniqueResolvedActorCount={UniqueResolvedActorCount}",
            $"placement.confirmedWriteKeyCount={ConfirmedWriteKeyCount}",
            $"placement.actorEpochChangedAfterConfirmedWrite={B(ActorEpochChangedAfterConfirmedWrite)}",
            // Same-tick placement retention (fix 3/4). Scalar distances only, never coordinates.
            // epsilon = the placement write path's own position-readback tolerance. terminalDrift is
            // "none" unless the last pre-login sample was comparable (same resolved actor + same
            // scene generation + a confirmed apply exists). A small terminal drift means only
            // "matched on the last valid observation" — it does not prove whole-interval retention
            // and does not identify any overwriting source.
            $"retention.epsilonMeters={FD(TitleBackgroundColdStartDiagnosticLogic.RetentionDriftEpsilonMeters)}",
            $"retention.terminalComparable={B(RetentionTerminalComparable)}",
            $"retention.terminalDriftMeters={FD(RetentionTerminalDriftMeters)}",
            $"retention.lastValidDriftMeters={FD(RetentionLastValidDriftMeters)}",
            $"retention.maxDriftMeters={FD(RetentionMaxDriftMeters)}",
            $"retention.comparableSampleCount={RetentionComparableSampleCountTotal}",
            $"retention.notComparableCount={RetentionNotComparableCount}",
            $"retention.closedIntervalCount={RetentionClosedIntervalCount}",
            $"retention.awaitingNewConfirmedPlacement={B(RetentionAwaitingNewConfirmedPlacement)}",
            $"retention.currentIntervalComparableSampleCount={RetentionIntervalComparableSampleCount}",
            $"retention.currentIntervalLastValidDriftMeters={FD(RetentionIntervalLastValidDriftMeters)}",
            $"retention.currentIntervalMaxDriftMeters={FD(RetentionIntervalMaxDriftMeters)}",
            // Closed intervals are kept separate from the current interval and the run-wide
            // aggregates so a stale over-epsilon spike from one target is not read as belonging to a
            // later one (P2 review).
            $"retention.closedIntervalsMaxDriftMeters={FD(RetentionClosedIntervalsMaxDriftMeters)}",
            $"retention.lastClosedIntervalReason={N(LastClosedIntervalReason)}",
            $"retention.lastClosedIntervalMaxDriftMeters={FD(LastClosedIntervalMaxDriftMeters)}",
            $"retention.lastClosedIntervalLastValidDriftMeters={FD(LastClosedIntervalLastValidDriftMeters)}",
            $"retention.lastClosedIntervalComparableSampleCount={LastClosedIntervalComparableSampleCount}",
            $"retention.driftExceededEpsilonEverTrue={B(RetentionDriftExceededEpsilonEverTrue)}",
            $"retention.lastObservationOrdinal={LastObservationOrdinal}",
            $"retention.lastObservationSceneGeneration={LastObservationSceneGeneration}",
            $"retention.lastObservationActorEpoch={LastObservationActorEpoch}",
            $"retention.lastObservationPlacementApplyCount={LastObservationPlacementApplyCount}",
            $"v2.framingAttemptCount={V2FramingAttemptCount}",
            $"v2.framingAppliedCount={V2FramingAppliedCount}",
            $"v2.lastFramingStatus={V2LastFramingStatus}",
            $"v2.windowClosed={B(V2WindowClosed)}",
            $"login.observed={B(LoginObserved)}",
            $"login.sceneOverrideActiveAfterLogin={B(PostLoginSceneOverrideActive)}",
            $"login.v2PostLoginWritesStopped={B(V2PostLoginWritesStopped)}",
            // Split representation (fix 6): the raw proof-run latch, the raw logout-transition
            // observation it depends on, and an interpretation — never a bare "stopped" claim. The
            // latch only becomes applicable on a proof run that observed a logout->CharaSelect
            // transition; a non-proof cold-start run leaves it not-applicable by design.
            $"login.placementLoginStopLatch={B(PlacementLoginStopLatch)}",
            $"login.placementLogoutTransitionObserved={B(PlacementLogoutTransitionObserved)}",
            $"login.placementLoginStopInterpretation={TitleBackgroundColdStartDiagnosticLogic.LoginStopInterpretation(PlacementLogoutTransitionObserved, PlacementLoginStopLatch)}",
            $"login.placementWriteAttemptDeltaAtLoginFrame={PlacementWriteAttemptDeltaAtLoginFrame}",
            "login.placementWriteObservationEndsAtLoginFrame=True",
            // Static code-gate fact, not a runtime measurement: the placement gate returns
            // Stop/"logged-in" and MaintainTitleEditInformedCharaSelectPlacement early-returns before
            // any native setter once IsLoggedIn is true.
            "login.placementPostLoginWriteGate=stop-on-logged-in-gate-early-return",
        };

        // Bounded variable-length sections (fix 5). Kept after the fixed block; report consumers read
        // key=value lines, order is not contractual. The latest valid state is always in the fixed
        // retention.* / actor.* / placement.latest* lines above, so checkpoint overflow never loses it.
        foreach (var observation in _writeObservations)
        {
            lines.Add($"placement.writeAttempt={observation}");
        }

        lines.Add($"checkpoints.count={_checkpoints.Count}");
        lines.Add($"checkpoints.dropped={CheckpointDroppedCount}");
        lines.Add("checkpoints.note=event-order-within-a-single-frame-is-unknown");
        for (var i = 0; i < _checkpoints.Count; i++)
        {
            lines.Add($"coldStart.checkpoint[{i}]={_checkpoints[i]}");
        }

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
                // Terminal path: record only from already-tracked runtime state (fix 7). No actor or
                // camera re-read here — the last retention/visual values stay the last safe pre-login
                // sample. The login-stop latch and its logout-transition precondition are recorded
                // raw and interpreted in the report (fix 6).
                _coldStartDiagnostic.RecordLoginEvidence(
                    _activeSceneOverride,
                    _v2.PostLoginWritesStopped,
                    _charaSelectPlacement.LoginStopped,
                    _charaSelectPlacement.LogoutTransitionObserved,
                    _charaSelectPlacement.PlacementWriteAttemptCount);
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

            // Observation must stay live for the entire active run (review 5119158365 MUST FIX): a
            // disappearance/visibility transition/DrawObject loss/actor recreation occurring after an
            // early fixed attempt count must not go unobserved. Bounded already by CharaSelect-only
            // scope, MaxDuration, and the login/session-end/dispose stops above and below.
            var actor = default(CharaSelectResolvedActorContext);
            _charaSelectService?.TryResolveCurrentCharaSelectActor(out actor);
            _coldStartDiagnostic.RecordResolverAttempt(actor);

            // Read only existing runtime evidence. Do not evaluate/arm an anchor or write a native value here.
            _coldStartDiagnostic.RecordRuntimeEvidence(
                _charaSelectPlacement,
                _charaSelectStaticAnchor.Snapshot,
                _v2);

            // Same-tick A+B correlation (fix 3). Reuse the actor context resolved above (pointer
            // valid only this frame) for one read-only Character.Position read, and pair it with the
            // placement runtime state so retention drift and the recorder identity epoch are
            // correlated at capture. Coordinate check: the placement write path writes
            // Character.Position via GameObject.SetPosition and confirms by reading Character.Position
            // back; TryReadCharaSelectCharacterTransform reads the same Character.Position field, so
            // the drift compares like-for-like against LastAppliedPosition (which stores exactly the
            // vector passed to SetPosition).
            var retentionDrift = float.NaN;
            var transformReadOk = false;
            if (actor.Valid
                && TitleBackgroundCharacterSourceProbe.TryReadCharaSelectCharacterTransform(
                    actor, out var actorPosition, out var actorRotationUnused)
                && float.IsFinite(actorRotationUnused))
            {
                transformReadOk = true;
                retentionDrift = TitleBackgroundColdStartDiagnosticLogic.ComputeRetentionDrift(
                    actorPosition, _charaSelectPlacement.LastAppliedPosition);
            }

            _coldStartDiagnostic.RecordPlacementCorrelation(new TitleBackgroundColdStartPlacementTickSnapshot(
                SceneGeneration: _activeCharaSelectSceneGeneration,
                ResolvedActorValid: actor.Valid,
                ResolvedActorKey: actor.IdentityKey,
                TransformReadOk: transformReadOk,
                PlacementApplyCount: _charaSelectPlacement.PlacementApplyCount,
                PlacementLastWriteReadbackConfirmed: _charaSelectPlacement.LastWriteReadbackConfirmed,
                PlacementLastWriteStatus: _charaSelectPlacement.LastWriteStatus,
                PlacementLastWritePositionReadback: _charaSelectPlacement.LastWritePositionReadbackConfirmed,
                PlacementLastWriteRotationReadback: _charaSelectPlacement.LastWriteRotationReadbackConfirmed,
                PlacementLastWriteSetterCompleted: _charaSelectPlacement.LastWriteSetterCallCompleted,
                PlacementWriteAttemptCount: _charaSelectPlacement.PlacementWriteAttemptCount,
                PlacementLastTrigger: _charaSelectPlacement.LastPlacementTrigger,
                PlacementLastAppliedActorKey: _charaSelectPlacement.LastAppliedActorKey,
                PlacementLastAppliedSceneGeneration: _charaSelectPlacement.LastAppliedSceneGeneration,
                RetentionDriftMeters: retentionDrift));
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
            _coldStartDiagnostic.LatestVisualDrawOffsetFinite,
            _coldStartDiagnostic.RetentionTerminalComparable,
            _coldStartDiagnostic.RetentionTerminalDriftMeters,
            _coldStartDiagnostic.RetentionDriftExceededEpsilonEverTrue);
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

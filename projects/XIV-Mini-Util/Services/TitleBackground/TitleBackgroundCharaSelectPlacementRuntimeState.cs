// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectPlacementRuntimeState.cs
// Description: TitleEdit-informed placement の run-scoped state と pointer-free proof snapshots。
// Reason: latest resolver attempt / last successful resolve / confirmed placement を分離し、
//         actor pointer を frame を越えて保持せず、前段失敗時の後段 write を構造的に禁止する。
using System.Numerics;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectPlacementProofSnapshot(
    string EngineOwner,
    bool PlacementProofArmed,
    bool PreLoginPlacementObserved,
    int PreLoginFrameworkFrameCount,
    int PlacementMaintainCallCount,
    int PlacementGateEvaluationCount,
    string FirstPreLoginGateReason,
    string LastPreLoginGateReason,
    bool CharaSelectSessionObserved,
    int SceneGenerationObserved,
    bool QuickCheckCollectingObserved,
    bool LogoutTransitionObserved,
    int OwnerNotPlacementWhileArmedCount,
    bool AttachedToActiveScene,
    // Effective resolver proof: confirmed placement > last successful > latest attempt。
    string ResolveSource,
    bool EntryAvailable,
    bool SelectedContentAvailable,
    bool MappingAvailable,
    bool MappingHit,
    bool ClientObjectIndexValid,
    bool ObjectResolved,
    bool IdentityMatched,
    bool DrawReady,
    int RetryCount,
    string CharacterResolveStatus,
    bool PositionCaptured,
    bool ZeroPositionAccepted,
    int CaptureStableSamples,
    bool CaptureTimedOut,
    int ApplyCount,
    string Trigger,
    bool LegacyOwnershipInactive,
    bool LoginStopped,
    string LastReason,
    string LastPreLoginReason,
    string CandidateId = "",
    bool CandidateMatches = false,
    string TargetScene = "",
    Vector3 TargetPosition = default,
    float TargetRotation = 0f,
    bool WriteConfirmed = false,
    bool SetterCallCompleted = false,
    bool PositionReadbackConfirmed = false,
    bool RotationReadbackConfirmed = false,
    string WriteStatus = "not-attempted",
    int WriteAttemptCount = 0,
    int WriteRetryBudget = TitleBackgroundCharaSelectPlacementLogic.PlacementWriteRetryBudget,
    bool WriteAuthorizedByResolver = false,
    string WriteResolveSource = "None",
    bool WriteIdentityMatched = false,
    int UniqueResolvedActorCount = 0,
    int ConfirmedWriteKeyCount = 0,
    TitleBackgroundActorResolveProofSnapshot LastResolveAttempt = default,
    TitleBackgroundActorResolveProofSnapshot LastSuccessfulResolveProof = default,
    TitleBackgroundConfirmedPlacementProof ConfirmedPlacementProof = default);

internal sealed class TitleBackgroundCharaSelectPlacementRuntimeState
{
    private readonly record struct ConfirmedWriteKey(
        int SceneGeneration,
        string CandidateId,
        CharaSelectActorIdentityKey ActorKey);

    private readonly HashSet<CharaSelectActorIdentityKey> _resolvedActorKeys = [];
    private readonly HashSet<ConfirmedWriteKey> _confirmedWriteKeys = [];

    public int SceneGeneration { get; private set; }

    // ---- bounded placement; pointer-free identity only ----
    public int LastAppliedSceneGeneration { get; private set; }
    public CharaSelectActorIdentityKey LastAppliedActorKey { get; private set; }
    public short LastAppliedClientObjectIndex => LastAppliedActorKey.ClientObjectIndex;
    internal ulong LastAppliedContentId => LastAppliedActorKey.ContentId;
    public string LastAppliedCandidateId { get; private set; } = string.Empty;
    public int PlacementApplyCount { get; private set; }
    public Vector3 LastAppliedPosition { get; private set; }
    public float LastAppliedRotation { get; private set; }
    public int LastAppliedFrame { get; private set; } = -1;
    public string LastPlacementTrigger { get; private set; } = "none";

    // ---- write attempt / authorization ----
    public int PlacementWriteAttemptCount { get; private set; }
    public int PlacementWriteAttemptsForTarget { get; private set; }
    public bool LastWriteSetterCallCompleted { get; private set; }
    public bool LastWritePositionReadbackConfirmed { get; private set; }
    public bool LastWriteRotationReadbackConfirmed { get; private set; }
    public bool LastWriteReadbackConfirmed { get; private set; }
    public string LastWriteStatus { get; private set; } = "not-attempted";
    public bool LastWriteAuthorizedByResolver { get; private set; }
    public string LastWriteResolveSource { get; private set; } = "None";
    public bool LastWriteIdentityMatched { get; private set; }
    private int _writeTargetSceneGeneration;
    private CharaSelectActorIdentityKey _writeTargetActorKey;
    private string _writeTargetCandidateId = string.Empty;

    // ---- gate / lifecycle ----
    public string LastReason { get; private set; } = "not-run";
    public string LastPreLoginReason { get; private set; } = "not-run";
    public bool LoginStopped { get; private set; }
    public int PreLoginFrameworkFrameCount { get; private set; }
    public int PlacementMaintainCallCount { get; private set; }
    public int PlacementGateEvaluationCount { get; private set; }
    public string FirstPreLoginGateReason { get; private set; } = "not-run";
    public string LastPreLoginGateReason { get; private set; } = "not-run";
    public bool CharaSelectSessionObserved { get; private set; }
    public int SceneGenerationObserved { get; private set; }
    public bool QuickCheckCollectingObserved { get; private set; }
    public bool LogoutTransitionObserved { get; private set; }
    public int OwnerNotPlacementWhileArmedCount { get; private set; }
    public bool AttachedToActiveScene { get; private set; }

    // ---- resolver semantics ----
    public TitleBackgroundActorResolveProofSnapshot LastResolveAttempt { get; private set; }
        = TitleBackgroundActorResolveProofSnapshot.NotRun;
    public TitleBackgroundActorResolveProofSnapshot LastSuccessfulResolveProof { get; private set; }
        = TitleBackgroundActorResolveProofSnapshot.NotRun;
    public TitleBackgroundConfirmedPlacementProof ConfirmedPlacementProof { get; private set; }
        = TitleBackgroundConfirmedPlacementProof.None;
    public bool PreLoginPlacementObserved { get; private set; }
    public int UniqueResolvedActorCount => _resolvedActorKeys.Count;
    public int ConfirmedWriteKeyCount => _confirmedWriteKeys.Count;

    // live compatibility accessors use semantic proof, never the latest failed attempt after success。
    private TitleBackgroundActorResolveProofSnapshot EffectiveResolveProof => ConfirmedPlacementProof.Valid
        ? ConfirmedPlacementProof.ResolveProof
        : LastSuccessfulResolveProof.Valid
            ? LastSuccessfulResolveProof
            : LastResolveAttempt;
    public string LastCharacterResolveStatus => EffectiveResolveProof.Valid ? "resolved" : LastResolveAttempt.Status;
    public string LastResolveSource => EffectiveResolveProof.Source;
    public bool LastEntryAvailable => EffectiveResolveProof.EntryAvailable;
    public bool LastSelectedContentAvailable => EffectiveResolveProof.SelectedContentAvailable;
    public bool LastMappingAvailable => EffectiveResolveProof.MappingAvailable;
    public bool LastMappingHit => EffectiveResolveProof.MappingHit;
    public bool LastClientObjectIndexValid => EffectiveResolveProof.ClientObjectIndexValid;
    public bool LastObjectResolved => EffectiveResolveProof.ObjectResolved;
    public bool LastIdentityMatched => EffectiveResolveProof.IdentityMatched;
    public bool LastDrawReady => EffectiveResolveProof.DrawReady;
    public int RetryCount => EffectiveResolveProof.RetryCount;

    // ---- active capture proof ----
    public int CaptureSampleStreak { get; private set; }
    public bool CaptureHasPreviousSample { get; private set; }
    public Vector3 CaptureLastSamplePosition { get; private set; }
    public float CaptureLastSampleRotation { get; private set; }
    public int CaptureFramesElapsed { get; private set; }
    public int CaptureStableSamplesAtPersist { get; private set; }
    public bool ZeroPositionAccepted { get; private set; }
    public bool CaptureTimedOut { get; private set; }
    public bool CaptureCompleted { get; private set; }
    public string CaptureCandidateId { get; private set; } = string.Empty;
    public Vector3 CapturedPosition { get; private set; }
    public float CapturedRotation { get; private set; }
    private CharaSelectActorIdentityKey _captureActorKey;
    private int _captureSceneGeneration;
    private string _captureResolveSource = "None";
    public bool CaptureCompletionPendingApply { get; private set; }

    private bool _preLoginFrozen;

    public void IncrementSceneGeneration() => SceneGeneration++;
    public void RecordPreLoginFrameworkFrame() => PreLoginFrameworkFrameCount++;
    public void RecordMaintainCall() => PlacementMaintainCallCount++;
    public void RecordOwnerNotPlacementWhileArmed() => OwnerNotPlacementWhileArmedCount++;

    public void RecordGateEvaluation(string reason, bool preLogin)
    {
        PlacementGateEvaluationCount++;
        if (!preLogin || _preLoginFrozen)
        {
            return;
        }

        if (FirstPreLoginGateReason == "not-run")
        {
            FirstPreLoginGateReason = reason;
        }

        LastPreLoginGateReason = reason;
    }

    public void ObserveLifecycle(
        bool charaSelectSessionActive,
        int activeSceneGeneration,
        bool quickCheckCollecting,
        bool logoutTransition,
        bool attachedToActiveScene)
    {
        CharaSelectSessionObserved |= charaSelectSessionActive;
        SceneGenerationObserved = Math.Max(SceneGenerationObserved, activeSceneGeneration);
        QuickCheckCollectingObserved |= quickCheckCollecting;
        LogoutTransitionObserved |= logoutTransition;
        AttachedToActiveScene |= attachedToActiveScene;
    }

    public void RecordCharacterResolve(
        in TitleBackgroundResolvedActorContext context,
        int retryCount)
    {
        if (_preLoginFrozen)
        {
            return;
        }

        var actor = context.Actor;
        var proof = new TitleBackgroundActorResolveProofSnapshot(
            actor.Valid,
            actor.Source.ToString(),
            context.RuntimeSceneGeneration,
            actor.CurrentCharacterAvailable,
            actor.EntryAvailable,
            actor.SelectedContentAvailable,
            actor.MappingAvailable,
            actor.MappingHit,
            actor.ClientObjectIndexValid,
            actor.ObjectResolved,
            actor.IdentityConsistent,
            actor.DrawReady,
            context.CandidateMatches,
            retryCount,
            actor.Valid ? "resolved" : "unresolved");
        LastResolveAttempt = proof;

        if (!proof.Valid)
        {
            return;
        }

        LastSuccessfulResolveProof = proof;
        PreLoginPlacementObserved = true;
        _resolvedActorKeys.Add(actor.IdentityKey);
    }

    public bool CaptureIdentityMatches(
        CharaSelectActorIdentityKey actorKey,
        int sceneGeneration,
        string candidateId,
        string resolveSource)
        => !CaptureHasPreviousSample
            || (_captureActorKey == actorKey
                && _captureSceneGeneration == sceneGeneration
                && string.Equals(_captureResolveSource, resolveSource, StringComparison.Ordinal)
                && string.Equals(CaptureCandidateId, candidateId, StringComparison.Ordinal));

    public void RecordCaptureSampleIdentity(
        CharaSelectActorIdentityKey actorKey,
        int sceneGeneration,
        string candidateId,
        string resolveSource)
    {
        _captureActorKey = actorKey;
        _captureSceneGeneration = sceneGeneration;
        CaptureCandidateId = candidateId ?? string.Empty;
        _captureResolveSource = resolveSource ?? "None";
    }

    public bool CaptureProofMatches(in TitleBackgroundResolvedActorContext context)
        => CaptureCompleted
            && context.Valid
            && _captureActorKey == context.Actor.IdentityKey
            && _captureSceneGeneration == context.RuntimeSceneGeneration
            && string.Equals(CaptureCandidateId, context.CandidateId, StringComparison.Ordinal)
            && string.Equals(_captureResolveSource, context.Actor.Source.ToString(), StringComparison.Ordinal);

    public bool CaptureGenerationMatches(in TitleBackgroundResolvedActorContext context)
        => CaptureCompleted && _captureSceneGeneration == context.RuntimeSceneGeneration;

    public bool CaptureActorIdentityMatches(in TitleBackgroundResolvedActorContext context)
        => CaptureCompleted && _captureActorKey == context.Actor.IdentityKey;

    public bool CaptureCandidateMatches(in TitleBackgroundResolvedActorContext context)
        => CaptureCompleted && string.Equals(CaptureCandidateId, context.CandidateId, StringComparison.Ordinal);

    public void RecordCaptureSample(int streak, Vector3 position, float rotation, int framesElapsed)
    {
        CaptureSampleStreak = streak;
        CaptureHasPreviousSample = true;
        CaptureLastSamplePosition = position;
        CaptureLastSampleRotation = rotation;
        CaptureFramesElapsed = framesElapsed;
    }

    public int RecordCaptureSamplingAttempt() => ++CaptureFramesElapsed;

    public void ResetCaptureStreak()
    {
        CaptureSampleStreak = 0;
        CaptureHasPreviousSample = false;
        CaptureLastSamplePosition = Vector3.Zero;
        CaptureLastSampleRotation = 0f;
        _captureActorKey = default;
        _captureSceneGeneration = 0;
        _captureResolveSource = "None";
    }

    public void ResetCaptureProof()
    {
        ResetCaptureSampling();
        CaptureStableSamplesAtPersist = 0;
        ZeroPositionAccepted = false;
        CaptureTimedOut = false;
        CaptureCompleted = false;
        CaptureCandidateId = string.Empty;
        CapturedPosition = Vector3.Zero;
        CapturedRotation = 0f;
        CaptureCompletionPendingApply = false;
    }

    public void RecordCapturePersisted(
        int stableSamples,
        bool zeroAccepted,
        Vector3 position,
        float rotation,
        in TitleBackgroundResolvedActorContext context)
    {
        CaptureStableSamplesAtPersist = stableSamples;
        ZeroPositionAccepted = zeroAccepted;
        CaptureCompleted = true;
        CaptureCandidateId = context.CandidateId ?? string.Empty;
        CapturedPosition = position;
        CapturedRotation = rotation;
        _captureActorKey = context.Actor.IdentityKey;
        _captureSceneGeneration = context.RuntimeSceneGeneration;
        _captureResolveSource = context.Actor.Source.ToString();
        CaptureCompletionPendingApply = true;
    }

    public void MarkCaptureTimedOut() => CaptureTimedOut = true;

    public bool CanAttemptPlacementWrite(
        int sceneGeneration,
        CharaSelectActorIdentityKey actorKey,
        string candidateId)
    {
        if (!actorKey.Valid)
        {
            return false;
        }

        var sameTarget = _writeTargetSceneGeneration == sceneGeneration
            && _writeTargetActorKey == actorKey
            && string.Equals(_writeTargetCandidateId, candidateId, StringComparison.Ordinal);
        return !sameTarget
            || PlacementWriteAttemptsForTarget < TitleBackgroundCharaSelectPlacementLogic.PlacementWriteRetryBudget;
    }

    public void RecordWriteAuthorization(
        TitleBackgroundPlacementWriteAuthorization authorization,
        string resolveSource)
    {
        LastWriteAuthorizedByResolver = authorization.AuthorizedByResolver;
        LastWriteResolveSource = resolveSource ?? "None";
        LastWriteIdentityMatched = authorization.IdentityMatched;
        if (!authorization.Allowed)
        {
            LastWriteStatus = authorization.Reason;
        }
    }

    public void RecordPlacementWriteAttempt(
        int sceneGeneration,
        CharaSelectActorIdentityKey actorKey,
        string candidateId,
        bool setterCallCompleted,
        bool positionReadbackConfirmed,
        bool rotationReadbackConfirmed,
        string status)
    {
        if (_writeTargetSceneGeneration != sceneGeneration
            || _writeTargetActorKey != actorKey
            || !string.Equals(_writeTargetCandidateId, candidateId, StringComparison.Ordinal))
        {
            _writeTargetSceneGeneration = sceneGeneration;
            _writeTargetActorKey = actorKey;
            _writeTargetCandidateId = candidateId ?? string.Empty;
            PlacementWriteAttemptsForTarget = 0;
        }

        PlacementWriteAttemptsForTarget++;
        PlacementWriteAttemptCount++;
        LastWriteSetterCallCompleted = setterCallCompleted;
        LastWritePositionReadbackConfirmed = positionReadbackConfirmed;
        LastWriteRotationReadbackConfirmed = rotationReadbackConfirmed;
        LastWriteReadbackConfirmed = setterCallCompleted
            && positionReadbackConfirmed
            && rotationReadbackConfirmed;
        LastWriteStatus = string.IsNullOrWhiteSpace(status) ? "unverified" : status;
    }

    public void RecordPlacementApplied(
        int sceneGeneration,
        CharaSelectActorIdentityKey actorKey,
        string candidateId,
        Vector3 position,
        float rotation,
        int frame,
        string trigger = "none")
    {
        LastAppliedSceneGeneration = sceneGeneration;
        LastAppliedActorKey = actorKey;
        LastAppliedCandidateId = candidateId ?? string.Empty;
        LastAppliedPosition = position;
        LastAppliedRotation = rotation;
        LastAppliedFrame = frame;
        LastPlacementTrigger = trigger;
        PlacementApplyCount++;
        CaptureCompletionPendingApply = false;
        SetReason("applied");
    }

    public void RecordConfirmedPlacementProof(
        in TitleBackgroundResolvedActorContext context,
        Vector3? targetPosition = null,
        float? targetRotation = null)
    {
        if (!CaptureProofMatches(context) || !LastWriteReadbackConfirmed)
        {
            return;
        }

        _confirmedWriteKeys.Add(new ConfirmedWriteKey(
            context.RuntimeSceneGeneration,
            context.CandidateId,
            context.Actor.IdentityKey));

        if (ConfirmedPlacementProof.Valid)
        {
            return;
        }

        var resolveProof = new TitleBackgroundActorResolveProofSnapshot(
            true,
            context.Actor.Source.ToString(),
            context.RuntimeSceneGeneration,
            context.Actor.CurrentCharacterAvailable,
            context.Actor.EntryAvailable,
            context.Actor.SelectedContentAvailable,
            context.Actor.MappingAvailable,
            context.Actor.MappingHit,
            context.Actor.ClientObjectIndexValid,
            context.Actor.ObjectResolved,
            context.Actor.IdentityConsistent,
            context.Actor.DrawReady,
            context.CandidateMatches,
            0,
            "resolved");
        var confirmedPosition = targetPosition.HasValue
            && TitleBackgroundCameraMath.IsFiniteVector(targetPosition.Value)
            ? targetPosition.Value
            : CapturedPosition;
        var confirmedRotation = targetRotation.HasValue
            && float.IsFinite(targetRotation.Value)
            ? targetRotation.Value
            : CapturedRotation;
        ConfirmedPlacementProof = new TitleBackgroundConfirmedPlacementProof(
            true,
            resolveProof,
            true,
            context.Actor.IdentityConsistent,
            context.RuntimeSceneGeneration,
            context.CandidateId,
            context.CandidateMatches,
            true,
            CaptureStableSamplesAtPersist,
            ZeroPositionAccepted,
            confirmedPosition,
            confirmedRotation,
            true);
    }

    public void RecordSkip(string reason) => SetReason(reason);

    public void MarkLoginStopped()
    {
        if (!_preLoginFrozen)
        {
            LastPreLoginReason = LastReason;
            _preLoginFrozen = true;
        }

        LoginStopped = true;
        LastReason = "logged-in";
    }

    public TitleBackgroundCharaSelectPlacementProofSnapshot CaptureProofSnapshot(
        string engineOwner,
        bool placementProofArmed,
        bool positionCaptured,
        bool legacyOwnershipInactive,
        string candidateId = "",
        bool candidateMatches = false,
        string targetScene = "",
        Vector3 targetPosition = default,
        float targetRotation = 0f)
    {
        var effectiveResolve = EffectiveResolveProof;
        var confirmed = ConfirmedPlacementProof;
        var effectivePositionCaptured = confirmed.Valid || positionCaptured;
        var effectivePosition = confirmed.Valid ? confirmed.Position : CaptureCompleted ? CapturedPosition : targetPosition;
        var effectiveRotation = confirmed.Valid ? confirmed.Rotation : CaptureCompleted ? CapturedRotation : targetRotation;
        var effectiveStableSamples = confirmed.Valid ? confirmed.StableSamples : CaptureStableSamplesAtPersist;
        var effectiveZeroAccepted = confirmed.Valid ? confirmed.ZeroPositionAccepted : ZeroPositionAccepted;
        var effectiveWriteConfirmed = confirmed.Valid || LastWriteReadbackConfirmed;

        return new TitleBackgroundCharaSelectPlacementProofSnapshot(
            engineOwner,
            placementProofArmed,
            PreLoginPlacementObserved,
            PreLoginFrameworkFrameCount,
            PlacementMaintainCallCount,
            PlacementGateEvaluationCount,
            FirstPreLoginGateReason,
            LastPreLoginGateReason,
            CharaSelectSessionObserved,
            SceneGenerationObserved,
            QuickCheckCollectingObserved,
            LogoutTransitionObserved,
            OwnerNotPlacementWhileArmedCount,
            AttachedToActiveScene,
            effectiveResolve.Source,
            effectiveResolve.EntryAvailable,
            effectiveResolve.SelectedContentAvailable,
            effectiveResolve.MappingAvailable,
            effectiveResolve.MappingHit,
            effectiveResolve.ClientObjectIndexValid,
            effectiveResolve.ObjectResolved,
            effectiveResolve.IdentityMatched,
            effectiveResolve.DrawReady,
            effectiveResolve.RetryCount,
            effectiveResolve.Valid ? "resolved" : effectiveResolve.Status,
            effectivePositionCaptured,
            effectiveZeroAccepted,
            effectiveStableSamples,
            CaptureTimedOut,
            PlacementApplyCount,
            LastPlacementTrigger,
            legacyOwnershipInactive,
            LoginStopped || _preLoginFrozen,
            LastReason,
            LastPreLoginReason,
            confirmed.Valid && !string.IsNullOrWhiteSpace(confirmed.CandidateId)
                ? confirmed.CandidateId
                : candidateId ?? string.Empty,
            confirmed.Valid ? confirmed.CandidateMatches : candidateMatches,
            targetScene ?? string.Empty,
            effectivePosition,
            effectiveRotation,
            effectiveWriteConfirmed,
            confirmed.Valid || LastWriteSetterCallCompleted,
            confirmed.Valid || LastWritePositionReadbackConfirmed,
            confirmed.Valid || LastWriteRotationReadbackConfirmed,
            confirmed.Valid ? "confirmed" : LastWriteStatus,
            PlacementWriteAttemptCount,
            TitleBackgroundCharaSelectPlacementLogic.PlacementWriteRetryBudget,
            confirmed.Valid || LastWriteAuthorizedByResolver,
            confirmed.Valid ? confirmed.ResolveProof.Source : LastWriteResolveSource,
            confirmed.Valid ? confirmed.IdentityMatched : LastWriteIdentityMatched,
            UniqueResolvedActorCount,
            ConfirmedWriteKeyCount,
            LastResolveAttempt,
            LastSuccessfulResolveProof,
            ConfirmedPlacementProof);
    }

    public void ResetCaptureSampling()
    {
        ResetCaptureStreak();
        CaptureFramesElapsed = 0;
    }

    public void Reset()
    {
        SceneGeneration = 0;
        LastAppliedSceneGeneration = 0;
        LastAppliedActorKey = default;
        LastAppliedCandidateId = string.Empty;
        PlacementApplyCount = 0;
        LastAppliedPosition = Vector3.Zero;
        LastAppliedRotation = 0f;
        LastAppliedFrame = -1;
        LastPlacementTrigger = "none";
        PlacementWriteAttemptCount = 0;
        PlacementWriteAttemptsForTarget = 0;
        LastWriteSetterCallCompleted = false;
        LastWritePositionReadbackConfirmed = false;
        LastWriteRotationReadbackConfirmed = false;
        LastWriteReadbackConfirmed = false;
        LastWriteStatus = "not-attempted";
        LastWriteAuthorizedByResolver = false;
        LastWriteResolveSource = "None";
        LastWriteIdentityMatched = false;
        _writeTargetSceneGeneration = 0;
        _writeTargetActorKey = default;
        _writeTargetCandidateId = string.Empty;
        LastReason = "not-run";
        LastPreLoginReason = "not-run";
        LoginStopped = false;
        PreLoginFrameworkFrameCount = 0;
        PlacementMaintainCallCount = 0;
        PlacementGateEvaluationCount = 0;
        FirstPreLoginGateReason = "not-run";
        LastPreLoginGateReason = "not-run";
        CharaSelectSessionObserved = false;
        SceneGenerationObserved = 0;
        QuickCheckCollectingObserved = false;
        LogoutTransitionObserved = false;
        OwnerNotPlacementWhileArmedCount = 0;
        AttachedToActiveScene = false;
        LastResolveAttempt = TitleBackgroundActorResolveProofSnapshot.NotRun;
        LastSuccessfulResolveProof = TitleBackgroundActorResolveProofSnapshot.NotRun;
        ConfirmedPlacementProof = TitleBackgroundConfirmedPlacementProof.None;
        PreLoginPlacementObserved = false;
        _resolvedActorKeys.Clear();
        _confirmedWriteKeys.Clear();
        _preLoginFrozen = false;
        ResetCaptureProof();
    }

    private void SetReason(string reason)
    {
        LastReason = reason;
        if (!_preLoginFrozen)
        {
            LastPreLoginReason = reason;
        }
    }
}

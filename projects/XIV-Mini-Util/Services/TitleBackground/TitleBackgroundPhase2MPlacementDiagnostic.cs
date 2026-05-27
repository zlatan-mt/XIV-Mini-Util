// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPhase2MPlacementDiagnostic.cs
// Description: Phase 2M character placement diagnostics summary logic
// Reason: Keep placement/ground-height verdicts testable without writing actor or camera state
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundPhase2MActorMatchKind
{
    None,
    Single,
    Ambiguous,
}

internal readonly record struct TitleBackgroundPhase2MActorCandidate(
    int SourceIndex,
    string Source,
    int ObjectIndex,
    string ObjectKind,
    string Name,
    ulong GameObjectId,
    uint EntityId,
    nint Address,
    Vector3 Position,
    float? Rotation,
    bool Named,
    bool PlayerLike,
    bool BattleCharacterLike,
    bool EventNpcLike,
    bool CompanionLike,
    bool VisibleHint,
    float? DistanceFromConfiguredCharacter,
    float? DistanceFromActiveLookAt,
    float? DistanceFromActiveCameraPosition,
    float? YDeltaFromConfiguredCharacter,
    bool NearConfiguredCharacter,
    bool NearCameraLookAt,
    bool NearCameraPosition,
    string CategoryReason);

internal readonly record struct TitleBackgroundPhase2MObjectTableStats(
    int TotalScanned,
    int NamedCount,
    int PlayerLikeCount,
    int BattleCharaCount,
    int EventNpcCount,
    int CompanionLikeCount,
    int NearCameraCount,
    int NearConfiguredCharacterCount);

internal readonly record struct TitleBackgroundPhase2MPlacementFrame(
    int Frame,
    string Reason,
    bool ActiveCameraCaptured,
    Vector3? ActiveCameraPosition,
    Vector3? ActiveCameraLookAt,
    float? ActiveCameraYaw,
    float? ActiveCameraPitch,
    float? ActiveCameraDistance,
    bool LobbyCameraCaptured,
    Vector3? LobbyCameraLookAt,
    float? LobbyDirH,
    float? LobbyDirV,
    float? LobbyDistance,
    float? LobbyInterpDistance,
    TitleBackgroundPhase2MActorMatchKind ActorMatchKind,
    TitleBackgroundPhase2MActorCandidate? Actor,
    IReadOnlyList<TitleBackgroundPhase2MActorCandidate> ObjectCandidates,
    int CandidateCount,
    string ActorStatus,
    TitleBackgroundPhase2MObjectTableStats ObjectTableStats,
    string ActorCandidateStatus,
    string ActorCandidateReason,
    string ActorSource,
    string NextNativeSourceToInspect,
    string GroundHeightStatus,
    float? GroundY,
    float? ActorToCameraDistance,
    Vector3? ActorToLookAtDelta,
    float? ActorYMinusPresetCharacterY,
    float? ActorYMinusFocusY,
    float? ActorYMinusNativeLookAtY);

internal readonly record struct TitleBackgroundPhase2MSummary(
    string ActorDiagnosticStatus,
    string ActorVisible,
    string ActorGroundAligned,
    string CameraFramesActor,
    string VisualPlacementSafety);

internal static class TitleBackgroundPhase2MPlacementDiagnostic
{
    public static readonly int[] RetainedFrames = [0, 30, 60, 120, 300, 600];

    public static bool ShouldCaptureFrame(int frame)
    {
        return Array.IndexOf(RetainedFrames, frame) >= 0;
    }

    public static TitleBackgroundPhase2MSummary BuildSummary(
        IReadOnlyCollection<TitleBackgroundPhase2MPlacementFrame> frames)
    {
        if (frames.Count == 0)
        {
            return new TitleBackgroundPhase2MSummary(
                "not-run",
                "not-observed",
                "unknown",
                "not-observed",
                "unsafe");
        }

        var ordered = frames.OrderBy(frame => frame.Frame).ToArray();
        var hasSingleActor = ordered.Any(frame => frame.ActorMatchKind == TitleBackgroundPhase2MActorMatchKind.Single);
        var hasAmbiguousActor = ordered.Any(frame => frame.ActorMatchKind == TitleBackgroundPhase2MActorMatchKind.Ambiguous);
        var hasActorObserved = hasSingleActor;
        var hasActorVisibleHint = ordered.Any(frame => frame.ActorMatchKind == TitleBackgroundPhase2MActorMatchKind.Single && frame.Actor?.VisibleHint == true);
        var hasActorCameraDeltas = ordered.Any(frame =>
            frame.ActorMatchKind == TitleBackgroundPhase2MActorMatchKind.Single
            && frame.ActorToCameraDistance.HasValue
            && frame.ActorToLookAtDelta.HasValue);
        var hasGroundUnavailable = ordered.Any(frame => string.Equals(frame.GroundHeightStatus, "unavailable", StringComparison.Ordinal));
        var hasGroundY = ordered.Any(frame => frame.GroundY.HasValue);

        var status = hasSingleActor
            ? "observed"
            : hasAmbiguousActor
                ? "ambiguous"
                : "not-observed";
        var visible = hasActorVisibleHint
            ? "observed"
            : hasActorObserved
                ? "unknown"
                : "not-observed";
        var groundAligned = hasGroundY
            ? "unknown"
            : hasGroundUnavailable
                ? "unknown"
                : "unknown";
        var cameraFramesActor = hasActorCameraDeltas
            ? "observed"
            : hasActorObserved
                ? "unknown"
                : "not-observed";
        var safety = BuildVisualPlacementSafety(status, visible, groundAligned, cameraFramesActor);

        return new TitleBackgroundPhase2MSummary(
            status,
            visible,
            groundAligned,
            cameraFramesActor,
            safety);
    }

    private static string BuildVisualPlacementSafety(
        string actorDiagnosticStatus,
        string actorVisible,
        string actorGroundAligned,
        string cameraFramesActor)
    {
        if (actorDiagnosticStatus is "not-run" or "not-observed" or "ambiguous")
        {
            return "unsafe";
        }

        if (actorVisible == "observed"
            && actorGroundAligned == "observed"
            && cameraFramesActor == "observed")
        {
            return "safe";
        }

        return "unknown";
    }
}

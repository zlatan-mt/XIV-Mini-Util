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
    float? Scale,
    float? HitboxRadius,
    uint? CurrentHp,
    uint? MaxHp,
    bool Targetable,
    string VisibilityHint,
    string SelectableHint,
    string Flags,
    string Customize,
    string Model,
    string DrawObject,
    bool DrawObjectNonNull,
    string ModelLikePointer,
    bool ModelLikeNonNull,
    string SafeReadError,
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
    string CategoryReason,
    int Score,
    string ScoreReason);

internal readonly record struct TitleBackgroundPhase2MSourceDiscovery(
    string Name,
    bool Available,
    int Count,
    int CandidateCount,
    string Error);

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
    IReadOnlyList<TitleBackgroundPhase2MSourceDiscovery> SourceDiscovery,
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
    Vector3 ConfiguredCharacterPosition,
    float ConfiguredCharacterRotation,
    float CurveLow,
    float CurveMid,
    float CurveHigh,
    float? ActorYMinusPresetCharacterY,
    float? ActorYMinusFocusY,
    float? ActorYMinusNativeLookAtY);

internal readonly record struct TitleBackgroundPhase2MSummary(
    string ActorDiagnosticStatus,
    string ActorVisible,
    string ActorGroundAligned,
    string CameraFramesActor,
    string VisualPlacementSafety,
    int ZeroPositionCandidateCount,
    int NonZeroPositionCandidateCount,
    int NamedCandidateCount,
    int VisibleHintTrueCount,
    int DrawObjectNonNullCount,
    int ModelLikeNonNullCount,
    int UniqueAddressCount,
    int UniqueObjectIdCount,
    int UniqueEntityIdCount,
    int SamePositionGroupCount,
    string ObjectTableIndexRange,
    string SourceBreakdown,
    string TransformValidity,
    string IdentityConfidence,
    string StubLikelihood,
    string BestCandidateIndex,
    string BestCandidateReason,
    bool ScoringEnabled,
    int BestScore,
    string BestCandidate,
    bool BestCandidateStableAcrossFrames,
    string Resolution,
    string BestSource,
    string NextNativeSourceToInspect,
    string NextAction,
    string NextActionReason);

public enum TitleBackgroundPhase2MExperimentalApplyMode
{
    None,
    CameraAnchorOnly,
    GeneratedCurvePlusCameraAnchor,
    ActorPlacementPreviewOnly,
    ActorPlacementOneShot,
    VisibilityProbeOnly,
}

internal static class TitleBackgroundPhase2MPlacementDiagnostic
{
    public static readonly int[] RetainedFrames = [0, 1, 2, 4, 8, 16, 30, 60, 120, 300, 600, 900, 1200];

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
                "unsafe",
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                "none",
                "none",
                "unknown",
                "none",
                "unknown",
                "none",
                "no-candidates",
                true,
                0,
                "none",
                false,
                "source-missing",
                "none",
                "CharacterManager",
                "insufficient-data",
                "Phase2M pre-login capture is unavailable");
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
        var candidates = ordered
            .SelectMany(frame => frame.ObjectCandidates)
            .GroupBy(BuildCandidateKey)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .ToArray();
        var summary = BuildCandidateResolution(candidates);
        var sourceDiscovery = ordered
            .SelectMany(frame => frame.SourceDiscovery)
            .GroupBy(source => source.Name)
            .Select(group => group.OrderByDescending(source => source.CandidateCount).ThenByDescending(source => source.Count).First())
            .ToArray();
        var bestSource = sourceDiscovery
            .Where(source => source.Available)
            .OrderByDescending(source => source.CandidateCount)
            .ThenByDescending(source => source.Count)
            .Select(source => source.Name)
            .FirstOrDefault() ?? "none";
        var nextNativeSource = SelectNextNativeSource(summary.Resolution, bestSource, sourceDiscovery);
        var (nextAction, nextActionReason) = SelectNextAction(summary.Resolution, summary.TransformValidity, summary.IdentityConfidence, visible, nextNativeSource);

        return new TitleBackgroundPhase2MSummary(
            status,
            visible,
            groundAligned,
            cameraFramesActor,
            safety,
            summary.ZeroPositionCandidateCount,
            summary.NonZeroPositionCandidateCount,
            summary.NamedCandidateCount,
            summary.VisibleHintTrueCount,
            summary.DrawObjectNonNullCount,
            summary.ModelLikeNonNullCount,
            summary.UniqueAddressCount,
            summary.UniqueObjectIdCount,
            summary.UniqueEntityIdCount,
            summary.SamePositionGroupCount,
            summary.ObjectTableIndexRange,
            summary.SourceBreakdown,
            summary.TransformValidity,
            summary.IdentityConfidence,
            summary.StubLikelihood,
            summary.BestCandidateIndex,
            summary.BestCandidateReason,
            true,
            summary.BestScore,
            summary.BestCandidate,
            summary.BestCandidateStableAcrossFrames,
            summary.Resolution,
            bestSource,
            nextNativeSource,
            nextAction,
            nextActionReason);
    }

    public static string EvaluateExperimentalApply(
        TitleBackgroundPhase2MExperimentalApplyMode mode,
        TitleBackgroundPhase2MSummary summary,
        bool sceneGenerationMatches,
        bool isCharaSelectActive,
        bool isLoggedIn)
    {
        if (mode == TitleBackgroundPhase2MExperimentalApplyMode.None)
        {
            return "skip:none-mode";
        }

        if (isLoggedIn)
        {
            return "skip:logged-in-context";
        }

        if (!isCharaSelectActive)
        {
            return "skip:inactive-chara-select";
        }

        if (!sceneGenerationMatches)
        {
            return "skip:scene-generation-mismatch";
        }

        if (mode == TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot)
        {
            if (summary.Resolution != "single")
            {
                return $"skip:resolution-{summary.Resolution}";
            }

            if (summary.TransformValidity != "valid-world-transform")
            {
                return $"skip:transform-{summary.TransformValidity}";
            }

            if (summary.IdentityConfidence is not "medium" and not "strong")
            {
                return $"skip:identity-{summary.IdentityConfidence}";
            }
        }

        return "ready";
    }

    private static TitleBackgroundPhase2MCandidateResolution BuildCandidateResolution(
        IReadOnlyCollection<TitleBackgroundPhase2MActorCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return TitleBackgroundPhase2MCandidateResolution.Empty("source-missing");
        }

        var zeroCount = candidates.Count(IsZeroPosition);
        var nonZeroCount = candidates.Count - zeroCount;
        var namedCount = candidates.Count(candidate => candidate.Named);
        var visibleTrueCount = candidates.Count(candidate => candidate.VisibleHint);
        var drawObjectCount = candidates.Count(candidate => candidate.DrawObjectNonNull);
        var modelLikeCount = candidates.Count(candidate => candidate.ModelLikeNonNull);
        var uniqueAddressCount = candidates.Where(candidate => candidate.Address != nint.Zero).Select(candidate => candidate.Address).Distinct().Count();
        var uniqueObjectIdCount = candidates.Where(candidate => candidate.GameObjectId != 0).Select(candidate => candidate.GameObjectId).Distinct().Count();
        var uniqueEntityIdCount = candidates.Where(candidate => candidate.EntityId != 0).Select(candidate => candidate.EntityId).Distinct().Count();
        var samePositionGroupCount = candidates.GroupBy(candidate => FormatPositionKey(candidate.Position)).Count(group => group.Count() > 1);
        var objectTableIndexes = candidates
            .Where(candidate => candidate.Source == "ObjectTable")
            .Select(candidate => candidate.ObjectIndex)
            .OrderBy(index => index)
            .ToArray();
        var sourceBreakdown = string.Join(",", candidates
            .GroupBy(candidate => candidate.Source)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}:{group.Count()}"));
        var transformValidity = nonZeroCount == 0
            ? candidates.Count > 0 ? "all-zero-transform" : "unknown"
            : zeroCount > 0
                ? "mixed"
                : "valid-world-transform";
        var stubLikelihood = nonZeroCount == 0 && namedCount == 0 ? "high" : zeroCount > nonZeroCount ? "medium" : "low";
        var identityConfidence = BuildIdentityConfidence(namedCount, drawObjectCount, modelLikeCount, nonZeroCount, uniqueObjectIdCount, uniqueEntityIdCount);
        var best = candidates.OrderByDescending(candidate => candidate.Score).First();
        var resolution = BuildResolution(candidates, transformValidity);

        return new TitleBackgroundPhase2MCandidateResolution(
            zeroCount,
            nonZeroCount,
            namedCount,
            visibleTrueCount,
            drawObjectCount,
            modelLikeCount,
            uniqueAddressCount,
            uniqueObjectIdCount,
            uniqueEntityIdCount,
            samePositionGroupCount,
            objectTableIndexes.Length == 0 ? "none" : $"{objectTableIndexes[0]}..{objectTableIndexes[^1]}",
            string.IsNullOrWhiteSpace(sourceBreakdown) ? "none" : sourceBreakdown,
            transformValidity,
            identityConfidence,
            stubLikelihood,
            best.SourceIndex >= 0 ? best.SourceIndex.ToString() : "none",
            BuildBestCandidateReason(best),
            best.Score,
            $"{best.Source}:{best.SourceIndex}",
            false,
            resolution);
    }

    private static string BuildResolution(IReadOnlyCollection<TitleBackgroundPhase2MActorCandidate> candidates, string transformValidity)
    {
        if (candidates.Count == 0)
        {
            return "source-missing";
        }

        if (candidates.All(IsZeroPosition))
        {
            return "stub-only";
        }

        var strong = candidates
            .Where(candidate => !IsZeroPosition(candidate)
                && candidate.DrawObjectNonNull
                && candidate.VisibleHint)
            .ToArray();
        if (strong.Length == 1)
        {
            return "single";
        }

        if (candidates.Count(candidate => !IsZeroPosition(candidate)) > 1)
        {
            return "ambiguous";
        }

        return transformValidity == "valid-world-transform" ? "single" : "ambiguous";
    }

    private static string BuildIdentityConfidence(int namedCount, int drawObjectCount, int modelLikeCount, int nonZeroCount, int uniqueObjectIdCount, int uniqueEntityIdCount)
    {
        if (namedCount == 0 && drawObjectCount == 0 && modelLikeCount == 0 && nonZeroCount == 0)
        {
            return "none";
        }

        if (nonZeroCount > 0 && (drawObjectCount > 0 || modelLikeCount > 0) && (uniqueObjectIdCount > 0 || uniqueEntityIdCount > 0 || namedCount > 0))
        {
            return "strong";
        }

        if (nonZeroCount > 0 && (drawObjectCount > 0 || modelLikeCount > 0 || namedCount > 0))
        {
            return "medium";
        }

        return "weak";
    }

    private static string BuildBestCandidateReason(TitleBackgroundPhase2MActorCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.ScoreReason)
            ? $"score={candidate.Score}"
            : $"score={candidate.Score};{candidate.ScoreReason}";
    }

    private static string SelectNextNativeSource(string resolution, string bestSource, IReadOnlyCollection<TitleBackgroundPhase2MSourceDiscovery> sources)
    {
        if (resolution == "single")
        {
            return "none";
        }

        if (bestSource != "none" && bestSource != "ObjectTable")
        {
            return bestSource;
        }

        if (sources.Any(source => source.Name == "CharacterManagerObjects" && source.Available))
        {
            return "CharacterManager";
        }

        if (sources.Any(source => source.Name == "PlayerObjects" && source.Available))
        {
            return "ClientObjectManager";
        }

        return "CharaSelectCharacterManager or UIStage CharaSelect model source";
    }

    private static (string Action, string Reason) SelectNextAction(
        string resolution,
        string transformValidity,
        string identityConfidence,
        string actorVisible,
        string nextNativeSource)
    {
        if (resolution == "stub-only")
        {
            return ("inspect-native-source", "ObjectTable candidates are all zero-transform stubs and no valid actor source was resolved");
        }

        if (resolution == "single" && transformValidity == "valid-world-transform" && identityConfidence is "medium" or "strong")
        {
            return actorVisible == "not-observed" || actorVisible == "unknown"
                ? ("enable-visibility-probe", "A valid actor candidate exists but visibility is not confirmed")
                : ("actor-placement-preview", "A valid actor candidate was resolved; preview placement before any write");
        }

        if (resolution == "source-missing")
        {
            return ("inspect-native-source", $"No candidate source resolved; inspect {nextNativeSource}");
        }

        return ("insufficient-data", $"resolution={resolution}; transformValidity={transformValidity}; identityConfidence={identityConfidence}");
    }

    private static bool IsZeroPosition(TitleBackgroundPhase2MActorCandidate candidate)
    {
        return Math.Abs(candidate.Position.X) <= 0.001f
            && Math.Abs(candidate.Position.Y) <= 0.001f
            && Math.Abs(candidate.Position.Z) <= 0.001f;
    }

    private static string BuildCandidateKey(TitleBackgroundPhase2MActorCandidate candidate)
    {
        return candidate.Address != nint.Zero
            ? $"address:{candidate.Address.ToInt64():X}"
            : $"{candidate.Source}:{candidate.SourceIndex}:{candidate.ObjectIndex}:{candidate.GameObjectId:X}:{candidate.EntityId:X}";
    }

    private static string FormatPositionKey(Vector3 position)
    {
        return $"{position.X:0.###},{position.Y:0.###},{position.Z:0.###}";
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

    private readonly record struct TitleBackgroundPhase2MCandidateResolution(
        int ZeroPositionCandidateCount,
        int NonZeroPositionCandidateCount,
        int NamedCandidateCount,
        int VisibleHintTrueCount,
        int DrawObjectNonNullCount,
        int ModelLikeNonNullCount,
        int UniqueAddressCount,
        int UniqueObjectIdCount,
        int UniqueEntityIdCount,
        int SamePositionGroupCount,
        string ObjectTableIndexRange,
        string SourceBreakdown,
        string TransformValidity,
        string IdentityConfidence,
        string StubLikelihood,
        string BestCandidateIndex,
        string BestCandidateReason,
        int BestScore,
        string BestCandidate,
        bool BestCandidateStableAcrossFrames,
        string Resolution)
    {
        public static TitleBackgroundPhase2MCandidateResolution Empty(string resolution)
        {
            return new TitleBackgroundPhase2MCandidateResolution(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "none", "none", "unknown", "none", "unknown",
                "none", "no-candidates", 0, "none", false, resolution);
        }
    }
}

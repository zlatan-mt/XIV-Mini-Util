// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectSourceLayoutRuntimeState.cs
// Description: Elpis の source-backed layout metadata と同一 terrain の world position を run-scoped に保持する。
// Reason: LayoutTerritoryTypeId / LayerFilterKey / Position を推測値や永続値から作らず、
//         OneClick の明示的な source gate を通った値だけを Character Select placement へ渡すため。
using System.Globalization;
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectSourceLayoutSnapshot(
    string CandidateId,
    bool CandidateMatch,
    uint CurrentTerritoryTypeId,
    string CurrentTerritoryPath,
    bool TerritoryMatch,
    bool TerritoryPathMatch,
    bool LayoutReady,
    int LayoutInitState,
    uint LayoutTerritoryTypeId,
    bool LayoutTerritoryMatch,
    uint LayoutLayerFilterKey,
    bool LayerFilterKeyAvailable,
    Vector3 Position,
    bool PositionCaptured,
    bool PositionAuthorized,
    string SourceMode,
    string FailureReason)
{
    public static TitleBackgroundCharaSelectSourceLayoutSnapshot Empty { get; } = new(
        string.Empty,
        false,
        0,
        string.Empty,
        false,
        false,
        false,
        -1,
        0,
        false,
        0,
        false,
        default,
        false,
        false,
        "not-run",
        "not-run");

    public bool Eligible => CandidateMatch
        && TerritoryMatch
        && TerritoryPathMatch
        && LayoutReady
        && LayoutTerritoryMatch
        && LayerFilterKeyAvailable
        && PositionCaptured;
}

internal static class TitleBackgroundCharaSelectSourceLayoutLogic
{
    public const string SameTerrainWorldSource = "same-terrain-world";

    public static TitleBackgroundCharaSelectSourceLayoutSnapshot Evaluate(
        TitleBackgroundCharacterSelectOverrideCandidate candidate,
        uint currentTerritoryTypeId,
        string? currentTerritoryPath,
        bool layoutReady,
        uint layoutTerritoryTypeId,
        uint layoutLayerFilterKey,
        Vector3 position,
        bool selectedCandidateIsElpis = true,
        int layoutInitState = -1)
    {
        var normalizedCurrentPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(currentTerritoryPath);
        var expectedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(candidate.TerritoryPath);
        var candidateMatch = candidate.RequiresSourceBackedLayout
            && string.Equals(
                candidate.Id,
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
                StringComparison.Ordinal)
            && selectedCandidateIsElpis;
        var territoryMatch = currentTerritoryTypeId == candidate.TerritoryId;
        var territoryPathMatch = string.Equals(
            normalizedCurrentPath,
            expectedPath,
            StringComparison.OrdinalIgnoreCase);
        var layoutTerritoryMatch = layoutTerritoryTypeId == candidate.TerritoryId;
        var layerFilterKeyAvailable = IsLayerFilterKeyValidForMatchingTerritory(
            currentTerritoryTypeId,
            layoutTerritoryTypeId,
            layoutLayerFilterKey);
        var effectiveLayoutReady = layoutReady
            && (layoutInitState < 0 || layoutInitState == 7);
        var positionCaptured = TitleBackgroundCameraMath.IsFiniteVector(position);
        var failureReason = ResolveFailureReason(
            candidateMatch,
            territoryMatch,
            territoryPathMatch,
            effectiveLayoutReady,
            layoutTerritoryMatch,
            layerFilterKeyAvailable,
            positionCaptured);

        return new TitleBackgroundCharaSelectSourceLayoutSnapshot(
            candidate.Id ?? string.Empty,
            candidateMatch,
            currentTerritoryTypeId,
            normalizedCurrentPath,
            territoryMatch,
            territoryPathMatch,
            effectiveLayoutReady,
            layoutInitState,
            layoutTerritoryTypeId,
            layoutTerritoryMatch,
            layoutLayerFilterKey,
            layerFilterKeyAvailable,
            position,
            positionCaptured,
            string.Equals(failureReason, "none", StringComparison.Ordinal),
            SameTerrainWorldSource,
            failureReason);
    }

    // Candidate field validation is shared by the runtime gate and QuickCheck input. For a
    // source-backed candidate, LayerFilterKey is intentionally only accepted when captured live.
    public static bool IsCandidateFieldsValid(
        TitleBackgroundCharacterSelectOverrideCandidate candidate,
        string? territoryPath,
        uint territoryTypeId,
        uint layoutTerritoryTypeId,
        uint layoutLayerFilterKey)
    {
        if (!TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(
                TitleBackgroundPathHelper.NormalizeTerritoryPathInput(candidate.TerritoryPath))
            || candidate.TerritoryId == 0
            || (!candidate.RequiresSourceBackedLayout
                && !candidate.ApprovedStaticAnchor.HasValue
                && candidate.LayerFilterKey == 0))
        {
            // approved-static-anchor 候補（FRU 等）は LayerFilterKey=0 を「base load をそのまま使う」意味で
            // 明示採用しており、独立確認済みの curated 定数なのでここで弾かない。
            return false;
        }

        if (!candidate.RequiresSourceBackedLayout)
        {
            // Preserve the existing static-candidate validation semantics. Only the
            // source-backed candidate needs configuration fields rechecked against live data.
            return true;
        }

        var normalizedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
        return TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(normalizedPath)
            && territoryTypeId == candidate.TerritoryId
            && layoutTerritoryTypeId == candidate.TerritoryId
            && IsLayerFilterKeyValidForMatchingTerritory(
                territoryTypeId,
                layoutTerritoryTypeId,
                layoutLayerFilterKey);
    }

    public static bool IsConfiguredForCandidate(
        TitleBackgroundCharacterSelectOverrideCandidate candidate,
        uint layoutTerritoryTypeId,
        uint layoutLayerFilterKey)
    {
        return layoutTerritoryTypeId == candidate.TerritoryId
            && IsLayerFilterKeyValidForMatchingTerritory(
                candidate.TerritoryId,
                layoutTerritoryTypeId,
                layoutLayerFilterKey)
            && (candidate.RequiresSourceBackedLayout || layoutLayerFilterKey == candidate.LayerFilterKey);
    }

    public static bool IsLayerFilterKeyValidForMatchingTerritory(
        uint territoryTypeId,
        uint layoutTerritoryTypeId,
        uint layoutLayerFilterKey)
    {
        return layoutLayerFilterKey != 0
            || (territoryTypeId != 0 && layoutTerritoryTypeId == territoryTypeId);
    }

    public static bool IsRetryableFailureReason(string? failureReason)
    {
        return string.Equals(failureReason, "active-layout-not-ready", StringComparison.Ordinal)
            || string.Equals(failureReason, "source-position-non-finite", StringComparison.Ordinal);
    }

    private static string ResolveFailureReason(
        bool candidateMatch,
        bool territoryMatch,
        bool territoryPathMatch,
        bool layoutReady,
        bool layoutTerritoryMatch,
        bool layerFilterKeyAvailable,
        bool positionCaptured)
    {
        if (!candidateMatch)
        {
            return "candidate-mismatch";
        }

        if (!territoryMatch)
        {
            return "source-territory-mismatch";
        }

        if (!territoryPathMatch)
        {
            return "source-territory-path-mismatch";
        }

        if (!layoutReady)
        {
            return "active-layout-not-ready";
        }

        if (!layoutTerritoryMatch)
        {
            return "source-layout-territory-mismatch";
        }

        if (!layerFilterKeyAvailable)
        {
            return "source-layout-layer-missing";
        }

        return positionCaptured ? "none" : "source-position-non-finite";
    }
}

internal sealed class TitleBackgroundCharaSelectSourceLayoutRuntimeState
{
    public const int SourceCaptureRetryBudget = 60;

    public TitleBackgroundCharaSelectSourceLayoutSnapshot Snapshot { get; private set; } =
        TitleBackgroundCharaSelectSourceLayoutSnapshot.Empty;

    public int SourceCaptureAttemptCount { get; private set; }

    public bool SourceCaptureRetryPending { get; private set; }

    public bool SourceCaptureRetryExhausted { get; private set; }

    public bool HasSnapshot => !string.Equals(Snapshot.SourceMode, "not-run", StringComparison.Ordinal);

    public void Capture(TitleBackgroundCharaSelectSourceLayoutSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void RecordSourceCaptureAttempt()
    {
        SourceCaptureAttemptCount = Math.Min(
            SourceCaptureRetryBudget,
            SourceCaptureAttemptCount + 1);
    }

    public void BeginSourceCaptureRetry()
    {
        SourceCaptureRetryPending = true;
        SourceCaptureRetryExhausted = false;
    }

    public bool PrepareNextSourceCaptureRetry()
    {
        if (!SourceCaptureRetryPending)
        {
            return false;
        }

        if (SourceCaptureAttemptCount >= SourceCaptureRetryBudget)
        {
            SourceCaptureRetryPending = false;
            SourceCaptureRetryExhausted = true;
            return false;
        }

        return true;
    }

    public void CompleteSourceCaptureRetry()
    {
        SourceCaptureRetryPending = false;
    }

    public bool IsUsableFor(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        return candidate.RequiresSourceBackedLayout
            && Snapshot.Eligible
            && string.Equals(Snapshot.CandidateId, candidate.Id, StringComparison.Ordinal);
    }

    public bool TryGetPosition(string candidateId, out Vector3 position)
    {
        if (Snapshot.Eligible
            && string.Equals(Snapshot.CandidateId, candidateId, StringComparison.Ordinal))
        {
            position = Snapshot.Position;
            return true;
        }

        position = default;
        return false;
    }

    public bool TryGetPersistenceMetadata(
        string candidateId,
        out uint layoutTerritoryTypeId,
        out uint layoutLayerFilterKey)
    {
        if (Snapshot.Eligible
            && string.Equals(Snapshot.CandidateId, candidateId, StringComparison.Ordinal))
        {
            layoutTerritoryTypeId = Snapshot.LayoutTerritoryTypeId;
            layoutLayerFilterKey = Snapshot.LayoutLayerFilterKey;
            return true;
        }

        layoutTerritoryTypeId = 0;
        layoutLayerFilterKey = 0;
        return false;
    }

    public void Reset()
    {
        Snapshot = TitleBackgroundCharaSelectSourceLayoutSnapshot.Empty;
        SourceCaptureAttemptCount = 0;
        SourceCaptureRetryPending = false;
        SourceCaptureRetryExhausted = false;
    }

    public IEnumerable<string> BuildDiagnosticLines(
        bool loadedLayoutObserved,
        uint loadedLayoutTerritoryTypeId,
        uint loadedLayoutLayerFilterKey)
    {
        var snapshot = Snapshot;
        var loadedLayoutTerritoryMatch = loadedLayoutObserved
            && snapshot.LayoutTerritoryTypeId != 0
            && loadedLayoutTerritoryTypeId == snapshot.LayoutTerritoryTypeId;
        var loadedLayoutLayerFilterKeyMatch = loadedLayoutObserved
            && loadedLayoutTerritoryMatch
            && snapshot.LayerFilterKeyAvailable
            && (snapshot.LayoutLayerFilterKey == 0
                || loadedLayoutLayerFilterKey == snapshot.LayoutLayerFilterKey);
        yield return $"characterPlace.sourceMode={Normalize(snapshot.SourceMode)}";
        yield return $"characterPlace.sourceCandidateMatch={snapshot.CandidateMatch}";
        yield return $"characterPlace.sourceTerritoryTypeId={snapshot.CurrentTerritoryTypeId}";
        yield return $"characterPlace.sourceTerritoryMatch={snapshot.TerritoryMatch}";
        yield return $"characterPlace.sourceTerritoryPathMatch={snapshot.TerritoryPathMatch}";
        yield return $"characterPlace.sourceLayoutReady={snapshot.LayoutReady}";
        yield return $"characterPlace.sourceLayoutInitState={snapshot.LayoutInitState}";
        yield return $"characterPlace.sourceLayoutTerritoryTypeId={snapshot.LayoutTerritoryTypeId}";
        yield return $"characterPlace.sourceLayoutTerritoryMatch={snapshot.LayoutTerritoryMatch}";
        yield return $"characterPlace.sourceLayoutLayerFilterKey={snapshot.LayoutLayerFilterKey}";
        yield return $"characterPlace.sourceLayerFilterKeyAvailable={snapshot.LayerFilterKeyAvailable}";
        yield return $"characterPlace.sourcePositionCaptured={snapshot.PositionCaptured}";
        yield return $"characterPlace.sourcePositionAuthorized={snapshot.PositionAuthorized}";
        yield return $"characterPlace.sourceTarget={(snapshot.PositionCaptured ? FormatVector(snapshot.Position) : "none")}";
        yield return $"characterPlace.sourceFailureReason={Normalize(snapshot.FailureReason)}";
        yield return $"characterPlace.sourceCaptureAttemptCount={SourceCaptureAttemptCount}";
        yield return $"characterPlace.sourceCaptureRetryBudget={SourceCaptureRetryBudget}";
        yield return $"characterPlace.sourceCaptureRetryPending={SourceCaptureRetryPending}";
        yield return $"characterPlace.sourceCaptureRetryExhausted={SourceCaptureRetryExhausted}";
        yield return $"characterPlace.loadedLayoutTerritoryTypeId={(loadedLayoutObserved ? loadedLayoutTerritoryTypeId : 0)}";
        yield return $"characterPlace.loadedLayoutLayerFilterKey={(loadedLayoutObserved ? loadedLayoutLayerFilterKey : 0)}";
        yield return $"characterPlace.loadedLayoutTerritoryMatch={loadedLayoutTerritoryMatch}";
        yield return $"characterPlace.loadedLayoutLayerFilterKeyMatch={loadedLayoutLayerFilterKeyMatch}";
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X.ToString("0.###", CultureInfo.InvariantCulture)};{value.Y.ToString("0.###", CultureInfo.InvariantCulture)};{value.Z.ToString("0.###", CultureInfo.InvariantCulture)})";
    }
}

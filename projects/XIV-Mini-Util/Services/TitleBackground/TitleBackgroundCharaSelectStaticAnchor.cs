// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectStaticAnchor.cs
// Description: FRU クリア後ステージ candidate の character 配置に使う user-approved static anchor の
//              authorization ロジックと run-scoped snapshot。
// Reason: n4gw scene には character 配置用の layout marker（PositionMarker / PopRange）が存在せず
//         （installed game-data 調査で確認済み）、Elpis の same-terrain capture も FRU territory へは
//         入れないため使えない。座標 (100,0,100) は「FRU アリーナ中心の floor position を、この task で
//         ユーザーが明示承認した static anchor」であり、source-backed marker ではない。third-party
//         preset の Position は使わない。ここでは pre-login CharaSelect で scene/layout identity が
//         期待どおりのときだけ anchor を認可し、いずれかの gate が外れたら fail-closed にする。
using System.Globalization;
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

// pointer を含まない run-scoped snapshot。auto-copy report と runtime gate が同じ値を参照する。
internal readonly record struct TitleBackgroundCharaSelectStaticAnchorSnapshot(
    string CandidateId,
    bool CandidateHasAnchor,
    Vector3 Anchor,
    bool AnchorFinite,
    string Provenance,
    bool PreLogin,
    bool CharaSelectMap,
    bool SceneOverrideApplied,
    string ExpectedScenePath,
    string AppliedScenePath,
    uint ExpectedTerritoryTypeId,
    uint AppliedTerritoryTypeId,
    uint ExpectedLayoutTerritoryTypeId,
    uint LoadedLayoutTerritoryTypeId,
    uint ExpectedLayerFilterKey,
    uint AppliedLayerFilterKey,
    uint LoadedLayerFilterKey,
    bool ActiveLayoutAvailable,
    int LayoutInitState,
    bool LayoutReady,
    int SceneGeneration,
    bool Authorized,
    string AuthorizationReason)
{
    public static TitleBackgroundCharaSelectStaticAnchorSnapshot Empty { get; } = new(
        string.Empty,
        false,
        default,
        false,
        string.Empty,
        false,
        false,
        false,
        string.Empty,
        "none",
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        -1,
        false,
        0,
        false,
        "not-run");
}

internal static class TitleBackgroundCharaSelectStaticAnchorLogic
{
    // source-backed marker と明確に区別する source mode 名。
    public const string ApprovedStaticAnchorSourceMode = "approved-static-anchor";
    public const string SourceBackedSameTerrainMode = "source-backed-same-terrain";
    public const string RunScopedCaptureMode = "run-scoped-capture";
    public const string NoPlacementSourceMode = "none";

    // candidate が anchor を持てば approved-static-anchor、source-backed candidate なら same-terrain、
    // それ以外は run-scoped capture（キャラの現在地）を使う。診断の placementSourceMode に出す。
    public static string ResolvePlacementSourceMode(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        if (candidate.ApprovedStaticAnchor.HasValue)
        {
            return ApprovedStaticAnchorSourceMode;
        }

        if (candidate.RequiresSourceBackedLayout)
        {
            return SourceBackedSameTerrainMode;
        }

        return candidate.BackgroundUsable ? RunScopedCaptureMode : NoPlacementSourceMode;
    }

    public static TitleBackgroundCharaSelectStaticAnchorSnapshot Evaluate(
        TitleBackgroundCharacterSelectOverrideCandidate candidate,
        bool preLogin,
        bool charaSelectMap,
        bool sceneOverrideApplied,
        string? appliedScenePath,
        uint appliedTerritoryTypeId,
        uint appliedLayerFilterKey,
        bool activeLayoutAvailable,
        int layoutInitState,
        uint loadedLayoutTerritoryTypeId,
        uint loadedLayerFilterKey,
        int sceneGeneration)
    {
        var hasAnchor = candidate.ApprovedStaticAnchor.HasValue;
        var anchor = candidate.ApprovedStaticAnchor.GetValueOrDefault();
        var anchorFinite = hasAnchor && TitleBackgroundCameraMath.IsFiniteVector(anchor);
        var expectedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(candidate.TerritoryPath);
        var normalizedApplied = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(appliedScenePath);
        var appliedPathMatch = !string.IsNullOrEmpty(normalizedApplied)
            && string.Equals(normalizedApplied, expectedPath, StringComparison.OrdinalIgnoreCase);
        var appliedTerritoryMatch = appliedTerritoryTypeId == candidate.TerritoryId;
        var appliedLayerMatch = appliedLayerFilterKey == candidate.LayerFilterKey;
        // 明示 layer 指定の候補は、値が 0 でも applied/loaded layer が一致することを要求する
        // （「明示 0」と「override しない」を分離）。非明示の legacy 候補は従来の緩い判定のまま。
        var layerChecked = candidate.LayerFilterKeyExplicit || candidate.LayerFilterKey != 0;
        var layoutReady = activeLayoutAvailable && layoutInitState == 7;
        var loadedLayoutTerritoryMatch = loadedLayoutTerritoryTypeId == candidate.TerritoryId;
        var loadedLayerConsistent = IsLayerFilterConsistent(
            candidate.LayerFilterKey,
            loadedLayerFilterKey,
            loadedLayoutTerritoryMatch,
            candidate.LayerFilterKeyExplicit);

        var reason = ResolveReason(
            hasAnchor,
            anchorFinite,
            preLogin,
            charaSelectMap,
            sceneOverrideApplied,
            appliedPathMatch,
            appliedTerritoryMatch,
            layerChecked && !appliedLayerMatch,
            sceneGeneration > 0,
            activeLayoutAvailable,
            layoutReady,
            loadedLayoutTerritoryMatch,
            loadedLayerConsistent);

        return new TitleBackgroundCharaSelectStaticAnchorSnapshot(
            candidate.Id ?? string.Empty,
            hasAnchor,
            anchor,
            anchorFinite,
            string.IsNullOrWhiteSpace(candidate.StaticAnchorProvenance) ? "none" : candidate.StaticAnchorProvenance,
            preLogin,
            charaSelectMap,
            sceneOverrideApplied,
            expectedPath,
            string.IsNullOrEmpty(normalizedApplied) ? "none" : normalizedApplied,
            candidate.TerritoryId,
            appliedTerritoryTypeId,
            candidate.TerritoryId,
            loadedLayoutTerritoryTypeId,
            candidate.LayerFilterKey,
            appliedLayerFilterKey,
            loadedLayerFilterKey,
            activeLayoutAvailable,
            layoutInitState,
            layoutReady,
            sceneGeneration,
            string.Equals(reason, "authorized", StringComparison.Ordinal),
            reason);
    }

    // 明示 layer 指定（explicit=true）なら値に関わらず loaded layer が厳密一致することを要求する。
    // 非明示で expected==0 のときだけ「base load をそのまま使う」意味とみなし territory 一致で可とする。
    public static bool IsLayerFilterConsistent(
        uint expectedLayerFilterKey,
        uint loadedLayerFilterKey,
        bool loadedLayoutTerritoryMatch,
        bool explicitLayerFilterKey)
    {
        if (explicitLayerFilterKey)
        {
            return loadedLayerFilterKey == expectedLayerFilterKey;
        }

        return expectedLayerFilterKey == 0
            ? loadedLayoutTerritoryMatch
            : loadedLayerFilterKey == expectedLayerFilterKey;
    }

    private static string ResolveReason(
        bool hasAnchor,
        bool anchorFinite,
        bool preLogin,
        bool charaSelectMap,
        bool sceneOverrideApplied,
        bool appliedPathMatch,
        bool appliedTerritoryMatch,
        bool appliedLayerMismatch,
        bool sceneGenerationObserved,
        bool activeLayoutAvailable,
        bool layoutReady,
        bool loadedLayoutTerritoryMatch,
        bool loadedLayerConsistent)
    {
        if (!hasAnchor)
        {
            return "candidate-has-no-anchor";
        }

        if (!anchorFinite)
        {
            return "anchor-non-finite";
        }

        if (!preLogin)
        {
            return "not-pre-login";
        }

        if (!charaSelectMap)
        {
            return "not-chara-select";
        }

        if (!sceneOverrideApplied)
        {
            return "scene-override-not-applied";
        }

        if (!appliedPathMatch)
        {
            return "applied-scene-path-mismatch";
        }

        if (!appliedTerritoryMatch)
        {
            return "applied-territory-mismatch";
        }

        if (appliedLayerMismatch)
        {
            return "applied-layer-filter-mismatch";
        }

        if (!sceneGenerationObserved)
        {
            return "scene-generation-not-observed";
        }

        if (!activeLayoutAvailable)
        {
            return "active-layout-unavailable";
        }

        if (!layoutReady)
        {
            return "active-layout-not-ready";
        }

        if (!loadedLayoutTerritoryMatch)
        {
            return "loaded-layout-territory-mismatch";
        }

        if (!loadedLayerConsistent)
        {
            return "loaded-layer-filter-mismatch";
        }

        return "authorized";
    }
}

// run-scoped holder。Elpis の TitleBackgroundCharaSelectSourceLayoutRuntimeState と同じ lifecycle 規約。
// native pointer は保持しない。値だけを保持する。
internal sealed class TitleBackgroundCharaSelectStaticAnchorRuntimeState
{
    public TitleBackgroundCharaSelectStaticAnchorSnapshot Snapshot { get; private set; } =
        TitleBackgroundCharaSelectStaticAnchorSnapshot.Empty;

    public bool HasSnapshot => !string.Equals(Snapshot.AuthorizationReason, "not-run", StringComparison.Ordinal);

    public void Capture(TitleBackgroundCharaSelectStaticAnchorSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void Reset()
    {
        Snapshot = TitleBackgroundCharaSelectStaticAnchorSnapshot.Empty;
    }

    // 認可済み anchor を返す。candidate id 一致・authorized・finite の全成立が必要（fail-closed）。
    public bool TryGetAuthorizedAnchor(string candidateId, out Vector3 anchor)
    {
        if (Snapshot.Authorized
            && Snapshot.CandidateHasAnchor
            && Snapshot.AnchorFinite
            && string.Equals(Snapshot.CandidateId, candidateId, StringComparison.Ordinal))
        {
            anchor = Snapshot.Anchor;
            return true;
        }

        anchor = default;
        return false;
    }

    public IEnumerable<string> BuildDiagnosticLines(string placementSourceMode)
    {
        var s = Snapshot;
        yield return $"characterPlace.placementSourceMode={Normalize(placementSourceMode)}";
        yield return $"characterPlace.staticAnchorCandidate={Normalize(s.CandidateId)}";
        yield return $"characterPlace.staticAnchorHasAnchor={s.CandidateHasAnchor}";
        yield return $"characterPlace.staticAnchor={(s.CandidateHasAnchor ? FormatVector(s.Anchor) : "none")}";
        yield return $"characterPlace.staticAnchorFinite={s.AnchorFinite}";
        yield return $"characterPlace.staticAnchorProvenance={Normalize(s.Provenance)}";
        yield return $"characterPlace.staticAnchorPreLogin={s.PreLogin}";
        yield return $"characterPlace.staticAnchorCharaSelect={s.CharaSelectMap}";
        yield return $"characterPlace.staticAnchorSceneOverrideApplied={s.SceneOverrideApplied}";
        yield return $"characterPlace.staticAnchorExpectedScenePath={Normalize(s.ExpectedScenePath)}";
        yield return $"characterPlace.staticAnchorAppliedScenePath={Normalize(s.AppliedScenePath)}";
        yield return $"characterPlace.staticAnchorExpectedTerritory={s.ExpectedTerritoryTypeId}";
        yield return $"characterPlace.staticAnchorAppliedTerritory={s.AppliedTerritoryTypeId}";
        yield return $"characterPlace.staticAnchorExpectedLayoutTerritory={s.ExpectedLayoutTerritoryTypeId}";
        yield return $"characterPlace.staticAnchorLoadedLayoutTerritory={s.LoadedLayoutTerritoryTypeId}";
        yield return $"characterPlace.staticAnchorExpectedLayerFilter={s.ExpectedLayerFilterKey}";
        yield return $"characterPlace.staticAnchorAppliedLayerFilter={s.AppliedLayerFilterKey}";
        yield return $"characterPlace.staticAnchorLoadedLayerFilter={s.LoadedLayerFilterKey}";
        yield return $"characterPlace.staticAnchorActiveLayoutAvailable={s.ActiveLayoutAvailable}";
        yield return $"characterPlace.staticAnchorLayoutInitState={s.LayoutInitState}";
        yield return $"characterPlace.staticAnchorLayoutReady={s.LayoutReady}";
        yield return $"characterPlace.staticAnchorSceneGeneration={s.SceneGeneration}";
        yield return $"characterPlace.staticAnchorAuthorized={s.Authorized}";
        yield return $"characterPlace.staticAnchorAuthorizationReason={Normalize(s.AuthorizationReason)}";
        yield return $"characterPlace.staticAnchorFirstFailedGate={(s.Authorized ? "none" : Normalize(s.AuthorizationReason))}";
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

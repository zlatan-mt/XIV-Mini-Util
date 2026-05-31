// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterSelectOverrideCandidateRegistry.cs
// Description: Character Select 背景のみ override 候補のレジストリ
// Reason: custom override target を preset と混同せず、UI/診断/テストで同じ候補情報を使うため
namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharacterSelectOverrideCandidate(
    string Id,
    string DisplayName,
    string TerritoryPath,
    uint TerritoryId,
    uint LayerFilterKey,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    bool BackgroundUsable,
    bool CharacterExpectedVisible,
    bool VerifiedInGame,
    string Warning,
    string KnownIssue,
    string RecommendedAction);

internal static class TitleBackgroundCharacterSelectOverrideCandidateRegistry
{
    public const string DefaultCandidateId = "custom:n4f4";

    private static readonly TitleBackgroundCharacterSelectOverrideCandidate CustomN4F4 = new(
        DefaultCandidateId,
        "Custom n4f4 override target",
        "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        816,
        51,
        TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
        TitleBackgroundCharacterSelectExpectedBrightness.Dark,
        true,
        false,
        true,
        "full scene override works as background-only; selected character is not expected to be visible",
        "selected character model is hidden with full scene override",
        "add-bright-override-candidate or use-background-only");

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> All { get; } =
    [
        CustomN4F4,
    ];

    public static bool TryGet(string? id, out TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var normalizedId = NormalizeId(id);
        foreach (var entry in All)
        {
            if (string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
            {
                candidate = entry;
                return true;
            }
        }

        candidate = default;
        return false;
    }

    public static TitleBackgroundCharacterSelectOverrideCandidate GetDefault()
    {
        return CustomN4F4;
    }

    public static TitleBackgroundCharacterSelectOverrideCandidate ResolveFromConfig(
        string? selectedCandidateId,
        string? overrideTerritoryPath,
        uint overrideTerritoryId,
        uint layerFilterKey)
    {
        var normalizedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(overrideTerritoryPath);
        if (TryGet(selectedCandidateId, out var selected)
            && Matches(selected, normalizedPath, overrideTerritoryId, layerFilterKey))
        {
            return selected;
        }

        foreach (var candidate in All)
        {
            if (Matches(candidate, normalizedPath, overrideTerritoryId, layerFilterKey))
            {
                return candidate;
            }
        }

        return CreateUnknownCustomCandidate(normalizedPath, overrideTerritoryId, layerFilterKey);
    }

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> GetBrightCandidates()
    {
        return GetBrightCandidates(All);
    }

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> GetBrightCandidates(
        IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.BackgroundUsable
                && candidate.ExpectedBrightness is TitleBackgroundCharacterSelectExpectedBrightness.Bright
                    or TitleBackgroundCharacterSelectExpectedBrightness.Normal)
            .ToList();
    }

    public static string BuildBrightLayerCandidateList(IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> candidates)
    {
        var brightCandidates = GetBrightCandidates(candidates);
        return brightCandidates.Count == 0
            ? "none"
            : string.Join(",", brightCandidates.Select(candidate => candidate.Id));
    }

    public static string BuildLightingRecommendedAction(IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> candidates)
    {
        return GetBrightCandidates(candidates).Count == 0
            ? "add-bright-override-candidate"
            : "try-bright-custom-target";
    }

    public static void ApplyToConfiguration(Configuration configuration, TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        configuration.TitleBackgroundSelectedPresetId = string.Empty;
        configuration.TitleBackgroundCharacterSelectOverrideCandidateId = candidate.Id;
        configuration.TitleBackgroundTerritoryPath = candidate.TerritoryPath;
        configuration.TitleBackgroundTerritoryTypeId = candidate.TerritoryId;
        configuration.TitleBackgroundLayoutTerritoryTypeId = candidate.TerritoryId;
        configuration.TitleBackgroundLayoutLayerFilterKey = candidate.LayerFilterKey;
    }

    public static string NormalizeId(string? id)
    {
        return (id ?? string.Empty).Trim();
    }

    public static bool IsDefaultCandidateTarget(string? territoryPath, uint territoryId, uint layerFilterKey)
    {
        return Matches(CustomN4F4, TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath), territoryId, layerFilterKey);
    }

    private static bool Matches(
        TitleBackgroundCharacterSelectOverrideCandidate candidate,
        string normalizedTerritoryPath,
        uint territoryId,
        uint layerFilterKey)
    {
        return string.Equals(candidate.TerritoryPath, normalizedTerritoryPath, StringComparison.OrdinalIgnoreCase)
            && candidate.TerritoryId == territoryId
            && candidate.LayerFilterKey == layerFilterKey;
    }

    private static TitleBackgroundCharacterSelectOverrideCandidate CreateUnknownCustomCandidate(
        string normalizedTerritoryPath,
        uint territoryId,
        uint layerFilterKey)
    {
        return new TitleBackgroundCharacterSelectOverrideCandidate(
            "custom",
            "Custom override target",
            string.IsNullOrWhiteSpace(normalizedTerritoryPath) ? "none" : normalizedTerritoryPath,
            territoryId,
            layerFilterKey,
            TitleBackgroundCharacterSelectCompatibility.Unknown,
            TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
            true,
            false,
            false,
            "custom override target has no Character Select compatibility metadata yet",
            "requires one real-game /xmutbgdiag capture",
            "add-bright-override-candidate");
    }
}

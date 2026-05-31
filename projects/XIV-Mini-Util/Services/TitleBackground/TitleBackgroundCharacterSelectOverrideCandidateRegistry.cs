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
    string Source,
    string Warning,
    string KnownIssue,
    string RecommendedAction);

internal readonly record struct TitleBackgroundCharacterSelectManualCandidateSlot(
    int SlotNumber,
    bool Enabled,
    string Id,
    string DisplayName,
    string TerritoryPath,
    uint TerritoryId,
    uint LayerFilterKey,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    bool Valid,
    string ValidationError);

internal static class TitleBackgroundCharacterSelectOverrideCandidateRegistry
{
    public const string DefaultCandidateId = "custom:n4f4";
    public const string ManualSlot1CandidateId = "manual:slot1";

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
        "registry",
        "full scene override works as background-only; selected character is not expected to be visible",
        "selected character model is hidden with full scene override",
        "add-bright-override-candidate or use-background-only");

    private static readonly TitleBackgroundCharacterSelectOverrideCandidate OldSharlayanOutdoorTest = new(
        "custom:old-sharlayan-k5t1",
        "Old Sharlayan outdoor test",
        "ex4/03_kld_k5/twn/k5t1/level/k5t1",
        962,
        8,
        TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
        TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
        true,
        false,
        false,
        "registry-observed",
        "observed as background-only in Character Select; not yet promoted to verified candidate",
        "selected character model is hidden with full scene override",
        "compare-screenshot-and-promote-if-stable");

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> All { get; } =
    [
        CustomN4F4,
        OldSharlayanOutdoorTest,
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
        return ResolveFromConfig(
            selectedCandidateId,
            overrideTerritoryPath,
            overrideTerritoryId,
            layerFilterKey,
            All);
    }

    public static TitleBackgroundCharacterSelectOverrideCandidate ResolveFromConfig(
        string? selectedCandidateId,
        string? overrideTerritoryPath,
        uint overrideTerritoryId,
        uint layerFilterKey,
        IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> availableCandidates)
    {
        var normalizedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(overrideTerritoryPath);
        if (TryGet(availableCandidates, selectedCandidateId, out var selected)
            && Matches(selected, normalizedPath, overrideTerritoryId, layerFilterKey))
        {
            return selected;
        }

        foreach (var candidate in availableCandidates)
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
            .Where(candidate => (candidate.BackgroundUsable || candidate.Source == "manual")
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
        var brightCandidates = GetBrightCandidates(candidates);
        if (brightCandidates.Any(candidate => candidate.Source == "manual"))
        {
            return "verify-manual-bright-candidate";
        }

        return brightCandidates.Count == 0
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

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> BuildAvailableCandidates(
        IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> manualSlots)
    {
        var candidates = new List<TitleBackgroundCharacterSelectOverrideCandidate>(All);
        foreach (var slot in manualSlots)
        {
            if (TryCreateManualCandidate(slot, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    public static TitleBackgroundCharacterSelectManualCandidateSlot BuildManualSlot(
        int slotNumber,
        bool enabled,
        string? displayName,
        string? territoryPath,
        uint territoryId,
        uint layerFilterKey,
        TitleBackgroundCharacterSelectExpectedBrightness expectedBrightness)
    {
        var id = slotNumber == 1 ? ManualSlot1CandidateId : $"manual:slot{slotNumber}";
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? $"Manual candidate slot {slotNumber}"
            : displayName.Trim();
        var normalizedPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
        var normalizedBrightness = Enum.IsDefined(typeof(TitleBackgroundCharacterSelectExpectedBrightness), expectedBrightness)
            ? expectedBrightness
            : TitleBackgroundCharacterSelectExpectedBrightness.Unknown;

        var validationError = ValidateManualSlot(enabled, normalizedPath, territoryId);
        return new TitleBackgroundCharacterSelectManualCandidateSlot(
            slotNumber,
            enabled,
            id,
            normalizedDisplayName,
            string.IsNullOrWhiteSpace(normalizedPath) ? "none" : normalizedPath,
            territoryId,
            layerFilterKey,
            normalizedBrightness,
            validationError == "none",
            validationError);
    }

    public static bool TryCreateManualCandidate(
        TitleBackgroundCharacterSelectManualCandidateSlot slot,
        out TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        if (!slot.Valid)
        {
            candidate = default;
            return false;
        }

        candidate = new TitleBackgroundCharacterSelectOverrideCandidate(
            slot.Id,
            slot.DisplayName,
            slot.TerritoryPath,
            slot.TerritoryId,
            slot.LayerFilterKey,
            TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
            slot.ExpectedBrightness,
            false,
            false,
            false,
            "manual",
            "manual candidate is unverified; test with /xmutbgdiag and screenshots before promoting",
            "selected character model is hidden with full scene override",
            "verify-with-screenshot-and-xmutbgdiag");
        return true;
    }

    public static bool IsDefaultCandidateTarget(string? territoryPath, uint territoryId, uint layerFilterKey)
    {
        return Matches(CustomN4F4, TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath), territoryId, layerFilterKey);
    }

    private static bool TryGet(
        IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> candidates,
        string? id,
        out TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var normalizedId = NormalizeId(id);
        foreach (var entry in candidates)
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
            "custom-override",
            "custom override target has no Character Select compatibility metadata yet",
            "requires one real-game /xmutbgdiag capture",
            "add-bright-override-candidate");
    }

    private static string ValidateManualSlot(bool enabled, string normalizedPath, uint territoryId)
    {
        if (!enabled)
        {
            return "disabled";
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return "territory-path-empty";
        }

        if (!TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(normalizedPath))
        {
            return "territory-path-invalid";
        }

        if (territoryId == 0)
        {
            return "territory-id-zero";
        }

        return "none";
    }
}

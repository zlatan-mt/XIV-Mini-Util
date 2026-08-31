// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterSelectOverrideCandidateRegistry.cs
// Description: Character Select 背景のみ override 候補のレジストリ
// Reason: custom override target を preset と混同せず、UI/診断/テストで同じ候補情報を使うため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharacterSelectCameraProfile(
    string ProfileId,
    string ProfileSource,
    float? Yaw,
    float? Pitch,
    float? Distance,
    Vector3? LookAt,
    Vector3? Position,
    float YawOffset,
    float PitchOffset,
    float DistanceMultiplier,
    float DistanceOffset,
    Vector3 LookAtOffset,
    Vector3 PositionOffset,
    float CurveLowOffset,
    float CurveMidOffset,
    float CurveHighOffset)
{
    public bool HasProfile => !string.IsNullOrWhiteSpace(ProfileId);

    public static TitleBackgroundCharacterSelectCameraProfile None { get; } = new(
        string.Empty,
        string.Empty,
        null,
        null,
        null,
        null,
        null,
        0f,
        0f,
        1f,
        0f,
        Vector3.Zero,
        Vector3.Zero,
        0f,
        0f,
        0f);
}

internal readonly record struct TitleBackgroundCapturedCameraProfileInput(
    bool LegacyCompositionEnabled,
    TitleBackgroundCharacterVisualStatus CharacterVisualStatus,
    float? DirH,
    float? DirV,
    float? Distance,
    Vector3? Position,
    Vector3? LookAt);

internal readonly record struct TitleBackgroundCapturedCameraProfileResult(
    bool Success,
    string FailureReason,
    string Source,
    float DirH,
    float DirV,
    float Distance,
    Vector3 Position,
    Vector3 LookAt);

internal static class TitleBackgroundCapturedCameraProfileLogic
{
    public const string VisibleLegacySource = "legacy-shooting-composition-visible";

    public static TitleBackgroundCapturedCameraProfileResult Validate(
        TitleBackgroundCapturedCameraProfileInput input)
    {
        if (!input.LegacyCompositionEnabled)
        {
            return Fail("legacy shooting composition is not enabled");
        }

        if (input.CharacterVisualStatus != TitleBackgroundCharacterVisualStatus.Visible)
        {
            return Fail("character not visually confirmed");
        }

        if (!input.DirH.HasValue || !input.DirV.HasValue || !input.Distance.HasValue)
        {
            return Fail("no current camera snapshot");
        }

        if (!float.IsFinite(input.DirH.Value)
            || !float.IsFinite(input.DirV.Value)
            || !float.IsFinite(input.Distance.Value))
        {
            return Fail("current camera values are invalid");
        }

        if (input.Distance.Value <= 0f)
        {
            return Fail("distance is zero");
        }

        if (!input.Position.HasValue
            || !input.LookAt.HasValue
            || !TitleBackgroundCameraMath.IsFiniteVector(input.Position.Value)
            || !TitleBackgroundCameraMath.IsFiniteVector(input.LookAt.Value))
        {
            return Fail("position/lookAt missing");
        }

        return new TitleBackgroundCapturedCameraProfileResult(
            true,
            "none",
            VisibleLegacySource,
            TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(input.DirH.Value),
            Math.Clamp(input.DirV.Value, -MathF.PI / 2f, MathF.PI / 2f),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(input.Distance.Value) ?? input.Distance.Value,
            TitleBackgroundCharaSelectCameraLogic.SanitizeVector(input.Position.Value),
            TitleBackgroundCharaSelectCameraLogic.SanitizeVector(input.LookAt.Value));
    }

    private static TitleBackgroundCapturedCameraProfileResult Fail(string reason)
    {
        return new TitleBackgroundCapturedCameraProfileResult(
            false,
            reason,
            string.Empty,
            0f,
            0f,
            0f,
            Vector3.Zero,
            Vector3.Zero);
    }
}

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
    string RecommendedAction,
    TitleBackgroundCharaSelectCameraFramingMode RecommendedCameraFraming = TitleBackgroundCharaSelectCameraFramingMode.Default,
    bool RequiresSourceBackedLayout = false,
    // user-approved static anchor（source-backed layout marker ではない）。null 以外を持つ候補は
    // OneClick proof 中この座標へキャラを配置する（rotation は既存 canonical stable capture のまま）。
    Vector3? ApprovedStaticAnchor = null,
    string StaticAnchorProvenance = "",
    // LayerFilterKey の値を「明示的に指定した値」として扱うか。true のとき static-anchor authorization は
    // loaded / applied layer が LayerFilterKey と厳密一致することを要求する（0 を「何でも可」にしない）。
    bool LayerFilterKeyExplicit = false);

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
    public const string ElpisCandidateId = "custom:ultima-thule-elpis";
    public const string FruCandidateId = "custom:fru-clear-stage";
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
        "full scene override works as background-only; character may appear visually but camera framing may be top-down",
        "current Y-only framing does not frame character reliably; initial camera is too high / top-down",
        "use n4f4 visible camera profile / CandidateRecommended",
        TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended);

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

    private static readonly TitleBackgroundCharacterSelectOverrideCandidate ElpisFlowerField = new(
        ElpisCandidateId,
        "エルピスの花畑",
        "ex4/04_uvs_u5/fld/u5f2/level/u5f2",
        960,
        0,
        TitleBackgroundCharacterSelectCompatibility.Compatible,
        TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
        true,
        true,
        false,
        "source-backed-same-terrain",
        "active layout and same-terrain position must be captured by OneClick before use",
        "not yet verified in game",
        "verify-with-one-click",
        TitleBackgroundCharaSelectCameraFramingMode.Default,
        true);

    // FRU / 絶もうひとつの未来のクリア後ステージ。territory 1238 / n4gw scene の直接ロード。
    // 定数は installed client / Lumina game-data で独立確認済み（TerritoryType 1238: Name=n4gw,
    // Bg=ex3/01_nvt_n4/goe/n4gw/level/n4gw, WeatherRate slot=1=Clear Skies）。
    // character 配置は user-approved static anchor (100,0,100)（FRU アリーナ中心の floor。
    // n4gw に PositionMarker は存在しないため source-backed marker ではない）。
    // 実機 OneClick で PASS 済み（clear-stage visual / gimmick clutter 解消 / lighting white-out 解消 /
    // anchor + position/rotation readback / suppression stable / pre-login weather=1 & DayTimeSeconds=55020
    // readback 一致 / login-safe / post-login leak none / placement promotion persisted）。
    private static readonly TitleBackgroundCharacterSelectOverrideCandidate FruClearStage = new(
        FruCandidateId,
        "FRU クリア後ステージ",
        "ex3/01_nvt_n4/goe/n4gw/level/n4gw",
        1238,
        0,
        TitleBackgroundCharacterSelectCompatibility.Compatible,
        TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
        true,
        true,
        true,
        "approved-static-anchor",
        "character placement uses a user-approved static anchor (FRU arena centre floor); not a source-backed layout marker; third-party preset Position is not used; verified in game",
        "none",
        "none",
        TitleBackgroundCharaSelectCameraFramingMode.Default,
        false,
        new Vector3(100f, 0f, 100f),
        "installed n4gw MapRange centre (100,0,100) adopted as task-approved static anchor for FRU arena floor; MapRange itself is not treated as a character marker; FRU.json Position not used",
        LayerFilterKeyExplicit: true);

    public static IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> All { get; } =
    [
        CustomN4F4,
        OldSharlayanOutdoorTest,
        ElpisFlowerField,
        FruClearStage,
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

    public static bool TryGetRecommendedCameraProfile(
        string? candidateId,
        TitleBackgroundCharaSelectCameraFramingMode framingMode,
        out TitleBackgroundCharacterSelectCameraProfile profile)
    {
        return TryGetPreferredCameraProfile(
            candidateId,
            framingMode,
            false,
            null,
            null,
            null,
            null,
            null,
            out profile);
    }

    public static bool TryGetPreferredCameraProfile(
        string? candidateId,
        TitleBackgroundCharaSelectCameraFramingMode framingMode,
        bool capturedProfileEnabled,
        float? capturedDirH,
        float? capturedDirV,
        float? capturedDistance,
        Vector3? capturedPosition,
        Vector3? capturedLookAt,
        out TitleBackgroundCharacterSelectCameraProfile profile)
    {
        var normalizedId = NormalizeId(candidateId);
        if (string.Equals(normalizedId, DefaultCandidateId, StringComparison.Ordinal)
            && framingMode is TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
                or TitleBackgroundCharaSelectCameraFramingMode.CustomExperimental)
        {
            if (capturedProfileEnabled
                && capturedDirH.HasValue
                && capturedDirV.HasValue
                && capturedDistance.HasValue
                && capturedDistance.Value > 0f)
            {
                profile = new TitleBackgroundCharacterSelectCameraProfile(
                    "n4f4-visible-captured",
                    "captured",
                    TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(capturedDirH.Value),
                    Math.Clamp(capturedDirV.Value, -MathF.PI / 2f, MathF.PI / 2f),
                    TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(capturedDistance.Value),
                    capturedLookAt,
                    capturedPosition,
                    0f,
                    0f,
                    1f,
                    0f,
                    Vector3.Zero,
                    Vector3.Zero,
                    -0.7f,
                    -0.7f,
                    -0.7f);
                return true;
            }

            profile = new TitleBackgroundCharacterSelectCameraProfile(
                "n4f4-visible",
                "candidate",
                null,
                null,
                null,
                null,
                null,
                0f,
                -0.18f,
                0.72f,
                0f,
                new Vector3(0f, -0.7f, 0f),
                Vector3.Zero,
                -0.7f,
                -0.7f,
                -0.7f);
            return true;
        }

        profile = TitleBackgroundCharacterSelectCameraProfile.None;
        return false;
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
            && (candidate.RequiresSourceBackedLayout || candidate.LayerFilterKey == layerFilterKey);
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

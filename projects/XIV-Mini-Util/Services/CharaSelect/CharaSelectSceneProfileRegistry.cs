// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectSceneProfileRegistry.cs
// Description: キャラ選択画面の撮影構成 profile を管理する
// Reason: 背景だけの scene override ではなく、選択キャラを残す CharaSelect route の候補をUI/診断/テストで共有するため
namespace XivMiniUtil.Services.CharaSelect;

public enum CharaSelectBrightnessRating
{
    Unknown,
    Dark,
    Acceptable,
    Bright,
    TooBright,
}

public enum CharaSelectScenePlacementMode
{
    Disabled,
    ObserveOnly,
    OneShotAfterDisplay,
}

public enum CharaSelectStageStrategy
{
    Disabled,
    ObserveOnly,
    ClientSelectDataTerritoryPatch,
    LobbyPositionPatch,
    LobbySheetResolvedPatch,
    LayoutPrefetchOnly,
    TitleBackgroundFullSceneFallback,
}

public enum CharaSelectSceneBinaryResult
{
    Unknown,
    Yes,
    No,
}

public enum CharaSelectSceneBrightnessResult
{
    Unknown,
    Dark,
    Acceptable,
    Bright,
}

public readonly record struct CharaSelectSceneProfile(
    string Id,
    string DisplayName,
    ushort TerritoryTypeId,
    string TerritoryPath,
    uint LayerFilterKey,
    float? CharacterX,
    float? CharacterY,
    float? CharacterZ,
    float? CharacterRotation,
    uint? EmoteId,
    CharaSelectBrightnessRating ExpectedBrightness,
    bool VerifiedInGame,
    string Source,
    bool CharacterExpectedVisible,
    string Warning,
    string RecommendedAction);

internal static class CharaSelectSceneProfileRegistry
{
    public const string DefaultProfileId = "scene:old-sharlayan-k5t1";

    private static readonly CharaSelectSceneProfile OldSharlayanOutdoorTest = new(
        DefaultProfileId,
        "Old Sharlayan outdoor test",
        962,
        "ex4/03_kld_k5/twn/k5t1/level/k5t1",
        8,
        null,
        null,
        null,
        null,
        null,
        CharaSelectBrightnessRating.Unknown,
        false,
        "observed",
        true,
        "background observed; character visibility and emote playback still require screenshot verification",
        "verify-character-visible-and-emote");

    public static IReadOnlyList<CharaSelectSceneProfile> All { get; } =
    [
        OldSharlayanOutdoorTest,
    ];

    public static CharaSelectSceneProfile GetDefault()
    {
        return OldSharlayanOutdoorTest;
    }

    public static bool TryGet(string? id, out CharaSelectSceneProfile profile)
    {
        var normalizedId = NormalizeId(id);
        foreach (var entry in All)
        {
            if (string.Equals(entry.Id, normalizedId, StringComparison.Ordinal))
            {
                profile = entry;
                return true;
            }
        }

        profile = default;
        return false;
    }

    public static CharaSelectSceneProfile Resolve(string? id)
    {
        return TryGet(id, out var profile) ? profile : GetDefault();
    }

    public static string NormalizeId(string? id)
    {
        var normalized = (id ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? DefaultProfileId : normalized;
    }

    public static string BuildLabel(CharaSelectSceneProfile profile)
    {
        var verified = profile.VerifiedInGame ? "Verified" : "Observed / Unverified";
        return $"{profile.DisplayName} [{verified} / {profile.ExpectedBrightness}]";
    }
}

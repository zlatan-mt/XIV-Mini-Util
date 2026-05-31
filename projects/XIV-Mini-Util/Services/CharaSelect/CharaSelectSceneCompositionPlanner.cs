// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectSceneCompositionPlanner.cs
// Description: CharaSelect scene profile を既存設定へ安全に反映する純粋ロジック
// Reason: runtime hook に触れずに profile 適用・診断・安全境界をテストするため
namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectSceneCompositionDiagnostic(
    bool Enabled,
    string ProfileId,
    string ProfileName,
    string Route,
    bool ExpectedCharacterVisible,
    string CharacterObserved,
    bool TerritoryOverrideEnabled,
    ushort TerritoryOverrideTerritoryTypeId,
    bool EmoteEnabled,
    string EmoteCurrent,
    CharaSelectScenePlacementMode PlacementMode,
    string NextAction);

internal static class CharaSelectSceneCompositionPlanner
{
    public const string ForegroundPreservingRoute = "foreground-preserving";

    public static CharaSelectSceneProfile ResolveProfile(Configuration configuration)
    {
        return CharaSelectSceneProfileRegistry.Resolve(configuration.CharaSelectSceneProfileId);
    }

    public static bool ShouldApplyRuntime(bool isLoggedIn, bool agentIsLoggedIn, bool enabled)
    {
        return enabled && !isLoggedIn && !agentIsLoggedIn;
    }

    public static void ApplyProfileToConfiguration(Configuration configuration, CharaSelectSceneProfile profile)
    {
        configuration.CharaSelectSceneProfileId = profile.Id;
        configuration.CharaSelectSceneExpectedBrightness = profile.ExpectedBrightness;

        if (configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseProfileTerritory)
        {
            configuration.CharaSelectOverrideTerritoryEnabled = true;
            configuration.CharaSelectOverrideTerritoryTypeId = profile.TerritoryTypeId;
        }

        if (configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseProfilePosition
            && profile.CharacterX.HasValue
            && profile.CharacterY.HasValue
            && profile.CharacterZ.HasValue)
        {
            configuration.CharaSelectOverridePositionEnabled = true;
            configuration.CharaSelectOverridePositionX = SanitizeCoordinate(profile.CharacterX.Value);
            configuration.CharaSelectOverridePositionY = SanitizeCoordinate(profile.CharacterY.Value);
            configuration.CharaSelectOverridePositionZ = SanitizeCoordinate(profile.CharacterZ.Value);
        }

        if (configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseSavedEmote)
        {
            configuration.CharaSelectEmoteEnabled = true;
        }
    }

    public static CharaSelectSceneCompositionDiagnostic BuildDiagnostic(
        Configuration configuration,
        string currentEmoteDisplayName,
        string characterObserved = "Unknown")
    {
        var profile = ResolveProfile(configuration);
        var territoryOverrideEnabled = configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseProfileTerritory
            && configuration.CharaSelectOverrideTerritoryEnabled
            && configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId;
        var emoteEnabled = configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseSavedEmote
            && configuration.CharaSelectEmoteEnabled;
        var nextAction = configuration.CharaSelectSceneCompositionEnabled
            ? "verify-character-visible-background-and-emote-with-screenshot"
            : "enable-scene-composition-and-select-profile";

        return new CharaSelectSceneCompositionDiagnostic(
            configuration.CharaSelectSceneCompositionEnabled,
            profile.Id,
            profile.DisplayName,
            ForegroundPreservingRoute,
            profile.CharacterExpectedVisible,
            characterObserved,
            territoryOverrideEnabled,
            territoryOverrideEnabled ? configuration.CharaSelectOverrideTerritoryTypeId : (ushort)0,
            emoteEnabled,
            string.IsNullOrWhiteSpace(currentEmoteDisplayName) ? "none" : currentEmoteDisplayName,
            configuration.CharaSelectScenePlacementMode,
            nextAction);
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(CharaSelectSceneCompositionDiagnostic diagnostic)
    {
        return
        [
            $"charaSelectScene.enabled={diagnostic.Enabled}",
            $"charaSelectScene.profileId={diagnostic.ProfileId}",
            $"charaSelectScene.profileName={diagnostic.ProfileName}",
            $"charaSelectScene.route={diagnostic.Route}",
            $"charaSelectScene.expectedCharacterVisible={diagnostic.ExpectedCharacterVisible}",
            $"charaSelectScene.characterObserved={diagnostic.CharacterObserved}",
            $"charaSelectScene.territoryOverride.enabled={diagnostic.TerritoryOverrideEnabled}",
            $"charaSelectScene.territoryOverride.territoryTypeId={diagnostic.TerritoryOverrideTerritoryTypeId}",
            $"charaSelectScene.emote.enabled={diagnostic.EmoteEnabled}",
            $"charaSelectScene.emote.current={diagnostic.EmoteCurrent}",
            $"charaSelectScene.placement.mode={diagnostic.PlacementMode}",
            $"charaSelectScene.nextAction={diagnostic.NextAction}",
        ];
    }

    private static float SanitizeCoordinate(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }
}

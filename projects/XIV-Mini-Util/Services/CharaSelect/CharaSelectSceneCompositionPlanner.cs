// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectSceneCompositionPlanner.cs
// Description: CharaSelect scene profile を既存設定へ安全に反映する純粋ロジック
// Reason: runtime hook に触れずに profile 適用・診断・安全境界をテストするため
namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectSceneCompositionDiagnostic(
    bool Enabled,
    bool TitleBackgroundConflict,
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
    CharaSelectSceneBinaryResult CharacterVisibleResult,
    CharaSelectSceneBinaryResult LocationChangedResult,
    CharaSelectSceneBinaryResult EmotePlayedResult,
    CharaSelectSceneBrightnessResult BrightnessResult,
    bool LastObservationAvailable,
    bool LastObservationCharacterPointerResolved,
    ulong LastObservationContentId,
    int LastObservationSelectedIndex,
    string LastObservationProfileId,
    string LastObservationProfileName,
    bool LastObservationTerritoryOverrideApplied,
    bool LastObservationEmoteReplayAttempted,
    bool LastObservationEmoteReplayApplied,
    bool LastObservationTitleBackgroundConflictDetected,
    string LastObservationObservedAt,
    string HumanVerdict,
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
            configuration.TitleBackgroundOverrideEnabled = false;
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

    public static void SetFinalCompositionEnabled(Configuration configuration, bool enabled)
    {
        configuration.CharaSelectSceneCompositionEnabled = enabled;
        if (enabled)
        {
            configuration.TitleBackgroundOverrideEnabled = false;
        }
    }

    public static void SetTitleBackgroundRouteEnabled(Configuration configuration, bool enabled)
    {
        configuration.TitleBackgroundOverrideEnabled = enabled;
        if (enabled)
        {
            configuration.CharaSelectSceneCompositionEnabled = false;
        }
    }

    public static CharaSelectSceneCompositionDiagnostic BuildDiagnostic(
        Configuration configuration,
        string currentEmoteDisplayName,
        string characterObserved = "Unknown",
        CharaSelectSceneLastObservation? lastObservation = null)
    {
        var profile = ResolveProfile(configuration);
        var titleBackgroundConflict = configuration.CharaSelectSceneCompositionEnabled
            && configuration.TitleBackgroundOverrideEnabled;
        var territoryOverrideEnabled = configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseProfileTerritory
            && configuration.CharaSelectOverrideTerritoryEnabled
            && configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId;
        var emoteEnabled = configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseSavedEmote
            && configuration.CharaSelectEmoteEnabled;
        var observation = lastObservation ?? CharaSelectSceneLastObservation.Empty;
        var nextAction = BuildNextAction(configuration);
        var humanVerdict = BuildHumanVerdict(configuration);

        return new CharaSelectSceneCompositionDiagnostic(
            configuration.CharaSelectSceneCompositionEnabled,
            titleBackgroundConflict,
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
            configuration.LastSceneProfileCharacterVisibleResult,
            configuration.LastSceneProfileLocationChangedResult,
            configuration.LastSceneProfileEmotePlayedResult,
            configuration.LastSceneProfileBrightnessResult,
            observation.Available,
            observation.CharacterPointerResolved,
            observation.ContentId,
            observation.SelectedIndex,
            observation.ProfileId,
            observation.ProfileName,
            observation.TerritoryOverrideApplied,
            observation.EmoteReplayAttempted,
            observation.EmoteReplayApplied,
            observation.TitleBackgroundConflictDetected,
            observation.ObservedAt,
            humanVerdict,
            nextAction);
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(CharaSelectSceneCompositionDiagnostic diagnostic)
    {
        return
        [
            $"charaSelectScene.enabled={diagnostic.Enabled}",
            $"charaSelectScene.finalMode.enabled={diagnostic.Enabled}",
            $"charaSelectScene.finalMode.titleBackgroundConflict={diagnostic.TitleBackgroundConflict}",
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
            $"charaSelectScene.manual.characterVisible={diagnostic.CharacterVisibleResult}",
            $"charaSelectScene.manual.locationChanged={diagnostic.LocationChangedResult}",
            $"charaSelectScene.manual.emotePlayed={diagnostic.EmotePlayedResult}",
            $"charaSelectScene.manual.brightness={diagnostic.BrightnessResult}",
            $"charaSelectScene.lastObservation.available={diagnostic.LastObservationAvailable}",
            $"charaSelectScene.lastObservation.characterPointerResolved={diagnostic.LastObservationCharacterPointerResolved}",
            $"charaSelectScene.lastObservation.contentId={diagnostic.LastObservationContentId}",
            $"charaSelectScene.lastObservation.selectedIndex={diagnostic.LastObservationSelectedIndex}",
            $"charaSelectScene.lastObservation.profileId={diagnostic.LastObservationProfileId}",
            $"charaSelectScene.lastObservation.profileName={diagnostic.LastObservationProfileName}",
            $"charaSelectScene.lastObservation.territoryOverrideApplied={diagnostic.LastObservationTerritoryOverrideApplied}",
            $"charaSelectScene.lastObservation.emoteReplayAttempted={diagnostic.LastObservationEmoteReplayAttempted}",
            $"charaSelectScene.lastObservation.emoteReplayApplied={diagnostic.LastObservationEmoteReplayApplied}",
            $"charaSelectScene.lastObservation.titleBackgroundConflictDetected={diagnostic.LastObservationTitleBackgroundConflictDetected}",
            $"charaSelectScene.lastObservation.observedAt={diagnostic.LastObservationObservedAt}",
            $"charaSelectScene.lastObservation.humanVerdict={diagnostic.HumanVerdict}",
            $"charaSelectScene.nextAction={diagnostic.NextAction}",
        ];
    }

    public static string BuildNextAction(Configuration configuration)
    {
        if (!configuration.CharaSelectSceneCompositionEnabled)
        {
            return "enable-scene-composition-and-select-profile";
        }

        if (configuration.TitleBackgroundOverrideEnabled)
        {
            return "disable-title-background-route-and-verify-foreground";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No)
        {
            return "disable-title-background-route-and-verify-foreground";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "fix-territory-display-patch-route";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.No)
        {
            return "fix-emote-replay-route";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "implement-one-shot-placement";
        }

        return "verify-character-visible-background-and-emote-with-screenshot";
    }

    private static string BuildHumanVerdict(Configuration configuration)
    {
        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No)
        {
            return "character-not-visible-screenshot";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "character-visible-location-changed-emote-played";
        }

        return "unconfirmed";
    }

    private static float SanitizeCoordinate(float value)
    {
        return float.IsFinite(value) ? value : 0f;
    }
}

internal readonly record struct CharaSelectSceneLastObservation(
    bool Available,
    bool CharacterPointerResolved,
    ulong ContentId,
    int SelectedIndex,
    string ProfileId,
    string ProfileName,
    bool TerritoryOverrideApplied,
    bool EmoteReplayAttempted,
    bool EmoteReplayApplied,
    bool TitleBackgroundConflictDetected,
    string ObservedAt)
{
    public static CharaSelectSceneLastObservation Empty { get; } = new(
        false,
        false,
        0,
        -1,
        "none",
        "none",
        false,
        false,
        false,
        false,
        "none");
}

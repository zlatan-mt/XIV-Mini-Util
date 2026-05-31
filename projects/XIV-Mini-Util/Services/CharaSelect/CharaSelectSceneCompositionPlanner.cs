// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectSceneCompositionPlanner.cs
// Description: CharaSelect scene profile を既存設定へ安全に反映する純粋ロジック
// Reason: runtime hook に触れずに profile 適用・診断・安全境界をテストするため
namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectVisualLocationDiagnostic(
    string ExpectedProfileId,
    string ExpectedProfileName,
    ushort ExpectedTerritoryTypeId,
    string ExpectedTerritoryPath,
    string CurrentRoute,
    bool CurrentRouteApplied,
    string ManualResult,
    string RouteVerdict,
    string NextAction);

internal readonly record struct CharaSelectStageStrategyDiagnostic(
    CharaSelectStageStrategy Selected,
    bool Available,
    bool Applied,
    string Reason,
    bool ExpectedCharacterVisible,
    bool RequiresScreenshot);

internal readonly record struct CharaSelectStageProbeSnapshot(
    bool Available,
    string Reason,
    string ObservedAt,
    int SelectedIndex,
    int NormalizedIndex,
    short WorldIndex,
    ulong ContentId,
    bool CharacterPointerResolved,
    ushort ClientSelectDataOriginalTerritoryType,
    ushort ClientSelectDataOriginalZoneId,
    ushort ClientSelectDataPatchedTerritoryType,
    ushort ClientSelectDataPatchedZoneId,
    bool ClientSelectDataPatchAttempted,
    bool ClientSelectDataPatchApplied,
    bool ClientSelectDataRestoreApplied,
    string ClientSelectDataVerdict,
    int LobbyPositionFallback,
    int LobbyPositionResolved,
    bool LobbyPositionChanged,
    int LastOverrideLoginPosition,
    string LobbyPositionVerdict,
    bool LobbySheetAvailable,
    int LobbySheetMatchCount,
    uint LobbySheetCandidate0RowId,
    uint LobbySheetCandidate0Type,
    uint LobbySheetCandidate0Param,
    uint LobbySheetCandidate0Link,
    string LobbySheetResolvedReason,
    bool LayoutPrefetchRequested,
    string LayoutPrefetchBg,
    uint LayoutPrefetchLevelId,
    byte LayoutPrefetchLayerEntryType,
    string LayoutPrefetchOwner,
    string LayoutPrefetchVerdict,
    bool TitleBackgroundRouteEnabled,
    bool TitleBackgroundRouteActive,
    string TitleBackgroundRouteVerdict,
    string RouteVerdict,
    string NextAction)
{
    public static CharaSelectStageProbeSnapshot Empty { get; } = new(
        false,
        "not-in-chara-select",
        "none",
        -1,
        -1,
        0,
        0,
        false,
        0,
        0,
        0,
        0,
        false,
        false,
        false,
        "not-visible-stage-source",
        0,
        0,
        false,
        0,
        "not-available",
        false,
        0,
        0,
        0,
        0,
        0,
        "not-run",
        false,
        "none",
        0,
        0,
        "None",
        "not-loaded",
        false,
        false,
        "disabled-for-final-composition",
        "source-not-resolved",
        "select-stage-strategy");
}

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
    CharaSelectStageStrategy StageStrategy,
    CharaSelectSceneBinaryResult CharacterVisibleResult,
    CharaSelectSceneBinaryResult LocationChangedResult,
    CharaSelectSceneBinaryResult EmotePlayedResult,
    CharaSelectSceneBrightnessResult BrightnessResult,
    CharaSelectVisualLocationDiagnostic VisualLocation,
    CharaSelectStageStrategyDiagnostic StageStrategyDiagnostic,
    CharaSelectStageProbeSnapshot StageProbe,
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
    public const string ClientSelectDataTerritoryPatchRoute = "ClientSelectDataTerritoryPatch";

    public static CharaSelectSceneProfile ResolveProfile(Configuration configuration)
    {
        return CharaSelectSceneProfileRegistry.Resolve(configuration.CharaSelectSceneProfileId);
    }

    public static bool ShouldApplyRuntime(bool isLoggedIn, bool agentIsLoggedIn, bool enabled)
    {
        return enabled && !isLoggedIn && !agentIsLoggedIn;
    }

    public static bool UsesClientSelectDataTerritoryPatch(Configuration configuration)
    {
        return configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneStageStrategy == CharaSelectStageStrategy.ClientSelectDataTerritoryPatch
            && configuration.CharaSelectSceneUseProfileTerritory;
    }

    public static void ApplyProfileToConfiguration(Configuration configuration, CharaSelectSceneProfile profile)
    {
        configuration.CharaSelectSceneProfileId = profile.Id;
        configuration.CharaSelectSceneExpectedBrightness = profile.ExpectedBrightness;

        if (configuration.CharaSelectSceneCompositionEnabled)
        {
            configuration.TitleBackgroundOverrideEnabled = false;
        }

        if (UsesClientSelectDataTerritoryPatch(configuration))
        {
            configuration.CharaSelectOverrideTerritoryEnabled = true;
            configuration.CharaSelectOverrideTerritoryTypeId = profile.TerritoryTypeId;
        }
        else if (configuration.CharaSelectOverrideTerritoryEnabled
                 && configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId)
        {
            configuration.CharaSelectOverrideTerritoryEnabled = false;
            configuration.CharaSelectOverrideTerritoryTypeId = 0;
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
        var territoryOverrideEnabled = UsesClientSelectDataTerritoryPatch(configuration)
            && configuration.CharaSelectOverrideTerritoryEnabled
            && configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId;
        var emoteEnabled = configuration.CharaSelectSceneCompositionEnabled
            && configuration.CharaSelectSceneUseSavedEmote
            && configuration.CharaSelectEmoteEnabled;
        var observation = lastObservation ?? CharaSelectSceneLastObservation.Empty;
        var stageProbe = observation.StageProbe.Available ? observation.StageProbe : CharaSelectStageProbeSnapshot.Empty;
        var visualLocation = BuildVisualLocationDiagnostic(configuration, profile, territoryOverrideEnabled, observation);
        var stageStrategyDiagnostic = BuildStageStrategyDiagnostic(configuration, profile, visualLocation, stageProbe);
        var nextAction = BuildNextAction(configuration, observation);
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
            configuration.CharaSelectSceneStageStrategy,
            configuration.LastSceneProfileCharacterVisibleResult,
            configuration.LastSceneProfileLocationChangedResult,
            configuration.LastSceneProfileEmotePlayedResult,
            configuration.LastSceneProfileBrightnessResult,
            visualLocation,
            stageStrategyDiagnostic,
            stageProbe,
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
            $"charaSelectScene.stageStrategy={diagnostic.StageStrategy}",
            $"charaSelectScene.manual.characterVisible={diagnostic.CharacterVisibleResult}",
            $"charaSelectScene.manual.locationChanged={diagnostic.LocationChangedResult}",
            $"charaSelectScene.manual.emotePlayed={diagnostic.EmotePlayedResult}",
            $"charaSelectScene.manual.brightness={diagnostic.BrightnessResult}",
            $"charaSelectScene.visualLocation.expectedProfileId={diagnostic.VisualLocation.ExpectedProfileId}",
            $"charaSelectScene.visualLocation.expectedProfileName={diagnostic.VisualLocation.ExpectedProfileName}",
            $"charaSelectScene.visualLocation.expectedTerritoryTypeId={diagnostic.VisualLocation.ExpectedTerritoryTypeId}",
            $"charaSelectScene.visualLocation.expectedTerritoryPath={diagnostic.VisualLocation.ExpectedTerritoryPath}",
            $"charaSelectScene.visualLocation.currentRoute={diagnostic.VisualLocation.CurrentRoute}",
            $"charaSelectScene.visualLocation.currentRouteApplied={diagnostic.VisualLocation.CurrentRouteApplied}",
            $"charaSelectScene.visualLocation.manualResult={diagnostic.VisualLocation.ManualResult}",
            $"charaSelectScene.visualLocation.routeVerdict={diagnostic.VisualLocation.RouteVerdict}",
            $"charaSelectScene.visualLocation.nextAction={diagnostic.VisualLocation.NextAction}",
            $"charaSelectStageStrategy.selected={diagnostic.StageStrategyDiagnostic.Selected}",
            $"charaSelectStageStrategy.available={diagnostic.StageStrategyDiagnostic.Available}",
            $"charaSelectStageStrategy.applied={diagnostic.StageStrategyDiagnostic.Applied}",
            $"charaSelectStageStrategy.reason={diagnostic.StageStrategyDiagnostic.Reason}",
            $"charaSelectStageStrategy.expectedCharacterVisible={diagnostic.StageStrategyDiagnostic.ExpectedCharacterVisible}",
            $"charaSelectStageStrategy.requiresScreenshot={diagnostic.StageStrategyDiagnostic.RequiresScreenshot}",
            $"charaSelectStageProbe.available={diagnostic.StageProbe.Available}",
            $"charaSelectStageProbe.reason={diagnostic.StageProbe.Reason}",
            $"charaSelectStageProbe.observedAt={diagnostic.StageProbe.ObservedAt}",
            $"charaSelectStageProbe.selectedIndex={diagnostic.StageProbe.SelectedIndex}",
            $"charaSelectStageProbe.normalizedIndex={diagnostic.StageProbe.NormalizedIndex}",
            $"charaSelectStageProbe.worldIndex={diagnostic.StageProbe.WorldIndex}",
            $"charaSelectStageProbe.contentId={diagnostic.StageProbe.ContentId}",
            $"charaSelectStageProbe.characterPointerResolved={diagnostic.StageProbe.CharacterPointerResolved}",
            $"charaSelectStageProbe.clientSelectData.originalTerritoryType={diagnostic.StageProbe.ClientSelectDataOriginalTerritoryType}",
            $"charaSelectStageProbe.clientSelectData.originalZoneId={diagnostic.StageProbe.ClientSelectDataOriginalZoneId}",
            $"charaSelectStageProbe.clientSelectData.patchedTerritoryType={diagnostic.StageProbe.ClientSelectDataPatchedTerritoryType}",
            $"charaSelectStageProbe.clientSelectData.patchedZoneId={diagnostic.StageProbe.ClientSelectDataPatchedZoneId}",
            $"charaSelectStageProbe.clientSelectData.patchAttempted={diagnostic.StageProbe.ClientSelectDataPatchAttempted}",
            $"charaSelectStageProbe.clientSelectData.patchApplied={diagnostic.StageProbe.ClientSelectDataPatchApplied}",
            $"charaSelectStageProbe.clientSelectData.restoreApplied={diagnostic.StageProbe.ClientSelectDataRestoreApplied}",
            $"charaSelectStageProbe.clientSelectData.verdict={diagnostic.StageProbe.ClientSelectDataVerdict}",
            $"charaSelectStageProbe.lobbyPosition.fallback={diagnostic.StageProbe.LobbyPositionFallback}",
            $"charaSelectStageProbe.lobbyPosition.resolved={diagnostic.StageProbe.LobbyPositionResolved}",
            $"charaSelectStageProbe.lobbyPosition.changed={diagnostic.StageProbe.LobbyPositionChanged}",
            $"charaSelectStageProbe.lobbyPosition.lastOverrideLoginPosition={diagnostic.StageProbe.LastOverrideLoginPosition}",
            $"charaSelectStageProbe.lobbyPosition.verdict={diagnostic.StageProbe.LobbyPositionVerdict}",
            $"charaSelectStageProbe.lobbySheet.available={diagnostic.StageProbe.LobbySheetAvailable}",
            $"charaSelectStageProbe.lobbySheet.matchCount={diagnostic.StageProbe.LobbySheetMatchCount}",
            $"charaSelectStageProbe.lobbySheet.candidate[0].rowId={diagnostic.StageProbe.LobbySheetCandidate0RowId}",
            $"charaSelectStageProbe.lobbySheet.candidate[0].type={diagnostic.StageProbe.LobbySheetCandidate0Type}",
            $"charaSelectStageProbe.lobbySheet.candidate[0].param={diagnostic.StageProbe.LobbySheetCandidate0Param}",
            $"charaSelectStageProbe.lobbySheet.candidate[0].link={diagnostic.StageProbe.LobbySheetCandidate0Link}",
            $"charaSelectStageProbe.lobbySheet.resolvedReason={diagnostic.StageProbe.LobbySheetResolvedReason}",
            $"charaSelectStageProbe.layoutPrefetch.requested={diagnostic.StageProbe.LayoutPrefetchRequested}",
            $"charaSelectStageProbe.layoutPrefetch.bg={diagnostic.StageProbe.LayoutPrefetchBg}",
            $"charaSelectStageProbe.layoutPrefetch.levelId={diagnostic.StageProbe.LayoutPrefetchLevelId}",
            $"charaSelectStageProbe.layoutPrefetch.layerEntryType={diagnostic.StageProbe.LayoutPrefetchLayerEntryType}",
            $"charaSelectStageProbe.layoutPrefetch.owner={diagnostic.StageProbe.LayoutPrefetchOwner}",
            $"charaSelectStageProbe.layoutPrefetch.verdict={diagnostic.StageProbe.LayoutPrefetchVerdict}",
            $"charaSelectStageProbe.titleBackgroundRoute.enabled={diagnostic.StageProbe.TitleBackgroundRouteEnabled}",
            $"charaSelectStageProbe.titleBackgroundRoute.active={diagnostic.StageProbe.TitleBackgroundRouteActive}",
            $"charaSelectStageProbe.titleBackgroundRoute.verdict={diagnostic.StageProbe.TitleBackgroundRouteVerdict}",
            $"charaSelectStageProbe.routeVerdict={diagnostic.StageProbe.RouteVerdict}",
            $"charaSelectStageProbe.nextAction={diagnostic.StageProbe.NextAction}",
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

    public static string BuildNextAction(
        Configuration configuration,
        CharaSelectSceneLastObservation? lastObservation = null)
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
            return "inspect-character-visibility-route";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "discover-visible-stage-source";
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

        var observation = lastObservation ?? CharaSelectSceneLastObservation.Empty;
        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Unknown
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Unknown
            && observation.CharacterPointerResolved
            && observation.EmoteReplayApplied)
        {
            return "verify-with-screenshot-and-set-manual-results";
        }

        return "verify-character-visible-background-and-emote-with-screenshot";
    }

    public static CharaSelectVisualLocationDiagnostic BuildVisualLocationDiagnostic(
        Configuration configuration,
        CharaSelectSceneProfile profile,
        bool currentRouteApplied,
        CharaSelectSceneLastObservation? lastObservation = null)
    {
        var observation = lastObservation ?? CharaSelectSceneLastObservation.Empty;
        var applied = currentRouteApplied || observation.TerritoryOverrideApplied || observation.StageProbe.ClientSelectDataPatchApplied;
        var manualResult = configuration.LastSceneProfileLocationChangedResult switch
        {
            CharaSelectSceneBinaryResult.Yes => "Changed",
            CharaSelectSceneBinaryResult.No => "Unchanged",
            _ => "Unknown",
        };
        var routeVerdict = BuildVisualLocationRouteVerdict(configuration, applied);
        return new CharaSelectVisualLocationDiagnostic(
            profile.Id,
            profile.DisplayName,
            profile.TerritoryTypeId,
            profile.TerritoryPath,
            BuildCurrentRouteName(configuration.CharaSelectSceneStageStrategy),
            applied,
            manualResult,
            routeVerdict,
            BuildVisualLocationNextAction(configuration, routeVerdict));
    }

    public static CharaSelectStageStrategyDiagnostic BuildStageStrategyDiagnostic(
        Configuration configuration,
        CharaSelectSceneProfile profile,
        CharaSelectVisualLocationDiagnostic visualLocation,
        CharaSelectStageProbeSnapshot stageProbe)
    {
        return configuration.CharaSelectSceneStageStrategy switch
        {
            CharaSelectStageStrategy.Disabled => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                false,
                false,
                "disabled",
                profile.CharacterExpectedVisible,
                false),
            CharaSelectStageStrategy.ObserveOnly => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                true,
                false,
                "read-only-diagnostics-only",
                profile.CharacterExpectedVisible,
                true),
            CharaSelectStageStrategy.ClientSelectDataTerritoryPatch => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                true,
                visualLocation.CurrentRouteApplied,
                visualLocation.RouteVerdict == "territory-patch-did-not-change-visible-stage"
                    ? "client-select-data-patch-applied-but-visible-stage-unchanged"
                    : "existing-safe-route-requires-screenshot",
                profile.CharacterExpectedVisible,
                true),
            CharaSelectStageStrategy.TitleBackgroundFullSceneFallback => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                false,
                false,
                "background-only-fallback-not-final-route",
                false,
                true),
            CharaSelectStageStrategy.LayoutPrefetchOnly => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                stageProbe.LayoutPrefetchRequested,
                stageProbe.LayoutPrefetchVerdict == "loaded",
                "prefetch-only-does-not-prove-visible-stage-source",
                profile.CharacterExpectedVisible,
                true),
            _ => new CharaSelectStageStrategyDiagnostic(
                configuration.CharaSelectSceneStageStrategy,
                false,
                false,
                "unavailable-no-safe-foreground-preserving-source",
                profile.CharacterExpectedVisible,
                true),
        };
    }

    public static string BuildStageProbeRouteVerdict(Configuration configuration)
    {
        if (configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "client-select-data-patch-applied-but-visible-stage-unchanged";
        }

        if (configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "candidate-source-found";
        }

        return "source-not-resolved";
    }

    private static string BuildCurrentRouteName(CharaSelectStageStrategy strategy)
    {
        return strategy == CharaSelectStageStrategy.ClientSelectDataTerritoryPatch
            ? ClientSelectDataTerritoryPatchRoute
            : strategy.ToString();
    }

    private static string BuildVisualLocationRouteVerdict(Configuration configuration, bool currentRouteApplied)
    {
        if (!configuration.CharaSelectSceneCompositionEnabled)
        {
            return "not-tested";
        }

        if (configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "changed";
        }

        if (configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "territory-patch-did-not-change-visible-stage";
        }

        return currentRouteApplied ? "unknown" : "stage-source-not-resolved";
    }

    private static string BuildVisualLocationNextAction(Configuration configuration, string routeVerdict)
    {
        if (routeVerdict == "changed"
            && configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "implement-one-shot-placement";
        }

        if (routeVerdict == "changed")
        {
            return "verify-stage-strategy";
        }

        if (routeVerdict == "territory-patch-did-not-change-visible-stage"
            || routeVerdict == "stage-source-not-resolved")
        {
            return "discover-visible-stage-source";
        }

        return "select-stage-strategy";
    }

    private static string BuildHumanVerdict(Configuration configuration)
    {
        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No)
        {
            return "character-not-visible-screenshot";
        }

        if (configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "character-visible-location-unchanged";
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
    string ObservedAt,
    CharaSelectStageProbeSnapshot StageProbe = default)
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
        "none",
        CharaSelectStageProbeSnapshot.Empty);
}

// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundDeliveryDiagnostic.cs
// Description: Character Select 背景配送 MVP の互換性・fallback・安全判定をまとめる
// Reason: 実機 write を増やさずに Phase 2N の配送状態を /xmutbgdiag とテストで判断するため
using System.Numerics;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundCharacterSelectBackgroundMode
{
    Disabled,
    DiagnosticsOnly,
    SceneOverrideOnly,
    PreserveCharaSelectForeground,
    NativePreviewModelSource,
    CompatiblePresetOnly,
}

public enum TitleBackgroundCharacterSelectLightingMode
{
    Default,
    DiagnosticsOnly,
    PreferBrightPreset,
    PreferBrightLayer,
    EnvironmentOverrideExperimental,
    DisableDarkeningExperimental,
}

public enum TitleBackgroundCharacterSelectCompatibility
{
    Unknown,
    Compatible,
    BackgroundOnly,
    CharacterHidden,
    TooDark,
    Unsupported,
}

public enum TitleBackgroundCharacterSelectExpectedBrightness
{
    Unknown,
    Bright,
    Normal,
    Dark,
    TooDark,
}

internal readonly record struct TitleBackgroundForegroundPreserveResult(
    bool Available,
    string Reason,
    string OriginalScenePath,
    string OverrideScenePath,
    bool CanKeepOriginalCharaStage,
    bool CanOverrideBackgroundOnly,
    string Blocker,
    string HookPoint,
    bool Applied,
    string SkippedReason);

internal readonly record struct TitleBackgroundNativePreviewSourceProbe(
    string Name,
    bool Available,
    string ReadStatus,
    int CandidateCount,
    int NonZeroTransformCount,
    int DrawObjectNonNullCount,
    int ModelLikeNonNullCount,
    string Error);

internal readonly record struct TitleBackgroundLightingDiagnostic(
    TitleBackgroundCharacterSelectLightingMode Mode,
    bool Available,
    string CurrentWeather,
    string CurrentTime,
    string EnvSet,
    string Fog,
    string Exposure,
    string Overlay,
    uint LayerFilterKey,
    string Error,
    string LastStatus,
    string LastSkippedReason,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    uint CurrentLayerFilterKey,
    bool LayerBrightnessKnown,
    string BrightLayerCandidates,
    string RecommendedAction);

internal readonly record struct TitleBackgroundPresetCompatibility(
    string CurrentPresetId,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    string Warning,
    TitleBackgroundCharacterSelectBackgroundMode RecommendedMode,
    string KnownIssue,
    bool SafeToUse,
    bool CharacterExpectedVisible);

internal readonly record struct TitleBackgroundOverrideCompatibility(
    string Source,
    string Id,
    string DisplayName,
    string SelectedPresetId,
    string CurrentOverrideId,
    string OverrideTerritoryPath,
    uint OverrideTerritoryId,
    uint LayerFilterKey,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    string CharacterVisibility,
    bool BackgroundUsable,
    bool CharacterExpectedVisible,
    string RecommendedAction,
    string Warning);

internal readonly record struct TitleBackgroundOverrideCandidateDiagnostic(
    TitleBackgroundCharacterSelectOverrideCandidate Selected,
    IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> Available,
    IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> ManualSlots,
    int RegistryCount,
    int RegistryVerifiedCount,
    int RegistryUnverifiedCount,
    int RegistryBrightCount,
    string RegistryDefaultId);

internal readonly record struct TitleBackgroundDeliveryBackgroundApplicationDiagnostic(
    bool Observed,
    bool LastHistoricalOverrideApplied,
    string LastHistoricalOverridePath,
    string CurrentCandidateId,
    bool VisualConfirmationRequired,
    string UserVerdict);

internal readonly record struct TitleBackgroundDeliverySafetyDiagnostic(
    string Verdict,
    string Reason,
    bool BlocksBackgroundCandidatePromotion);

internal readonly record struct TitleBackgroundDeliverySummary(
    string FeatureGoal,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    string BackgroundModeReason,
    string CharacterVisibilityExpected,
    string CharacterVisibilityObserved,
    string CharacterVisibilityBlocker,
    bool NativePreviewSourceSearched,
    IReadOnlyList<TitleBackgroundNativePreviewSourceProbe> NativePreviewSources,
    string NativePreviewSourceBestSource,
    string NativePreviewSourceBestCandidate,
    string NativePreviewSourceResolution,
    string NativePreviewSourceCaptureContext,
    string NativePreviewSourceReadStatus,
    int NativePreviewSourceObservedFrameCount,
    string NativePreviewSourceAddressStable,
    bool NativePreviewSourcePostLoginReadAttempted,
    string NativePreviewSourceBlocker,
    bool NativePreviewSourceCurrentObjectTableIgnored,
    string NativePreviewSourceCurrentObjectTableIgnoredReason,
    TitleBackgroundForegroundPreserveResult ForegroundPreserve,
    TitleBackgroundPresetCompatibility PresetCompatibility,
    TitleBackgroundOverrideCompatibility OverrideCompatibility,
    TitleBackgroundOverrideCandidateDiagnostic OverrideCandidate,
    TitleBackgroundLightingDiagnostic Lighting,
    bool ObjectTableActorRejected,
    string ObjectTableActorRejectedReason,
    bool ActorPlacementReady,
    string ActorPlacementBlocker,
    string DeliveryVerdict,
    TitleBackgroundDeliveryBackgroundApplicationDiagnostic BackgroundApplication,
    TitleBackgroundDeliverySafetyDiagnostic Safety,
    string BackgroundDeliveryVerdict,
    string TransitionSafetyVerdict,
    string PostLoginLeakVerdict,
    string UserMessage,
    string UserNextAction,
    string CandidateHumanName,
    string CandidateHumanStatus,
    string TransitionUserMessage,
    string MvpStatus,
    string MvpBlockingIssue,
    string MvpKnownLimitation,
    string NextAction,
    string NextActionReason);

internal static class TitleBackgroundDeliveryDiagnostic
{
    public const string OriginalCharaSelectScenePath = "ffxiv/zon_z1/chr/z1c1/level/z1c1";

    public static bool IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return mode is TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly
            or TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground
            or TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource
            or TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly;
    }

    public static bool IsKnownDarkOverrideTarget(string territoryPath, uint territoryId, uint layerFilterKey)
    {
        return TitleBackgroundCharacterSelectOverrideCandidateRegistry.IsDefaultCandidateTarget(
            territoryPath,
            territoryId,
            layerFilterKey);
    }

    public static List<string> BuildLineList(TitleBackgroundDeliverySummary summary)
    {
        var lines = new List<string>();
        AddLines(lines, summary);
        return lines;
    }

    public static void AddLines(List<string> lines, TitleBackgroundDeliverySummary summary)
    {
        var aliasStartIndex = lines.Count;
        lines.Add($"phase2N.featureGoal={summary.FeatureGoal}");
        lines.Add($"phase2N.backgroundMode={summary.BackgroundMode}");
        lines.Add($"phase2N.backgroundMode.reason={FormatNone(summary.BackgroundModeReason)}");
        lines.Add($"phase2N.characterVisibility.expected={FormatNone(summary.CharacterVisibilityExpected)}");
        lines.Add($"phase2N.characterVisibility.observed={FormatNone(summary.CharacterVisibilityObserved)}");
        lines.Add($"phase2N.characterVisibility.blocker={FormatNone(summary.CharacterVisibilityBlocker)}");
        lines.Add($"phase2N.nativePreviewSource.searched={summary.NativePreviewSourceSearched}");
        for (var i = 0; i < summary.NativePreviewSources.Count; i++)
        {
            var source = summary.NativePreviewSources[i];
            lines.Add($"phase2N.nativePreviewSource.source[{i}].name={FormatNone(source.Name)}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].available={source.Available}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].readStatus={FormatNone(source.ReadStatus)}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].candidateCount={source.CandidateCount}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].nonZeroTransformCount={source.NonZeroTransformCount}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].drawObjectNonNullCount={source.DrawObjectNonNullCount}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].modelLikeNonNullCount={source.ModelLikeNonNullCount}");
            lines.Add($"phase2N.nativePreviewSource.source[{i}].error={FormatNone(source.Error)}");
        }

        lines.Add($"phase2N.nativePreviewSource.bestSource={FormatNone(summary.NativePreviewSourceBestSource)}");
        lines.Add($"phase2N.nativePreviewSource.bestCandidate={FormatNone(summary.NativePreviewSourceBestCandidate)}");
        lines.Add($"phase2N.nativePreviewSource.resolution={FormatNone(summary.NativePreviewSourceResolution)}");
        lines.Add($"phase2N.nativePreviewSource.captureContext={FormatNone(summary.NativePreviewSourceCaptureContext)}");
        lines.Add($"phase2N.nativePreviewSource.readStatus={FormatNone(summary.NativePreviewSourceReadStatus)}");
        lines.Add($"phase2N.nativePreviewSource.observedFrameCount={summary.NativePreviewSourceObservedFrameCount}");
        lines.Add($"phase2N.nativePreviewSource.addressStable={FormatNone(summary.NativePreviewSourceAddressStable)}");
        lines.Add($"phase2N.nativePreviewSource.postLoginReadAttempted={summary.NativePreviewSourcePostLoginReadAttempted}");
        lines.Add($"phase2N.nativePreviewSource.blocker={FormatNone(summary.NativePreviewSourceBlocker)}");
        lines.Add($"phase2N.nativePreviewSource.currentObjectTableIgnored={summary.NativePreviewSourceCurrentObjectTableIgnored}");
        lines.Add($"phase2N.nativePreviewSource.currentObjectTableIgnoredReason={FormatNone(summary.NativePreviewSourceCurrentObjectTableIgnoredReason)}");
        lines.Add($"phase2N.foregroundPreserve.available={summary.ForegroundPreserve.Available}");
        lines.Add($"phase2N.foregroundPreserve.reason={FormatNone(summary.ForegroundPreserve.Reason)}");
        lines.Add($"phase2N.foregroundPreserve.originalScenePath={FormatNone(summary.ForegroundPreserve.OriginalScenePath)}");
        lines.Add($"phase2N.foregroundPreserve.overrideScenePath={FormatNone(summary.ForegroundPreserve.OverrideScenePath)}");
        lines.Add($"phase2N.foregroundPreserve.canKeepOriginalCharaStage={summary.ForegroundPreserve.CanKeepOriginalCharaStage}");
        lines.Add($"phase2N.foregroundPreserve.canOverrideBackgroundOnly={summary.ForegroundPreserve.CanOverrideBackgroundOnly}");
        lines.Add($"phase2N.foregroundPreserve.blocker={FormatNone(summary.ForegroundPreserve.Blocker)}");
        lines.Add($"phase2N.foregroundPreserve.hookPoint={FormatNone(summary.ForegroundPreserve.HookPoint)}");
        lines.Add($"phase2N.foregroundPreserve.applied={summary.ForegroundPreserve.Applied}");
        lines.Add($"phase2N.foregroundPreserve.skippedReason={FormatNone(summary.ForegroundPreserve.SkippedReason)}");
        lines.Add($"phase2N.objectTableActorRejected={summary.ObjectTableActorRejected}");
        lines.Add($"phase2N.objectTableActorRejected.reason={FormatNone(summary.ObjectTableActorRejectedReason)}");
        lines.Add($"phase2N.actorPlacement.ready={summary.ActorPlacementReady}");
        lines.Add($"phase2N.actorPlacement.blocker={FormatNone(summary.ActorPlacementBlocker)}");
        lines.Add($"phase2N.backgroundApplication.observed={summary.BackgroundApplication.Observed}");
        lines.Add($"phase2N.backgroundApplication.lastHistoricalOverrideApplied={summary.BackgroundApplication.LastHistoricalOverrideApplied}");
        lines.Add($"phase2N.backgroundApplication.lastHistoricalOverridePath={FormatNone(summary.BackgroundApplication.LastHistoricalOverridePath)}");
        lines.Add($"phase2N.backgroundApplication.currentCandidateId={FormatNone(summary.BackgroundApplication.CurrentCandidateId)}");
        lines.Add($"phase2N.backgroundApplication.visualConfirmationRequired={summary.BackgroundApplication.VisualConfirmationRequired}");
        lines.Add($"phase2N.backgroundApplication.userVerdict={FormatNone(summary.BackgroundApplication.UserVerdict)}");
        lines.Add($"phase2N.safety.verdict={FormatNone(summary.Safety.Verdict)}");
        lines.Add($"phase2N.safety.reason={FormatNone(summary.Safety.Reason)}");
        lines.Add($"phase2N.safety.blocksBackgroundCandidatePromotion={summary.Safety.BlocksBackgroundCandidatePromotion}");
        lines.Add($"phase2N.presetCompatibility.currentPresetId={FormatNone(summary.PresetCompatibility.CurrentPresetId)}");
        lines.Add($"phase2N.presetCompatibility.expectedCompatibility={summary.PresetCompatibility.ExpectedCompatibility}");
        lines.Add($"phase2N.presetCompatibility.expectedBrightness={summary.PresetCompatibility.ExpectedBrightness}");
        lines.Add($"phase2N.presetCompatibility.warning={FormatNone(summary.PresetCompatibility.Warning)}");
        lines.Add($"phase2N.presetCompatibility.recommendedMode={summary.PresetCompatibility.RecommendedMode}");
        lines.Add($"phase2N.presetCompatibility.knownIssue={FormatNone(summary.PresetCompatibility.KnownIssue)}");
        lines.Add($"phase2N.presetCompatibility.safeToUse={summary.PresetCompatibility.SafeToUse}");
        lines.Add($"phase2N.presetCompatibility.characterExpectedVisible={summary.PresetCompatibility.CharacterExpectedVisible}");
        lines.Add($"phase2N.overrideCompatibility.source={FormatNone(summary.OverrideCompatibility.Source)}");
        lines.Add($"phase2N.overrideCompatibility.selectedPresetId={FormatNone(summary.OverrideCompatibility.SelectedPresetId)}");
        lines.Add($"phase2N.overrideCompatibility.currentOverrideId={FormatNone(summary.OverrideCompatibility.CurrentOverrideId)}");
        lines.Add($"phase2N.overrideCompatibility.overrideTerritoryPath={FormatNone(summary.OverrideCompatibility.OverrideTerritoryPath)}");
        lines.Add($"phase2N.overrideCompatibility.overrideTerritoryId={summary.OverrideCompatibility.OverrideTerritoryId}");
        lines.Add($"phase2N.overrideCompatibility.layerFilterKey={summary.OverrideCompatibility.LayerFilterKey}");
        lines.Add($"phase2N.overrideCompatibility.expectedCompatibility={summary.OverrideCompatibility.ExpectedCompatibility}");
        lines.Add($"phase2N.overrideCompatibility.expectedBrightness={summary.OverrideCompatibility.ExpectedBrightness}");
        lines.Add($"phase2N.overrideCompatibility.characterExpectedVisible={summary.OverrideCompatibility.CharacterExpectedVisible}");
        lines.Add($"phase2N.overrideCompatibility.warning={FormatNone(summary.OverrideCompatibility.Warning)}");
        lines.Add($"phase2N.compatibility.source={FormatNone(summary.OverrideCompatibility.Source)}");
        lines.Add($"phase2N.compatibility.id={FormatNone(summary.OverrideCompatibility.Id)}");
        lines.Add($"phase2N.compatibility.displayName={FormatNone(summary.OverrideCompatibility.DisplayName)}");
        lines.Add($"phase2N.compatibility.characterVisibility={FormatNone(summary.OverrideCompatibility.CharacterVisibility)}");
        lines.Add($"phase2N.compatibility.characterExpectedVisible={summary.OverrideCompatibility.CharacterExpectedVisible}");
        lines.Add($"phase2N.compatibility.backgroundUsable={summary.OverrideCompatibility.BackgroundUsable}");
        lines.Add($"phase2N.compatibility.brightness={summary.OverrideCompatibility.ExpectedBrightness}");
        lines.Add($"phase2N.compatibility.recommendedAction={FormatNone(summary.OverrideCompatibility.RecommendedAction)}");
        lines.Add($"phase2N.overrideCandidate.selectedId={FormatNone(summary.OverrideCandidate.Selected.Id)}");
        lines.Add($"phase2N.overrideCandidate.source={FormatNone(summary.OverrideCandidate.Selected.Source)}");
        lines.Add($"phase2N.overrideCandidate.displayName={FormatNone(summary.OverrideCandidate.Selected.DisplayName)}");
        lines.Add($"phase2N.overrideCandidate.verifiedInGame={summary.OverrideCandidate.Selected.VerifiedInGame}");
        lines.Add($"phase2N.overrideCandidate.expectedCompatibility={summary.OverrideCandidate.Selected.ExpectedCompatibility}");
        lines.Add($"phase2N.overrideCandidate.expectedBrightness={summary.OverrideCandidate.Selected.ExpectedBrightness}");
        lines.Add($"phase2N.overrideCandidate.backgroundUsable={summary.OverrideCandidate.Selected.BackgroundUsable}");
        lines.Add($"phase2N.overrideCandidate.characterExpectedVisible={summary.OverrideCandidate.Selected.CharacterExpectedVisible}");
        lines.Add($"phase2N.overrideCandidate.warning={FormatNone(summary.OverrideCandidate.Selected.Warning)}");
        lines.Add($"phase2N.overrideCandidate.knownIssue={FormatNone(summary.OverrideCandidate.Selected.KnownIssue)}");
        lines.Add($"phase2N.overrideCandidate.recommendedAction={FormatNone(summary.OverrideCandidate.Selected.RecommendedAction)}");
        lines.Add($"phase2N.overrideCandidate.availableCount={summary.OverrideCandidate.Available.Count}");
        lines.Add($"phase2N.overrideCandidate.registry.count={summary.OverrideCandidate.RegistryCount}");
        lines.Add($"phase2N.overrideCandidate.registry.verifiedCount={summary.OverrideCandidate.RegistryVerifiedCount}");
        lines.Add($"phase2N.overrideCandidate.registry.unverifiedCount={summary.OverrideCandidate.RegistryUnverifiedCount}");
        lines.Add($"phase2N.overrideCandidate.registry.brightCount={summary.OverrideCandidate.RegistryBrightCount}");
        lines.Add($"phase2N.overrideCandidate.registry.defaultId={FormatNone(summary.OverrideCandidate.RegistryDefaultId)}");
        for (var i = 0; i < summary.OverrideCandidate.Available.Count; i++)
        {
            var candidate = summary.OverrideCandidate.Available[i];
            lines.Add($"phase2N.overrideCandidate.available[{i}].id={FormatNone(candidate.Id)}");
            lines.Add($"phase2N.overrideCandidate.available[{i}].displayName={FormatNone(candidate.DisplayName)}");
            lines.Add($"phase2N.overrideCandidate.available[{i}].brightness={candidate.ExpectedBrightness}");
            lines.Add($"phase2N.overrideCandidate.available[{i}].verifiedInGame={candidate.VerifiedInGame}");
            lines.Add($"phase2N.overrideCandidate.available[{i}].source={FormatNone(candidate.Source)}");
        }

        foreach (var slot in summary.OverrideCandidate.ManualSlots)
        {
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].enabled={slot.Enabled}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].valid={slot.Valid}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].id={FormatNone(slot.Id)}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].displayName={FormatNone(slot.DisplayName)}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].territoryPath={FormatNone(slot.TerritoryPath)}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].territoryId={slot.TerritoryId}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].layerFilterKey={slot.LayerFilterKey}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].expectedBrightness={slot.ExpectedBrightness}");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].verifiedInGame=False");
            lines.Add($"phase2N.overrideCandidate.manualSlot[{slot.SlotNumber}].validationError={FormatNone(slot.ValidationError)}");
        }

        lines.Add($"phase2N.lighting.mode={summary.Lighting.Mode}");
        lines.Add($"phase2N.lighting.diagnostic.available={summary.Lighting.Available}");
        lines.Add($"phase2N.lighting.diagnostic.currentWeather={FormatNone(summary.Lighting.CurrentWeather)}");
        lines.Add($"phase2N.lighting.diagnostic.currentTime={FormatNone(summary.Lighting.CurrentTime)}");
        lines.Add($"phase2N.lighting.diagnostic.envSet={FormatNone(summary.Lighting.EnvSet)}");
        lines.Add($"phase2N.lighting.diagnostic.fog={FormatNone(summary.Lighting.Fog)}");
        lines.Add($"phase2N.lighting.diagnostic.exposure={FormatNone(summary.Lighting.Exposure)}");
        lines.Add($"phase2N.lighting.diagnostic.overlay={FormatNone(summary.Lighting.Overlay)}");
        lines.Add($"phase2N.lighting.diagnostic.layerFilterKey={summary.Lighting.LayerFilterKey}");
        lines.Add($"phase2N.lighting.diagnostic.error={FormatNone(summary.Lighting.Error)}");
        lines.Add($"phase2N.lighting.lastStatus={FormatNone(summary.Lighting.LastStatus)}");
        lines.Add($"phase2N.lighting.lastSkippedReason={FormatNone(summary.Lighting.LastSkippedReason)}");
        lines.Add($"phase2N.lighting.expectedBrightness={summary.Lighting.ExpectedBrightness}");
        lines.Add($"phase2N.lighting.currentLayerFilterKey={summary.Lighting.CurrentLayerFilterKey}");
        lines.Add($"phase2N.lighting.layerBrightnessKnown={summary.Lighting.LayerBrightnessKnown}");
        lines.Add($"phase2N.lighting.brightLayerCandidates={FormatNone(summary.Lighting.BrightLayerCandidates)}");
        lines.Add($"phase2N.lighting.recommendedAction={FormatNone(summary.Lighting.RecommendedAction)}");
        lines.Add($"phase2N.deliveryVerdict={FormatNone(summary.DeliveryVerdict)}");
        lines.Add($"phase2N.backgroundDeliveryVerdict={FormatNone(summary.BackgroundDeliveryVerdict)}");
        lines.Add($"phase2N.transitionSafetyVerdict={FormatNone(summary.TransitionSafetyVerdict)}");
        lines.Add($"phase2N.postLoginLeakVerdict={FormatNone(summary.PostLoginLeakVerdict)}");
        lines.Add($"phase2N.userMessage={FormatNone(summary.UserMessage)}");
        lines.Add($"phase2N.userNextAction={FormatNone(summary.UserNextAction)}");
        lines.Add($"phase2N.candidateHumanName={FormatNone(summary.CandidateHumanName)}");
        lines.Add($"phase2N.candidateHumanStatus={FormatNone(summary.CandidateHumanStatus)}");
        lines.Add($"transition.userMessage={FormatNone(summary.TransitionUserMessage)}");
        lines.Add($"phase2N.mvpStatus={FormatNone(summary.MvpStatus)}");
        lines.Add($"phase2N.mvpBlockingIssue={FormatNone(summary.MvpBlockingIssue)}");
        lines.Add($"phase2N.mvpKnownLimitation={FormatNone(summary.MvpKnownLimitation)}");
        lines.Add($"phase2N.nextAction={FormatNone(summary.NextAction)}");
        lines.Add($"phase2N.nextAction.reason={FormatNone(summary.NextActionReason)}");
        DiagnosticReportBuilder.AddPrefixAliasLines(lines, aliasStartIndex, "phase2N.", "delivery.");
    }

    private static string FormatNone(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }

    public static TitleBackgroundDeliverySummary BuildSummary(
        TitleBackgroundCharacterSelectBackgroundMode backgroundMode,
        TitleBackgroundCharacterSelectLightingMode lightingMode,
        string selectedPresetId,
        string overrideScenePath,
        uint overrideTerritoryId,
        uint layerFilterKey,
        bool sceneOverrideEnabled,
        bool lastOverrideApplied,
        string phase2MResolution,
        string phase2MTransformValidity,
        string phase2MActorVisible,
        int zeroPositionCandidateCount,
        int nonZeroPositionCandidateCount,
        int drawObjectNonNullCount,
        int modelLikeNonNullCount,
        string bestCandidate,
        IReadOnlyList<TitleBackgroundCharacterPlacementSourceDiscovery> sourceDiscovery,
        string transitionSafety,
        bool currentObjectTableValidForCharaSelect = true,
        string currentObjectTableInvalidReason = "none",
        string selectedOverrideCandidateId = "",
        IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot>? manualCandidateSlots = null,
        bool historicalLastOverrideApplied = false,
        string historicalLastOverridePath = "",
        bool sceneReadyAcceptedMultipleTimes = false,
        bool activeAfterLoginDetected = false,
        bool phase2GAppliedAfterLogin = false,
        TitleBackgroundCharacterSourceSummary nativeCharacterSource = default,
        bool characterCompositedApplied = false)
    {
        var normalizedManualSlots = manualCandidateSlots ?? [];
        var availableCandidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(normalizedManualSlots);
        var overrideCandidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            selectedOverrideCandidateId,
            overrideScenePath,
            overrideTerritoryId,
            layerFilterKey,
            availableCandidates);
        var overrideCandidateDiagnostic = new TitleBackgroundOverrideCandidateDiagnostic(
            overrideCandidate,
            availableCandidates,
            normalizedManualSlots,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All.Count,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All.Count(candidate => candidate.VerifiedInGame),
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All.Count(candidate => !candidate.VerifiedInGame),
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetBrightCandidates(TitleBackgroundCharacterSelectOverrideCandidateRegistry.All).Count,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId);
        var currentObjectTableIgnored = !currentObjectTableValidForCharaSelect;
        var currentObjectTableIgnoredReason = currentObjectTableIgnored
            ? string.IsNullOrWhiteSpace(currentObjectTableInvalidReason)
                ? "post-login-world-object-table-not-valid-for-chara-select"
                : currentObjectTableInvalidReason
            : "none";
        var presetCompatibility = BuildPresetCompatibility(
            selectedPresetId,
            overrideScenePath,
            overrideTerritoryId,
            layerFilterKey,
            backgroundMode,
            overrideCandidate);
        var overrideCompatibility = BuildOverrideCompatibility(
            selectedPresetId,
            overrideScenePath,
            overrideTerritoryId,
            layerFilterKey,
            presetCompatibility,
            overrideCandidate);
        var foreground = BuildForegroundPreserve(backgroundMode, overrideScenePath);
        var nativeSources = BuildNativeSourceProbes(
            sourceDiscovery,
            zeroPositionCandidateCount,
            nonZeroPositionCandidateCount,
            drawObjectNonNullCount,
            modelLikeNonNullCount);
        var hasPreLoginNativeCapture = nativeCharacterSource.CaptureContext == "pre-login";
        var nativeResolution = hasPreLoginNativeCapture
            ? nativeCharacterSource.Resolution
            : currentObjectTableIgnored
                ? "not-verifiable-post-login"
                : BuildNativeResolution(phase2MResolution, phase2MTransformValidity, nativeSources);
        var objectTableRejected = currentObjectTableIgnored
            || phase2MResolution == "stub-only"
            || (zeroPositionCandidateCount > 0
                && nonZeroPositionCandidateCount == 0
                && drawObjectNonNullCount == 0
                && modelLikeNonNullCount == 0);
        var actorPlacementReady = nativeResolution == "found-single"
            && (!hasPreLoginNativeCapture || nativeCharacterSource.AddressStable == "true");
        var lighting = BuildLightingDiagnostic(
            lightingMode,
            presetCompatibility.ExpectedBrightness,
            layerFilterKey,
            overrideCandidateDiagnostic.Available);
        var characterExpected = backgroundMode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "native",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "native",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => "hidden-or-unknown",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "background-only",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => foreground.Available ? "visible" : "unsupported",
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => actorPlacementReady ? "visible-experimental" : "unsupported",
            _ => "unknown",
        };
        var characterObserved = characterCompositedApplied
            ? "composited-experimental"
            : BuildCharacterVisibilityObserved(
                phase2MActorVisible,
                nativeResolution,
                objectTableRejected,
                nonZeroPositionCandidateCount,
                drawObjectNonNullCount,
                modelLikeNonNullCount);
        var characterBlocker = characterCompositedApplied
            ? "visual-confirmation-required"
            : actorPlacementReady
            ? "none"
            : hasPreLoginNativeCapture && nativeResolution == "not-found"
                ? "native-preview-source-not-found"
                : currentObjectTableIgnored
                    ? "post-login-object-table-not-valid"
                    : objectTableRejected
            ? "stub-only-object-table"
            : nativeResolution == "not-found"
                ? "native-preview-source-not-found"
                : nativeResolution == "found-ambiguous"
                    ? "native-preview-source-ambiguous"
                    : "none";
        var modeReason = BuildBackgroundModeReason(backgroundMode, sceneOverrideEnabled, lastOverrideApplied, foreground, nativeResolution);
        var (verdict, nextAction, nextActionReason) = SelectDeliveryVerdict(
            backgroundMode,
            transitionSafety,
            lastOverrideApplied,
            nativeResolution,
            foreground.Available,
            presetCompatibility,
            lighting);
        var backgroundApplication = BuildBackgroundApplication(
            lastOverrideApplied,
            historicalLastOverrideApplied,
            historicalLastOverridePath,
            overrideCandidate.Id);
        var safety = BuildSafety(
            transitionSafety,
            sceneReadyAcceptedMultipleTimes,
            activeAfterLoginDetected,
            phase2GAppliedAfterLogin);
        var backgroundDeliveryVerdict = backgroundApplication.Observed && overrideCompatibility.BackgroundUsable
            ? "working-background-only-observed"
            : "not-observed";
        var transitionSafetyVerdict = BuildTransitionSafetyVerdict(safety, sceneReadyAcceptedMultipleTimes);
        var postLoginLeakVerdict = BuildPostLoginLeakVerdict(activeAfterLoginDetected, phase2GAppliedAfterLogin);
        var candidateHumanStatus = BuildCandidateHumanStatus(overrideCandidate);
        var userMessage = characterCompositedApplied
            ? "Character placement writes were applied, but on-screen visibility still requires confirmation."
            : backgroundApplication.Observed && !overrideCompatibility.CharacterExpectedVisible
            ? "Background was applied as background-only. Selected character model is expected to remain hidden."
            : "Background application still requires Character Select screenshot confirmation.";
        var userNextAction = nativeResolution == "found-single"
            ? "Paste the automatically copied report for read-only native source review."
            : "Run Automatic Check once and paste the copied report.";
        var transitionUserMessage = postLoginLeakVerdict == "not-observed" && sceneReadyAcceptedMultipleTimes
            ? "No post-login scene override leak observed, but sceneReady was accepted multiple times in this session."
            : postLoginLeakVerdict == "not-observed"
                ? "No post-login scene override leak observed."
                : "Post-login scene override leak was observed; do not promote this candidate.";
        var (mvpStatus, mvpBlockingIssue, mvpKnownLimitation) = characterCompositedApplied
            ? ("character-placement-applied-unverified", "visual-confirmation-required", "experimental-per-frame-character-placement")
            : BuildMvpSummary(
                verdict,
                transitionSafety,
                overrideCompatibility.BackgroundUsable,
                overrideCompatibility.CharacterExpectedVisible,
                actorPlacementReady,
                currentObjectTableIgnored,
                nativeResolution,
                nextAction,
                characterBlocker);

        return new TitleBackgroundDeliverySummary(
            "character-select-background",
            backgroundMode,
            modeReason,
            characterExpected,
            characterObserved,
            characterBlocker,
            true,
            nativeSources,
            nativeResolution == "found-single" ? nativeSources.FirstOrDefault(source => source.NonZeroTransformCount > 0).Name ?? "none" : "none",
            nativeResolution == "found-single" ? bestCandidate : "none",
            nativeResolution,
            hasPreLoginNativeCapture ? nativeCharacterSource.CaptureContext : "not-observed",
            hasPreLoginNativeCapture ? nativeCharacterSource.ReadStatus : "not-run",
            hasPreLoginNativeCapture ? nativeCharacterSource.ObservedFrameCount : 0,
            hasPreLoginNativeCapture ? nativeCharacterSource.AddressStable : "not-observed",
            nativeCharacterSource.PostLoginReadAttempted,
            hasPreLoginNativeCapture ? nativeCharacterSource.Blocker : "pre-login-native-source-not-captured",
            currentObjectTableIgnored,
            currentObjectTableIgnoredReason,
            foreground,
            presetCompatibility,
            overrideCompatibility,
            overrideCandidateDiagnostic,
            lighting,
            objectTableRejected,
            currentObjectTableIgnored ? currentObjectTableIgnoredReason : objectTableRejected ? "zero-transform-stub-only" : "none",
            actorPlacementReady,
            actorPlacementReady ? "none" : hasPreLoginNativeCapture ? nativeCharacterSource.Blocker : currentObjectTableIgnored ? currentObjectTableIgnoredReason : objectTableRejected ? "stub-only-object-table" : $"native-preview-source-{nativeResolution}",
            verdict,
            backgroundApplication,
            safety,
            backgroundDeliveryVerdict,
            transitionSafetyVerdict,
            postLoginLeakVerdict,
            userMessage,
            userNextAction,
            overrideCandidate.DisplayName,
            candidateHumanStatus,
            transitionUserMessage,
            mvpStatus,
            mvpBlockingIssue,
            mvpKnownLimitation,
            nextAction,
            nextActionReason);
    }

    private static TitleBackgroundDeliveryBackgroundApplicationDiagnostic BuildBackgroundApplication(
        bool lastOverrideApplied,
        bool historicalLastOverrideApplied,
        string historicalLastOverridePath,
        string currentCandidateId)
    {
        var historicalPath = string.IsNullOrWhiteSpace(historicalLastOverridePath)
            ? "none"
            : historicalLastOverridePath;
        var observed = lastOverrideApplied || historicalLastOverrideApplied;
        return new TitleBackgroundDeliveryBackgroundApplicationDiagnostic(
            observed,
            historicalLastOverrideApplied,
            historicalPath,
            string.IsNullOrWhiteSpace(currentCandidateId) ? "custom" : currentCandidateId,
            true,
            observed ? "background-applied-character-hidden" : "not-confirmed");
    }

    private static TitleBackgroundDeliverySafetyDiagnostic BuildSafety(
        string transitionSafety,
        bool sceneReadyAcceptedMultipleTimes,
        bool activeAfterLoginDetected,
        bool phase2GAppliedAfterLogin)
    {
        if (activeAfterLoginDetected || phase2GAppliedAfterLogin)
        {
            return new TitleBackgroundDeliverySafetyDiagnostic(
                "unsafe",
                activeAfterLoginDetected ? "scene-override-active-after-login" : "phase2g-applied-after-login",
                true);
        }

        if (sceneReadyAcceptedMultipleTimes || transitionSafety == "unsafe")
        {
            return new TitleBackgroundDeliverySafetyDiagnostic(
                "warning",
                sceneReadyAcceptedMultipleTimes ? "scene-ready-accepted-multiple-times" : "login-transition-safety-unsafe",
                true);
        }

        return new TitleBackgroundDeliverySafetyDiagnostic("safe", "none", false);
    }

    private static string BuildTransitionSafetyVerdict(
        TitleBackgroundDeliverySafetyDiagnostic safety,
        bool sceneReadyAcceptedMultipleTimes)
    {
        if (sceneReadyAcceptedMultipleTimes)
        {
            return "warning-scene-ready-accepted-multiple-times";
        }

        return safety.Verdict switch
        {
            "safe" => "safe",
            "warning" => $"warning-{safety.Reason}",
            "unsafe" => $"unsafe-{safety.Reason}",
            _ => "unknown",
        };
    }

    private static string BuildPostLoginLeakVerdict(bool activeAfterLoginDetected, bool phase2GAppliedAfterLogin)
    {
        return activeAfterLoginDetected || phase2GAppliedAfterLogin
            ? "observed"
            : "not-observed";
    }

    private static string BuildCandidateHumanStatus(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var verification = candidate.VerifiedInGame ? "Verified" : "Observed / Unverified";
        var compatibility = candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
            ? "Background-only"
            : candidate.ExpectedCompatibility.ToString();
        return $"{verification} / {compatibility}";
    }

    private static (string Status, string BlockingIssue, string KnownLimitation) BuildMvpSummary(
        string deliveryVerdict,
        string transitionSafety,
        bool backgroundUsable,
        bool characterExpectedVisible,
        bool actorPlacementReady,
        bool currentObjectTableIgnored,
        string nativeResolution,
        string nextAction,
        string characterBlocker)
    {
        var nativeSourceAcceptableForBackgroundOnly = currentObjectTableIgnored
            || nativeResolution is "found-single" or "found-ambiguous" or "found-but-no-transform" or "not-found" or "not-verifiable-post-login";
        if (deliveryVerdict == "working-background-only"
            && backgroundUsable
            && !characterExpectedVisible
            && nativeSourceAcceptableForBackgroundOnly
            && transitionSafety == "safe")
        {
            return ("complete-background-only", "none", "selected-character-model-hidden");
        }

        var blockingIssue = characterBlocker != "none" ? characterBlocker : nextAction;
        return ("incomplete", blockingIssue, characterExpectedVisible ? "none" : "selected-character-model-hidden");
    }

    private static string BuildCharacterVisibilityObserved(
        string phase2MActorVisible,
        string nativeResolution,
        bool objectTableRejected,
        int nonZeroPositionCandidateCount,
        int drawObjectNonNullCount,
        int modelLikeNonNullCount)
    {
        if (nativeResolution == "found-single"
            && nonZeroPositionCandidateCount > 0
            && (drawObjectNonNullCount > 0 || modelLikeNonNullCount > 0))
        {
            return string.Equals(phase2MActorVisible, "observed", StringComparison.Ordinal)
                ? "observed"
                : "not-verifiable";
        }

        if (objectTableRejected || nativeResolution == "not-found")
        {
            return "not-observed";
        }

        return "not-verifiable";
    }

    public static string EvaluateExperimentalActorPlacement(
        TitleBackgroundCharacterPlacementExperimentalApplyMode mode,
        TitleBackgroundCharacterPlacementSummary summary,
        bool sceneGenerationMatches,
        bool isCharaSelectActive,
        bool isLoggedIn)
    {
        if (mode == TitleBackgroundCharacterPlacementExperimentalApplyMode.None)
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

        if (mode == TitleBackgroundCharacterPlacementExperimentalApplyMode.ActorPlacementOneShot
            && summary.Resolution == "stub-only")
        {
            return "skip:stub-only-object-table";
        }

        return TitleBackgroundCharacterPlacementDiagnostic.EvaluateExperimentalApply(
            mode,
            summary,
            sceneGenerationMatches,
            isCharaSelectActive,
            isLoggedIn);
    }

    private static TitleBackgroundPresetCompatibility BuildPresetCompatibility(
        string selectedPresetId,
        string territoryPath,
        uint territoryId,
        uint layerFilterKey,
        TitleBackgroundCharacterSelectBackgroundMode backgroundMode,
        TitleBackgroundCharacterSelectOverrideCandidate overrideCandidate)
    {
        var hasSelectedPreset = !string.IsNullOrWhiteSpace(selectedPresetId);
        var presetId = hasSelectedPreset
            ? selectedPresetId.Trim()
            : overrideCandidate.Id;
        if (overrideCandidate.ExpectedCompatibility != TitleBackgroundCharacterSelectCompatibility.Unknown)
        {
            var warning = hasSelectedPreset
                ? overrideCandidate.Warning
                : overrideCandidate.Id == TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId
                    ? "synthetic custom override compatibility entry; full scene override works as background-only and selected character is not expected to be visible"
                    : overrideCandidate.Warning;
            return new TitleBackgroundPresetCompatibility(
                presetId,
                overrideCandidate.ExpectedCompatibility,
                overrideCandidate.ExpectedBrightness,
                warning,
                backgroundMode == TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground
                    ? TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground
                    : TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly,
                overrideCandidate.KnownIssue,
                overrideCandidate.BackgroundUsable,
                overrideCandidate.CharacterExpectedVisible);
        }

        return new TitleBackgroundPresetCompatibility(
            presetId,
            TitleBackgroundCharacterSelectCompatibility.Unknown,
            TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
            hasSelectedPreset
                ? "preset has no Character Select compatibility metadata yet"
                : "custom override target has no Character Select compatibility metadata yet",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly,
            "requires one real-game /xmutbgdiag capture",
            true,
            false);
    }

    private static TitleBackgroundOverrideCompatibility BuildOverrideCompatibility(
        string selectedPresetId,
        string territoryPath,
        uint territoryId,
        uint layerFilterKey,
        TitleBackgroundPresetCompatibility presetCompatibility,
        TitleBackgroundCharacterSelectOverrideCandidate overrideCandidate)
    {
        var hasSelectedPreset = !string.IsNullOrWhiteSpace(selectedPresetId);
        var source = hasSelectedPreset ? "selected-preset" : "custom-override";
        var normalizedSelectedPresetId = hasSelectedPreset ? selectedPresetId.Trim() : "none";
        var id = presetCompatibility.CurrentPresetId;
        var displayName = source == "custom-override" ? overrideCandidate.DisplayName : id;
        var characterVisibility = presetCompatibility.CharacterExpectedVisible
            ? "visible"
            : presetCompatibility.ExpectedCompatibility is TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
                or TitleBackgroundCharacterSelectCompatibility.CharacterHidden
                    ? "hidden"
                    : "unknown";
        var recommendedAction = presetCompatibility.ExpectedBrightness is TitleBackgroundCharacterSelectExpectedBrightness.Dark
            or TitleBackgroundCharacterSelectExpectedBrightness.TooDark
                ? "add-bright-override-candidate"
                : presetCompatibility.CharacterExpectedVisible ? "use-compatible-candidate" : "use-background-only";
        var backgroundUsable = presetCompatibility.SafeToUse
            && (presetCompatibility.ExpectedCompatibility is TitleBackgroundCharacterSelectCompatibility.Compatible
                or TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
                or TitleBackgroundCharacterSelectCompatibility.CharacterHidden);

        return new TitleBackgroundOverrideCompatibility(
            source,
            id,
            displayName,
            normalizedSelectedPresetId,
            source == "custom-override" ? id : "none",
            string.IsNullOrWhiteSpace(territoryPath) ? "none" : territoryPath,
            territoryId,
            layerFilterKey,
            presetCompatibility.ExpectedCompatibility,
            presetCompatibility.ExpectedBrightness,
            characterVisibility,
            backgroundUsable,
            presetCompatibility.CharacterExpectedVisible,
            recommendedAction,
            presetCompatibility.Warning);
    }

    private static TitleBackgroundForegroundPreserveResult BuildForegroundPreserve(
        TitleBackgroundCharacterSelectBackgroundMode backgroundMode,
        string overrideScenePath)
    {
        return new TitleBackgroundForegroundPreserveResult(
            false,
            "unsupported: no safe public hook point found to keep original CharaSelect stage while replacing only the background",
            OriginalCharaSelectScenePath,
            string.IsNullOrWhiteSpace(overrideScenePath) ? "none" : overrideScenePath,
            false,
            false,
            "CreateScene replaces the full lobby scene; no background-only layer boundary is exposed",
            "none",
            false,
            backgroundMode == TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground
                ? "unsupported-no-safe-hook-point"
                : "mode-not-selected");
    }

    private static IReadOnlyList<TitleBackgroundNativePreviewSourceProbe> BuildNativeSourceProbes(
        IReadOnlyList<TitleBackgroundCharacterPlacementSourceDiscovery> sourceDiscovery,
        int zeroPositionCandidateCount,
        int nonZeroPositionCandidateCount,
        int drawObjectNonNullCount,
        int modelLikeNonNullCount)
    {
        var probes = new List<TitleBackgroundNativePreviewSourceProbe>();
        foreach (var source in sourceDiscovery)
        {
            probes.Add(new TitleBackgroundNativePreviewSourceProbe(
                source.Name,
                source.Available,
                source.ReadStatus == "unknown"
                    ? source.Available ? "read" : "not-available"
                    : source.ReadStatus,
                source.CandidateCount,
                source.NonZeroTransformCount,
                source.DrawObjectNonNullCount,
                source.ModelLikeNonNullCount,
                string.IsNullOrWhiteSpace(source.Error) ? "none" : source.Error));
        }

        if (probes.All(probe => probe.Name != "CharaSelectCharacterManager"))
        {
            probes.Add(new TitleBackgroundNativePreviewSourceProbe("CharaSelectCharacterManager", false, "not-resolved", 0, 0, 0, 0, "no public field or signature-safe source"));
        }

        if (probes.All(probe => !probe.Name.StartsWith("UIStage", StringComparison.Ordinal)))
        {
            probes.Add(new TitleBackgroundNativePreviewSourceProbe("UIStage CharaSelect model source", false, "not-resolved", 0, 0, 0, 0, "no safe model owner path found"));
        }

        return probes;
    }

    private static string BuildNativeResolution(
        string phase2MResolution,
        string transformValidity,
        IReadOnlyList<TitleBackgroundNativePreviewSourceProbe> nativeSources)
    {
        if (phase2MResolution == "single" && transformValidity == "valid-world-transform")
        {
            return "found-single";
        }

        if (phase2MResolution == "ambiguous")
        {
            return "found-ambiguous";
        }

        if (phase2MResolution == "stub-only")
        {
            return "not-found";
        }

        if (nativeSources.Any(source => source.CandidateCount > 0 && source.NonZeroTransformCount == 0))
        {
            return "found-but-no-transform";
        }

        return "not-found";
    }

    private static TitleBackgroundLightingDiagnostic BuildLightingDiagnostic(
        TitleBackgroundCharacterSelectLightingMode mode,
        TitleBackgroundCharacterSelectExpectedBrightness expectedBrightness,
        uint layerFilterKey,
        IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> availableCandidates)
    {
        var isDark = expectedBrightness is TitleBackgroundCharacterSelectExpectedBrightness.Dark or TitleBackgroundCharacterSelectExpectedBrightness.TooDark;
        var brightCandidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(availableCandidates);
        var hasBrightCandidates = brightCandidates != "none";
        var recommended = hasBrightCandidates
            ? TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(availableCandidates)
            : isDark ? "add-bright-override-candidate" : "none";
        var lastSkipped = mode is TitleBackgroundCharacterSelectLightingMode.EnvironmentOverrideExperimental
            or TitleBackgroundCharacterSelectLightingMode.DisableDarkeningExperimental
            ? "safe-public-write-api-not-found"
            : "read-only";

        return new TitleBackgroundLightingDiagnostic(
            mode,
            true,
            "not-read",
            "not-read",
            "not-read",
            "not-read",
            "not-read",
            "not-read",
            layerFilterKey,
            "none",
            "diagnostic-only",
            lastSkipped,
            expectedBrightness,
            layerFilterKey,
            expectedBrightness != TitleBackgroundCharacterSelectExpectedBrightness.Unknown,
            hasBrightCandidates || isDark ? brightCandidates : "not-needed",
            recommended);
    }

    private static string BuildBackgroundModeReason(
        TitleBackgroundCharacterSelectBackgroundMode mode,
        bool sceneOverrideEnabled,
        bool lastOverrideApplied,
        TitleBackgroundForegroundPreserveResult foreground,
        string nativeResolution)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "disabled-by-config",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "read-only diagnostics",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => lastOverrideApplied
                ? "full scene override applied"
                : sceneOverrideEnabled ? "full scene override armed" : "scene override disabled",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => foreground.Available
                ? "background-only hook available"
                : foreground.SkippedReason,
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => nativeResolution,
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "compatibility metadata gates warning/fallback",
            _ => "unknown",
        };
    }

    private static (string Verdict, string NextAction, string Reason) SelectDeliveryVerdict(
        TitleBackgroundCharacterSelectBackgroundMode mode,
        string transitionSafety,
        bool lastOverrideApplied,
        string nativeResolution,
        bool foregroundAvailable,
        TitleBackgroundPresetCompatibility compatibility,
        TitleBackgroundLightingDiagnostic lighting)
    {
        if (transitionSafety == "unsafe")
        {
            return ("unsafe", "unsafe-stop", "login transition safety is unsafe");
        }

        if (mode == TitleBackgroundCharacterSelectBackgroundMode.Disabled)
        {
            return ("blocked-incompatible-scene", "use-background-only", "Character Select background mode is disabled");
        }

        if (foregroundAvailable)
        {
            return ("needs-one-more-experimental-run", "try-preserve-foreground", "foreground preserve route needs real-game verification");
        }

        if (nativeResolution == "found-single"
            && mode == TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource)
        {
            return ("needs-one-more-experimental-run", "try-native-preview-source", "native preview source candidate is ready but default-off");
        }

        if (lighting.RecommendedAction is "try-bright-layer" or "try-bright-custom-target" or "verify-manual-bright-candidate")
        {
            return ("working-background-only", lighting.RecommendedAction, "background is available but brightness is dark");
        }

        if (lastOverrideApplied)
        {
            return nativeResolution == "found-single"
                ? ("working-background-only", "review-native-source", "background delivery works; read-only current character source was captured")
                : ("working-background-only", "use-background-only", "background delivery works; selected character source is not available");
        }

        if (compatibility.ExpectedCompatibility is TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
            or TitleBackgroundCharacterSelectCompatibility.CharacterHidden)
        {
            return ("needs-one-more-experimental-run", "use-background-only", "preset metadata allows background-only, but this run has not observed the scene override yet");
        }

        return ("blocked-character-source-not-found", "try-compatible-preset", "native preview source was not found and no applied background was observed");
    }
}



// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPhase2NDeliveryDiagnostic.cs
// Description: Character Select 背景配送 MVP の互換性・fallback・安全判定をまとめる
// Reason: 実機 write を増やさずに Phase 2N の配送状態を /xmutbgdiag とテストで判断するため
using System.Numerics;

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

internal readonly record struct TitleBackgroundPhase2NForegroundPreserveResult(
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

internal readonly record struct TitleBackgroundPhase2NNativeSourceProbe(
    string Name,
    bool Available,
    string ReadStatus,
    int CandidateCount,
    int NonZeroTransformCount,
    int DrawObjectNonNullCount,
    int ModelLikeNonNullCount,
    string Error);

internal readonly record struct TitleBackgroundPhase2NLightingDiagnostic(
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

internal readonly record struct TitleBackgroundPhase2NPresetCompatibility(
    string CurrentPresetId,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    string Warning,
    TitleBackgroundCharacterSelectBackgroundMode RecommendedMode,
    string KnownIssue,
    bool SafeToUse,
    bool CharacterExpectedVisible);

internal readonly record struct TitleBackgroundPhase2NOverrideCompatibility(
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

internal readonly record struct TitleBackgroundPhase2NOverrideCandidateDiagnostic(
    TitleBackgroundCharacterSelectOverrideCandidate Selected,
    IReadOnlyList<TitleBackgroundCharacterSelectOverrideCandidate> Available,
    IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> ManualSlots,
    int RegistryCount,
    int RegistryVerifiedCount,
    int RegistryUnverifiedCount,
    int RegistryBrightCount,
    string RegistryDefaultId);

internal readonly record struct TitleBackgroundPhase2NBackgroundApplicationDiagnostic(
    bool Observed,
    bool LastHistoricalOverrideApplied,
    string LastHistoricalOverridePath,
    string CurrentCandidateId,
    bool VisualConfirmationRequired,
    string UserVerdict);

internal readonly record struct TitleBackgroundPhase2NSafetyDiagnostic(
    string Verdict,
    string Reason,
    bool BlocksBackgroundCandidatePromotion);

internal readonly record struct TitleBackgroundPhase2NDeliverySummary(
    string FeatureGoal,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    string BackgroundModeReason,
    string CharacterVisibilityExpected,
    string CharacterVisibilityObserved,
    string CharacterVisibilityBlocker,
    bool NativePreviewSourceSearched,
    IReadOnlyList<TitleBackgroundPhase2NNativeSourceProbe> NativePreviewSources,
    string NativePreviewSourceBestSource,
    string NativePreviewSourceBestCandidate,
    string NativePreviewSourceResolution,
    bool NativePreviewSourceCurrentObjectTableIgnored,
    string NativePreviewSourceCurrentObjectTableIgnoredReason,
    TitleBackgroundPhase2NForegroundPreserveResult ForegroundPreserve,
    TitleBackgroundPhase2NPresetCompatibility PresetCompatibility,
    TitleBackgroundPhase2NOverrideCompatibility OverrideCompatibility,
    TitleBackgroundPhase2NOverrideCandidateDiagnostic OverrideCandidate,
    TitleBackgroundPhase2NLightingDiagnostic Lighting,
    bool ObjectTableActorRejected,
    string ObjectTableActorRejectedReason,
    bool ActorPlacementReady,
    string ActorPlacementBlocker,
    string DeliveryVerdict,
    TitleBackgroundPhase2NBackgroundApplicationDiagnostic BackgroundApplication,
    TitleBackgroundPhase2NSafetyDiagnostic Safety,
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

internal static class TitleBackgroundPhase2NDeliveryDiagnostic
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

    public static TitleBackgroundPhase2NDeliverySummary BuildSummary(
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
        IReadOnlyList<TitleBackgroundPhase2MSourceDiscovery> sourceDiscovery,
        string transitionSafety,
        bool currentObjectTableValidForCharaSelect = true,
        string currentObjectTableInvalidReason = "none",
        string selectedOverrideCandidateId = "",
        IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot>? manualCandidateSlots = null,
        bool historicalLastOverrideApplied = false,
        string historicalLastOverridePath = "",
        bool sceneReadyAcceptedMultipleTimes = false,
        bool activeAfterLoginDetected = false,
        bool phase2GAppliedAfterLogin = false)
    {
        var normalizedManualSlots = manualCandidateSlots ?? [];
        var availableCandidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(normalizedManualSlots);
        var overrideCandidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            selectedOverrideCandidateId,
            overrideScenePath,
            overrideTerritoryId,
            layerFilterKey,
            availableCandidates);
        var overrideCandidateDiagnostic = new TitleBackgroundPhase2NOverrideCandidateDiagnostic(
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
        var nativeResolution = currentObjectTableIgnored
            ? "not-verifiable-post-login"
            : BuildNativeResolution(phase2MResolution, phase2MTransformValidity, nativeSources);
        var objectTableRejected = currentObjectTableIgnored
            || phase2MResolution == "stub-only"
            || (zeroPositionCandidateCount > 0
                && nonZeroPositionCandidateCount == 0
                && drawObjectNonNullCount == 0
                && modelLikeNonNullCount == 0);
        var actorPlacementReady = nativeResolution == "found-single" && !objectTableRejected;
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
        var characterObserved = BuildCharacterVisibilityObserved(
            phase2MActorVisible,
            nativeResolution,
            objectTableRejected,
            nonZeroPositionCandidateCount,
            drawObjectNonNullCount,
            modelLikeNonNullCount);
        var characterBlocker = currentObjectTableIgnored
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
        var userMessage = backgroundApplication.Observed && !overrideCompatibility.CharacterExpectedVisible
            ? "Background was applied as background-only. Selected character model is expected to remain hidden."
            : "Background application still requires Character Select screenshot confirmation.";
        var userNextAction = "Take screenshot in Character Select, then run /xmutbgdiag after login.";
        var transitionUserMessage = postLoginLeakVerdict == "not-observed" && sceneReadyAcceptedMultipleTimes
            ? "No post-login scene override leak observed, but sceneReady was accepted multiple times in this session."
            : postLoginLeakVerdict == "not-observed"
                ? "No post-login scene override leak observed."
                : "Post-login scene override leak was observed; do not promote this candidate.";
        var (mvpStatus, mvpBlockingIssue, mvpKnownLimitation) = BuildMvpSummary(
            verdict,
            transitionSafety,
            overrideCompatibility.BackgroundUsable,
            overrideCompatibility.CharacterExpectedVisible,
            actorPlacementReady,
            currentObjectTableIgnored,
            nativeResolution,
            nextAction,
            characterBlocker);

        return new TitleBackgroundPhase2NDeliverySummary(
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
            actorPlacementReady ? "none" : currentObjectTableIgnored ? currentObjectTableIgnoredReason : objectTableRejected ? "stub-only-object-table" : $"native-preview-source-{nativeResolution}",
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

    private static TitleBackgroundPhase2NBackgroundApplicationDiagnostic BuildBackgroundApplication(
        bool lastOverrideApplied,
        bool historicalLastOverrideApplied,
        string historicalLastOverridePath,
        string currentCandidateId)
    {
        var historicalPath = string.IsNullOrWhiteSpace(historicalLastOverridePath)
            ? "none"
            : historicalLastOverridePath;
        var observed = lastOverrideApplied || historicalLastOverrideApplied;
        return new TitleBackgroundPhase2NBackgroundApplicationDiagnostic(
            observed,
            historicalLastOverrideApplied,
            historicalPath,
            string.IsNullOrWhiteSpace(currentCandidateId) ? "custom" : currentCandidateId,
            true,
            observed ? "background-applied-character-hidden" : "not-confirmed");
    }

    private static TitleBackgroundPhase2NSafetyDiagnostic BuildSafety(
        string transitionSafety,
        bool sceneReadyAcceptedMultipleTimes,
        bool activeAfterLoginDetected,
        bool phase2GAppliedAfterLogin)
    {
        if (activeAfterLoginDetected || phase2GAppliedAfterLogin)
        {
            return new TitleBackgroundPhase2NSafetyDiagnostic(
                "unsafe",
                activeAfterLoginDetected ? "scene-override-active-after-login" : "phase2g-applied-after-login",
                true);
        }

        if (sceneReadyAcceptedMultipleTimes || transitionSafety == "unsafe")
        {
            return new TitleBackgroundPhase2NSafetyDiagnostic(
                "warning",
                sceneReadyAcceptedMultipleTimes ? "scene-ready-accepted-multiple-times" : "login-transition-safety-unsafe",
                true);
        }

        return new TitleBackgroundPhase2NSafetyDiagnostic("safe", "none", false);
    }

    private static string BuildTransitionSafetyVerdict(
        TitleBackgroundPhase2NSafetyDiagnostic safety,
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
            || nativeResolution is "not-found" or "not-verifiable-post-login";
        if (deliveryVerdict == "working-background-only"
            && backgroundUsable
            && !characterExpectedVisible
            && !actorPlacementReady
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
        if (objectTableRejected || nativeResolution == "not-found")
        {
            return "not-observed";
        }

        if (nativeResolution == "found-single"
            && nonZeroPositionCandidateCount > 0
            && (drawObjectNonNullCount > 0 || modelLikeNonNullCount > 0))
        {
            return string.Equals(phase2MActorVisible, "observed", StringComparison.Ordinal)
                ? "observed"
                : "not-verifiable";
        }

        return "not-verifiable";
    }

    public static string EvaluateExperimentalActorPlacement(
        TitleBackgroundPhase2MExperimentalApplyMode mode,
        TitleBackgroundPhase2MSummary summary,
        bool sceneGenerationMatches,
        bool isCharaSelectActive,
        bool isLoggedIn)
    {
        if (mode == TitleBackgroundPhase2MExperimentalApplyMode.None)
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

        if (mode == TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot
            && summary.Resolution == "stub-only")
        {
            return "skip:stub-only-object-table";
        }

        return TitleBackgroundPhase2MPlacementDiagnostic.EvaluateExperimentalApply(
            mode,
            summary,
            sceneGenerationMatches,
            isCharaSelectActive,
            isLoggedIn);
    }

    private static TitleBackgroundPhase2NPresetCompatibility BuildPresetCompatibility(
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
            return new TitleBackgroundPhase2NPresetCompatibility(
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

        return new TitleBackgroundPhase2NPresetCompatibility(
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

    private static TitleBackgroundPhase2NOverrideCompatibility BuildOverrideCompatibility(
        string selectedPresetId,
        string territoryPath,
        uint territoryId,
        uint layerFilterKey,
        TitleBackgroundPhase2NPresetCompatibility presetCompatibility,
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

        return new TitleBackgroundPhase2NOverrideCompatibility(
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

    private static TitleBackgroundPhase2NForegroundPreserveResult BuildForegroundPreserve(
        TitleBackgroundCharacterSelectBackgroundMode backgroundMode,
        string overrideScenePath)
    {
        return new TitleBackgroundPhase2NForegroundPreserveResult(
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

    private static IReadOnlyList<TitleBackgroundPhase2NNativeSourceProbe> BuildNativeSourceProbes(
        IReadOnlyList<TitleBackgroundPhase2MSourceDiscovery> sourceDiscovery,
        int zeroPositionCandidateCount,
        int nonZeroPositionCandidateCount,
        int drawObjectNonNullCount,
        int modelLikeNonNullCount)
    {
        var probes = new List<TitleBackgroundPhase2NNativeSourceProbe>();
        foreach (var source in sourceDiscovery)
        {
            var objectTableLike = source.Name is "ObjectTable" or "PlayerObjects" or "CharacterManagerObjects";
            probes.Add(new TitleBackgroundPhase2NNativeSourceProbe(
                source.Name,
                source.Available,
                source.Available ? "read" : "not-available",
                source.CandidateCount,
                objectTableLike ? source.NonZeroTransformCount : 0,
                objectTableLike ? source.DrawObjectNonNullCount : 0,
                objectTableLike ? source.ModelLikeNonNullCount : 0,
                string.IsNullOrWhiteSpace(source.Error) ? "none" : source.Error));
        }

        if (probes.All(probe => probe.Name != "CharaSelectCharacterManager"))
        {
            probes.Add(new TitleBackgroundPhase2NNativeSourceProbe("CharaSelectCharacterManager", false, "not-resolved", 0, 0, 0, 0, "no public field or signature-safe source"));
        }

        if (probes.All(probe => !probe.Name.StartsWith("UIStage", StringComparison.Ordinal)))
        {
            probes.Add(new TitleBackgroundPhase2NNativeSourceProbe("UIStage CharaSelect model source", false, "not-resolved", 0, 0, 0, 0, "no safe model owner path found"));
        }

        return probes;
    }

    private static string BuildNativeResolution(
        string phase2MResolution,
        string transformValidity,
        IReadOnlyList<TitleBackgroundPhase2NNativeSourceProbe> nativeSources)
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

    private static TitleBackgroundPhase2NLightingDiagnostic BuildLightingDiagnostic(
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

        return new TitleBackgroundPhase2NLightingDiagnostic(
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
        TitleBackgroundPhase2NForegroundPreserveResult foreground,
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
        TitleBackgroundPhase2NPresetCompatibility compatibility,
        TitleBackgroundPhase2NLightingDiagnostic lighting)
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

        if (nativeResolution == "found-single")
        {
            return ("needs-one-more-experimental-run", "try-native-preview-source", "native preview source candidate is ready but default-off");
        }

        if (lighting.RecommendedAction is "try-bright-layer" or "try-bright-custom-target" or "verify-manual-bright-candidate")
        {
            return ("working-background-only", lighting.RecommendedAction, "background is available but brightness is dark");
        }

        if (lastOverrideApplied)
        {
            return ("working-background-only", "use-background-only", "background delivery works; selected character source is not available");
        }

        if (compatibility.ExpectedCompatibility is TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
            or TitleBackgroundCharacterSelectCompatibility.CharacterHidden)
        {
            return ("needs-one-more-experimental-run", "use-background-only", "preset metadata allows background-only, but this run has not observed the scene override yet");
        }

        return ("blocked-character-source-not-found", "try-compatible-preset", "native preview source was not found and no applied background was observed");
    }
}

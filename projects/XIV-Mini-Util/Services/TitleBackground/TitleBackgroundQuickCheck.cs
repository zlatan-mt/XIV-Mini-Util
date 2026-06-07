// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundQuickCheck.cs
// Description: Character Select 背景 QuickCheck の状態、判定、UI表示文字列をまとめる
// Reason: /xmutbgdiag の累積診断に頼らず、1回の確認結果を OK/WARN/NG で出すため
namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundQuickCheckLevel
{
    NotRun,
    OK,
    WARN,
    NG,
}

public enum TitleBackgroundQuickCheckRunState
{
    Idle,
    Armed,
    CharaSelectObserved,
    LoggedInObserved,
    Completed,
    Failed,
}

public enum TitleBackgroundSettingsDisplayMode
{
    Simple,
    Advanced,
    DeveloperDiagnostics,
}

internal readonly record struct TitleBackgroundQuickCheckState(
    TitleBackgroundQuickCheckRunState RunState,
    DateTimeOffset? StartedAt,
    int SceneGenerationStart,
    int SceneReadyAcceptedCountStart,
    int OverrideAppliedCountStart,
    int Phase2GApplyCountStart,
    string SelectedCandidateIdStart,
    string EffectiveCandidateIdStart,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundModeStart,
    TitleBackgroundCharacterSelectLightingMode LightingModeStart,
    string CurrentLobbyMapStart,
    bool StartedLoggedIn)
{
    public static TitleBackgroundQuickCheckState Idle { get; } = new(
        TitleBackgroundQuickCheckRunState.Idle,
        null,
        0,
        0,
        0,
        0,
        string.Empty,
        string.Empty,
        TitleBackgroundCharacterSelectBackgroundMode.Disabled,
        TitleBackgroundCharacterSelectLightingMode.Default,
        "None",
        false);
}

internal readonly record struct TitleBackgroundQuickCheckInput(
    bool RunScoped,
    TitleBackgroundQuickCheckRunState RunState,
    DateTimeOffset? StartedAt,
    DateTimeOffset CompletedAt,
    bool StartedLoggedIn,
    bool CharaSelectObserved,
    int SceneGenerationStart,
    int SceneGenerationEnd,
    int SceneReadyAcceptedCount,
    int OverrideAppliedCount,
    int Phase2GApplyCount,
    bool PluginOrHookError,
    string PluginOrHookErrorReason,
    bool IsLoggedIn,
    string CurrentLobbyMap,
    bool CurrentLobbyMapRemainedAfterLogin,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    TitleBackgroundCharacterSelectLightingMode LightingMode,
    string CandidateId,
    string CandidateDisplayName,
    bool CandidateVerifiedInGame,
    string CandidateSource,
    TitleBackgroundCharacterSelectCompatibility ExpectedCompatibility,
    TitleBackgroundCharacterSelectExpectedBrightness ExpectedBrightness,
    string OverrideTerritoryPath,
    uint OverrideTerritoryId,
    uint OverrideLayerFilterKey,
    bool CandidateFieldsValid,
    bool BackgroundApplied,
    bool BackgroundObserved,
    bool VisualConfirmationRequired,
    bool CharacterExpectedVisible,
    string CharacterObserved,
    bool CharacterKnownLimitation,
    bool SceneOverrideActiveAfterLogin,
    bool ActiveAfterLoginDetected,
    bool StaleCharaSelectStateAfterLogin,
    bool Phase2GAppliedAfterLogin,
    bool ForegroundPreserveUnavailable,
    bool ActorSourceAmbiguous,
    bool ObjectTableZeroTransformStubs,
    TitleBackgroundCharacterVisualStatus CharacterVisualStatus = TitleBackgroundCharacterVisualStatus.Unknown,
    TitleBackgroundCharaSelectCameraFramingMode CameraFramingMode = TitleBackgroundCharaSelectCameraFramingMode.Default,
    TitleBackgroundCharaSelectCameraFramingMode CandidateRecommendedFraming = TitleBackgroundCharaSelectCameraFramingMode.Default,
    string CandidateRecommendedAction = "",
    bool TitleBackgroundOverrideEnabledAtCheck = true,
    bool TitleBackgroundCameraOverrideEnabledAtCheck = true,
    bool LegacySceneCompositionEnabledAtCheck = false,
    bool TitleBackgroundIntegratedCompositionEnabledAtCheck = true,
    bool ShouldArmAdapterAtCheck = true,
    string ShouldArmAdapterReasonAtCheck = "");

internal readonly record struct TitleBackgroundQuickCheckResult(
    TitleBackgroundQuickCheckLevel Level,
    TitleBackgroundQuickCheckRunState RunState,
    string Reason,
    string CandidateId,
    string CandidateDisplayName,
    DateTimeOffset CompletedAt,
    string NextAction,
    string DetailFileName,
    TitleBackgroundCharacterSelectBackgroundMode BackgroundMode,
    string BackgroundStatus,
    string LoginTransitionStatus,
    string PostLoginLeakStatus,
    string CharacterStatus,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DetailLines);

internal static class TitleBackgroundQuickCheckEvaluator
{
    public const string DetailFileName = "title-background-quickcheck.txt";

    public static TitleBackgroundQuickCheckResult Evaluate(TitleBackgroundQuickCheckInput input)
    {
        var warnings = new List<string>();
        var ngReason = GetNgReason(input);
        var loginChecked = IsLoginTransitionChecked(input);

        if (!input.RunScoped)
        {
            warnings.Add("QuickCheck was not started; run-scoped confidence is limited");
        }

        if (input.RunScoped && input.StartedLoggedIn)
        {
            warnings.Add("Start QuickCheck from title/character select for a clean run. Current run started while already logged in.");
        }

        if (input.RunScoped && !input.CharaSelectObserved)
        {
            warnings.Add("Character Select was not observed during this QuickCheck run");
        }

        if (!loginChecked)
        {
            warnings.Add("login transition has not been checked yet");
        }

        if (input.SceneReadyAcceptedCount > 1)
        {
            warnings.Add($"sceneReady accepted multiple times during this run ({input.SceneReadyAcceptedCount})");
        }

        if (!input.CandidateVerifiedInGame)
        {
            warnings.Add("selected candidate is unverified");
        }

        if (input.VisualConfirmationRequired)
        {
            warnings.Add("visual confirmation is required");
        }

        if (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.VisibleButTooSmall
                or TitleBackgroundCharacterVisualStatus.VisibleTopDown)
        {
            var framingDetail = input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default
                ? "try lower camera framing"
                : "framing still needs tuning";
            warnings.Add($"camera framing needs adjustment: character is top-down or too small / {framingDetail}");
        }

        if (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.NotVisible
                or TitleBackgroundCharacterVisualStatus.Offscreen)
        {
            warnings.Add("character not visible or offscreen in frame; camera framing may be misaligned");
        }

        if (input.IsLoggedIn
            && string.Equals(input.CurrentLobbyMap, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("currentLobbyMap could not be read after login");
        }

        var level = !string.IsNullOrWhiteSpace(ngReason)
            ? TitleBackgroundQuickCheckLevel.NG
            : warnings.Count > 0
                ? TitleBackgroundQuickCheckLevel.WARN
                : TitleBackgroundQuickCheckLevel.OK;
        var reason = level switch
        {
            TitleBackgroundQuickCheckLevel.NG => ngReason,
            TitleBackgroundQuickCheckLevel.WARN when input.RunScoped && input.StartedLoggedIn => "QuickCheck started while already logged in. Start from title/character select for a clean run.",
            TitleBackgroundQuickCheckLevel.WARN when input.RunScoped && !input.CharaSelectObserved => "Character Select was not observed during this QuickCheck run.",
            TitleBackgroundQuickCheckLevel.WARN when !loginChecked => "login transition has not been checked yet. Log in, then run QuickCheck again.",
            TitleBackgroundQuickCheckLevel.WARN => "background works with warnings",
            _ => "background-only works, no post-login leak",
        };
        var runState = level == TitleBackgroundQuickCheckLevel.NG
            ? TitleBackgroundQuickCheckRunState.Failed
            : TitleBackgroundQuickCheckRunState.Completed;
        var nextAction = BuildNextAction(level, input, warnings);
        var result = new TitleBackgroundQuickCheckResult(
            level,
            runState,
            reason,
            NormalizeNone(input.CandidateId),
            NormalizeNone(input.CandidateDisplayName),
            input.CompletedAt,
            nextAction,
            DetailFileName,
            input.BackgroundMode,
            input.BackgroundApplied && input.BackgroundObserved ? "applied" : "not applied",
            BuildLoginTransitionStatus(input, loginChecked, level),
            BuildPostLoginLeakStatus(input, loginChecked),
            BuildCharacterStatus(input),
            warnings,
            []);

        return result with
        {
            DetailLines = BuildDetailLines(input, result),
        };
    }

    private static string GetNgReason(TitleBackgroundQuickCheckInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CandidateId) || input.CandidateId == "none")
        {
            return "effective candidate was none";
        }

        if (!input.CandidateFieldsValid)
        {
            return "selected candidate has invalid territory path/id/layer";
        }

        if (input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled)
        {
            return "background mode is disabled";
        }

        if (!TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(input.BackgroundMode))
        {
            return "background mode does not apply scene override";
        }

        if (!input.TitleBackgroundOverrideEnabledAtCheck)
        {
            return "Character Select Background is disabled";
        }

        if (!input.TitleBackgroundCameraOverrideEnabledAtCheck)
        {
            return "Title Background camera override is disabled";
        }

        if (!input.ShouldArmAdapterAtCheck)
        {
            return $"adapter was not armed: {NormalizeNone(input.ShouldArmAdapterReasonAtCheck)}";
        }

        if (input.OverrideAppliedCount <= 0 || !input.BackgroundApplied || !input.BackgroundObserved)
        {
            return !input.TitleBackgroundIntegratedCompositionEnabledAtCheck
                ? "integrated character composition was not armed"
                : "background was not applied";
        }

        if (input.PluginOrHookError)
        {
            return $"plugin or hook error: {NormalizeNone(input.PluginOrHookErrorReason)}";
        }

        if (input.IsLoggedIn && input.CurrentLobbyMapRemainedAfterLogin)
        {
            return "currentLobbyMap remained after login";
        }

        if (input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected)
        {
            return "post-login scene override leak detected";
        }

        if (input.Phase2GAppliedAfterLogin)
        {
            return "Phase2G applied after login";
        }

        if (input.StaleCharaSelectStateAfterLogin)
        {
            return "stale Character Select state remained after login";
        }

        return string.Empty;
    }

    private static string BuildNextAction(
        TitleBackgroundQuickCheckLevel level,
        TitleBackgroundQuickCheckInput input,
        IReadOnlyList<string> warnings)
    {
        if (level == TitleBackgroundQuickCheckLevel.NG)
        {
            return input.BackgroundMode == TitleBackgroundCharacterSelectBackgroundMode.Disabled
                ? "enable Character Select background and select custom:n4f4"
                : "check Settings UI candidate selection and transition diagnostics";
        }

        if (warnings.Any(warning => warning.Contains("login transition has not been checked", StringComparison.Ordinal)))
        {
            return "log in, then run QuickCheck again";
        }

        if (warnings.Any(warning => warning.Contains("already logged in", StringComparison.Ordinal)
                || warning.Contains("Character Select was not observed", StringComparison.Ordinal)))
        {
            return "start QuickCheck from title/character select, then log in and run check";
        }

        if (warnings.Any(warning => warning.Contains("run-scoped confidence", StringComparison.Ordinal)))
        {
            return "start QuickCheck, enter Character Select, log in, then run check";
        }

        if (warnings.Any(warning => warning.Contains("sceneReady accepted multiple", StringComparison.Ordinal)))
        {
            return "retry once from clean title screen if needed";
        }

        if (warnings.Any(warning => warning.Contains("framing needs adjustment", StringComparison.Ordinal)))
        {
            return input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default
                ? "try Camera framing = Lower camera or n4f4 experimental"
                : "framing still needs tuning; adjust preset offset";
        }

        if (warnings.Any(warning => warning.Contains("not visible or offscreen", StringComparison.Ordinal)))
        {
            return "try another camera framing or reset visual status after screenshot";
        }

        return "use background-only, or add a bright candidate";
    }

    public static IReadOnlyList<string> BuildChatLines(TitleBackgroundQuickCheckResult result)
    {
        var lines = new List<string>
        {
            $"[XMU QuickCheck] {result.Level}",
            $"Candidate: {result.CandidateId}" + (result.CandidateDisplayName == "none" ? string.Empty : $" / {result.CandidateDisplayName}"),
            $"Mode: {result.BackgroundMode}",
            $"Background: {result.BackgroundStatus}",
            $"Login transition: {result.LoginTransitionStatus}",
            $"Post-login leak: {result.PostLoginLeakStatus}",
            $"Character: {result.CharacterStatus}",
            $"Reason: {result.Reason} / Next: {result.NextAction}",
            $"Details: {result.DetailFileName}",
        };

        foreach (var warning in result.Warnings.Take(Math.Max(0, 10 - lines.Count)))
        {
            lines.Insert(lines.Count - 2, $"Warning: {warning}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildDetailLines(
        TitleBackgroundQuickCheckInput input,
        TitleBackgroundQuickCheckResult result)
    {
        return
        [
            $"result={result.Level}",
            $"reason={NormalizeNone(result.Reason)}",
            $"candidate.id={NormalizeNone(input.CandidateId)}",
            $"candidate.displayName={NormalizeNone(input.CandidateDisplayName)}",
            $"candidate.verifiedInGame={input.CandidateVerifiedInGame}",
            $"candidate.source={NormalizeNone(input.CandidateSource)}",
            $"candidate.expectedCompatibility={input.ExpectedCompatibility}",
            $"candidate.brightness={input.ExpectedBrightness}",
            $"background.mode={input.BackgroundMode}",
            $"lighting.mode={input.LightingMode}",
            $"override.path={NormalizeNone(input.OverrideTerritoryPath)}",
            $"override.territoryId={input.OverrideTerritoryId}",
            $"override.layerFilterKey={input.OverrideLayerFilterKey}",
            $"background.applied={input.BackgroundApplied}",
            $"background.observed={input.BackgroundObserved}",
            $"background.status={result.BackgroundStatus}",
            $"character.expectedVisible={input.CharacterExpectedVisible}",
            $"character.observed={NormalizeNone(input.CharacterObserved)}",
            $"character.knownLimitation={input.CharacterKnownLimitation}",
            $"character.status={NormalizeNone(result.CharacterStatus)}",
            $"character.visualStatus={input.CharacterVisualStatus}",
            $"camera.framingMode={input.CameraFramingMode}",
            $"camera.framingOffset={TitleBackgroundCharaSelectCameraLogic.GetCameraFramingCurveOffset(input.CameraFramingMode):F3}",
            $"camera.profileSource={BuildCameraProfileSource(input)}",
            $"camera.recommendedFraming={input.CandidateRecommendedFraming}",
            $"camera.recommendedAction={NormalizeNone(input.CandidateRecommendedAction)}",
            $"knownLimitation.characterHidden={!input.CharacterExpectedVisible || input.CharacterKnownLimitation}",
            $"knownLimitation.foregroundPreserveUnavailable={input.ForegroundPreserveUnavailable}",
            $"knownLimitation.brightnessDark={input.ExpectedBrightness is TitleBackgroundCharacterSelectExpectedBrightness.Dark or TitleBackgroundCharacterSelectExpectedBrightness.TooDark}",
            $"developerNote.actorSourceAmbiguous={input.ActorSourceAmbiguous}",
            $"developerNote.objectTableZeroTransformStubs={input.ObjectTableZeroTransformStubs}",
            $"postLogin.sceneOverrideActive={input.SceneOverrideActiveAfterLogin}",
            $"postLogin.activeAfterLoginDetected={input.ActiveAfterLoginDetected}",
            $"postLogin.phase2GAppliedAfterLogin={input.Phase2GAppliedAfterLogin}",
            $"postLogin.currentLobbyMap={NormalizeNone(input.CurrentLobbyMap)}",
            $"postLogin.currentLobbyMapRemained={input.CurrentLobbyMapRemainedAfterLogin}",
            $"postLogin.loginTransitionStatus={NormalizeNone(result.LoginTransitionStatus)}",
            $"postLogin.leakStatus={NormalizeNone(result.PostLoginLeakStatus)}",
            $"quickCheck.titleBackgroundOverrideEnabled={input.TitleBackgroundOverrideEnabledAtCheck}",
            $"quickCheck.titleBackgroundCameraOverrideEnabled={input.TitleBackgroundCameraOverrideEnabledAtCheck}",
            $"quickCheck.legacySceneCompositionEnabled={input.LegacySceneCompositionEnabledAtCheck}",
            $"quickCheck.integratedCompositionEnabled={input.TitleBackgroundIntegratedCompositionEnabledAtCheck}",
            $"quickCheck.shouldArmAdapter={input.ShouldArmAdapterAtCheck}",
            $"quickCheck.shouldArmAdapter.reason={NormalizeNone(input.ShouldArmAdapterReasonAtCheck)}",
            $"quickCheck.runScoped={input.RunScoped}",
            $"quickCheck.startedLoggedIn={input.StartedLoggedIn}",
            $"quickCheck.charaSelectObserved={input.CharaSelectObserved}",
            $"quickCheck.state={result.RunState}",
            $"quickCheck.sceneReadyAcceptedCount={input.SceneReadyAcceptedCount}",
            $"quickCheck.overrideAppliedCount={input.OverrideAppliedCount}",
            $"quickCheck.phase2GApplyCount={input.Phase2GApplyCount}",
            $"quickCheck.sceneGenerationStart={input.SceneGenerationStart}",
            $"quickCheck.sceneGenerationEnd={input.SceneGenerationEnd}",
            $"quickCheck.startedAt={FormatTime(input.StartedAt)}",
            $"quickCheck.completedAt={FormatTime(input.CompletedAt)}",
            $"quickCheck.warningCount={result.Warnings.Count}",
            $"nextAction={NormalizeNone(result.NextAction)}",
            "detail.transition=title-background-transitiondiag.txt",
            "detail.placement=title-background-placementdiag.txt",
            "detail.delivery=title-background-deliverydiag.txt",
        ];
    }

    public static bool IsSafeAfterLoginAdapterState(string? state)
    {
        return NormalizeNone(state) is "Inactive" or "Stopping";
    }

    public static bool IsUnsafeAfterLoginAdapterState(string? state)
    {
        var normalized = NormalizeNone(state);
        return normalized.Contains("Active", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Running", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Applying", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoginTransitionChecked(TitleBackgroundQuickCheckInput input)
    {
        if (input.RunScoped)
        {
            return input.CharaSelectObserved
                && !input.StartedLoggedIn
                && input.RunState is TitleBackgroundQuickCheckRunState.LoggedInObserved
                    or TitleBackgroundQuickCheckRunState.Completed;
        }

        return input.IsLoggedIn;
    }

    private static string BuildLoginTransitionStatus(
        TitleBackgroundQuickCheckInput input,
        bool loginChecked,
        TitleBackgroundQuickCheckLevel level)
    {
        if (!loginChecked)
        {
            return "not checked";
        }

        if (level == TitleBackgroundQuickCheckLevel.NG
            && (input.SceneOverrideActiveAfterLogin
                || input.ActiveAfterLoginDetected
                || input.StaleCharaSelectStateAfterLogin
                || input.Phase2GAppliedAfterLogin
                || input.CurrentLobbyMapRemainedAfterLogin))
        {
            return "ng";
        }

        return input.SceneReadyAcceptedCount > 1 ? "warn" : "safe";
    }

    private static string BuildPostLoginLeakStatus(TitleBackgroundQuickCheckInput input, bool loginChecked)
    {
        if (!loginChecked)
        {
            return "not checked";
        }

        return input.SceneOverrideActiveAfterLogin || input.ActiveAfterLoginDetected
            ? "detected"
            : "none";
    }

    private static string BuildCharacterStatus(TitleBackgroundQuickCheckInput input)
    {
        var visualStatusLabel = input.CharacterVisualStatus switch
        {
            TitleBackgroundCharacterVisualStatus.Visible => "visible",
            TitleBackgroundCharacterVisualStatus.VisibleButTooSmall => "visible / too small",
            TitleBackgroundCharacterVisualStatus.VisibleTopDown => "visible / top-down",
            TitleBackgroundCharacterVisualStatus.NotVisible => "not visible",
            TitleBackgroundCharacterVisualStatus.Offscreen => "offscreen",
            _ => null,
        };

        if (visualStatusLabel != null)
        {
            if (input.CharacterVisualStatus is TitleBackgroundCharacterVisualStatus.VisibleButTooSmall
                    or TitleBackgroundCharacterVisualStatus.VisibleTopDown)
            {
                return $"{visualStatusLabel} / camera framing needs adjustment";
            }

            return visualStatusLabel;
        }

        if (!input.CharacterExpectedVisible || input.CharacterKnownLimitation)
        {
            return "not detected by diagnostics / visual confirmation required";
        }

        return NormalizeNone(input.CharacterObserved);
    }

    private static string BuildCameraProfileSource(TitleBackgroundQuickCheckInput input)
    {
        if (input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.Default)
        {
            return "default";
        }

        if (input.CameraFramingMode == TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended
            || (input.CandidateRecommendedFraming != TitleBackgroundCharaSelectCameraFramingMode.Default
                && input.CameraFramingMode == input.CandidateRecommendedFraming))
        {
            return "candidate-recommended";
        }

        return "user-selected";
    }

    private static string FormatTime(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "none";
    }

    private static string NormalizeNone(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}

internal readonly record struct TitleBackgroundQuickCheckUiSummary(
    TitleBackgroundQuickCheckLevel Level,
    string StatusLine,
    string CandidateLine,
    string LastResultLine,
    string LastReasonLine,
    string NextActionLine,
    string DetailLine,
    string KnownLimitationLine);

internal static class TitleBackgroundQuickCheckUiPresenter
{
    public static TitleBackgroundQuickCheckUiSummary BuildSummary(Configuration configuration)
    {
        var level = NormalizeLevel(configuration.TitleBackgroundLastQuickCheckResult);
        var candidate = string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckCandidateId)
            ? configuration.TitleBackgroundCharacterSelectOverrideCandidateId
            : configuration.TitleBackgroundLastQuickCheckCandidateId;
        var candidateLine = string.IsNullOrWhiteSpace(candidate)
            ? "Character Select Background: none"
            : $"Character Select Background: {candidate}";
        var reason = string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckReason)
            ? "Not checked yet"
            : configuration.TitleBackgroundLastQuickCheckReason;
        var statusLine = level switch
        {
            TitleBackgroundQuickCheckLevel.OK => "Status: OK - background-only works",
            TitleBackgroundQuickCheckLevel.WARN => $"Status: WARN - {reason}",
            TitleBackgroundQuickCheckLevel.NG => $"Status: NG - {reason}",
            _ => string.IsNullOrWhiteSpace(configuration.TitleBackgroundLastQuickCheckReason)
                ? "Status: Not checked yet"
                : $"Status: {reason}",
        };

        return new TitleBackgroundQuickCheckUiSummary(
            level,
            statusLine,
            candidateLine,
            level == TitleBackgroundQuickCheckLevel.NotRun
                ? "Last QuickCheck Result: Not Run"
                : $"Last QuickCheck Result: {level}",
            $"Last Reason: {reason}",
            $"Next Action: {NormalizeForUi(configuration.TitleBackgroundLastQuickCheckNextAction, "Run QuickCheck after entering Character Select and logging in once.")}",
            $"Detail: {NormalizeForUi(configuration.TitleBackgroundLastQuickCheckDetailFileName, TitleBackgroundQuickCheckEvaluator.DetailFileName)}",
            "Known limitation: character source is not resolved by diagnostics; visual confirmation is required. Character may appear off-center or too small with current camera framing.");
    }

    public static IReadOnlyList<string> GetSimpleModeItems(Configuration configuration)
    {
        var summary = BuildSummary(configuration);
        return
        [
            "Enable Character Select Background",
            "Background Candidate",
            summary.StatusLine,
            "Start QuickCheck",
            "Run Check",
            summary.LastResultLine,
            summary.KnownLimitationLine,
            summary.NextActionLine,
        ];
    }

    public static IReadOnlyList<string> GetAdvancedModeItems(Configuration configuration)
    {
        return
        [
            "Runtime Mode",
            "Background Mode",
            "Lighting Mode",
            "Effective Candidate Details",
            "Clear",
        ];
    }

    public static bool IsExperimentalModeVisibleInSimple(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return false;
    }

    public static string BuildCandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        var verified = candidate.VerifiedInGame ? "Verified" : "Unverified";
        var compatibility = candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
            ? "Background-only"
            : candidate.ExpectedCompatibility.ToString();
        return $"{candidate.Id} - {candidate.DisplayName} [{verified} / {candidate.ExpectedBrightness} / {compatibility} / {candidate.Source}]";
    }

    public static string GetBackgroundModeUiLabel(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "Off",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "Diagnostics only",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => "Background only",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "Background only / recommended",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => "Experimental: preserve character foreground",
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => "Experimental: native preview model source",
            _ => mode.ToString(),
        };
    }

    public static string GetBackgroundModeTooltip(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectBackgroundMode.Disabled => "Character Select background override is disabled.",
            TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly => "Collect diagnostics without changing the background.",
            TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly => "Replace the full lobby scene as background-only. Character model is expected to be hidden.",
            TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly => "Use only candidates known to be compatible as background-only.",
            TitleBackgroundCharacterSelectBackgroundMode.PreserveCharaSelectForeground => "Experimental. Not supported as stable because no safe public hook point is known.",
            TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource => "Experimental. Native CharaSelect actor source is not resolved yet.",
            _ => string.Empty,
        };
    }

    private static TitleBackgroundQuickCheckLevel NormalizeLevel(TitleBackgroundQuickCheckLevel level)
    {
        return Enum.IsDefined(typeof(TitleBackgroundQuickCheckLevel), level)
            ? level
            : TitleBackgroundQuickCheckLevel.NotRun;
    }

    private static string NormalizeForUi(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

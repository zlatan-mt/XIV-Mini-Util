// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.QuickCheck.cs
// Description: TitleBackground の QuickCheck 実行状態と評価入力の組み立てを提供する
// Reason: QuickCheck 統合処理を TitleScreenBackgroundService の本体状態管理から分離するため
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService
{
    public IReadOnlyList<string> StartQuickCheck()
    {
        var currentMap = TryReadCurrentLobbyMap(out var map) ? map.ToString() : "unknown";
        var candidate = ResolveCurrentOverrideCandidate();
        // Reset integrated composition route tracking for this run before recording the baseline.
        // Route invocation below may trigger a scene reload; the override counter change will be
        // captured relative to the baseline saved in _quickCheckState.
        _integratedCompositionRouteInvoked = false;
        _integratedCompositionRouteLastReason = string.Empty;
        _charaSelectService?.ResetTitleBackgroundCharacterCompositionBridgeSnapshot();
        if (_configuration.TitleBackgroundIntegratedCompositionEnabled)
        {
            // Invoke before saving the baseline so CreateScene fires before the user logs in.
            TryInvokeIntegratedCompositionRoute();
            _charaSelectService?.ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState();
        }

        _quickCheckState = new TitleBackgroundQuickCheckState(
            TitleBackgroundQuickCheckRunState.Armed,
            DateTimeOffset.Now,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
            _sceneReadySignalAcceptedCount,
            _quickCheckOverrideAppliedCount,
            GetPhase2GApplyCount(),
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            candidate.Id,
            _configuration.TitleBackgroundCharacterSelectBackgroundMode,
            _configuration.TitleBackgroundCharacterSelectLightingMode,
            currentMap,
            _clientState.IsLoggedIn);
        _configuration.TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.NotRun;
        _configuration.TitleBackgroundLastQuickCheckCandidateId = candidate.Id;
        var startReason = _clientState.IsLoggedIn
            ? "Start QuickCheck from title/character select for a clean run. Current run started while already logged in."
            : "Armed: enter Character Select, log in, then run check";
        _configuration.TitleBackgroundLastQuickCheckReason = startReason;
        _configuration.TitleBackgroundLastQuickCheckNextAction = _clientState.IsLoggedIn
            ? "return to title/character select, start QuickCheck, then log in and run check"
            : "enter Character Select, log in, then run check";
        _configuration.TitleBackgroundLastQuickCheckTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
        _configuration.TitleBackgroundLastQuickCheckDetailFileName = TitleBackgroundQuickCheckEvaluator.DetailFileName;
        _configuration.Save();

        return
        [
            "[XMU QuickCheck] START",
            $"Candidate: {candidate.Id} / {candidate.DisplayName}",
            $"Next: {_configuration.TitleBackgroundLastQuickCheckNextAction}",
        ];
    }

    public IReadOnlyList<string> GetQuickCheckStatusLines()
    {
        var last = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        return
        [
            $"[XMU QuickCheck] state={_quickCheckState.RunState}",
            $"Started: {(_quickCheckState.StartedAt.HasValue ? _quickCheckState.StartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "none")}",
            last.LastResultLine,
            last.LastReasonLine,
            last.NextActionLine,
            last.DetailLine,
        ];
    }

    public IReadOnlyList<string> ResetQuickCheck()
    {
        _quickCheckState = TitleBackgroundQuickCheckState.Idle;
        _configuration.TitleBackgroundLastQuickCheckResult = TitleBackgroundQuickCheckLevel.NotRun;
        _configuration.TitleBackgroundLastQuickCheckCandidateId = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckReason = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckNextAction = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckTime = string.Empty;
        _configuration.TitleBackgroundLastQuickCheckDetailFileName = string.Empty;
        _configuration.Save();

        return
        [
            "[XMU QuickCheck] RESET",
            "Next: run /xmutbgcheck start before the next Character Select login check.",
        ];
    }

    public IReadOnlyList<string> RunQuickCheck()
    {
        var result = EvaluateQuickCheck();
        SaveQuickCheckResult(result);
        _quickCheckState = _quickCheckState with { RunState = result.RunState };
        return TitleBackgroundQuickCheckEvaluator.BuildChatLines(result);
    }

    private TitleBackgroundQuickCheckResult EvaluateQuickCheck()
    {
        var input = BuildQuickCheckInput();
        return TitleBackgroundQuickCheckEvaluator.Evaluate(input);
    }

    private TitleBackgroundQuickCheckInput BuildQuickCheckInput()
    {
        var candidate = ResolveCurrentOverrideCandidate();
        var runScoped = _quickCheckState.StartedAt.HasValue
            && _quickCheckState.RunState != TitleBackgroundQuickCheckRunState.Idle;
        var sceneReadyAcceptedCount = runScoped
            ? Math.Max(0, _sceneReadySignalAcceptedCount - _quickCheckState.SceneReadyAcceptedCountStart)
            : _sceneReadySignalAcceptedCount;
        var overrideAppliedCount = runScoped
            ? Math.Max(0, _quickCheckOverrideAppliedCount - _quickCheckState.OverrideAppliedCountStart)
            : _quickCheckOverrideAppliedCount;
        var phase2GApplyCount = runScoped
            ? Math.Max(0, GetPhase2GApplyCount() - _quickCheckState.Phase2GApplyCountStart)
            : GetPhase2GApplyCount();
        var currentLobbyMapAvailable = TryReadCurrentLobbyMap(out var currentLobbyMap);
        var currentLobbyMapName = currentLobbyMapAvailable ? currentLobbyMap.ToString() : "unknown";
        var currentLobbyMapRemainedAfterLogin = _clientState.IsLoggedIn
            && currentLobbyMapAvailable
            && currentLobbyMap != GameLobbyType.None;
        var phase2MSummary = TitleBackgroundCharacterPlacementDiagnostic.BuildSummary(_phase2MPlacementFrames.Values);
        var characterKnownLimitation = !candidate.CharacterExpectedVisible
            || string.Equals(phase2MSummary.ActorVisible, "not-observed", StringComparison.OrdinalIgnoreCase);
        var actorSourceAmbiguous = string.Equals(GetLatestCharacterPlacementActorCandidateStatus(), "ambiguous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase2MSummary.Resolution, "ambiguous", StringComparison.OrdinalIgnoreCase);
        var zeroTransformStubs = phase2MSummary.ZeroPositionCandidateCount > 0
            && phase2MSummary.NonZeroPositionCandidateCount == 0;
        var backgroundApplied = runScoped
            ? overrideAppliedCount > 0
            : overrideAppliedCount > 0 || (_lastOverrideApplied && _lastOverrideLobbyType == GameLobbyType.CharaSelect);
        var adapterState = _charaSelectCameraAdapter.State.ToString();
        var staleCharaSelectStateAfterLogin = _transitionDiagnostics.StaleAdapterStateAfterLogin
            || (_clientState.IsLoggedIn && TitleBackgroundQuickCheckEvaluator.IsUnsafeAfterLoginAdapterState(adapterState));
        var activeAfterLoginDetected = _transitionDiagnostics.StaleSceneOverrideStateAfterLogin
            || (_clientState.IsLoggedIn && _activeSceneOverride);
        var pluginOrHookError = _state is TitleBackgroundServiceState.InvalidConfiguration
            or TitleBackgroundServiceState.AddressResolveFailed
            or TitleBackgroundServiceState.HookCreateFailed
            or TitleBackgroundServiceState.HookEnableFailed
            or TitleBackgroundServiceState.RuntimeError;
        var candidateFieldsValid = !string.IsNullOrWhiteSpace(candidate.TerritoryPath)
            && candidate.TerritoryPath != "none"
            && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(candidate.TerritoryPath)
            && candidate.TerritoryId != 0
            && candidate.LayerFilterKey != 0;

        var serviceReady = _state == TitleBackgroundServiceState.Ready;
        // Derive shouldArmAdapter from reason so both fields are always consistent.
        // ShouldArmAdapter(3 params) is kept for actual adapter arming in ConfigureCharaSelectCameraAdapter;
        // the QuickCheck diagnostic must reflect ALL conditions including integrated composition.
        var shouldArmAdapterReason = TitleBackgroundCharaSelectCameraLogic.BuildShouldArmAdapterReason(
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundIntegratedCompositionEnabled,
            candidateFieldsValid,
            serviceReady,
            serviceReady);
        var shouldArmAdapter = shouldArmAdapterReason == "none";
        var sceneOverrideApplyObserved = overrideAppliedCount > 0;
        var cameraFramingApplied = phase2GApplyCount > 0;
        var bridgeSnapshotBase = _charaSelectService?.GetTitleBackgroundCharacterCompositionBridgeSnapshot()
            ?? TitleBackgroundCharacterCompositionBridgeSnapshot.Empty;
        var bridgeSnapshot = bridgeSnapshotBase with
        {
            AppliedCamera = cameraFramingApplied,
            CharacterVisualKnownByBridge = bridgeSnapshotBase.CharacterVisualKnownByBridge && cameraFramingApplied,
        };
        var cameraProfile = ResolveCurrentTitleBackgroundCameraProfile(candidate.Id);
        var hasLatestTimelineSample = TryGetLatestPhase2CTimelineSnapshot(out var latestTimelineSample);
        var finalYawPitchDistanceMatchesProfile = cameraProfile.HasProfile && hasLatestTimelineSample
            ? BuildPhase2GFinalCameraStateMatchesPresetVerdict(latestTimelineSample)
            : "unknown";
        var currentDirH = hasLatestTimelineSample ? latestTimelineSample.LobbyDirH ?? latestTimelineSample.DirH : null;
        var currentDirV = hasLatestTimelineSample ? latestTimelineSample.LobbyDirV ?? latestTimelineSample.DirV : null;
        var currentDistance = hasLatestTimelineSample ? latestTimelineSample.LobbyDistance ?? latestTimelineSample.Distance : null;
        var currentPosition = hasLatestTimelineSample ? latestTimelineSample.SceneCameraPosition : _lastPostFixOnSceneCameraPosition;
        var currentLookAt = hasLatestTimelineSample ? latestTimelineSample.SceneCameraLookAtVector ?? latestTimelineSample.LobbyLastLookAtVector : _lastPostFixOnLookAtVector;
        var runtimeHasProfilePose = _charaSelectCameraAdapter.RuntimeState.HasCameraPose;
        var visibleProfileAppliedState = BuildVisibleProfileAppliedState(cameraProfile, runtimeHasProfilePose, cameraFramingApplied);

        return new TitleBackgroundQuickCheckInput(
            runScoped,
            _quickCheckState.RunState,
            _quickCheckState.StartedAt,
            DateTimeOffset.Now,
            runScoped && _quickCheckState.StartedLoggedIn,
            IsQuickCheckCharaSelectObserved(),
            runScoped ? _quickCheckState.SceneGenerationStart : 0,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration,
            sceneReadyAcceptedCount,
            overrideAppliedCount,
            phase2GApplyCount,
            pluginOrHookError,
            _stateReason,
            _clientState.IsLoggedIn,
            currentLobbyMapName,
            currentLobbyMapRemainedAfterLogin,
            _configuration.TitleBackgroundCharacterSelectBackgroundMode,
            _configuration.TitleBackgroundCharacterSelectLightingMode,
            candidate.Id,
            candidate.DisplayName,
            candidate.VerifiedInGame,
            candidate.Source,
            candidate.ExpectedCompatibility,
            candidate.ExpectedBrightness,
            candidate.TerritoryPath,
            candidate.TerritoryId,
            candidate.LayerFilterKey,
            candidateFieldsValid,
            backgroundApplied,
            backgroundApplied,
            !candidate.VerifiedInGame,
            candidate.CharacterExpectedVisible,
            phase2MSummary.ActorVisible,
            characterKnownLimitation,
            _clientState.IsLoggedIn && _activeSceneOverride,
            activeAfterLoginDetected,
            staleCharaSelectStateAfterLogin,
            _transitionDiagnostics.Phase2GAppliedAfterLogin,
            true,
            actorSourceAmbiguous,
            zeroTransformStubs,
            _configuration.TitleBackgroundCharacterVisualStatus,
            _configuration.TitleBackgroundCharaSelectCameraFramingMode,
            candidate.RecommendedCameraFraming,
            candidate.RecommendedAction,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.CharaSelectSceneCompositionEnabled,
            _configuration.TitleBackgroundIntegratedCompositionEnabled,
            shouldArmAdapter,
            shouldArmAdapterReason,
            _integratedCompositionRouteInvoked,
            _integratedCompositionRouteLastReason,
            cameraFramingApplied,
            sceneOverrideApplyObserved,
            _integratedCompositionAutoEnabled,
            bridgeSnapshot,
            cameraProfile.ProfileId,
            cameraProfile.ProfileSource,
            FormatFloat(cameraProfile.Yaw ?? _charaSelectCameraAdapter.GetRestoredYaw() ?? _charaSelectCameraAdapter.RuntimeState.Yaw),
            FormatFloat(cameraProfile.Pitch ?? _charaSelectCameraAdapter.RuntimeState.Pitch),
            FormatFloat(cameraProfile.Distance ?? _charaSelectCameraAdapter.RuntimeState.Distance),
            FormatVector(cameraProfile.LookAtOffset),
            FormatVector(cameraProfile.PositionOffset),
            CharacterPlacementStatusToQuickCheckTriState(phase2MSummary.CameraFramesActor),
            VerdictToQuickCheckTriState(finalYawPitchDistanceMatchesProfile),
            cameraProfile.HasProfile,
            visibleProfileAppliedState == "True",
            visibleProfileAppliedState,
            BuildCameraProfileApplyRoute(cameraProfile, runtimeHasProfilePose, cameraFramingApplied),
            _configuration.TitleBackgroundCapturedCameraProfileEnabled,
            FormatFloat(_configuration.TitleBackgroundCapturedDirH),
            FormatFloat(_configuration.TitleBackgroundCapturedDirV),
            FormatFloat(_configuration.TitleBackgroundCapturedDistance),
            FormatVector(BuildCapturedProfilePosition()),
            FormatVector(BuildCapturedProfileLookAt()),
            FormatFloat(currentDirH),
            FormatFloat(currentDirV),
            FormatFloat(currentDistance),
            FormatVector(currentPosition),
            FormatVector(currentLookAt),
            bridgeSnapshot.AppliedStage && bridgeSnapshot.AppliedCharacter,
            cameraProfile.HasProfile && runtimeHasProfilePose);
    }
}

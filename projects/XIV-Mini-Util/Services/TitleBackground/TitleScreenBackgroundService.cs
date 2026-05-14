// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs
// Description: キャラ選択画面背景差し替えの設定、診断、native hook lifecycleを管理する
// Reason: HaselTweaks相当のemote/pet/preload機能から背景差し替えを分離するため
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ClientVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe class TitleScreenBackgroundService : IDisposable
{
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly ISigScanner _sigScanner;
    private readonly IFramework _framework;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly TitleBackgroundAddressResolver _addressResolver = new();
    private readonly TitleBackgroundCameraCaptureService _cameraCaptureService;

    private Hook<CreateSceneDelegate>? _createSceneHook;
    private Hook<LobbyUpdateDelegate>? _lobbyUpdateHook;
    private Hook<LoadLobbySceneDelegate>? _loadLobbySceneHook;
    private Hook<LobbyCameraFixOnDelegate>? _cameraFixOnHook;
    private TitleBackgroundServiceState _state = TitleBackgroundServiceState.Disabled;
    private string _stateReason = "無効";
    private string _validatedTerritoryPath = string.Empty;
    private string _validationError = string.Empty;
    private GameLobbyType _lastLobbyUpdateMapId = GameLobbyType.None;
    private GameLobbyType _loadingLobbyType = GameLobbyType.None;
    private bool _cameraApplyPending;
    private bool _currentMapWriteAttempted;
    private bool _lastCurrentMapWriteSucceeded;
    private bool _disposed;
    private string _lastObservedCreateScenePath = string.Empty;
    private bool _lastOverrideApplied;
    private GameLobbyType _lastOverrideLobbyType = GameLobbyType.None;
    private string _lastOverrideOriginalPath = string.Empty;
    private string _lastOverrideNewPath = string.Empty;
    private Vector3? _lastObservedFixOnCamera;
    private Vector3? _lastObservedFixOnFocus;
    private float? _lastObservedFixOnFovY;
    private bool _lastCameraOverrideApplied;
    private Vector3? _lastAppliedCamera;
    private Vector3? _lastAppliedFocus;
    private float? _lastAppliedFovY;
    private string _lastFixOnInvocationMode = "not-run";
    private string _lastPostFixOnCameraCaptureStatus = "not-run";
    private string _lastPostFixOnCameraCaptureError = string.Empty;
    private Vector3? _lastPostFixOnSceneCameraPosition;
    private Vector3? _lastPostFixOnLookAtVector;
    private float? _lastPostFixOnDistance;
    private float? _lastPostFixOnFovY;
    private TitleBackgroundCameraCaptureResult _lastCameraCaptureResult = TitleBackgroundCameraCaptureResult.NotRun;
    private TitleBackgroundProbeSession? _activeProbeSession;
    private TitleBackgroundProbeSession? _lastProbeSession;
    private TitleBackgroundProbeCounters _automaticProbeCounters = new();
    private bool _automaticProbeCountersEnabled;
    private TitleBackgroundCameraProbeSession? _cameraProbeSession;
    private int _cameraProbeDelayedCaptureFramesRemaining;
    private string _cameraProbeDelayedCaptureStatus = "not-run";
    private string _cameraProbeDelayedCaptureError = string.Empty;
    private Vector3? _cameraProbeDelayedSceneCameraPosition;
    private Vector3? _cameraProbeDelayedLookAtVector;

    private GameLobbyType EffectiveLobbyType =>
        _loadingLobbyType != GameLobbyType.None
            ? _loadingLobbyType
            : _lastLobbyUpdateMapId;

    public TitleScreenBackgroundService(
        IGameInteropProvider gameInteropProvider,
        ISigScanner sigScanner,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration)
    {
        _gameInteropProvider = gameInteropProvider;
        _sigScanner = sigScanner;
        _framework = framework;
        _dataManager = dataManager;
        _log = log;
        _configuration = configuration;
        _cameraCaptureService = new TitleBackgroundCameraCaptureService(clientState, objectTable, dataManager, log);
        _framework.Update += OnFrameworkUpdate;

        InitializeHooks();
        ApplyFromConfiguration();
    }

    public void SetEnabled(bool enabled)
    {
        _configuration.TitleBackgroundOverrideEnabled = enabled;
        _configuration.Save();
        ReloadNativeIntegration();
    }

    public void SetCameraOverrideEnabled(bool enabled)
    {
        _configuration.TitleBackgroundCameraOverrideEnabled = enabled;
        _configuration.Save();
        ReloadNativeIntegration();
    }

    internal TitleBackgroundCameraCaptureResult LastCameraCaptureResult => _lastCameraCaptureResult;

    internal bool TryCopyLastObservedCreateSceneToOverrideConfiguration(out string errorMessage)
    {
        var lastPath = _automaticProbeCounters.LastCreateScenePath;
        if (string.IsNullOrWhiteSpace(lastPath))
        {
            errorMessage = "HookProbe で観測した CreateScene path がありません。";
            return false;
        }

        if (!TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(lastPath, out var normalizedPath, out errorMessage))
        {
            return false;
        }

        var lvbPath = TitleBackgroundPathHelper.BuildLvbPath(normalizedPath);
        try
        {
            if (!_dataManager.FileExists(lvbPath))
            {
                errorMessage = $"LVB が見つかりません: {lvbPath}";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"LVB validation failed: {ex.Message}";
            return false;
        }

        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _configuration.TitleBackgroundCameraOverrideEnabled = false;
        _configuration.TitleBackgroundTerritoryPath = normalizedPath;
        _configuration.TitleBackgroundTerritoryTypeId = _automaticProbeCounters.LastCreateSceneTerritoryId;
        _configuration.TitleBackgroundLayoutTerritoryTypeId = _automaticProbeCounters.LastCreateSceneTerritoryId;
        _configuration.TitleBackgroundLayoutLayerFilterKey = _automaticProbeCounters.LastCreateSceneLayerFilterKey;
        _configuration.Save();
        ApplyFromConfiguration();
        _log.Information(
            "[XMU BG] Copied observed CreateScene values to override configuration. path={Path}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}",
            normalizedPath,
            _configuration.TitleBackgroundLayoutTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey);
        errorMessage = string.Empty;
        return true;
    }

    internal bool TryApplyBuiltInPreset(string presetId, out string errorMessage)
    {
        if (!TitleBackgroundBuiltInPresetCatalog.TryGetById(presetId, out var entry))
        {
            errorMessage = "preset が見つかりません。";
            return false;
        }

        if (!TitleBackgroundPresetApplicator.TryApplyPreset(
            _configuration,
            entry.Preset,
            entry.Id,
            _dataManager.FileExists,
            out errorMessage))
        {
            return false;
        }

        _configuration.Save();
        ApplyFromConfiguration();
        _log.Information(
            "[XMU BG] Built-in preset applied. presetId={PresetId}, territoryPath={TerritoryPath}",
            entry.Id,
            _configuration.TitleBackgroundTerritoryPath);
        return true;
    }

    internal TitleBackgroundCameraCaptureResult CaptureCurrentLocationAndCamera()
    {
        var result = _cameraCaptureService.Capture(_configuration);
        _lastCameraCaptureResult = result;

        if (!result.Success || result.Preset == null)
        {
            _log.Warning(
                "[XMU BG] Camera capture failed. reason={Reason}",
                string.IsNullOrWhiteSpace(result.FailureReason) ? "unknown" : result.FailureReason);
            return result;
        }

        TitleBackgroundPresetApplicator.ApplyDebugPreset(_configuration, result.Preset);
        _configuration.Save();
        ApplyFromConfiguration();
        _log.Information(
            "[XMU BG] Camera capture saved. territoryPath={TerritoryPath}, camera={Camera}, focus={Focus}, fovY={FovY}",
            result.Preset.TerritoryPath,
            FormatVector(new Vector3(result.Preset.CameraX, result.Preset.CameraY, result.Preset.CameraZ)),
            FormatVector(new Vector3(result.Preset.FocusX, result.Preset.FocusY, result.Preset.FocusZ)),
            result.Preset.FovY);
        return result;
    }

    public void ApplyFromConfiguration()
    {
        NormalizeConfiguration();
        UpdateAutomaticProbeCounterState();
        if (_configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly)
        {
            DisposeHooks();
            _cameraApplyPending = false;
            _state = TitleBackgroundServiceState.Disabled;
            _stateReason = "resolver-only";
            return;
        }

        if (_configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.Disabled
            || !_configuration.TitleBackgroundOverrideEnabled)
        {
            DisposeHooks();
            _cameraApplyPending = false;
            _state = TitleBackgroundServiceState.Disabled;
            _stateReason = "無効";
            return;
        }

        if (ShouldValidateSceneOverrideConfiguration() && !ValidateCurrentConfiguration(out var errorMessage))
        {
            DisposeHooks();
            _state = TitleBackgroundServiceState.InvalidConfiguration;
            _stateReason = errorMessage;
            _cameraApplyPending = false;
            return;
        }

        if (ShouldCreateSceneHooks() && (!AreSceneHooksReady() || !IsHookSetAlignedWithConfiguration()))
        {
            DisposeHooks();
            InitializeHooks();
        }

        if (!AreSceneReady())
        {
            _state = TitleBackgroundServiceState.HookCreateFailed;
            _stateReason = "scene hook unavailable";
            return;
        }

        if (IsCameraHookRequired() && !AreCameraHookReady())
        {
            _state = TitleBackgroundServiceState.HookCreateFailed;
            _stateReason = "camera hook unavailable";
            return;
        }

        _state = TitleBackgroundServiceState.Ready;
        _stateReason = "準備完了";
    }

    public void ReloadNativeIntegration()
    {
        _cameraApplyPending = false;
        ResetCameraOverrideObservation();
        ResetSceneOverrideObservation();
        _loadingLobbyType = GameLobbyType.None;
        DisposeHooks();
        InitializeHooks();
        ApplyFromConfiguration();
    }

    public void ClearOverride()
    {
        _configuration.TitleBackgroundOverrideEnabled = false;
        _configuration.Save();
        _cameraApplyPending = false;
        ResetCameraOverrideObservation();
        ResetSceneOverrideObservation();
        DisposeHooks();
        _state = TitleBackgroundServiceState.Disabled;
        _stateReason = "無効";
    }

    public string GetStatusText()
    {
        return _state switch
        {
            TitleBackgroundServiceState.Disabled => $"状態: 無効 ({(_configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly ? "resolve-only" : "hook ready")})",
            TitleBackgroundServiceState.Ready => $"状態: 有効 - {_validatedTerritoryPath}",
            TitleBackgroundServiceState.InvalidConfiguration => $"状態: 設定エラー - {_stateReason}",
            TitleBackgroundServiceState.AddressResolveFailed => $"状態: address解決失敗 - {_stateReason}",
            TitleBackgroundServiceState.HookCreateFailed => $"状態: hook作成失敗 - {_stateReason}",
            TitleBackgroundServiceState.HookEnableFailed => $"状態: hook有効化失敗 - {_stateReason}",
            TitleBackgroundServiceState.RuntimeError => $"状態: runtime error - {_stateReason}",
            _ => $"状態: {_state} - {_stateReason}",
        };
    }

    public IReadOnlyList<string> GetDiagnosticLines()
    {
        var sceneHooksReady = AreSceneHooksReady();
        var cameraHookReady = AreCameraHookReady();
        var cameraHookRequired = IsCameraHookRequired();
        var hooksReady = sceneHooksReady && (!cameraHookRequired || cameraHookReady);
        var hooksEnabled = IsHookEnabled(_createSceneHook)
            || IsHookEnabled(_lobbyUpdateHook)
            || IsHookEnabled(_loadLobbySceneHook)
            || IsHookEnabled(_cameraFixOnHook);
        var capturePreset = _lastCameraCaptureResult.Preset;
        var currentCameraCaptured = TryCaptureActiveCameraSnapshot(out var currentCamera, out var currentCaptureError);
        var currentCaptureStatus = currentCameraCaptured ? "success" : "failed";
        Vector3? currentSceneCameraPosition = currentCameraCaptured ? currentCamera.SceneCameraPosition : null;
        Vector3? currentLookAtVector = currentCameraCaptured ? currentCamera.LookAtVector : null;
        float? currentDistance = currentCameraCaptured ? currentCamera.Distance : null;
        float? currentFovY = currentCameraCaptured ? currentCamera.FovY : null;
        var effectiveOverrideTerritoryId = GetEffectiveOverrideTerritoryId();
        var lines = new List<string>
        {
            $"runtimeMode={_configuration.TitleBackgroundRuntimeMode}",
            $"probeMode={IsHookProbeMode()}",
            $"probeMutatesScene=False",
            $"probeWritesCurrentMap=False",
            $"probeEnablesCameraHook=False",
            $"automaticProbeCountersEnabled={_automaticProbeCountersEnabled}",
            $"CreateSceneProbe.callCount={_automaticProbeCounters.CreateSceneCallCount}",
            $"CreateSceneProbe.lastPath={FormatNone(_automaticProbeCounters.LastCreateScenePath)}",
            $"CreateSceneProbe.lastTerritoryId={_automaticProbeCounters.LastCreateSceneTerritoryId}",
            $"CreateSceneProbe.lastLayerFilterKey={_automaticProbeCounters.LastCreateSceneLayerFilterKey}",
            $"LobbyUpdateProbe.callCount={_automaticProbeCounters.LobbyUpdateCallCount}",
            $"LobbyUpdateProbe.lastMapId={_automaticProbeCounters.LastLobbyUpdateMapId}",
            $"LobbyUpdateProbe.lastTime={_automaticProbeCounters.LastLobbyUpdateTime}",
            $"LoadLobbySceneProbe.callCount={_automaticProbeCounters.LoadLobbySceneCallCount}",
            $"LoadLobbySceneProbe.lastMapId={_automaticProbeCounters.LastLoadLobbySceneMapId}",
            $"sceneOverrideEnabled={IsSceneOverrideEnabled()}",
            $"overrideMutationBranchArmed={IsOverrideMutationBranchArmed()}",
            $"overrideTerritoryPath={FormatNone(_configuration.TitleBackgroundTerritoryPath)}",
            $"overrideTerritoryId={effectiveOverrideTerritoryId}",
            $"overrideLayerFilterKey={_configuration.TitleBackgroundLayoutLayerFilterKey}",
            $"lastOverrideApplied={_lastOverrideApplied}",
            $"lastOverrideLobbyType={_lastOverrideLobbyType}",
            $"lastOverrideOriginalPath={FormatNone(_lastOverrideOriginalPath)}",
            $"lastOverrideNewPath={FormatNone(_lastOverrideNewPath)}",
            $"hooksReady={hooksReady}",
            $"sceneHooksReady={sceneHooksReady}",
            $"cameraHookReady={cameraHookReady}",
            $"cameraHookRequired={cameraHookRequired}",
            $"cameraHookEnabled={IsHookEnabled(_cameraFixOnHook)}",
            $"cameraOverrideEnabled={_configuration.TitleBackgroundCameraOverrideEnabled}",
            $"selectedPresetId={FormatNone(_configuration.TitleBackgroundSelectedPresetId)}",
            $"cameraOverrideApplyPending={_cameraApplyPending}",
            $"cameraCapture.lastStatus={_lastCameraCaptureResult.Status}",
            $"cameraCapture.lastFailureReason={FormatNone(_lastCameraCaptureResult.FailureReason)}",
            $"cameraCapture.lastFovState={_lastCameraCaptureResult.FovState}",
            $"lastCapturedTerritoryPath={(capturePreset == null ? "none" : capturePreset.TerritoryPath)}",
            $"lastCapturedCamera={FormatVector(capturePreset == null ? null : new Vector3(capturePreset.CameraX, capturePreset.CameraY, capturePreset.CameraZ))}",
            $"lastCapturedFocus={FormatVector(capturePreset == null ? null : new Vector3(capturePreset.FocusX, capturePreset.FocusY, capturePreset.FocusZ))}",
            $"lastCapturedFovY={(capturePreset == null ? "none" : capturePreset.FovY.ToString("0.###"))}",
            $"lastObservedFixOnCamera={FormatVector(_lastObservedFixOnCamera)}",
            $"lastObservedFixOnFocus={FormatVector(_lastObservedFixOnFocus)}",
            $"lastObservedFixOnFovY={(_lastObservedFixOnFovY.HasValue ? _lastObservedFixOnFovY.Value.ToString("0.###") : "none")}",
            $"lastFixOnFovY={(_lastObservedFixOnFovY.HasValue ? _lastObservedFixOnFovY.Value.ToString("0.###") : "none")}",
            $"lastCameraOverrideApplied={_lastCameraOverrideApplied}",
            $"lastAppliedCamera={FormatVector(_lastAppliedCamera)}",
            "cameraInputMeaning=FixOn camera argument; not yet verified as final stable camera position",
            $"lastAppliedFocus={FormatVector(_lastAppliedFocus)}",
            "focusInputMeaning=FixOn focus argument; observed relation exists, but semantics remain under verification",
            $"lastAppliedFovY={(_lastAppliedFovY.HasValue ? _lastAppliedFovY.Value.ToString("0.###") : "none")}",
            $"lastFixOnInvocationMode={_lastFixOnInvocationMode}",
            $"postFixOnCameraCaptureStatus={_lastPostFixOnCameraCaptureStatus}",
            $"postFixOnCameraCaptureError={FormatNone(_lastPostFixOnCameraCaptureError)}",
            $"postFixOnSceneCameraPosition={FormatVector(_lastPostFixOnSceneCameraPosition)}",
            $"postFixOnLookAtVector={FormatVector(_lastPostFixOnLookAtVector)}",
            "postFixOnLookAtVectorMeaning=raw SceneCamera.LookAtVector; meaning unverified, but live observation showed FixOn focusPos matched this field",
            $"postFixOnDistance={(_lastPostFixOnDistance.HasValue ? _lastPostFixOnDistance.Value.ToString("0.###") : "none")}",
            $"postFixOnFovY={(_lastPostFixOnFovY.HasValue ? _lastPostFixOnFovY.Value.ToString("0.###") : "none")}",
            $"currentCameraCaptureStatus={currentCaptureStatus}",
            $"currentCameraCaptureError={FormatNone(currentCaptureError)}",
            $"currentSceneCameraPosition={FormatVector(currentSceneCameraPosition)}",
            $"currentLookAtVector={FormatVector(currentLookAtVector)}",
            "currentLookAtVectorMeaning=raw SceneCamera.LookAtVector; meaning unverified",
            $"currentDistance={FormatFloat(currentDistance)}",
            $"currentFovY={FormatFloat(currentFovY)}",
            $"cameraDelta.appliedToPostFixOn={FormatVectorDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera)}",
            $"cameraDelta.appliedToPostFixOn.x={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 0)}",
            $"cameraDelta.appliedToPostFixOn.y={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 1)}",
            $"cameraDelta.appliedToPostFixOn.z={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 2)}",
            $"cameraDelta.postFixOnToCurrent={FormatVectorDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition)}",
            $"cameraDelta.postFixOnToCurrent.x={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 0)}",
            $"cameraDelta.postFixOnToCurrent.y={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 1)}",
            $"cameraDelta.postFixOnToCurrent.z={FormatVectorAxisDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition, 2)}",
            $"focusDelta.appliedToPostFixOn={FormatVectorDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus)}",
            $"focusDelta.appliedToPostFixOn.x={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 0)}",
            $"focusDelta.appliedToPostFixOn.y={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 1)}",
            $"focusDelta.appliedToPostFixOn.z={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 2)}",
            $"focusDelta.postFixOnToCurrent={FormatVectorDelta(currentLookAtVector, _lastPostFixOnLookAtVector)}",
            $"focusDelta.postFixOnToCurrent.x={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 0)}",
            $"focusDelta.postFixOnToCurrent.y={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 1)}",
            $"focusDelta.postFixOnToCurrent.z={FormatVectorAxisDelta(currentLookAtVector, _lastPostFixOnLookAtVector, 2)}",
            $"currentVsPostFixOnCameraDelta={FormatVectorDelta(currentSceneCameraPosition, _lastPostFixOnSceneCameraPosition)}",
            $"currentVsPostFixOnLookAtDelta={FormatVectorDelta(currentLookAtVector, _lastPostFixOnLookAtVector)}",
            $"currentVsPostFixOnDistanceDelta={FormatFloatDelta(currentDistance, _lastPostFixOnDistance)}",
            $"currentVsPostFixOnFovYDelta={FormatFloatDelta(currentFovY, _lastPostFixOnFovY)}",
            $"titleOverrideImplemented={TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(_configuration.TitleBackgroundRuntimeMode)}",
            "fixOnAbiVerified=False",
            $"hooksEnabled={hooksEnabled}",
            "",
            BuildAddressLine("CreateScene.configured", _configuration.TitleBackgroundCreateSceneSignature),
            $"CreateScene.resolverMode={_configuration.TitleBackgroundCreateSceneResolverMode}",
            $"CreateScene.match={FormatAddress(_addressResolver.CreateSceneMatch)}",
            $"CreateScene.resolvedCandidate={FormatAddress(GetResolvedCandidate("CreateScene"))}",
            $"CreateScene.hookTarget={FormatAddress(_addressResolver.CreateScene)}",
            $"CreateScene.hookTargetVerified={GetHookTargetVerified("CreateScene")}",
            $"CreateScene.method={GetResolveMethod("CreateScene", "TryScanText+NearbyE8Rel32")}",
            $"CreateScene.candidateReadable={GetCandidateReadable("CreateScene")}",
            $"CreateScene.candidatePrologueHint={GetCandidatePrologueHint("CreateScene")}",
            $"CreateScene.candidateFirstBytes={GetCandidateFirstBytes("CreateScene")}",
            $"CreateScene.targetWithinText={GetTargetWithinText("CreateScene")}",
            "",
            BuildAddressLine("LobbyUpdate.configured", _configuration.TitleBackgroundLobbyUpdateSignature),
            $"LobbyUpdate.resolverMode={_configuration.TitleBackgroundLobbyUpdateResolverMode}",
            $"LobbyUpdate.match={FormatAddress(_addressResolver.LobbyUpdateMatch)}",
            $"LobbyUpdate.resolvedCandidate={FormatAddress(GetResolvedCandidate("LobbyUpdate"))}",
            $"LobbyUpdate.hookTarget={FormatAddress(_addressResolver.LobbyUpdate)}",
            $"LobbyUpdate.hookTargetVerified={GetHookTargetVerified("LobbyUpdate")}",
            $"LobbyUpdate.method={GetResolveMethod("LobbyUpdate", "TryScanText+NearbyE8Rel32")}",
            $"LobbyUpdate.candidateReadable={GetCandidateReadable("LobbyUpdate")}",
            $"LobbyUpdate.candidatePrologueHint={GetCandidatePrologueHint("LobbyUpdate")}",
            $"LobbyUpdate.candidateFirstBytes={GetCandidateFirstBytes("LobbyUpdate")}",
            $"LobbyUpdate.targetWithinText={GetTargetWithinText("LobbyUpdate")}",
            "",
            BuildAddressLine("LoadLobbyScene.configured", _configuration.TitleBackgroundLoadLobbySceneSignature),
            $"LoadLobbyScene.address={FormatAddress(_addressResolver.LoadLobbyScene)}",
            "LoadLobbyScene.method=TryScanText",
            $"LoadLobbyScene.hookTargetVerified={GetHookTargetVerified("LoadLobbyScene")}",
            $"LoadLobbyScene.targetWithinText={GetTargetWithinText("LoadLobbyScene")}",
            "",
            BuildAddressLine("LobbyCurrentMap.configured", _configuration.TitleBackgroundLobbyCurrentMapSignature),
            $"LobbyCurrentMap.staticAddress={FormatAddress(_addressResolver.LobbyCurrentMap)}",
            $"LobbyCurrentMap.readShort={ReadCurrentLobbyMapRawText()}",
            $"LobbyCurrentMap.enumDecoded={ReadCurrentLobbyMapDecodedText()}",
            $"LobbyCurrentMap.writeAttempted={_currentMapWriteAttempted}",
            $"LobbyCurrentMap.lastWriteSucceeded={_lastCurrentMapWriteSucceeded}",
            "",
            BuildAddressLine("LobbyCameraFixOn.configured", _configuration.TitleBackgroundFixOnSignature),
            $"LobbyCameraFixOn.address={FormatAddress(_addressResolver.FixOn)}",
            "LobbyCameraFixOn.method=TryScanText",
            $"LobbyCameraFixOn.hookTargetVerified={GetHookTargetVerified("LobbyCameraFixOn")}",
            $"LobbyCameraFixOn.targetWithinText={GetTargetWithinText("LobbyCameraFixOn")}",
            "",
            "UpdateLobbyUIStage.optional=True",
            $"UpdateLobbyUIStage.address={FormatAddress(_addressResolver.UpdateLobbyUIStage)}",
            $"UpdateLobbyUIStage.resolvedCandidate={FormatAddress(GetResolvedCandidate("UpdateLobbyUIStage"))}",
            $"UpdateLobbyUIStage.hookTargetVerified={GetHookTargetVerified("UpdateLobbyUIStage")}",
            $"Focus.reservedForCameraPhase={!TitleBackgroundRuntimeModeHelper.IsFocusUsed(_configuration.TitleBackgroundCameraOverrideEnabled)}",
            $"lastLobbyUpdateMapId={_lastLobbyUpdateMapId}",
            $"loadingLobbyType={_loadingLobbyType}",
            $"effectiveLobbyType={EffectiveLobbyType}",
            $"lastCreateScenePath={(_lastObservedCreateScenePath.Length == 0 ? "none" : _lastObservedCreateScenePath)}",
            $"resolverError={(_addressResolver.LastError.Length == 0 ? "none" : _addressResolver.LastError)}",
        };

        foreach (var result in _addressResolver.ScanResults)
        {
            lines.Add($"signatureScan: name={result.Name}, method={result.Method}, status={result.Status}, address={FormatAddress(result.Address)}, resolvedCandidate={FormatAddress(result.ResolvedCandidate)}, hookTarget={FormatAddress(result.HookTarget)}, hookTargetVerified={result.HookTargetVerified}, addressSource={result.AddressSource}, targetWithinText={result.TargetWithinText}, hookTargetWithinText={result.HookTargetWithinText}, candidateReadable={result.CandidateDiagnostics.Readable}, candidatePrologueHint={result.CandidateDiagnostics.PrologueHint}, candidateFirstBytes={(string.IsNullOrWhiteSpace(result.CandidateDiagnostics.FirstBytesHex) ? "none" : result.CandidateDiagnostics.FirstBytesHex)}, safetyNote={(string.IsNullOrWhiteSpace(result.SafetyNote) ? "none" : result.SafetyNote)}");
        }

        return lines;
    }

    public IReadOnlyList<string> StartProbe()
    {
        if (_cameraProbeSession != null)
        {
            return ["[Probe] camera probe is armed; run /xmutbgcamprobe restore before starting hook probe."];
        }

        if (_activeProbeSession != null)
        {
            return ["[Probe] already active; existing session was left unchanged."];
        }

        var session = new TitleBackgroundProbeSession(TitleBackgroundProbeSettingsSnapshot.Capture(_configuration));
        _activeProbeSession = session;
        _lastProbeSession = session;

        try
        {
            _configuration.TitleBackgroundOverrideEnabled = true;
            _configuration.TitleBackgroundCameraOverrideEnabled = false;
            _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.HookProbe;
            _configuration.TitleBackgroundCreateSceneResolverMode = TitleBackgroundResolverMode.ManualDirectTextProbe;
            _configuration.TitleBackgroundLobbyUpdateResolverMode = TitleBackgroundResolverMode.ManualDirectTextProbe;
            ReloadNativeIntegration();
            session.HookEnabledAtStart = AreAnyHooksEnabled();

            return
            [
                "[Probe] started.",
                ..GetProbeReportLines(session, isActive: true),
                "",
                ..GetDiagnosticLines(),
            ];
        }
        catch (Exception ex)
        {
            session.RuntimeErrorOccurred = true;
            session.LastError = ex.Message;
            var rollbackMessage = TryRestoreProbeSettings(session.OriginalSettings);
            _activeProbeSession = null;
            return
            [
                "[Probe] failed to start.",
                rollbackMessage,
                $"[Probe] error={ex.Message}",
            ];
        }
    }

    public IReadOnlyList<string> StopProbe()
    {
        if (_activeProbeSession == null)
        {
            return ["[Probe] no active session; nothing to stop."];
        }

        var session = _activeProbeSession;
        _activeProbeSession = null;
        session.HookEnabledAtEnd = AreAnyHooksEnabled();
        var restoreMessage = TryRestoreProbeSettings(session.OriginalSettings);

        return
        [
            restoreMessage,
            ..GetProbeReportLines(session, isActive: false),
        ];
    }

    public IReadOnlyList<string> GetProbeReportLines()
    {
        var activeSession = _activeProbeSession;
        var lastSession = activeSession ?? _lastProbeSession;
        var reportInput = BuildProbeReportInput(lastSession, activeSession != null);
        var summaryLines = BuildProbeReportSummaryLines(reportInput);

        if (_activeProbeSession != null)
        {
            return
            [
                ..summaryLines,
                "",
                "[Probe] Raw session",
                ..GetProbeReportLines(_activeProbeSession, isActive: true),
            ];
        }

        if (_lastProbeSession != null)
        {
            return
            [
                ..summaryLines,
                "",
                "[Probe] Raw session",
                ..GetProbeReportLines(_lastProbeSession, isActive: false),
            ];
        }

        return summaryLines;
    }

    public IReadOnlyList<string> ArmCameraYProbe()
    {
        if (_activeProbeSession != null)
        {
            return ["[CameraProbe] hook probe is active; run /xmutbgprobe off before arming camera probe."];
        }

        if (_cameraProbeSession != null)
        {
            return ["[CameraProbe] already armed; run /xmutbgcamprobe restore before arming again."];
        }

        var baselineCamera = new Vector3(
            _configuration.TitleBackgroundCameraX,
            _configuration.TitleBackgroundCameraY,
            _configuration.TitleBackgroundCameraZ);
        var baselineFocus = new Vector3(
            _configuration.TitleBackgroundFocusX,
            _configuration.TitleBackgroundFocusY,
            _configuration.TitleBackgroundFocusZ);
        var probeCamera = new Vector3(
            baselineCamera.X,
            TitleBackgroundPreset.SanitizeCoordinate(baselineCamera.Y + 50f),
            baselineCamera.Z);
        var probeFocus = new Vector3(
            baselineFocus.X,
            TitleBackgroundPreset.SanitizeCoordinate(baselineFocus.Y - 50f),
            baselineFocus.Z);
        var session = new TitleBackgroundCameraProbeSession(
            TitleBackgroundCameraProbeSettingsSnapshot.Capture(_configuration),
            baselineCamera,
            baselineFocus,
            probeCamera,
            probeFocus);

        _cameraProbeSession = session;
        try
        {
            _configuration.TitleBackgroundSelectedPresetId = string.Empty;
            _configuration.TitleBackgroundCameraOverrideEnabled = true;
            _configuration.TitleBackgroundCameraX = probeCamera.X;
            _configuration.TitleBackgroundCameraY = probeCamera.Y;
            _configuration.TitleBackgroundCameraZ = probeCamera.Z;
            _configuration.TitleBackgroundFocusX = probeFocus.X;
            _configuration.TitleBackgroundFocusY = probeFocus.Y;
            _configuration.TitleBackgroundFocusZ = probeFocus.Z;
            _configuration.Save();
            ApplyFromConfiguration();
            ResetCameraOverrideObservation();
            ResetCameraProbeDelayedObservation();

            return
            [
                "[CameraProbe] armed-y.",
                $"[CameraProbe] baselineCamera={FormatVector(baselineCamera)}",
                $"[CameraProbe] baselineFocus={FormatVector(baselineFocus)}",
                $"[CameraProbe] probeCamera={FormatVector(probeCamera)}",
                $"[CameraProbe] probeFocus={FormatVector(probeFocus)}",
                $"[CameraProbe] expectedCameraYDelta={FormatFloat(probeCamera.Y - baselineCamera.Y)}",
                $"[CameraProbe] expectedFocusYDelta={FormatFloat(probeFocus.Y - baselineFocus.Y)}",
                "[CameraProbe] next=logout -> character select -> login -> /xmutbgcamprobe report",
            ];
        }
        catch (Exception ex)
        {
            _cameraProbeSession = null;
            session.OriginalSettings.ApplyTo(_configuration);
            _configuration.Save();
            ApplyFromConfiguration();
            _log.Warning(ex, "TitleBackground camera probe failed to arm.");
            return
            [
                "[CameraProbe] failed to arm-y.",
                $"[CameraProbe] error={ex.Message}",
            ];
        }
    }

    public IReadOnlyList<string> GetCameraProbeReportLines()
    {
        var currentCameraCaptured = TryCaptureActiveCameraSnapshot(out var currentCamera, out var currentCaptureError);
        var reportTimeSceneCameraPosition = currentCameraCaptured ? currentCamera.SceneCameraPosition : (Vector3?)null;
        var reportTimeLookAtVector = currentCameraCaptured ? currentCamera.LookAtVector : (Vector3?)null;
        var stabilitySceneCameraPosition = _cameraProbeDelayedSceneCameraPosition ?? reportTimeSceneCameraPosition;
        var stabilityLookAtVector = _cameraProbeDelayedLookAtVector ?? reportTimeLookAtVector;
        var stabilitySampleSource = _cameraProbeDelayedSceneCameraPosition.HasValue || _cameraProbeDelayedLookAtVector.HasValue
            ? "delayed-probe-snapshot"
            : "report-time-current";
        var session = _cameraProbeSession;
        var input = new TitleBackgroundCameraProbeReportInput(
            session != null,
            session?.BaselineCamera ?? default,
            session?.BaselineFocus ?? default,
            session?.ProbeCamera ?? default,
            session?.ProbeFocus ?? default,
            _lastAppliedCamera,
            _lastPostFixOnSceneCameraPosition,
            stabilitySceneCameraPosition,
            _lastAppliedFocus,
            _lastPostFixOnLookAtVector,
            stabilityLookAtVector);
        var result = TitleBackgroundCameraProbeReport.Evaluate(input);

        var lines = new List<string>
        {
            "[CameraProbe] Report",
            $"[CameraProbe] armed={input.Armed}",
        };
        if (session != null)
        {
            lines.Add($"[CameraProbe] armedAt={session.ArmedAt:yyyy-MM-dd HH:mm:ss zzz}");
            lines.Add($"[CameraProbe] baselineCamera={FormatVector(session.BaselineCamera)}");
            lines.Add($"[CameraProbe] baselineFocus={FormatVector(session.BaselineFocus)}");
            lines.Add($"[CameraProbe] probeCamera={FormatVector(session.ProbeCamera)}");
            lines.Add($"[CameraProbe] probeFocus={FormatVector(session.ProbeFocus)}");
            lines.Add($"[CameraProbe] expectedCameraYDelta={FormatFloat(session.ProbeCamera.Y - session.BaselineCamera.Y)}");
            lines.Add($"[CameraProbe] expectedFocusYDelta={FormatFloat(session.ProbeFocus.Y - session.BaselineFocus.Y)}");
        }
        else
        {
            lines.Add("[CameraProbe] baselineCamera=none");
            lines.Add("[CameraProbe] baselineFocus=none");
            lines.Add("[CameraProbe] probeCamera=none");
            lines.Add("[CameraProbe] probeFocus=none");
        }

        lines.AddRange(
        [
            $"[CameraProbe] lastAppliedCamera={FormatVector(_lastAppliedCamera)}",
            $"[CameraProbe] postFixOnSceneCameraPosition={FormatVector(_lastPostFixOnSceneCameraPosition)}",
            $"[CameraProbe] currentSceneCameraPosition={FormatVector(reportTimeSceneCameraPosition)}",
            $"[CameraProbe] delayedSceneCameraPosition={FormatVector(_cameraProbeDelayedSceneCameraPosition)}",
            $"[CameraProbe] lastAppliedFocus={FormatVector(_lastAppliedFocus)}",
            $"[CameraProbe] postFixOnLookAtVector={FormatVector(_lastPostFixOnLookAtVector)}",
            $"[CameraProbe] currentLookAtVector={FormatVector(reportTimeLookAtVector)}",
            $"[CameraProbe] delayedLookAtVector={FormatVector(_cameraProbeDelayedLookAtVector)}",
            $"[CameraProbe] stabilitySampleSource={stabilitySampleSource}",
            $"[CameraProbe] delayedCaptureStatus={_cameraProbeDelayedCaptureStatus}",
            $"[CameraProbe] delayedCaptureError={FormatNone(_cameraProbeDelayedCaptureError)}",
            $"[CameraProbe] currentCameraCaptureStatus={(currentCameraCaptured ? "success" : "failed")}",
            $"[CameraProbe] currentCameraCaptureError={FormatNone(currentCaptureError)}",
            $"[CameraProbe] cameraY.appliedToPostFixOn.delta={FormatVectorAxisDelta(_lastPostFixOnSceneCameraPosition, _lastAppliedCamera, 1)}",
            $"[CameraProbe] cameraY.postFixOnToStabilitySample.delta={FormatVectorAxisDelta(stabilitySceneCameraPosition, _lastPostFixOnSceneCameraPosition, 1)}",
            $"[CameraProbe] focusY.appliedToPostFixOn.delta={FormatVectorAxisDelta(_lastPostFixOnLookAtVector, _lastAppliedFocus, 1)}",
            $"[CameraProbe] focusY.postFixOnToStabilitySample.delta={FormatVectorAxisDelta(stabilityLookAtVector, _lastPostFixOnLookAtVector, 1)}",
            $"[CameraProbe] verdict.cameraYFixOnReflection={TitleBackgroundCameraProbeReport.FormatVerdict(result.CameraYFixOnReflection)}",
            $"[CameraProbe] verdict.cameraYPostFixOnStability={TitleBackgroundCameraProbeReport.FormatVerdict(result.CameraYPostFixOnStability)}",
            $"[CameraProbe] verdict.focusYFixOnReflection={TitleBackgroundCameraProbeReport.FormatVerdict(result.FocusYFixOnReflection)}",
            $"[CameraProbe] verdict.focusYPostFixOnStability={TitleBackgroundCameraProbeReport.FormatVerdict(result.FocusYPostFixOnStability)}",
            $"[CameraProbe] {result.LikelyConclusion}",
        ]);

        return lines;
    }

    public IReadOnlyList<string> RestoreCameraProbe()
    {
        if (_cameraProbeSession == null)
        {
            return ["[CameraProbe] no armed session; nothing to restore."];
        }

        var session = _cameraProbeSession;
        _cameraProbeSession = null;
        session.OriginalSettings.ApplyTo(_configuration);
        _configuration.Save();
        ApplyFromConfiguration();
        ResetCameraOverrideObservation();
        ResetCameraProbeDelayedObservation();

        return
        [
            "[CameraProbe] restored original camera settings.",
            $"[CameraProbe] restoredCamera={FormatVector(session.BaselineCamera)}",
            $"[CameraProbe] restoredFocus={FormatVector(session.BaselineFocus)}",
            $"[CameraProbe] restoredCameraOverrideEnabled={session.OriginalSettings.CameraOverrideEnabled}",
        ];
    }

    public bool ValidateCurrentConfiguration(out string errorMessage)
    {
        var normalized = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(_configuration.TitleBackgroundTerritoryPath);
        _validatedTerritoryPath = string.Empty;
        _validationError = string.Empty;

        if (!TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(normalized, out normalized, out errorMessage))
        {
            _validationError = errorMessage;
            return false;
        }

        var lvbPath = TitleBackgroundPathHelper.BuildLvbPath(normalized);
        try
        {
            if (!_dataManager.FileExists(lvbPath))
            {
                errorMessage = $"LVB が見つかりません: {lvbPath}";
                _validationError = errorMessage;
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"LVB validation failed: {ex.Message}";
            _validationError = errorMessage;
            return false;
        }

        _validatedTerritoryPath = normalized;
        errorMessage = string.Empty;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _framework.Update -= OnFrameworkUpdate;
        RestoreCameraProbeSettingsOnDispose();
        _cameraApplyPending = false;
        ResetCameraOverrideObservation();
        _loadingLobbyType = GameLobbyType.None;
        _lastLobbyUpdateMapId = GameLobbyType.None;
        _currentMapWriteAttempted = false;
        _lastCurrentMapWriteSucceeded = false;
        if (_activeProbeSession != null)
        {
            _activeProbeSession.OriginalSettings.ApplyTo(_configuration);
            _activeProbeSession = null;
        }

        DisposeHooks();
    }

    private void InitializeHooks()
    {
        try
        {
            if (!_addressResolver.Resolve(_sigScanner, _configuration))
            {
                _state = TitleBackgroundServiceState.AddressResolveFailed;
                _stateReason = _addressResolver.LastError;
                return;
            }

            if (!ShouldCreateSceneHooks())
            {
                _state = TitleBackgroundServiceState.Disabled;
                _stateReason = _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly
                    ? "resolver-only"
                    : "無効";
                return;
            }

            _createSceneHook = _gameInteropProvider.HookFromAddress<CreateSceneDelegate>(_addressResolver.CreateScene, CreateSceneDetour);
            _lobbyUpdateHook = _gameInteropProvider.HookFromAddress<LobbyUpdateDelegate>(_addressResolver.LobbyUpdate, LobbyUpdateDetour);
            _loadLobbySceneHook = _gameInteropProvider.HookFromAddress<LoadLobbySceneDelegate>(_addressResolver.LoadLobbyScene, LoadLobbySceneDetour);
            if (ShouldCreateCameraHook())
            {
                _cameraFixOnHook = _gameInteropProvider.HookFromAddress<LobbyCameraFixOnDelegate>(_addressResolver.FixOn, LobbyCameraFixOnDetour);
            }

            _createSceneHook.Enable();
            _lobbyUpdateHook.Enable();
            _loadLobbySceneHook.Enable();
            _cameraFixOnHook?.Enable();
            if (_cameraFixOnHook != null)
            {
                _log.Information(
                    "[XMU BG] LobbyCameraFixOn hook resolved/enabled. address={Address}, enabled={Enabled}",
                    FormatAddress(_addressResolver.FixOn),
                    _cameraFixOnHook.IsEnabled);
            }

            _state = TitleBackgroundServiceState.Disabled;
            _stateReason = "無効";
        }
        catch (Exception ex)
        {
            _state = _createSceneHook == null ? TitleBackgroundServiceState.HookCreateFailed : TitleBackgroundServiceState.HookEnableFailed;
            _stateReason = ex.Message;
            _log.Warning(ex, "TitleBackground: native integration failed.");
            DisposeHooks();
        }
    }

    private void LoadLobbySceneDetour(GameLobbyType mapId)
    {
        _loadingLobbyType = mapId;
        RecordProbeLoadLobbyScene(mapId);
        _log.Debug("[XMU BG] LoadLobbyScene mapId={MapId}", mapId);
        _loadLobbySceneHook?.Original(mapId);
    }

    private int CreateSceneDetour(byte* territoryPath, uint territoryId, nint p3, uint layerFilterKey, nint festivals, int p6, uint contentFinderConditionId)
    {
        byte[]? overrideBytes = null;
        try
        {
            var lobbyType = EffectiveLobbyType;
            var originalPath = territoryPath == null ? string.Empty : Marshal.PtrToStringUTF8((nint)territoryPath) ?? string.Empty;
            _lastObservedCreateScenePath = originalPath;
            RecordProbeCreateScene(lobbyType, originalPath, territoryId, layerFilterKey);
            _log.Debug("[XMU BG] CreateScene lobbyType={LobbyType}, path={Path}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", lobbyType, originalPath, territoryId, layerFilterKey);

            if (IsHookProbeMode())
            {
                return _createSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
            }

            if (ShouldOverrideCharaSelect(lobbyType))
            {
                LayoutWorld.UnloadPrefetchLayout();
                var presetTerritoryId = GetEffectiveOverrideTerritoryId();
                if (presetTerritoryId != 0)
                {
                    territoryId = presetTerritoryId;
                }

                if (_configuration.TitleBackgroundLayoutLayerFilterKey != 0)
                {
                    layerFilterKey = _configuration.TitleBackgroundLayoutLayerFilterKey;
                }

                overrideBytes = Encoding.UTF8.GetBytes(_validatedTerritoryPath + '\0');
                _cameraApplyPending = true;
                _lastOverrideApplied = true;
                _lastOverrideLobbyType = lobbyType;
                _lastOverrideOriginalPath = originalPath;
                _lastOverrideNewPath = _validatedTerritoryPath;
                _log.Information("[XMU BG] Override CharaSelect scene lobbyType={LobbyType}, originalPath={OriginalPath}, newPath={NewPath}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", lobbyType, originalPath, _validatedTerritoryPath, territoryId, layerFilterKey);
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(CreateSceneDetour));
            overrideBytes = null;
        }
        finally
        {
            _loadingLobbyType = GameLobbyType.None;
        }

        if (overrideBytes is { Length: > 0 })
        {
            fixed (byte* overridePath = overrideBytes)
            {
                return _createSceneHook?.Original(overridePath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
            }
        }

        return _createSceneHook?.Original(territoryPath, territoryId, p3, layerFilterKey, festivals, p6, contentFinderConditionId) ?? 0;
    }

    private byte LobbyUpdateDetour(GameLobbyType mapId, int time)
    {
        try
        {
            RecordProbeLobbyUpdate(mapId, time);
            if (IsHookProbeMode())
            {
                return _lobbyUpdateHook?.Original(mapId, time) ?? 0;
            }

            if (ShouldResetCurrentMapForReload(mapId))
            {
                _currentMapWriteAttempted = true;
                _lastCurrentMapWriteSucceeded = TryWriteCurrentLobbyMap(GameLobbyType.None);
                _log.Debug("[XMU BG] CurrentLobbyMap reset requested. next={NextMap}, success={Success}", mapId, _lastCurrentMapWriteSucceeded);
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyUpdateDetour));
        }
        finally
        {
            _lastLobbyUpdateMapId = mapId;
        }

        return _lobbyUpdateHook?.Original(mapId, time) ?? 0;
    }

    private nint LobbyCameraFixOnDetour(nint self, float* cameraPos, float* focusPos, float fovY)
    {
        float[]? cameraOverride = null;
        float[]? focusOverride = null;
        var overrideFovY = fovY;
        var invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
        try
        {
            _lastObservedFixOnCamera = TryReadVector(cameraPos);
            _lastObservedFixOnFocus = TryReadVector(focusPos);
            _lastObservedFixOnFovY = fovY;
            if (ShouldOverrideCamera())
            {
                _cameraApplyPending = false;
                var plan = TitleBackgroundCameraOverridePlan.FromConfiguration(_configuration);
                cameraOverride =
                [
                    plan.Camera.X,
                    plan.Camera.Y,
                    plan.Camera.Z,
                ];
                focusOverride =
                [
                    plan.Focus.X,
                    plan.Focus.Y,
                    plan.Focus.Z,
                ];
                overrideFovY = plan.FovY;
                _lastCameraOverrideApplied = true;
                invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(true);
                _lastAppliedCamera = plan.Camera;
                _lastAppliedFocus = plan.Focus;
                _lastAppliedFovY = plan.FovY;
                _log.Information(
                    "[XMU BG] Camera override applied. camera={Camera}, focus={Focus}, fovY={FovY}",
                    FormatVector(plan.Camera),
                    FormatVector(plan.Focus),
                    plan.FovY);
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyCameraFixOnDetour));
            cameraOverride = null;
            focusOverride = null;
            overrideFovY = fovY;
            invocationMode = TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(false);
        }

        _lastFixOnInvocationMode = invocationMode;
        nint result;
        if (cameraOverride != null && focusOverride != null)
        {
            fixed (float* cameraPointer = cameraOverride)
            fixed (float* focusPointer = focusOverride)
            {
                result = _cameraFixOnHook?.Original(self, cameraPointer, focusPointer, overrideFovY) ?? nint.Zero;
            }
        }
        else
        {
            result = _cameraFixOnHook?.Original(self, cameraPos, focusPos, fovY) ?? nint.Zero;
        }

        CapturePostFixOnCameraState();
        ScheduleCameraProbeDelayedCapture(cameraOverride != null && focusOverride != null);
        return result;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (_disposed || _cameraProbeDelayedCaptureFramesRemaining <= 0)
        {
            return;
        }

        _cameraProbeDelayedCaptureFramesRemaining--;
        if (_cameraProbeDelayedCaptureFramesRemaining > 0)
        {
            return;
        }

        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            _cameraProbeDelayedSceneCameraPosition = snapshot.SceneCameraPosition;
            _cameraProbeDelayedLookAtVector = snapshot.LookAtVector;
            _cameraProbeDelayedCaptureStatus = "success";
            _cameraProbeDelayedCaptureError = string.Empty;
        }
        else
        {
            _cameraProbeDelayedCaptureStatus = "failed";
            _cameraProbeDelayedCaptureError = string.IsNullOrWhiteSpace(errorMessage) ? "unknown" : errorMessage;
        }
    }

    private void ScheduleCameraProbeDelayedCapture(bool overrideAppliedInThisInvocation)
    {
        if (_cameraProbeSession == null || !overrideAppliedInThisInvocation)
        {
            return;
        }

        _cameraProbeDelayedCaptureFramesRemaining = 30;
        _cameraProbeDelayedCaptureStatus = "pending";
        _cameraProbeDelayedCaptureError = string.Empty;
        _cameraProbeDelayedSceneCameraPosition = null;
        _cameraProbeDelayedLookAtVector = null;
    }

    private void ResetCameraProbeDelayedObservation()
    {
        _cameraProbeDelayedCaptureFramesRemaining = 0;
        _cameraProbeDelayedCaptureStatus = "not-run";
        _cameraProbeDelayedCaptureError = string.Empty;
        _cameraProbeDelayedSceneCameraPosition = null;
        _cameraProbeDelayedLookAtVector = null;
    }

    private void RestoreCameraProbeSettingsOnDispose()
    {
        if (_cameraProbeSession == null)
        {
            return;
        }

        try
        {
            _cameraProbeSession.OriginalSettings.ApplyTo(_configuration);
            _configuration.Save();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "TitleBackground camera probe failed to restore settings on dispose.");
        }
        finally
        {
            _cameraProbeSession = null;
            ResetCameraProbeDelayedObservation();
        }
    }

    private void CapturePostFixOnCameraState()
    {
        ClearPostFixOnCameraObservation();
        if (!TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            MarkPostFixOnCameraCaptureFailed(errorMessage);
            return;
        }

        _lastPostFixOnSceneCameraPosition = snapshot.SceneCameraPosition;
        _lastPostFixOnLookAtVector = snapshot.LookAtVector;
        _lastPostFixOnDistance = snapshot.Distance;
        _lastPostFixOnFovY = snapshot.FovY;
        _lastPostFixOnCameraCaptureStatus = "success";
        _lastPostFixOnCameraCaptureError = string.Empty;
    }

    private bool TryCaptureActiveCameraSnapshot(out TitleBackgroundActiveCameraSnapshot snapshot, out string errorMessage)
    {
        snapshot = default;
        errorMessage = string.Empty;
        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var activeCamera = cameraManager->GetActiveCamera();
            if (activeCamera == null)
            {
                errorMessage = "active camera unavailable";
                return false;
            }

            var sceneCameraPosition = ToNumerics(activeCamera->CameraBase.SceneCamera.Position);
            if (!TitleBackgroundCameraMath.IsFiniteVector(sceneCameraPosition))
            {
                errorMessage = "SceneCamera.Position contains non-finite values";
                return false;
            }

            var lookAtVector = ToNumerics(activeCamera->CameraBase.SceneCamera.LookAtVector);
            if (!TitleBackgroundCameraMath.IsFiniteVector(lookAtVector))
            {
                errorMessage = "SceneCamera.LookAtVector contains non-finite values";
                return false;
            }

            snapshot = new TitleBackgroundActiveCameraSnapshot(
                sceneCameraPosition,
                lookAtVector,
                float.IsFinite(activeCamera->Distance) ? activeCamera->Distance : null,
                float.IsFinite(activeCamera->FoV) ? activeCamera->FoV : null);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground active camera capture failed.");
            return false;
        }
    }

    private void MarkPostFixOnCameraCaptureFailed(string reason)
    {
        _lastPostFixOnCameraCaptureStatus = "failed";
        _lastPostFixOnCameraCaptureError = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
    }

    private void ClearPostFixOnCameraObservation()
    {
        _lastPostFixOnCameraCaptureStatus = "unavailable";
        _lastPostFixOnCameraCaptureError = string.Empty;
        _lastPostFixOnSceneCameraPosition = null;
        _lastPostFixOnLookAtVector = null;
        _lastPostFixOnDistance = null;
        _lastPostFixOnFovY = null;
    }

    private bool ShouldResetCurrentMapForReload(GameLobbyType nextMap)
    {
        return _state == TitleBackgroundServiceState.Ready
            && !IsHookProbeMode()
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && TryReadCurrentLobbyMap(out var currentMap)
            && GameLobbyTypeHelper.IsTitleCharaSelectTransition(currentMap, nextMap);
    }

    private bool ShouldOverrideCharaSelect(GameLobbyType lobbyType)
    {
        return IsOverrideMutationBranchArmed()
            && lobbyType == GameLobbyType.CharaSelect;
    }

    private bool IsSceneOverrideEnabled()
    {
        return _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    private bool IsOverrideMutationBranchArmed()
    {
        return _state == TitleBackgroundServiceState.Ready
            && !IsHookProbeMode()
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && !string.IsNullOrWhiteSpace(_validatedTerritoryPath);
    }

    private uint GetEffectiveOverrideTerritoryId()
    {
        return _configuration.TitleBackgroundLayoutTerritoryTypeId != 0
            ? _configuration.TitleBackgroundLayoutTerritoryTypeId
            : _configuration.TitleBackgroundTerritoryTypeId;
    }

    private bool ShouldOverrideCamera()
    {
        var currentMapAvailable = TryReadCurrentLobbyMap(out var currentMap);
        return TitleBackgroundCameraOverridePlan.ShouldApply(
            _configuration.TitleBackgroundCameraOverrideEnabled,
            IsHookProbeMode(),
            _cameraApplyPending,
            _state == TitleBackgroundServiceState.Ready,
            currentMapAvailable,
            currentMap);
    }

    private bool TryReadCurrentLobbyMap(out GameLobbyType map)
    {
        map = GameLobbyType.None;
        if (_addressResolver.LobbyCurrentMap == nint.Zero)
        {
            return false;
        }

        var raw = *(short*)_addressResolver.LobbyCurrentMap;
        if (!Enum.IsDefined(typeof(GameLobbyType), raw))
        {
            return false;
        }

        map = (GameLobbyType)raw;
        return true;
    }

    private bool TryWriteCurrentLobbyMap(GameLobbyType map)
    {
        if (_addressResolver.LobbyCurrentMap == nint.Zero)
        {
            return false;
        }

        *(short*)_addressResolver.LobbyCurrentMap = (short)map;
        return true;
    }

    private void NormalizeConfiguration()
    {
        _configuration.TitleBackgroundTerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(_configuration.TitleBackgroundTerritoryPath);
        _configuration.TitleBackgroundCreateSceneResolverMode = NormalizeResolverMode(_configuration.TitleBackgroundCreateSceneResolverMode);
        _configuration.TitleBackgroundLobbyUpdateResolverMode = NormalizeResolverMode(_configuration.TitleBackgroundLobbyUpdateResolverMode);
        _configuration.TitleBackgroundCharacterPositionX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionX);
        _configuration.TitleBackgroundCharacterPositionY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionY);
        _configuration.TitleBackgroundCharacterPositionZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterPositionZ);
        _configuration.TitleBackgroundCharacterRotation = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCharacterRotation);
        _configuration.TitleBackgroundCameraX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraX);
        _configuration.TitleBackgroundCameraY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraY);
        _configuration.TitleBackgroundCameraZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraZ);
        _configuration.TitleBackgroundFocusX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusX);
        _configuration.TitleBackgroundFocusY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusY);
        _configuration.TitleBackgroundFocusZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusZ);
        _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(_configuration.TitleBackgroundFovY);
        _configuration.TitleBackgroundCreateSceneSignature = NormalizeSignature(_configuration.TitleBackgroundCreateSceneSignature);
        _configuration.TitleBackgroundFixOnSignature = NormalizeSignature(_configuration.TitleBackgroundFixOnSignature);
        _configuration.TitleBackgroundLobbyUpdateSignature = NormalizeSignature(_configuration.TitleBackgroundLobbyUpdateSignature);
        _configuration.TitleBackgroundLoadLobbySceneSignature = NormalizeSignature(_configuration.TitleBackgroundLoadLobbySceneSignature);
        _configuration.TitleBackgroundLobbyCurrentMapSignature = NormalizeSignature(_configuration.TitleBackgroundLobbyCurrentMapSignature);
        TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(_configuration);
    }

    private bool AreSceneReady()
    {
        return AreSceneHooksReady() && AreNativeSceneAddressesReady();
    }

    private bool AreSceneHooksReady()
    {
        if (!TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled))
        {
            return false;
        }

        return TitleBackgroundRuntimeModeHelper.AreSceneHooksReady(
            _createSceneHook != null,
            _lobbyUpdateHook != null,
            _loadLobbySceneHook != null);
    }

    private bool AreCameraHookReady()
    {
        return _cameraFixOnHook != null;
    }

    private bool IsCameraHookRequired()
    {
        return TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled);
    }

    private bool ShouldCreateCameraHook()
    {
        return IsCameraHookRequired()
            || (ShouldCreateSceneHooks()
                && !IsHookProbeMode()
                && _addressResolver.FixOn != nint.Zero);
    }

    private bool AreNativeSceneAddressesReady()
    {
        if (IsHookProbeMode())
        {
            return TitleBackgroundRuntimeModeHelper.AreNativeProbeAddressesReady(
                _addressResolver.CreateScene != nint.Zero,
                _addressResolver.LobbyUpdate != nint.Zero,
                _addressResolver.LoadLobbyScene != nint.Zero);
        }

        return TitleBackgroundRuntimeModeHelper.AreNativeSceneAddressesReady(
            _addressResolver.CreateScene != nint.Zero,
            _addressResolver.LobbyUpdate != nint.Zero,
            _addressResolver.LoadLobbyScene != nint.Zero,
            _addressResolver.LobbyCurrentMap != nint.Zero);
    }

    private bool ShouldCreateSceneHooks()
    {
        return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled);
    }

    private bool ShouldValidateSceneOverrideConfiguration()
    {
        return TitleBackgroundRuntimeModeHelper.ShouldValidateSceneOverrideConfiguration(
            _configuration.TitleBackgroundRuntimeMode);
    }

    private bool IsHookProbeMode()
    {
        return _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.HookProbe;
    }

    private bool IsHookSetAlignedWithConfiguration()
    {
        var shouldCreateCameraHook = TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled);
        var shouldCreateOptionalCameraHook = ShouldCreateSceneHooks()
            && !IsHookProbeMode()
            && _addressResolver.FixOn != nint.Zero;
        return (shouldCreateCameraHook || shouldCreateOptionalCameraHook) == (_cameraFixOnHook != null);
    }

    private void MarkRuntimeError(Exception ex, string hookName)
    {
        _state = TitleBackgroundServiceState.RuntimeError;
        _stateReason = $"{hookName}: {ex.Message}";
        _cameraApplyPending = false;
        if (_activeProbeSession != null)
        {
            _activeProbeSession.RuntimeErrorOccurred = true;
            _activeProbeSession.LastError = _stateReason;
        }

        _log.Warning(ex, "TitleBackground runtime error in {HookName}.", hookName);
    }

    private void ResetCameraOverrideObservation()
    {
        _lastCameraOverrideApplied = false;
        _lastAppliedCamera = null;
        _lastAppliedFocus = null;
        _lastAppliedFovY = null;
        _lastFixOnInvocationMode = "not-run";
        ClearPostFixOnCameraObservation();
        _lastPostFixOnCameraCaptureStatus = "not-run";
    }

    private void ResetSceneOverrideObservation()
    {
        _lastOverrideApplied = false;
        _lastOverrideLobbyType = GameLobbyType.None;
        _lastOverrideOriginalPath = string.Empty;
        _lastOverrideNewPath = string.Empty;
    }

    private void UpdateAutomaticProbeCounterState()
    {
        var shouldEnable = TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCreateSceneResolverMode,
            _configuration.TitleBackgroundLobbyUpdateResolverMode);
        if (shouldEnable && !_automaticProbeCountersEnabled)
        {
            _automaticProbeCounters = new TitleBackgroundProbeCounters();
        }

        _automaticProbeCountersEnabled = shouldEnable;
    }

    private void DisposeHooks()
    {
        DisposeHook(_cameraFixOnHook, nameof(_cameraFixOnHook));
        DisposeHook(_loadLobbySceneHook, nameof(_loadLobbySceneHook));
        DisposeHook(_lobbyUpdateHook, nameof(_lobbyUpdateHook));
        DisposeHook(_createSceneHook, nameof(_createSceneHook));
        _cameraFixOnHook = null;
        _loadLobbySceneHook = null;
        _lobbyUpdateHook = null;
        _createSceneHook = null;
    }

    private void DisposeHook<T>(Hook<T>? hook, string name)
        where T : Delegate
    {
        try
        {
            hook?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to dispose TitleBackground hook {HookName}.", name);
        }
    }

    private TitleBackgroundSignatureScanResult? FindResult(string name)
    {
        return _addressResolver.ScanResults.FirstOrDefault(result => result.Name == name);
    }

    private string GetTargetWithinText(string name)
    {
        return FindResult(name)?.TargetWithinText.ToString() ?? "False";
    }

    private nint GetResolvedCandidate(string name)
    {
        return FindResult(name)?.ResolvedCandidate ?? nint.Zero;
    }

    private string GetHookTargetVerified(string name)
    {
        return FindResult(name)?.HookTargetVerified.ToString() ?? "False";
    }

    private string GetResolveMethod(string name, string fallback)
    {
        return FindResult(name)?.Method ?? fallback;
    }

    private string GetCandidateReadable(string name)
    {
        return FindResult(name)?.CandidateDiagnostics.Readable.ToString() ?? "False";
    }

    private string GetCandidatePrologueHint(string name)
    {
        return FindResult(name)?.CandidateDiagnostics.PrologueHint ?? "unavailable";
    }

    private string GetCandidateFirstBytes(string name)
    {
        var bytes = FindResult(name)?.CandidateDiagnostics.FirstBytesHex ?? string.Empty;
        return string.IsNullOrWhiteSpace(bytes) ? "none" : bytes;
    }

    private string ReadCurrentLobbyMapRawText()
    {
        if (_addressResolver.LobbyCurrentMap == nint.Zero)
        {
            return "unavailable";
        }

        return (*(short*)_addressResolver.LobbyCurrentMap).ToString();
    }

    private string ReadCurrentLobbyMapDecodedText()
    {
        if (!TryReadCurrentLobbyMap(out var map))
        {
            return "Unknown";
        }

        return map.ToString();
    }

    private static string BuildAddressLine(string key, string value)
    {
        return $"{key}={(string.IsNullOrWhiteSpace(value) ? "no" : "yes")}";
    }

    private static bool IsHookEnabled<T>(Hook<T>? hook)
        where T : Delegate
    {
        return hook?.IsEnabled == true;
    }

    private static string FormatAddress(nint address)
    {
        return address == nint.Zero ? "0x0" : $"0x{address.ToInt64():X}";
    }

    private static string FormatNone(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }

    private static string FormatVector(Vector3? value)
    {
        return value.HasValue ? FormatVector(value.Value) : "none";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
    }

    private static string FormatFloat(float? value)
    {
        return value.HasValue ? value.Value.ToString("0.###") : "none";
    }

    private static string FormatVectorDelta(Vector3? current, Vector3? baseline)
    {
        return FormatVector(TitleBackgroundCameraMath.CalculateVectorDelta(current, baseline));
    }

    private static string FormatVectorAxisDelta(Vector3? current, Vector3? baseline, int axis)
    {
        var delta = TitleBackgroundCameraMath.CalculateVectorDelta(current, baseline);
        if (!delta.HasValue)
        {
            return "none";
        }

        return axis switch
        {
            0 => FormatFloat(delta.Value.X),
            1 => FormatFloat(delta.Value.Y),
            2 => FormatFloat(delta.Value.Z),
            _ => "none",
        };
    }

    private static string FormatFloatDelta(float? current, float? baseline)
    {
        return FormatFloat(TitleBackgroundCameraMath.CalculateFloatDelta(current, baseline));
    }

    private static Vector3? TryReadVector(float* values)
    {
        if (values == null)
        {
            return null;
        }

        var vector = new Vector3(values[0], values[1], values[2]);
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z)
            ? vector
            : null;
    }

    private static Vector3 ToNumerics(ClientVector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private static string NormalizeSignature(string? signature)
    {
        return (signature ?? string.Empty).Trim();
    }

    private string TryRestoreProbeSettings(TitleBackgroundProbeSettingsSnapshot snapshot)
    {
        try
        {
            snapshot.ApplyTo(_configuration);
            ReloadNativeIntegration();
            return "[Probe] original settings restored.";
        }
        catch (Exception ex)
        {
            if (_lastProbeSession != null)
            {
                _lastProbeSession.RuntimeErrorOccurred = true;
                _lastProbeSession.LastError = $"restore failed: {ex.Message}";
            }

            _log.Warning(ex, "TitleBackground probe failed to restore original settings.");
            return $"[Probe] failed to restore original settings: {ex.Message}";
        }
    }

    private TitleBackgroundProbeReportInput BuildProbeReportInput(TitleBackgroundProbeSession? session, bool isActive)
    {
        var useAutomaticCounters = _automaticProbeCountersEnabled;
        var hooksEnabled = isActive || session == null ? AreAnyHooksEnabled() : session.HookEnabledAtEnd;
        var runtimeError = (isActive || session == null) && _state == TitleBackgroundServiceState.RuntimeError;
        if (session != null)
        {
            runtimeError |= session.RuntimeErrorOccurred;
        }

        var lastError = session?.LastError ?? string.Empty;
        var createSceneCallCount = useAutomaticCounters ? _automaticProbeCounters.CreateSceneCallCount : session?.CreateSceneCallCount ?? 0;
        var lobbyUpdateCallCount = useAutomaticCounters ? _automaticProbeCounters.LobbyUpdateCallCount : session?.LobbyUpdateCallCount ?? 0;
        var loadLobbySceneCallCount = useAutomaticCounters ? _automaticProbeCounters.LoadLobbySceneCallCount : session?.LoadLobbySceneCallCount ?? 0;
        var lastCreateScenePath = useAutomaticCounters ? _automaticProbeCounters.LastCreateScenePath : session?.LastCreateScenePath ?? string.Empty;
        var lastCreateSceneTerritoryId = useAutomaticCounters ? _automaticProbeCounters.LastCreateSceneTerritoryId : session?.LastCreateSceneTerritoryId ?? 0;
        var lastCreateSceneLayerFilterKey = useAutomaticCounters ? _automaticProbeCounters.LastCreateSceneLayerFilterKey : session?.LastCreateSceneLayerFilterKey ?? 0;
        var lastLobbyUpdateMapId = useAutomaticCounters ? _automaticProbeCounters.LastLobbyUpdateMapId : session?.LastLobbyUpdateMapId ?? GameLobbyType.None;
        var lastLobbyUpdateTime = useAutomaticCounters ? _automaticProbeCounters.LastLobbyUpdateTime : session?.LastLobbyUpdateTime ?? 0;
        var lastLoadLobbySceneMapId = useAutomaticCounters ? _automaticProbeCounters.LastLoadLobbySceneMapId : session?.LastLoadLobbySceneMapId ?? GameLobbyType.None;

        return new TitleBackgroundProbeReportInput(
            ProbeActive: isActive,
            OverrideEnabled: _configuration.TitleBackgroundOverrideEnabled,
            RuntimeMode: _configuration.TitleBackgroundRuntimeMode,
            CreateSceneResolverMode: _configuration.TitleBackgroundCreateSceneResolverMode,
            LobbyUpdateResolverMode: _configuration.TitleBackgroundLobbyUpdateResolverMode,
            AutomaticCountersEnabled: _automaticProbeCountersEnabled,
            HooksEnabled: hooksEnabled,
            RuntimeError: runtimeError,
            ResolverError: _addressResolver.LastError,
            LastError: lastError,
            CreateSceneCallCount: createSceneCallCount,
            LobbyUpdateCallCount: lobbyUpdateCallCount,
            LoadLobbySceneCallCount: loadLobbySceneCallCount,
            LastCreateScenePath: lastCreateScenePath,
            LastCreateSceneTerritoryId: lastCreateSceneTerritoryId,
            LastCreateSceneLayerFilterKey: lastCreateSceneLayerFilterKey,
            LastLobbyUpdateMapId: lastLobbyUpdateMapId,
            LastLobbyUpdateTime: lastLobbyUpdateTime,
            LastLoadLobbySceneMapId: lastLoadLobbySceneMapId);
    }

    private static IReadOnlyList<string> BuildProbeReportSummaryLines(TitleBackgroundProbeReportInput input)
    {
        var attentionItems = TitleBackgroundProbeReportHelper.GetAttentionItems(input);
        var lines = new List<string>
        {
            "[Probe] Summary",
            $"[Probe] active={input.ProbeActive}",
            $"[Probe] status={TitleBackgroundProbeReportHelper.GetOverallStatus(input)}",
            $"[Probe] modeStatus={TitleBackgroundProbeReportHelper.GetModeStatus(input)}",
            $"[Probe] runtimeMode={input.RuntimeMode}",
            $"[Probe] overrideEnabled={input.OverrideEnabled}",
            $"[Probe] createSceneResolver={input.CreateSceneResolverMode}",
            $"[Probe] lobbyUpdateResolver={input.LobbyUpdateResolverMode}",
            $"[Probe] automaticCountersEnabled={input.AutomaticCountersEnabled}",
            $"[Probe] hooksEnabled={input.HooksEnabled}",
            "",
            "[Probe] Observed",
            $"[Probe] CreateScene.callCount={input.CreateSceneCallCount}",
            $"[Probe] LobbyUpdate.callCount={input.LobbyUpdateCallCount}",
            $"[Probe] LoadLobbyScene.callCount={input.LoadLobbySceneCallCount}",
            "",
            "[Probe] Latest Values",
            $"[Probe] CreateScene.lastPath={FormatNone(input.LastCreateScenePath)}",
            $"[Probe] CreateScene.lastTerritoryId={input.LastCreateSceneTerritoryId}",
            $"[Probe] CreateScene.lastLayerFilterKey={input.LastCreateSceneLayerFilterKey}",
            $"[Probe] LobbyUpdate.lastMapId={input.LastLobbyUpdateMapId}",
            $"[Probe] LobbyUpdate.lastTime={input.LastLobbyUpdateTime}",
            $"[Probe] LoadLobbyScene.lastMapId={input.LastLoadLobbySceneMapId}",
            "",
            "[Probe] Not Yet Observed / Attention",
        };

        if (attentionItems.Count == 0)
        {
            lines.Add("[Probe] none");
        }
        else
        {
            foreach (var item in attentionItems)
            {
                lines.Add($"[Probe] {item}");
            }
        }

        lines.Add("");
        lines.Add("[Probe] Next Check");
        lines.Add($"[Probe] {TitleBackgroundProbeReportHelper.GetNextCheck(input)}");
        return lines;
    }

    private IReadOnlyList<string> GetProbeReportLines(TitleBackgroundProbeSession session, bool isActive)
    {
        var hooksEnabled = isActive ? AreAnyHooksEnabled() : session.HookEnabledAtEnd;
        var runtimeError = session.RuntimeErrorOccurred || (isActive && _state == TitleBackgroundServiceState.RuntimeError);
        var observedAnyDetour = session.CreateSceneCallCount > 0 || session.LobbyUpdateCallCount > 0;
        var result = !hooksEnabled || runtimeError
            ? "Failure"
            : observedAnyDetour
                ? "Success"
                : "Warning";

        return
        [
            $"[Probe] active={isActive}",
            $"[Probe] startedAt={session.StartedAt:yyyy-MM-dd HH:mm:ss zzz}",
            $"[Probe] result={result}",
            $"[Probe] hooksEnabled={hooksEnabled}",
            $"[Probe] hookEnabledAtStart={session.HookEnabledAtStart}",
            $"[Probe] runtimeError={runtimeError}",
            $"[Probe] createSceneCalls={session.CreateSceneCallCount}",
            $"[Probe] createSceneCharaSelectCalls={session.CreateSceneCharaSelectCallCount}",
            $"[Probe] lobbyUpdateCalls={session.LobbyUpdateCallCount}",
            $"[Probe] loadLobbySceneCalls={session.LoadLobbySceneCallCount}",
            $"[Probe] lastCreateSceneLobbyType={session.LastCreateSceneLobbyType}",
            $"[Probe] lastCreateScenePath={(string.IsNullOrWhiteSpace(session.LastCreateScenePath) ? "none" : session.LastCreateScenePath)}",
            $"[Probe] lastCreateSceneTerritoryId={session.LastCreateSceneTerritoryId}",
            $"[Probe] lastCreateSceneLayerFilterKey={session.LastCreateSceneLayerFilterKey}",
            $"[Probe] lastLobbyUpdateMapId={session.LastLobbyUpdateMapId}",
            $"[Probe] lastLobbyUpdateTime={session.LastLobbyUpdateTime}",
            $"[Probe] lastLoadLobbySceneMapId={session.LastLoadLobbySceneMapId}",
            $"[Probe] createSceneHistory={FormatProbeHistory(session.CreateSceneHistory)}",
            $"[Probe] lastError={(string.IsNullOrWhiteSpace(session.LastError) ? "none" : session.LastError)}",
        ];
    }

    private void RecordProbeCreateScene(GameLobbyType lobbyType, string path, uint territoryId, uint layerFilterKey)
    {
        if (_automaticProbeCountersEnabled)
        {
            _automaticProbeCounters.CreateSceneCallCount++;
            _automaticProbeCounters.LastCreateScenePath = path;
            _automaticProbeCounters.LastCreateSceneTerritoryId = territoryId;
            _automaticProbeCounters.LastCreateSceneLayerFilterKey = layerFilterKey;
        }

        if (_activeProbeSession == null)
        {
            return;
        }

        _activeProbeSession.CreateSceneCallCount++;
        if (lobbyType == GameLobbyType.CharaSelect)
        {
            _activeProbeSession.CreateSceneCharaSelectCallCount++;
        }

        _activeProbeSession.LastCreateSceneLobbyType = lobbyType;
        _activeProbeSession.LastCreateScenePath = path;
        _activeProbeSession.LastCreateSceneTerritoryId = territoryId;
        _activeProbeSession.LastCreateSceneLayerFilterKey = layerFilterKey;
        _activeProbeSession.CreateSceneHistory.Add(
            $"lobbyType={lobbyType},path={(string.IsNullOrWhiteSpace(path) ? "none" : path)},territoryId={territoryId},layerFilterKey={layerFilterKey}");
        if (_activeProbeSession.CreateSceneHistory.Count > 5)
        {
            _activeProbeSession.CreateSceneHistory.RemoveAt(0);
        }
    }

    private static string FormatProbeHistory(IReadOnlyList<string> history)
    {
        return history.Count == 0 ? "none" : string.Join(" | ", history);
    }

    private void RecordProbeLobbyUpdate(GameLobbyType mapId, int time)
    {
        if (_automaticProbeCountersEnabled)
        {
            _automaticProbeCounters.LobbyUpdateCallCount++;
            _automaticProbeCounters.LastLobbyUpdateMapId = mapId;
            _automaticProbeCounters.LastLobbyUpdateTime = time;
        }

        if (_activeProbeSession == null)
        {
            return;
        }

        _activeProbeSession.LobbyUpdateCallCount++;
        _activeProbeSession.LastLobbyUpdateMapId = mapId;
        _activeProbeSession.LastLobbyUpdateTime = time;
    }

    private void RecordProbeLoadLobbyScene(GameLobbyType mapId)
    {
        if (_automaticProbeCountersEnabled)
        {
            _automaticProbeCounters.LoadLobbySceneCallCount++;
            _automaticProbeCounters.LastLoadLobbySceneMapId = mapId;
        }

        if (_activeProbeSession == null)
        {
            return;
        }

        _activeProbeSession.LoadLobbySceneCallCount++;
        _activeProbeSession.LastLoadLobbySceneMapId = mapId;
    }

    private bool AreAnyHooksEnabled()
    {
        return IsHookEnabled(_createSceneHook)
            || IsHookEnabled(_lobbyUpdateHook)
            || IsHookEnabled(_loadLobbySceneHook)
            || IsHookEnabled(_cameraFixOnHook);
    }

    private static TitleBackgroundResolverMode NormalizeResolverMode(TitleBackgroundResolverMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundResolverMode), mode)
            ? mode
            : TitleBackgroundResolverMode.AutoDiagnosticOnly;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CreateSceneDelegate(byte* territoryPath, uint territoryId, nint p3, uint layerFilterKey, nint festivals, int p6, uint contentFinderConditionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte LobbyUpdateDelegate(GameLobbyType mapId, int time);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LoadLobbySceneDelegate(GameLobbyType mapId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    // UNVERIFIED ABI - DO NOT ENABLE BY DEFAULT. Phase 1 does not create this hook.
    private delegate nint LobbyCameraFixOnDelegate(nint self, float* cameraPos, float* focusPos, float fovY);

    private readonly record struct TitleBackgroundActiveCameraSnapshot(
        Vector3 SceneCameraPosition,
        Vector3 LookAtVector,
        float? Distance,
        float? FovY);

    private sealed class TitleBackgroundProbeSession
    {
        public TitleBackgroundProbeSession(TitleBackgroundProbeSettingsSnapshot originalSettings)
        {
            OriginalSettings = originalSettings;
        }

        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public TitleBackgroundProbeSettingsSnapshot OriginalSettings { get; }
        public bool HookEnabledAtStart { get; set; }
        public bool HookEnabledAtEnd { get; set; }
        public bool RuntimeErrorOccurred { get; set; }
        public string LastError { get; set; } = string.Empty;
        public int CreateSceneCallCount { get; set; }
        public int CreateSceneCharaSelectCallCount { get; set; }
        public int LobbyUpdateCallCount { get; set; }
        public int LoadLobbySceneCallCount { get; set; }
        public GameLobbyType LastCreateSceneLobbyType { get; set; } = GameLobbyType.None;
        public string LastCreateScenePath { get; set; } = string.Empty;
        public uint LastCreateSceneTerritoryId { get; set; }
        public uint LastCreateSceneLayerFilterKey { get; set; }
        public List<string> CreateSceneHistory { get; } = [];
        public GameLobbyType LastLobbyUpdateMapId { get; set; } = GameLobbyType.None;
        public int LastLobbyUpdateTime { get; set; }
        public GameLobbyType LastLoadLobbySceneMapId { get; set; } = GameLobbyType.None;
    }

    private sealed class TitleBackgroundProbeCounters
    {
        public int CreateSceneCallCount { get; set; }
        public string LastCreateScenePath { get; set; } = string.Empty;
        public uint LastCreateSceneTerritoryId { get; set; }
        public uint LastCreateSceneLayerFilterKey { get; set; }
        public int LobbyUpdateCallCount { get; set; }
        public GameLobbyType LastLobbyUpdateMapId { get; set; } = GameLobbyType.None;
        public int LastLobbyUpdateTime { get; set; }
        public int LoadLobbySceneCallCount { get; set; }
        public GameLobbyType LastLoadLobbySceneMapId { get; set; } = GameLobbyType.None;
    }

    private sealed class TitleBackgroundCameraProbeSession
    {
        public TitleBackgroundCameraProbeSession(
            TitleBackgroundCameraProbeSettingsSnapshot originalSettings,
            Vector3 baselineCamera,
            Vector3 baselineFocus,
            Vector3 probeCamera,
            Vector3 probeFocus)
        {
            OriginalSettings = originalSettings;
            BaselineCamera = baselineCamera;
            BaselineFocus = baselineFocus;
            ProbeCamera = probeCamera;
            ProbeFocus = probeFocus;
        }

        public DateTimeOffset ArmedAt { get; } = DateTimeOffset.Now;
        public TitleBackgroundCameraProbeSettingsSnapshot OriginalSettings { get; }
        public Vector3 BaselineCamera { get; }
        public Vector3 BaselineFocus { get; }
        public Vector3 ProbeCamera { get; }
        public Vector3 ProbeFocus { get; }
    }

    private sealed record TitleBackgroundCameraProbeSettingsSnapshot(
        string SelectedPresetId,
        bool CameraOverrideEnabled,
        float CameraX,
        float CameraY,
        float CameraZ,
        float FocusX,
        float FocusY,
        float FocusZ)
    {
        public static TitleBackgroundCameraProbeSettingsSnapshot Capture(Configuration configuration)
        {
            return new TitleBackgroundCameraProbeSettingsSnapshot(
                configuration.TitleBackgroundSelectedPresetId,
                configuration.TitleBackgroundCameraOverrideEnabled,
                configuration.TitleBackgroundCameraX,
                configuration.TitleBackgroundCameraY,
                configuration.TitleBackgroundCameraZ,
                configuration.TitleBackgroundFocusX,
                configuration.TitleBackgroundFocusY,
                configuration.TitleBackgroundFocusZ);
        }

        public void ApplyTo(Configuration configuration)
        {
            configuration.TitleBackgroundSelectedPresetId = SelectedPresetId;
            configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
            configuration.TitleBackgroundCameraX = CameraX;
            configuration.TitleBackgroundCameraY = CameraY;
            configuration.TitleBackgroundCameraZ = CameraZ;
            configuration.TitleBackgroundFocusX = FocusX;
            configuration.TitleBackgroundFocusY = FocusY;
            configuration.TitleBackgroundFocusZ = FocusZ;
        }
    }

    private sealed record TitleBackgroundProbeSettingsSnapshot(
        bool OverrideEnabled,
        bool CameraOverrideEnabled,
        TitleBackgroundRuntimeMode RuntimeMode,
        TitleBackgroundResolverMode CreateSceneResolverMode,
        TitleBackgroundResolverMode LobbyUpdateResolverMode)
    {
        public static TitleBackgroundProbeSettingsSnapshot Capture(Configuration configuration)
        {
            return new TitleBackgroundProbeSettingsSnapshot(
                configuration.TitleBackgroundOverrideEnabled,
                configuration.TitleBackgroundCameraOverrideEnabled,
                configuration.TitleBackgroundRuntimeMode,
                configuration.TitleBackgroundCreateSceneResolverMode,
                configuration.TitleBackgroundLobbyUpdateResolverMode);
        }

        public void ApplyTo(Configuration configuration)
        {
            configuration.TitleBackgroundOverrideEnabled = OverrideEnabled;
            configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
            configuration.TitleBackgroundRuntimeMode = RuntimeMode;
            configuration.TitleBackgroundCreateSceneResolverMode = CreateSceneResolverMode;
            configuration.TitleBackgroundLobbyUpdateResolverMode = LobbyUpdateResolverMode;
        }
    }
}

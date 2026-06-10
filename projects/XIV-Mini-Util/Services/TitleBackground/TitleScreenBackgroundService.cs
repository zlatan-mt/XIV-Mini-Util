// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs
// Description: キャラ選択画面背景差し替えの設定、診断、native hook lifecycleを管理する
// Reason: HaselTweaks相当のemote/pet/preload機能から背景差し替えを分離するため
using Dalamud.Hooking;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ClientVector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe partial class TitleScreenBackgroundService : IDisposable
{
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly ISigScanner _sigScanner;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly string _configDirectory;
    private readonly Configuration _configuration;
    private readonly CharaSelectService? _charaSelectService;
    private readonly TitleBackgroundAddressResolver _addressResolver = new();
    private readonly TitleBackgroundCameraCaptureService _cameraCaptureService;
    private readonly TitleBackgroundCharaSelectCameraAdapter _charaSelectCameraAdapter = new();
    private readonly TitleBackgroundTransitionDiagnosticRecorder _transitionDiagnostics = new();
    private TitleBackgroundQuickCheckState _quickCheckState = TitleBackgroundQuickCheckState.Idle;

    private Hook<CreateSceneDelegate>? _createSceneHook;
    private Hook<LobbyUpdateDelegate>? _lobbyUpdateHook;
    private Hook<LoadLobbySceneDelegate>? _loadLobbySceneHook;
    private Hook<LobbySceneLoadedDelegate>? _lobbySceneLoadedHook;
    private Hook<LobbyCameraFixOnDelegate>? _cameraFixOnHook;
    private Hook<CalculateLobbyCameraLookAtYDelegate>? _calculateLobbyCameraLookAtYHook;
    private Hook<SetCameraCurveMidPointDelegate>? _setCameraCurveMidPointHook;
    private Hook<CalculateCameraCurveLowAndHighPointDelegate>? _calculateCameraCurveLowAndHighPointHook;
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
    private uint _lastOverrideTerritoryId;
    private uint _lastOverrideLayerFilterKey;
    private bool _charaSelectTitleBackgroundSessionActive;
    private int _activeCharaSelectSceneGeneration;
    private bool _activeSceneOverride;
    private GameLobbyType _activeSceneOverrideLobbyType = GameLobbyType.None;
    private string _activeSceneOverridePath = string.Empty;
    private string _lastHistoricalOverridePath = string.Empty;
    private string _sceneOverrideCleanupReason = "none";
    private bool _loggedInWorldTransitionRecorded;
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
    private string _lastCharaSelectCameraRuntimeRecordStatus = "not-run";
    private string _lastCharaSelectCameraRuntimeRestoreStatus = "not-run";
    private string _lastCharaSelectCameraRuntimeRecordError = string.Empty;
    private string _lastCharaSelectCameraRuntimeRestoreFailureReason = string.Empty;
    private int _lastCharaSelectCameraRuntimeRestoreSceneGeneration;
    private int _sceneReadySignalCallCount;
    private int _sceneReadySignalAcceptedCount;
    private string _sceneReadySignalLastAdapterStateBeforeHandle = "not-run";
    private GameLobbyType _sceneReadySignalLastResolvedLobbyMap = GameLobbyType.None;
    private int _runtimeRestoreAttemptCount;
    private int _runtimeRestoreSuccessCount;
    private float? _runtimeRestoreLastRestoredYaw;
    private float? _runtimeRestoreLastRestoredPitch;
    private float? _runtimeRestoreLastRestoredDistance;
    private float? _runtimeRestoreLastRestoredFovY;
    private int _curveApplyAttemptCount;
    private int _curveApplySuccessCount;
    private string _curveApplyLastStatus = "not-run";
    private string _curveApplyLastFailureReason = string.Empty;
    private float? _curveApplyLastAppliedLow;
    private float? _curveApplyLastAppliedMid;
    private float? _curveApplyLastAppliedHigh;
    private int? _curveApplyAppliedFrame;
    private float? _curveApplyRequestedMid;
    private float? _curveApplyReadBackValueImmediatelyAfterWrite;
    private string _curveApplyImmediateReadBackStatus = "not-run";
    private TitleBackgroundActiveCameraSnapshot? _curveApplyActiveCameraBefore;
    private TitleBackgroundActiveCameraSnapshot? _curveApplyActiveCameraAfter;
    private string _curveApplyActiveCameraBeforeStatus = "not-run";
    private string _curveApplyActiveCameraAfterStatus = "not-run";
    private int? _runtimeRestoreAppliedFrame;
    private TitleBackgroundProbeSession? _activeProbeSession;
    private TitleBackgroundProbeSession? _lastProbeSession;
    private TitleBackgroundProbeCounters _automaticProbeCounters = new();
    private bool _automaticProbeCountersEnabled;
    private TitleBackgroundCameraProbeSession? _cameraProbeSession;
    private int _cameraProbeTimelineFrameCounter = -1;
    private string _cameraProbeTimelineStatus = "not-run";
    private string _cameraProbeTimelineError = string.Empty;
    private readonly Dictionary<int, TitleBackgroundCameraProbeTimelineSnapshot> _cameraProbeTimelineSnapshots = [];
    private readonly Dictionary<int, TitleBackgroundCameraProbeTimelineEventCounts> _cameraProbeTimelineEventCounts = [];
    private readonly Dictionary<int, TitleBackgroundCameraProbeLobbyUpdateSnapshot> _cameraProbeLobbyUpdateSnapshots = [];
    private int _phase2CTimelineFrameCounter = -1;
    private string _phase2CTimelineStatus = "not-run";
    private string _phase2CTimelineError = string.Empty;
    private readonly Dictionary<int, TitleBackgroundPhase2CTimelineSnapshot> _phase2CTimelineSnapshots = [];
    private readonly Dictionary<int, TitleBackgroundPhase2MPlacementFrame> _phase2MPlacementFrames = [];
    private int _phase2MPlacementCaptureSceneGeneration;
    private string _phase2MPlacementCaptureReason = "not-run";
    private string _phase2MExperimentalLastStatus = "not-run";
    private int _phase2MExperimentalWriteCount;
    private int _phase2MExperimentalSkippedCount;
    private readonly List<TitleBackgroundPhase2ECalculateLookAtYCall> _phase2ECalculateLookAtYCalls = [];
    private readonly List<TitleBackgroundPhase2FGeneratedCurveCall> _phase2FSetCameraCurveMidPointCalls = [];
    private readonly List<TitleBackgroundPhase2FGeneratedCurveCall> _phase2FCalculateCameraCurveLowAndHighPointCalls = [];
    private readonly List<TitleBackgroundPhase2FGeneratedCurveCall> _phase2FSetCameraCurveMidPointInterestingCalls = [];
    private readonly List<TitleBackgroundPhase2FGeneratedCurveCall> _phase2FCalculateCameraCurveLowAndHighPointInterestingCalls = [];
    private int _phase2ECalculateLookAtYCallCount;
    private int _phase2FSetCameraCurveMidPointCallCount;
    private int _phase2FCalculateCameraCurveLowAndHighPointCallCount;
    private float? _phase2FSetCameraCurveMidPointPreviousInputValue;
    private float? _phase2FCalculateCameraCurveLowAndHighPointPreviousInputValue;
    private string _phase2ECalculateLookAtYLastError = string.Empty;
    private string _phase2FSetCameraCurveMidPointLastError = string.Empty;
    private string _phase2FCalculateCameraCurveLowAndHighPointLastError = string.Empty;
    private int _phase2GGenerationOverrideSetMidAttemptCount;
    private int _phase2GGenerationOverrideSetMidAppliedCount;
    private int _phase2GGenerationOverrideLowHighAttemptCount;
    private int _phase2GGenerationOverrideLowHighAppliedCount;
    private int? _phase2GGenerationOverrideLastAppliedFrame;
    private int _phase2GGenerationOverrideLastAppliedSceneGeneration;
    private string _phase2GGenerationOverrideLastStatus = "not-run";
    private string _phase2GGenerationOverrideLastSkippedReason = string.Empty;
    private int _quickCheckOverrideAppliedCount;
    private bool _integratedCompositionRouteInvoked;
    private string _integratedCompositionRouteLastReason = string.Empty;
    private bool _integratedCompositionAutoEnabled;
    private string _lastCurrentLobbyMapResetReason = "none";
    private bool _postLoginDiagnosticSeen;
    private TitleBackgroundSelfTestSession? _selfTestSession;
    private const int SelfTestMaxFrame = 600;
    private static readonly int[] CameraProbeTimelineFrames = [0, 1, 2, 4, 8, 16, 30, 60, 90, 120, 180, 240, 300, 450, 600, 900, 1200];
    private static readonly int[] Phase2FGeneratedCurveSamplingFrames = [0, 1, 2, 4, 8, 16, 30, 60, 90, 120];
    private const int Phase2EMaxRecordedCalls = 64;
    private const int Phase2FMaxRecordedGeneratedCurveCalls = 64;
    private const int Phase2FMaxInterestingGeneratedCurveCalls = 256;
    private const int Phase2FGeneratedCurveInterestingMaximumFrame = 120;
    private const int Phase2FGeneratedCurveSamplingFrameRange = 1;
    private const int LobbyCameraExpandedCameraCurveEnabledOffset = 0x2C2;
    private const int LobbyCameraExpandedMidPointOffset = 0x2D0;
    private const int LobbyCameraExpandedLowPointOffset = 0x2E0;
    private const int LobbyCameraExpandedHighPointOffset = 0x2F0;

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
        string configDirectory,
        Configuration configuration,
        CharaSelectService? charaSelectService = null)
    {
        _gameInteropProvider = gameInteropProvider;
        _sigScanner = sigScanner;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _log = log;
        _configDirectory = configDirectory;
        _configuration = configuration;
        _charaSelectService = charaSelectService;
        _cameraCaptureService = new TitleBackgroundCameraCaptureService(clientState, objectTable, dataManager, log);
        _framework.Update += OnFrameworkUpdate;

        RecordTransitionEvent("plugin initialized", "constructor");
        InitializeHooks();
        ApplyFromConfiguration();
    }

    public event Action<string>? SelfTestCompleted;

    public void SetEnabled(bool enabled)
    {
        _configuration.TitleBackgroundOverrideEnabled = enabled;
        if (enabled && !_configuration.TitleBackgroundCameraOverrideEnabled)
        {
            // Camera framing (Phase2G generated curve override) is part of the Title Background
            // feature and must be armed together. Auto-enable so the adapter can arm correctly.
            _configuration.TitleBackgroundCameraOverrideEnabled = true;
        }

        if (enabled && !_configuration.TitleBackgroundIntegratedCompositionEnabled)
        {
            // Integrated composition is the Title Background route for character scene composition.
            // Auto-enable so diagnostics correctly reflect that this route is active.
            _configuration.TitleBackgroundIntegratedCompositionEnabled = true;
        }

        _configuration.Save();
        RecordTransitionEvent(enabled ? "title background feature enabled" : "title background feature disabled", "SetEnabled");
        ReloadNativeIntegration();
        if (enabled && _configuration.TitleBackgroundIntegratedCompositionEnabled)
        {
            _charaSelectService?.ResetTitleBackgroundCharacterCompositionBridgeSnapshot();
            // Integrated composition route: trigger CharaSelect scene reload so CreateScene fires
            // and applies the n4f4 override. This connects the flag to real scene processing.
            // RequestCharaSelectReload handles precondition checks (Ready state, CharaSelect map).
            TryInvokeIntegratedCompositionRoute();
            _charaSelectService?.ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState();
        }
    }

    internal TitleBackgroundSimpleUiSummary RunSimpleAutoSetup()
    {
        if (_configuration.CharaSelectSceneCompositionEnabled)
        {
            _charaSelectService?.DisableSceneCompositionForTitleBackgroundRoute();
        }

        TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(_configuration);
        _configuration.Save();
        RecordTransitionEvent("simple auto setup applied", "n4f4 recommended");
        ReloadNativeIntegration();
        _charaSelectService?.ResetTitleBackgroundCharacterCompositionBridgeSnapshot();
        TryInvokeIntegratedCompositionRoute();
        _charaSelectService?.ApplyTitleBackgroundCharacterCompositionBridgeRuntimeState();
        StartQuickCheck();
        return TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(_configuration);
    }

    internal TitleBackgroundSimpleUiSummary RunSimpleCheck()
    {
        if (!_quickCheckState.StartedAt.HasValue
            || _quickCheckState.RunState == TitleBackgroundQuickCheckRunState.Idle)
        {
            StartQuickCheck();
        }

        RunQuickCheck();
        return TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(_configuration);
    }

    public void SetCameraOverrideEnabled(bool enabled)
    {
        _configuration.TitleBackgroundCameraOverrideEnabled = enabled;
        _configuration.Save();
        RecordTransitionEvent("cameraOverrideEnabled changed", enabled ? "enabled" : "disabled");
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
        _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
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
        ConfigureCharaSelectCameraAdapter();
        RecordTransitionEvent("runtimeMode changed or observed", _configuration.TitleBackgroundRuntimeMode.ToString());
        RecordTransitionEvent("cameraOverrideEnabled changed", _configuration.TitleBackgroundCameraOverrideEnabled.ToString());
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
        RecordTransitionEvent("adapter stop requested", "ReloadNativeIntegration");
        _cameraApplyPending = false;
        _charaSelectCameraAdapter.ResetRuntimeCameraState();
        RecordTransitionEvent("adapter stopped/reset/cleared", "ResetRuntimeCameraState");
        ResetCameraOverrideObservation();
        ResetSceneOverrideObservation();
        _loadingLobbyType = GameLobbyType.None;
        DisposeHooks();
        InitializeHooks();
        ApplyFromConfiguration();
    }

    public void ClearOverride()
    {
        RecordTransitionEvent("title background feature disabled", "ClearOverride");
        _configuration.TitleBackgroundOverrideEnabled = false;
        _configuration.Save();
        _cameraApplyPending = false;
        _charaSelectCameraAdapter.Configure(false, TitleBackgroundCharaSelectCameraInput.FromConfiguration(_configuration));
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

    private bool ShouldResetCurrentMapForReload(GameLobbyType nextMap)
    {
        return _state == TitleBackgroundServiceState.Ready
            && !IsHookProbeMode()
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && TryReadCurrentLobbyMap(out var currentMap)
            && GameLobbyTypeHelper.IsTitleCharaSelectTransition(currentMap, nextMap);
    }

    private void RecordCharaSelectRuntimeCameraStateBeforeSceneReload(GameLobbyType mapId)
    {
        RecordTransitionEvent("runtime record attempted", mapId.ToString());
        if (!ShouldRecordCharaSelectRuntimeCameraState(mapId))
        {
            _lastCharaSelectCameraRuntimeRecordStatus = "skipped";
            _lastCharaSelectCameraRuntimeRecordError = string.Empty;
            RecordTransitionEvent("runtime record failed", "skipped");
            return;
        }

        if (IsRuntimeCameraStateCurrentPreset())
        {
            _lastCharaSelectCameraRuntimeRecordStatus = "kept-runtime";
            _lastCharaSelectCameraRuntimeRecordError = string.Empty;
            RecordTransitionEvent("runtime record succeeded", "kept-runtime");
            return;
        }

        if (!TryBuildPresetCameraRuntimePose(out var pose, out var errorMessage))
        {
            _lastCharaSelectCameraRuntimeRecordStatus = "failed";
            _lastCharaSelectCameraRuntimeRecordError = errorMessage;
            RecordTransitionEvent("runtime record failed", errorMessage);
            _log.Debug("[XMU BG] CharaSelect camera preset pose build skipped. reason={Reason}", errorMessage);
            return;
        }

        _charaSelectCameraAdapter.SavePresetCameraState(
            pose.Yaw,
            pose.Pitch,
            pose.Distance,
            pose.LookAtY,
            pose.LookAt);
        _lastCharaSelectCameraRuntimeRecordStatus = "preset-derived";
        _lastCharaSelectCameraRuntimeRecordError = string.Empty;
        RecordTransitionEvent("runtime record succeeded", "preset-derived");
        _log.Debug(
            "[XMU BG] CharaSelect camera preset pose derived. yaw={Yaw}, pitch={Pitch}, distance={Distance}, lookAtY={LookAtY}, generation={Generation}",
            pose.Yaw,
            pose.Pitch,
            pose.Distance,
            pose.LookAtY,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration);
    }

    private bool ShouldRecordCharaSelectRuntimeCameraState(GameLobbyType mapId)
    {
        return _state == TitleBackgroundServiceState.Ready
            && !IsHookProbeMode()
            && TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(mapId)
            && _charaSelectCameraAdapter.IsArmed;
    }

    private bool IsRuntimeCameraStateCurrentPreset()
    {
        var runtimeState = _charaSelectCameraAdapter.RuntimeState;
        if (!runtimeState.HasCameraPose || !runtimeState.HasLookAt || !runtimeState.CurveAtRecord.HasValue)
        {
            return false;
        }

        if (!TryBuildPresetCameraRuntimePose(out var expectedPose, out _))
        {
            return false;
        }

        var curve = _charaSelectCameraAdapter.Curve;
        var restoredYaw = runtimeState.GetRestoredYaw(_charaSelectCameraAdapter.Input.CharacterRotation);
        return restoredYaw.HasValue
            && IsNear(restoredYaw.Value, expectedPose.Yaw)
            && runtimeState.Pitch.HasValue
            && IsNear(runtimeState.Pitch.Value, expectedPose.Pitch)
            && runtimeState.Distance.HasValue
            && IsNear(runtimeState.Distance.Value, expectedPose.Distance)
            && TitleBackgroundCameraMath.CalculateVectorDelta(runtimeState.LookAt, expectedPose.LookAt) is { } focusDelta
            && Math.Abs(focusDelta.X) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance
            && Math.Abs(focusDelta.Y) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance
            && Math.Abs(focusDelta.Z) <= TitleBackgroundCameraProbeReport.StabilizationVectorTolerance
            && IsNear(runtimeState.CurveAtRecord.Value.Low, curve.Low)
            && IsNear(runtimeState.CurveAtRecord.Value.Mid, curve.Mid)
            && IsNear(runtimeState.CurveAtRecord.Value.High, curve.High);
    }

    private bool TryBuildPresetCameraRuntimePose(out TitleBackgroundRuntimeCameraPose pose, out string errorMessage)
    {
        pose = default;
        var camera = new Vector3(
            _configuration.TitleBackgroundCameraX,
            _configuration.TitleBackgroundCameraY,
            _configuration.TitleBackgroundCameraZ);
        var focus = new Vector3(
            _configuration.TitleBackgroundFocusX,
            _configuration.TitleBackgroundFocusY,
            _configuration.TitleBackgroundFocusZ);

        if (!TitleBackgroundCharaSelectCameraLogic.TryBuildPoseFromCameraFocus(
                camera,
                focus,
                out var yaw,
                out var pitch,
                out var distance,
                out var lookAtY,
                out errorMessage))
        {
            return false;
        }

        var cameraProfile = ResolveCurrentTitleBackgroundCameraProfile();
        if (cameraProfile.HasProfile)
        {
            yaw = cameraProfile.Yaw.HasValue
                ? TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(cameraProfile.Yaw.Value)
                : TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(yaw + cameraProfile.YawOffset);
            pitch = cameraProfile.Pitch.HasValue
                ? Math.Clamp(cameraProfile.Pitch.Value, -MathF.PI / 2f, MathF.PI / 2f)
                : Math.Clamp(pitch + cameraProfile.PitchOffset, -MathF.PI / 2f, MathF.PI / 2f);
            distance = cameraProfile.Distance.HasValue
                ? TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(cameraProfile.Distance.Value) ?? distance
                : TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(
                        (distance * cameraProfile.DistanceMultiplier) + cameraProfile.DistanceOffset)
                    ?? distance;
            focus = cameraProfile.LookAt.HasValue
                ? TitleBackgroundCharaSelectCameraLogic.SanitizeVector(cameraProfile.LookAt.Value)
                : TitleBackgroundCharaSelectCameraLogic.SanitizeVector(focus + cameraProfile.LookAtOffset);
            lookAtY = TitleBackgroundPreset.SanitizeCoordinate(focus.Y);
        }

        pose = new TitleBackgroundRuntimeCameraPose(
            yaw,
            pitch,
            distance,
            lookAtY,
            focus);
        return true;
    }

    private GameLobbyType ResolveSceneReadySignalLobbyMap()
    {
        if (!TryReadCurrentLobbyMap(out var map) || map == GameLobbyType.None)
        {
            return EffectiveLobbyType;
        }

        return map;
    }

    private bool ShouldHandleCharaSelectSceneReadySignal(
        TitleBackgroundCharaSelectCameraAdapterState stateBeforeHandle,
        GameLobbyType map)
    {
        return TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            _state == TitleBackgroundServiceState.Ready,
            IsHookProbeMode(),
            _charaSelectCameraAdapter.IsArmed,
            stateBeforeHandle,
            map);
    }

    private void RestoreCharaSelectRuntimeCameraStateAfterSceneLoad()
    {
        _runtimeRestoreAttemptCount++;
        RecordTransitionEvent("runtime restore attempted", $"attempt={_runtimeRestoreAttemptCount}");
        if (!_charaSelectCameraAdapter.ShouldRestoreRuntimeCameraState())
        {
            _lastCharaSelectCameraRuntimeRestoreStatus = "skipped";
            _lastCharaSelectCameraRuntimeRestoreFailureReason = string.Empty;
            RecordTransitionEvent("runtime restore failed", "skipped");
            return;
        }

        if (!TryApplyRuntimeCameraPose(_charaSelectCameraAdapter.RuntimeState, _charaSelectCameraAdapter.GetRestoredYaw(), out var errorMessage))
        {
            _lastCharaSelectCameraRuntimeRestoreStatus = "failed";
            _lastCharaSelectCameraRuntimeRestoreFailureReason = errorMessage;
            RecordTransitionEvent("runtime restore failed", errorMessage);
            _log.Debug("[XMU BG] CharaSelect camera runtime state restore skipped. reason={Reason}", errorMessage);
            return;
        }

        var restoredYaw = _charaSelectCameraAdapter.GetRestoredYaw()!.Value;
        var restoredPitch = _charaSelectCameraAdapter.RuntimeState.Pitch!.Value;
        var restoredDistance = _charaSelectCameraAdapter.RuntimeState.Distance!.Value;
        _charaSelectCameraAdapter.MarkRuntimeCameraStateRestored();
        _lastCharaSelectCameraRuntimeRestoreStatus = "success";
        _lastCharaSelectCameraRuntimeRestoreFailureReason = string.Empty;
        _lastCharaSelectCameraRuntimeRestoreSceneGeneration = _charaSelectCameraAdapter.RuntimeState.SceneGeneration;
        _runtimeRestoreSuccessCount++;
        _runtimeRestoreLastRestoredYaw = restoredYaw;
        _runtimeRestoreLastRestoredPitch = restoredPitch;
        _runtimeRestoreLastRestoredDistance = restoredDistance;
        _runtimeRestoreLastRestoredFovY = _configuration.TitleBackgroundFovY;
        _runtimeRestoreAppliedFrame = GetCurrentPhase2CFrame();
        RecordTransitionEvent("runtime restore succeeded", $"generation={_lastCharaSelectCameraRuntimeRestoreSceneGeneration}");
        _log.Information(
            "[XMU BG] CharaSelect camera runtime state restored. yaw={Yaw}, pitch={Pitch}, distance={Distance}, generation={Generation}",
            restoredYaw,
            restoredPitch,
            restoredDistance,
            _lastCharaSelectCameraRuntimeRestoreSceneGeneration);
    }

    private void ApplyCharaSelectCameraCurveAfterSceneLoad()
    {
        _curveApplyAttemptCount++;
        RecordTransitionEvent("curve apply attempted", $"attempt={_curveApplyAttemptCount}");
        if (!_charaSelectCameraAdapter.ShouldApplyCurve())
        {
            _curveApplyLastStatus = "skipped";
            _curveApplyLastFailureReason = string.Empty;
            RecordTransitionEvent("curve apply failed", "skipped");
            return;
        }

        var curve = _charaSelectCameraAdapter.RuntimeState.CurveAtRecord ?? _charaSelectCameraAdapter.Curve;
        _curveApplyRequestedMid = curve.Mid;
        CaptureCurveApplyActiveCameraBefore();
        if (!TryApplyCharaSelectCameraCurve(curve, out var errorMessage))
        {
            _curveApplyLastStatus = "failed";
            _curveApplyLastFailureReason = errorMessage;
            RecordTransitionEvent("curve apply failed", errorMessage);
            _log.Debug("[XMU BG] CharaSelect camera curve apply skipped. reason={Reason}", errorMessage);
            return;
        }

        CaptureCurveApplyActiveCameraAfter();
        _charaSelectCameraAdapter.MarkCurveApplied();
        _curveApplyLastStatus = "success";
        _curveApplyLastFailureReason = string.Empty;
        _curveApplyLastAppliedLow = curve.Low;
        _curveApplyLastAppliedMid = curve.Mid;
        _curveApplyLastAppliedHigh = curve.High;
        _curveApplyAppliedFrame = GetCurrentPhase2CFrame();
        _curveApplyReadBackValueImmediatelyAfterWrite = null;
        _curveApplyImmediateReadBackStatus = "readback unavailable";
        _curveApplySuccessCount++;
        RecordTransitionEvent("curve apply succeeded", $"generation={_charaSelectCameraAdapter.RuntimeState.SceneGeneration}");
        _log.Information(
            "[XMU BG] CharaSelect camera curve applied. low={Low}, mid={Mid}, high={High}, generation={Generation}",
            curve.Low,
            curve.Mid,
            curve.High,
            _charaSelectCameraAdapter.RuntimeState.SceneGeneration);
    }

    private bool TryApplyCharaSelectCameraCurve(
        TitleBackgroundCharaSelectCameraCurve curve,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var lobbyCamera = cameraManager->LobbyCamera;
            if (lobbyCamera == null)
            {
                errorMessage = "LobbyCamera unavailable";
                return false;
            }

            lobbyCamera->SetTiltOffset(curve.Mid);
            WriteCurvePointY((nint)lobbyCamera, LobbyCameraExpandedLowPointOffset, curve.Low);
            WriteCurvePointY((nint)lobbyCamera, LobbyCameraExpandedMidPointOffset, curve.Mid);
            WriteCurvePointY((nint)lobbyCamera, LobbyCameraExpandedHighPointOffset, curve.High);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground camera curve apply failed.");
            return false;
        }
    }

    private void CaptureCurveApplyActiveCameraBefore()
    {
        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            _curveApplyActiveCameraBefore = snapshot;
            _curveApplyActiveCameraBeforeStatus = "success";
            return;
        }

        _curveApplyActiveCameraBefore = null;
        _curveApplyActiveCameraBeforeStatus = string.IsNullOrWhiteSpace(errorMessage)
            ? "failed"
            : $"failed: {errorMessage}";
    }

    private void CaptureCurveApplyActiveCameraAfter()
    {
        if (TryCaptureActiveCameraSnapshot(out var snapshot, out var errorMessage))
        {
            _curveApplyActiveCameraAfter = snapshot;
            _curveApplyActiveCameraAfterStatus = "success";
            return;
        }

        _curveApplyActiveCameraAfter = null;
        _curveApplyActiveCameraAfterStatus = string.IsNullOrWhiteSpace(errorMessage)
            ? "failed"
            : $"failed: {errorMessage}";
    }

    private bool TryApplyRuntimeCameraPose(
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState,
        float? restoredYaw,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!restoredYaw.HasValue || !runtimeState.Pitch.HasValue || !runtimeState.Distance.HasValue)
        {
            errorMessage = "runtime camera pose is incomplete";
            return false;
        }

        try
        {
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null)
            {
                errorMessage = "CameraManager.Instance() unavailable";
                return false;
            }

            var lobbyCamera = cameraManager->LobbyCamera;
            if (lobbyCamera == null)
            {
                errorMessage = "LobbyCamera unavailable";
                return false;
            }

            lobbyCamera->DirH = restoredYaw.Value;
            lobbyCamera->DirV = runtimeState.Pitch.Value;
            lobbyCamera->Distance = runtimeState.Distance.Value;
            lobbyCamera->InterpDistance = runtimeState.Distance.Value;
            lobbyCamera->FoV = _configuration.TitleBackgroundFovY;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _log.Warning(ex, "TitleBackground runtime camera pose restore failed.");
            return false;
        }
    }

    private bool ShouldOverrideCharaSelect(GameLobbyType lobbyType)
    {
        return IsOverrideMutationBranchArmed()
            && lobbyType == GameLobbyType.CharaSelect;
    }

    private bool IsSceneOverrideEnabled()
    {
        return _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(_configuration.TitleBackgroundCharacterSelectBackgroundMode);
    }

    private bool IsOverrideMutationBranchArmed()
    {
        return _state == TitleBackgroundServiceState.Ready
            && !IsHookProbeMode()
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(_configuration.TitleBackgroundCharacterSelectBackgroundMode)
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
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = NormalizeSignature(_configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature);
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = NormalizeSignature(_configuration.TitleBackgroundSetCameraCurveMidPointSignature);
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = NormalizeSignature(_configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature);
        TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(_configuration);

        // Migration: TitleBackground override requires camera override and integrated composition.
        // Correct any pre-existing config that was saved before these flags were introduced.
        if (TitleBackgroundCharaSelectCameraLogic.NormalizeAndMigrateFlags(
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.TitleBackgroundIntegratedCompositionEnabled,
            out var normCam,
            out var normIntegrated))
        {
            var wasIntegratedDisabled = !_configuration.TitleBackgroundIntegratedCompositionEnabled;
            _configuration.TitleBackgroundCameraOverrideEnabled = normCam;
            _configuration.TitleBackgroundIntegratedCompositionEnabled = normIntegrated;
            if (wasIntegratedDisabled)
            {
                _integratedCompositionAutoEnabled = true;
            }

            RecordTransitionEvent("config migration", "TitleBackground flags normalized on load");
            _configuration.Save();
        }
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
        return TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(_configuration.TitleBackgroundCharacterSelectBackgroundMode)
            && TitleBackgroundRuntimeModeHelper.ShouldValidateSceneOverrideConfiguration(
                _configuration.TitleBackgroundRuntimeMode);
    }

    private bool IsHookProbeMode()
    {
        return _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.HookProbe;
    }

    private bool IsHookSetAlignedWithConfiguration()
    {
        return _cameraFixOnHook == null;
    }

    private void ConfigureCharaSelectCameraAdapter()
    {
        var enabled = TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled,
            _configuration.TitleBackgroundRuntimeMode);
        _charaSelectCameraAdapter.Configure(
            enabled,
            TitleBackgroundCharaSelectCameraInput.FromConfiguration(_configuration));
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
        _lastCharaSelectCameraRuntimeRecordStatus = "not-run";
        _lastCharaSelectCameraRuntimeRestoreStatus = "not-run";
        _lastCharaSelectCameraRuntimeRecordError = string.Empty;
        _lastCharaSelectCameraRuntimeRestoreFailureReason = string.Empty;
        _lastCharaSelectCameraRuntimeRestoreSceneGeneration = 0;
        _sceneReadySignalCallCount = 0;
        _sceneReadySignalAcceptedCount = 0;
        _sceneReadySignalLastAdapterStateBeforeHandle = "not-run";
        _sceneReadySignalLastResolvedLobbyMap = GameLobbyType.None;
        _runtimeRestoreAttemptCount = 0;
        _runtimeRestoreSuccessCount = 0;
        _runtimeRestoreLastRestoredYaw = null;
        _runtimeRestoreLastRestoredPitch = null;
        _runtimeRestoreLastRestoredDistance = null;
        _runtimeRestoreLastRestoredFovY = null;
        _runtimeRestoreAppliedFrame = null;
        _curveApplyAttemptCount = 0;
        _curveApplySuccessCount = 0;
        _curveApplyLastStatus = "not-run";
        _curveApplyLastFailureReason = string.Empty;
        _curveApplyLastAppliedLow = null;
        _curveApplyLastAppliedMid = null;
        _curveApplyLastAppliedHigh = null;
        _curveApplyAppliedFrame = null;
        _curveApplyRequestedMid = null;
        _curveApplyReadBackValueImmediatelyAfterWrite = null;
        _curveApplyImmediateReadBackStatus = "not-run";
        _curveApplyActiveCameraBefore = null;
        _curveApplyActiveCameraAfter = null;
        _curveApplyActiveCameraBeforeStatus = "not-run";
        _curveApplyActiveCameraAfterStatus = "not-run";
        _phase2CTimelineFrameCounter = -1;
        _phase2CTimelineStatus = "not-run";
        _phase2CTimelineError = string.Empty;
        _phase2CTimelineSnapshots.Clear();
        _phase2MPlacementFrames.Clear();
        ResetPhase2ECalculateLookAtYObservation();
        ClearPostFixOnCameraObservation();
        _lastPostFixOnCameraCaptureStatus = "not-run";
    }

    private void ResetSceneOverrideObservation()
    {
        _lastOverrideApplied = false;
        _lastOverrideLobbyType = GameLobbyType.None;
        _lastOverrideOriginalPath = string.Empty;
        _lastOverrideNewPath = string.Empty;
        _lastOverrideTerritoryId = 0;
        _lastOverrideLayerFilterKey = 0;
        _activeSceneOverride = false;
        _activeSceneOverrideLobbyType = GameLobbyType.None;
        _activeSceneOverridePath = string.Empty;
        _charaSelectTitleBackgroundSessionActive = false;
        _activeCharaSelectSceneGeneration = 0;
        _sceneOverrideCleanupReason = "none";
        RecordTransitionEvent("adapter stopped/reset/cleared", "ResetSceneOverrideObservation");
    }

    private void EndCharaSelectTitleBackgroundSessionIfNeeded(GameLobbyType currentMap, string source)
    {
        if (!TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(_clientState.IsLoggedIn, currentMap))
        {
            return;
        }

        var reason = _clientState.IsLoggedIn
            ? "world-login-transition"
            : "leaving-chara-select-context";
        if (!_charaSelectTitleBackgroundSessionActive
            && !_activeSceneOverride
            && _sceneOverrideCleanupReason == reason)
        {
            return;
        }

        EndCharaSelectTitleBackgroundSession(reason, source);
    }

    private void EndCharaSelectTitleBackgroundSession(string reason, string source)
    {
        _cameraApplyPending = false;
        _charaSelectTitleBackgroundSessionActive = false;
        _activeCharaSelectSceneGeneration = 0;
        _activeSceneOverride = false;
        _activeSceneOverrideLobbyType = GameLobbyType.None;
        _activeSceneOverridePath = string.Empty;
        _sceneOverrideCleanupReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        if (_sceneOverrideCleanupReason == "world-login-transition" && !_loggedInWorldTransitionRecorded)
        {
            _loggedInWorldTransitionRecorded = true;
            if (_quickCheckState.RunState == TitleBackgroundQuickCheckRunState.CharaSelectObserved)
            {
                _quickCheckState = _quickCheckState with { RunState = TitleBackgroundQuickCheckRunState.LoggedInObserved };
            }

            RecordTransitionEvent("entered logged-in world", source);
        }

        _charaSelectCameraAdapter.EndSession();
        _currentMapWriteAttempted = true;
        _lastCurrentMapWriteSucceeded = TryWriteCurrentLobbyMap(GameLobbyType.None);
        _lastCurrentLobbyMapResetReason = _sceneOverrideCleanupReason;
        RecordTransitionEvent("CharaSelect title background session cleanup executed", $"{_sceneOverrideCleanupReason}; source={source}");
        RecordTransitionEvent("CurrentLobbyMap reset", _lastCurrentLobbyMapResetReason);
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
        var hadEnabledHooks = AreAnyHooksEnabled();
        DisposeHook(_cameraFixOnHook, nameof(_cameraFixOnHook));
        DisposeHook(_calculateLobbyCameraLookAtYHook, nameof(_calculateLobbyCameraLookAtYHook));
        DisposeHook(_setCameraCurveMidPointHook, nameof(_setCameraCurveMidPointHook));
        DisposeHook(_calculateCameraCurveLowAndHighPointHook, nameof(_calculateCameraCurveLowAndHighPointHook));
        DisposeHook(_lobbySceneLoadedHook, nameof(_lobbySceneLoadedHook));
        DisposeHook(_loadLobbySceneHook, nameof(_loadLobbySceneHook));
        DisposeHook(_lobbyUpdateHook, nameof(_lobbyUpdateHook));
        DisposeHook(_createSceneHook, nameof(_createSceneHook));
        _cameraFixOnHook = null;
        _calculateLobbyCameraLookAtYHook = null;
        _setCameraCurveMidPointHook = null;
        _calculateCameraCurveLowAndHighPointHook = null;
        _lobbySceneLoadedHook = null;
        _loadLobbySceneHook = null;
        _lobbyUpdateHook = null;
        _createSceneHook = null;
        if (hadEnabledHooks)
        {
            RecordTransitionEvent("hooks disabled", "DisposeHooks");
        }
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

    private TitleBackgroundPhase2CVerdicts BuildPhase2CVerdicts(TitleBackgroundPhase2CTimelineSnapshot stableSample)
    {
        return new TitleBackgroundPhase2CVerdicts(
            EvaluateFloatStability(_runtimeRestoreLastRestoredDistance, stableSample.Distance),
            EvaluateTiltOffsetObservableEffect(stableSample));
    }

    private TitleBackgroundPhase2DAnalysis BuildPhase2DVerdicts(IReadOnlyList<TitleBackgroundPhase2CTimelineSnapshot> samples)
    {
        return TitleBackgroundCameraProbeReport.AnalyzePhase2D(
            samples
                .Select(sample => new TitleBackgroundPhase2DTimelineSample(
                    sample.Frame,
                    sample.SceneCameraPosition,
                    sample.SceneCameraLookAtVector,
                    sample.Distance,
                    sample.DirH,
                    sample.DirV))
                .ToArray(),
            _runtimeRestoreLastRestoredDistance);
    }

    private string EvaluateTiltOffsetObservableEffect(TitleBackgroundPhase2CTimelineSnapshot stableSample)
    {
        if (_curveApplyLastStatus != "success")
        {
            return "inconclusive";
        }

        var immediateDirVDelta = TitleBackgroundCameraMath.CalculateFloatDelta(_curveApplyActiveCameraAfter?.DirV, _curveApplyActiveCameraBefore?.DirV);
        var immediateLookAtDelta = TitleBackgroundCameraMath.CalculateVectorDelta(_curveApplyActiveCameraAfter?.LookAtVector, _curveApplyActiveCameraBefore?.LookAtVector);
        if ((immediateDirVDelta.HasValue && Math.Abs(immediateDirVDelta.Value) >= TitleBackgroundCameraProbeReport.ReflectionTolerance)
            || (immediateLookAtDelta.HasValue && Math.Abs(immediateLookAtDelta.Value.Y) >= TitleBackgroundCameraProbeReport.ReflectionTolerance))
        {
            return "observable-immediate-change";
        }

        if (!_phase2CTimelineSnapshots.TryGetValue(0, out var frame0)
            || !frame0.ActiveCameraCaptured
            || !stableSample.ActiveCameraCaptured)
        {
            return "inconclusive";
        }

        var dirVDelta = TitleBackgroundCameraMath.CalculateFloatDelta(stableSample.DirV, frame0.DirV);
        var lookAtDelta = TitleBackgroundCameraMath.CalculateVectorDelta(stableSample.SceneCameraLookAtVector, frame0.SceneCameraLookAtVector);
        if ((dirVDelta.HasValue && Math.Abs(dirVDelta.Value) >= TitleBackgroundCameraProbeReport.ReflectionTolerance)
            || (lookAtDelta.HasValue && Math.Abs(lookAtDelta.Value.Y) >= TitleBackgroundCameraProbeReport.ReflectionTolerance))
        {
            return "observable-change";
        }

        return "not-observed";
    }

    private static string EvaluateFloatStability(float? baseline, float? stableValue)
    {
        if (!baseline.HasValue || !stableValue.HasValue)
        {
            return "inconclusive";
        }

        return Math.Abs(stableValue.Value - baseline.Value) <= TitleBackgroundCameraProbeReport.ReflectionTolerance
            ? "stable"
            : Math.Abs(stableValue.Value - baseline.Value) >= TitleBackgroundCameraProbeReport.OverwriteThreshold
                ? "possibly-overwritten"
                : "inconclusive";
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

    private static string FormatObjectId(ulong? value)
    {
        return value.HasValue && value.Value != 0 ? $"0x{value.Value:X}" : "none";
    }

    private static string FormatEntityId(uint? value)
    {
        return value.HasValue && value.Value != 0 ? $"0x{value.Value:X}" : "none";
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

    private static string FormatNullableUInt(uint? value)
    {
        return value.HasValue ? value.Value.ToString() : "none";
    }

    private static string FormatFrame(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "none";
    }

    private static string FormatBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString() : "none";
    }

    private static string JoinErrors(string first, string second)
    {
        first = string.IsNullOrWhiteSpace(first) ? string.Empty : first;
        second = string.IsNullOrWhiteSpace(second) ? string.Empty : second;
        if (first.Length == 0)
        {
            return second;
        }

        if (second.Length == 0)
        {
            return first;
        }

        return $"{first}; {second}";
    }

    private static string FormatUnavailable(float? value, string status)
    {
        return value.HasValue ? FormatFloat(value) : FormatNone(status);
    }

    private static string FormatCurvePoint(TitleBackgroundCurvePointSnapshot? value)
    {
        return value.HasValue
            ? $"({FormatFloat(value.Value.X)}, {FormatFloat(value.Value.Y)})"
            : "none";
    }

    private static string FormatNullableInt(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "none";
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

    private static string FormatVectorAxis(Vector3? value, int axis)
    {
        if (!value.HasValue)
        {
            return "none";
        }

        return axis switch
        {
            0 => FormatFloat(value.Value.X),
            1 => FormatFloat(value.Value.Y),
            2 => FormatFloat(value.Value.Z),
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

    private void AddPhase2MPreLoginCaptureLines(List<string> lines)
    {
        var frames = _phase2MPlacementFrames.Keys.OrderBy(frame => frame).ToArray();
        lines.Add($"phase2M.preLoginCapture.available={frames.Length > 0}");
        lines.Add($"phase2M.preLoginCapture.sceneGeneration={_phase2MPlacementCaptureSceneGeneration}");
        lines.Add($"phase2M.preLoginCapture.firstFrame={(frames.Length > 0 ? frames[0].ToString() : "none")}");
        lines.Add($"phase2M.preLoginCapture.lastFrame={(frames.Length > 0 ? frames[^1].ToString() : "none")}");
        lines.Add($"phase2M.preLoginCapture.frameCount={frames.Length}");
        lines.Add($"phase2M.preLoginCapture.reason={FormatNone(_phase2MPlacementCaptureReason)}");
    }

    private IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> BuildPhase2PManualCandidateSlots()
    {
        return
        [
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildManualSlot(
                1,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness),
        ];
    }

    private void AddPhase2MSummaryLines(List<string> lines, TitleBackgroundPhase2MSummary summary)
    {
        lines.Add($"phase2M.actorCandidate.zeroPositionCandidateCount={summary.ZeroPositionCandidateCount}");
        lines.Add($"phase2M.actorCandidate.nonZeroPositionCandidateCount={summary.NonZeroPositionCandidateCount}");
        lines.Add($"phase2M.actorCandidate.namedCandidateCount={summary.NamedCandidateCount}");
        lines.Add($"phase2M.actorCandidate.visibleHintTrueCount={summary.VisibleHintTrueCount}");
        lines.Add($"phase2M.actorCandidate.drawObjectNonNullCount={summary.DrawObjectNonNullCount}");
        lines.Add($"phase2M.actorCandidate.modelLikeNonNullCount={summary.ModelLikeNonNullCount}");
        lines.Add($"phase2M.actorCandidate.uniqueAddressCount={summary.UniqueAddressCount}");
        lines.Add($"phase2M.actorCandidate.uniqueObjectIdCount={summary.UniqueObjectIdCount}");
        lines.Add($"phase2M.actorCandidate.uniqueEntityIdCount={summary.UniqueEntityIdCount}");
        lines.Add($"phase2M.actorCandidate.samePositionGroupCount={summary.SamePositionGroupCount}");
        lines.Add($"phase2M.actorCandidate.objectTableIndexRange={summary.ObjectTableIndexRange}");
        lines.Add($"phase2M.actorCandidate.sourceBreakdown={summary.SourceBreakdown}");
        lines.Add($"phase2M.actorCandidate.transformValidity={summary.TransformValidity}");
        lines.Add($"phase2M.actorCandidate.identityConfidence={summary.IdentityConfidence}");
        lines.Add($"phase2M.actorCandidate.stubLikelihood={summary.StubLikelihood}");
        lines.Add($"phase2M.actorCandidate.bestCandidateIndex={summary.BestCandidateIndex}");
        lines.Add($"phase2M.actorCandidate.bestCandidateReason={summary.BestCandidateReason}");
        lines.Add($"phase2M.actorCandidate.scoring.enabled={summary.ScoringEnabled}");
        lines.Add($"phase2M.actorCandidate.bestScore={summary.BestScore}");
        lines.Add($"phase2M.actorCandidate.bestCandidate={summary.BestCandidate}");
        lines.Add($"phase2M.actorCandidate.bestCandidateStableAcrossFrames={summary.BestCandidateStableAcrossFrames}");
        lines.Add($"phase2M.actorCandidate.resolution={summary.Resolution}");
        lines.Add($"phase2M.sourceDiscovery.bestSource={summary.BestSource}");
        lines.Add($"phase2M.sourceDiscovery.nextNativeSourceToInspect={summary.NextNativeSourceToInspect}");
        lines.Add($"phase2M.nextAction={summary.NextAction}");
        lines.Add($"phase2M.nextAction.reason={summary.NextActionReason}");
    }

    private void AddPhase2MTopCandidateLines(List<string> lines)
    {
        var topCandidates = _phase2MPlacementFrames.Values
            .OrderByDescending(frame => frame.Frame)
            .SelectMany(frame => frame.ObjectCandidates)
            .GroupBy(BuildPhase2MCandidateKey)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .Take(3)
            .ToArray();
        for (var i = 0; i < topCandidates.Length; i++)
        {
            AddPhase2MObjectCandidateLines(lines, $"phase2M.topCandidate[{i}]", topCandidates[i]);
        }
    }

    private void AddPhase2MPlacementFrameLines(List<string> lines, TitleBackgroundPhase2MPlacementFrame frame)
    {
        var prefix = $"phase2M.placementFrame[{frame.Frame}]";
        lines.Add($"{prefix}.reason={frame.Reason}");
        lines.Add($"{prefix}.actor.status={frame.ActorStatus}");
        lines.Add($"{prefix}.actor.matchKind={frame.ActorMatchKind}");
        lines.Add($"{prefix}.actor.candidateCount={frame.CandidateCount}");
        lines.Add($"{prefix}.actorCandidate.status={frame.ActorCandidateStatus}");
        lines.Add($"{prefix}.actorCandidate.reason={frame.ActorCandidateReason}");
        lines.Add($"{prefix}.actor.source={frame.ActorSource}");
        lines.Add($"{prefix}.actor.nextNativeSourceToInspect={frame.NextNativeSourceToInspect}");
        lines.Add($"{prefix}.objectTable.totalScanned={frame.ObjectTableStats.TotalScanned}");
        lines.Add($"{prefix}.objectTable.namedCount={frame.ObjectTableStats.NamedCount}");
        lines.Add($"{prefix}.objectTable.playerLikeCount={frame.ObjectTableStats.PlayerLikeCount}");
        lines.Add($"{prefix}.objectTable.battleCharaCount={frame.ObjectTableStats.BattleCharaCount}");
        lines.Add($"{prefix}.objectTable.eventNpcCount={frame.ObjectTableStats.EventNpcCount}");
        lines.Add($"{prefix}.objectTable.companionLikeCount={frame.ObjectTableStats.CompanionLikeCount}");
        lines.Add($"{prefix}.objectTable.nearCameraCount={frame.ObjectTableStats.NearCameraCount}");
        lines.Add($"{prefix}.objectTable.nearConfiguredCharacterCount={frame.ObjectTableStats.NearConfiguredCharacterCount}");
        lines.Add($"{prefix}.configured.characterPosition={FormatVector(frame.ConfiguredCharacterPosition)}");
        lines.Add($"{prefix}.configured.characterRotation={FormatFloat(frame.ConfiguredCharacterRotation)}");
        lines.Add($"{prefix}.configured.curveLow={FormatFloat(frame.CurveLow)}");
        lines.Add($"{prefix}.configured.curveMid={FormatFloat(frame.CurveMid)}");
        lines.Add($"{prefix}.configured.curveHigh={FormatFloat(frame.CurveHigh)}");
        lines.Add($"{prefix}.actor.candidate.source={FormatNone(frame.Actor?.Source)}");
        lines.Add($"{prefix}.actor.candidate.sourceIndex={FormatFrame(frame.Actor?.SourceIndex)}");
        lines.Add($"{prefix}.actor.candidate.objectIndex={FormatFrame(frame.Actor?.ObjectIndex)}");
        lines.Add($"{prefix}.actor.candidate.objectId={FormatObjectId(frame.Actor?.GameObjectId)}");
        lines.Add($"{prefix}.actor.candidate.entityId={FormatEntityId(frame.Actor?.EntityId)}");
        lines.Add($"{prefix}.actor.candidate.address={FormatAddress(frame.Actor?.Address ?? nint.Zero)}");
        lines.Add($"{prefix}.actor.candidate.kind={FormatNone(frame.Actor?.ObjectKind)}");
        lines.Add($"{prefix}.actor.candidate.name={FormatNone(frame.Actor?.Name)}");
        lines.Add($"{prefix}.actor.candidate.position={FormatVector(frame.Actor?.Position)}");
        lines.Add($"{prefix}.actor.candidate.rotation={FormatFloat(frame.Actor?.Rotation)}");
        lines.Add($"{prefix}.actor.candidate.scale={FormatFloat(frame.Actor?.Scale)}");
        lines.Add($"{prefix}.actor.candidate.hitboxRadius={FormatFloat(frame.Actor?.HitboxRadius)}");
        lines.Add($"{prefix}.actor.candidate.currentHp={FormatNullableUInt(frame.Actor?.CurrentHp)}");
        lines.Add($"{prefix}.actor.candidate.maxHp={FormatNullableUInt(frame.Actor?.MaxHp)}");
        lines.Add($"{prefix}.actor.candidate.targetable={FormatBool(frame.Actor?.Targetable)}");
        lines.Add($"{prefix}.actor.candidate.visibilityHint={FormatNone(frame.Actor?.VisibilityHint)}");
        lines.Add($"{prefix}.actor.candidate.selectableHint={FormatNone(frame.Actor?.SelectableHint)}");
        lines.Add($"{prefix}.actor.candidate.flags={FormatNone(frame.Actor?.Flags)}");
        lines.Add($"{prefix}.actor.candidate.customize={FormatNone(frame.Actor?.Customize)}");
        lines.Add($"{prefix}.actor.candidate.model={FormatNone(frame.Actor?.Model)}");
        lines.Add($"{prefix}.actor.candidate.drawObject={FormatNone(frame.Actor?.DrawObject)}");
        lines.Add($"{prefix}.actor.candidate.drawObjectNonNull={FormatBool(frame.Actor?.DrawObjectNonNull)}");
        lines.Add($"{prefix}.actor.candidate.modelLikePointer={FormatNone(frame.Actor?.ModelLikePointer)}");
        lines.Add($"{prefix}.actor.candidate.modelLikeNonNull={FormatBool(frame.Actor?.ModelLikeNonNull)}");
        lines.Add($"{prefix}.actor.candidate.safeReadError={FormatNone(frame.Actor?.SafeReadError)}");
        lines.Add($"{prefix}.actor.candidate.named={FormatBool(frame.Actor?.Named)}");
        lines.Add($"{prefix}.actor.candidate.playerLike={FormatBool(frame.Actor?.PlayerLike)}");
        lines.Add($"{prefix}.actor.candidate.battleCharacterLike={FormatBool(frame.Actor?.BattleCharacterLike)}");
        lines.Add($"{prefix}.actor.candidate.eventNpcLike={FormatBool(frame.Actor?.EventNpcLike)}");
        lines.Add($"{prefix}.actor.candidate.companionLike={FormatBool(frame.Actor?.CompanionLike)}");
        lines.Add($"{prefix}.actor.candidate.visibleHint={FormatBool(frame.Actor?.VisibleHint)}");
        lines.Add($"{prefix}.actor.candidate.distanceFromConfiguredCharacter={FormatFloat(frame.Actor?.DistanceFromConfiguredCharacter)}");
        lines.Add($"{prefix}.actor.candidate.distanceFromActiveLookAt={FormatFloat(frame.Actor?.DistanceFromActiveLookAt)}");
        lines.Add($"{prefix}.actor.candidate.distanceFromActiveCameraPosition={FormatFloat(frame.Actor?.DistanceFromActiveCameraPosition)}");
        lines.Add($"{prefix}.actor.candidate.yDeltaFromConfiguredCharacter={FormatFloat(frame.Actor?.YDeltaFromConfiguredCharacter)}");
        lines.Add($"{prefix}.actor.candidate.nearConfiguredCharacter={FormatBool(frame.Actor?.NearConfiguredCharacter)}");
        lines.Add($"{prefix}.actor.candidate.nearCameraLookAt={FormatBool(frame.Actor?.NearCameraLookAt)}");
        lines.Add($"{prefix}.actor.candidate.nearCameraPosition={FormatBool(frame.Actor?.NearCameraPosition)}");
        lines.Add($"{prefix}.actor.candidate.categoryReason={FormatNone(frame.Actor?.CategoryReason)}");
        lines.Add($"{prefix}.actor.candidate.stableAcrossFrames={FormatBool(frame.Actor.HasValue ? IsStablePhase2MCandidate(frame.Actor.Value) : null)}");
        lines.Add($"{prefix}.activeCamera.captureStatus={(frame.ActiveCameraCaptured ? "success" : "failed")}");
        lines.Add($"{prefix}.activeCamera.position={FormatVector(frame.ActiveCameraPosition)}");
        lines.Add($"{prefix}.activeCamera.lookAt={FormatVector(frame.ActiveCameraLookAt)}");
        lines.Add($"{prefix}.activeCamera.yaw={FormatFloat(frame.ActiveCameraYaw)}");
        lines.Add($"{prefix}.activeCamera.pitch={FormatFloat(frame.ActiveCameraPitch)}");
        lines.Add($"{prefix}.activeCamera.distance={FormatFloat(frame.ActiveCameraDistance)}");
        lines.Add($"{prefix}.lobbyCamera.captureStatus={(frame.LobbyCameraCaptured ? "success" : "failed")}");
        lines.Add($"{prefix}.lobbyCamera.lookAt={FormatVector(frame.LobbyCameraLookAt)}");
        lines.Add($"{prefix}.lobbyCamera.DirH={FormatFloat(frame.LobbyDirH)}");
        lines.Add($"{prefix}.lobbyCamera.DirV={FormatFloat(frame.LobbyDirV)}");
        lines.Add($"{prefix}.lobbyCamera.Distance={FormatFloat(frame.LobbyDistance)}");
        lines.Add($"{prefix}.lobbyCamera.InterpDistance={FormatFloat(frame.LobbyInterpDistance)}");
        lines.Add($"{prefix}.delta.actorToCameraDistance={FormatFloat(frame.ActorToCameraDistance)}");
        lines.Add($"{prefix}.delta.actorToLookAt={FormatVector(frame.ActorToLookAtDelta)}");
        lines.Add($"{prefix}.delta.configuredCharacterToActiveLookAt={FormatVectorDelta(frame.ConfiguredCharacterPosition, frame.ActiveCameraLookAt)}");
        lines.Add($"{prefix}.delta.configuredCharacterToActiveCamera={FormatVectorDelta(frame.ConfiguredCharacterPosition, frame.ActiveCameraPosition)}");
        lines.Add($"{prefix}.delta.bestCandidateToConfiguredCharacter={FormatVectorDelta(frame.Actor?.Position, frame.ConfiguredCharacterPosition)}");
        lines.Add($"{prefix}.delta.bestCandidateToActiveLookAt={FormatVectorDelta(frame.Actor?.Position, frame.ActiveCameraLookAt)}");
        lines.Add($"{prefix}.delta.bestCandidateToActiveCamera={FormatVectorDelta(frame.Actor?.Position, frame.ActiveCameraPosition)}");
        lines.Add($"{prefix}.delta.bestCandidateYMinusConfiguredY={FormatFloat(frame.ActorYMinusPresetCharacterY)}");
        lines.Add($"{prefix}.delta.actorYMinusPresetCharacterY={FormatFloat(frame.ActorYMinusPresetCharacterY)}");
        lines.Add($"{prefix}.delta.actorYMinusFocusY={FormatFloat(frame.ActorYMinusFocusY)}");
        lines.Add($"{prefix}.delta.actorYMinusNativeLookAtY={FormatFloat(frame.ActorYMinusNativeLookAtY)}");
        lines.Add($"{prefix}.groundHeight.source={frame.GroundHeightStatus}");
        lines.Add($"{prefix}.groundHeight.y={FormatFloat(frame.GroundY)}");

        for (var i = 0; i < frame.ObjectCandidates.Count; i++)
        {
            AddPhase2MObjectCandidateLines(lines, $"{prefix}.objectCandidate[{i}]", frame.ObjectCandidates[i]);
        }

        for (var i = 0; i < frame.SourceDiscovery.Count; i++)
        {
            var source = frame.SourceDiscovery[i];
            lines.Add($"{prefix}.sourceDiscovery.source[{i}].name={source.Name}");
            lines.Add($"{prefix}.sourceDiscovery.source[{i}].available={source.Available}");
            lines.Add($"{prefix}.sourceDiscovery.source[{i}].count={source.Count}");
            lines.Add($"{prefix}.sourceDiscovery.source[{i}].candidateCount={source.CandidateCount}");
            lines.Add($"{prefix}.sourceDiscovery.source[{i}].error={FormatNone(source.Error)}");
        }
    }

    private void AddPhase2MObjectCandidateLines(
        List<string> lines,
        string prefix,
        TitleBackgroundPhase2MActorCandidate candidate)
    {
        lines.Add($"{prefix}.source={FormatNone(candidate.Source)}");
        lines.Add($"{prefix}.sourceIndex={candidate.SourceIndex}");
        lines.Add($"{prefix}.objectIndex={candidate.ObjectIndex}");
        lines.Add($"{prefix}.objectId={FormatObjectId(candidate.GameObjectId)}");
        lines.Add($"{prefix}.entityId={FormatEntityId(candidate.EntityId)}");
        lines.Add($"{prefix}.address={FormatAddress(candidate.Address)}");
        lines.Add($"{prefix}.kind={FormatNone(candidate.ObjectKind)}");
        lines.Add($"{prefix}.name={FormatNone(candidate.Name)}");
        lines.Add($"{prefix}.position={FormatVector(candidate.Position)}");
        lines.Add($"{prefix}.rotation={FormatFloat(candidate.Rotation)}");
        lines.Add($"{prefix}.scale={FormatFloat(candidate.Scale)}");
        lines.Add($"{prefix}.hitboxRadius={FormatFloat(candidate.HitboxRadius)}");
        lines.Add($"{prefix}.currentHp={FormatNullableUInt(candidate.CurrentHp)}");
        lines.Add($"{prefix}.maxHp={FormatNullableUInt(candidate.MaxHp)}");
        lines.Add($"{prefix}.targetable={FormatBool(candidate.Targetable)}");
        lines.Add($"{prefix}.visibilityHint={FormatNone(candidate.VisibilityHint)}");
        lines.Add($"{prefix}.selectableHint={FormatNone(candidate.SelectableHint)}");
        lines.Add($"{prefix}.flags={FormatNone(candidate.Flags)}");
        lines.Add($"{prefix}.customize={FormatNone(candidate.Customize)}");
        lines.Add($"{prefix}.model={FormatNone(candidate.Model)}");
        lines.Add($"{prefix}.drawObject={FormatNone(candidate.DrawObject)}");
        lines.Add($"{prefix}.drawObjectNonNull={FormatBool(candidate.DrawObjectNonNull)}");
        lines.Add($"{prefix}.modelLikePointer={FormatNone(candidate.ModelLikePointer)}");
        lines.Add($"{prefix}.modelLikeNonNull={FormatBool(candidate.ModelLikeNonNull)}");
        lines.Add($"{prefix}.safeReadError={FormatNone(candidate.SafeReadError)}");
        lines.Add($"{prefix}.distanceFromConfiguredCharacter={FormatFloat(candidate.DistanceFromConfiguredCharacter)}");
        lines.Add($"{prefix}.distanceFromActiveLookAt={FormatFloat(candidate.DistanceFromActiveLookAt)}");
        lines.Add($"{prefix}.distanceFromActiveCameraPosition={FormatFloat(candidate.DistanceFromActiveCameraPosition)}");
        lines.Add($"{prefix}.yDeltaFromConfiguredCharacter={FormatFloat(candidate.YDeltaFromConfiguredCharacter)}");
        lines.Add($"{prefix}.named={FormatBool(candidate.Named)}");
        lines.Add($"{prefix}.playerLike={FormatBool(candidate.PlayerLike)}");
        lines.Add($"{prefix}.battleCharacterLike={FormatBool(candidate.BattleCharacterLike)}");
        lines.Add($"{prefix}.eventNpcLike={FormatBool(candidate.EventNpcLike)}");
        lines.Add($"{prefix}.companionLike={FormatBool(candidate.CompanionLike)}");
        lines.Add($"{prefix}.visibleHint={FormatBool(candidate.VisibleHint)}");
        lines.Add($"{prefix}.nearConfiguredCharacter={FormatBool(candidate.NearConfiguredCharacter)}");
        lines.Add($"{prefix}.nearCameraLookAt={FormatBool(candidate.NearCameraLookAt)}");
        lines.Add($"{prefix}.nearCameraPosition={FormatBool(candidate.NearCameraPosition)}");
        lines.Add($"{prefix}.categoryReason={FormatNone(candidate.CategoryReason)}");
        lines.Add($"{prefix}.stableAcrossFrames={FormatBool(IsStablePhase2MCandidate(candidate))}");
        lines.Add($"{prefix}.score={candidate.Score}");
        lines.Add($"{prefix}.scoreReason={FormatNone(candidate.ScoreReason)}");
    }

    private static void AddGeneratedCurveCallLines(
        List<string> lines,
        string prefix,
        TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        AddGeneratedCurveCallLines(lines, prefix, "call", call);
    }

    private static void AddGeneratedCurveCallLines(
        List<string> lines,
        string prefix,
        string callLabel,
        TitleBackgroundPhase2FGeneratedCurveCall call)
    {
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].frame={FormatFrame(call.Frame)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].inputValue={FormatFloat(call.InputValue)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].interestingReason={FormatNone(call.InterestingReason)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].interestingPriority={call.InterestingPriority}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCharacterY={FormatFloat(call.SavedCharacterY)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveLow={FormatFloat(call.SavedCurveLow)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveMid={FormatFloat(call.SavedCurveMid)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveHigh={FormatFloat(call.SavedCurveHigh)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveLowMinusCharacterY={FormatFloat(call.SavedCurveLow - call.SavedCharacterY)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveMidMinusCharacterY={FormatFloat(call.SavedCurveMid - call.SavedCharacterY)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].savedCurveHighMinusCharacterY={FormatFloat(call.SavedCurveHigh - call.SavedCharacterY)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].before.CameraCurveEnabled={FormatBool(call.Before?.CameraCurveEnabled)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].before.lowPoint={FormatCurvePoint(call.Before?.LowPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].before.midPoint={FormatCurvePoint(call.Before?.MidPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].before.highPoint={FormatCurvePoint(call.Before?.HighPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.CameraCurveEnabled={FormatBool(call.After?.CameraCurveEnabled)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.lowPoint={FormatCurvePoint(call.After?.LowPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.midPoint={FormatCurvePoint(call.After?.MidPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.highPoint={FormatCurvePoint(call.After?.HighPoint)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.LowPoint.ValueMinusInput={FormatFloatDelta(call.After?.LowPoint.Y, call.InputValue)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.MidPoint.ValueMinusInput={FormatFloatDelta(call.After?.MidPoint.Y, call.InputValue)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].after.HighPoint.ValueMinusInput={FormatFloatDelta(call.After?.HighPoint.Y, call.InputValue)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].activeBefore.Distance={FormatFloat(call.ActiveDistanceBefore)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].activeBefore.LookAtY={FormatFloat(call.ActiveLookAtYBefore)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].activeAfter.Distance={FormatFloat(call.ActiveDistanceAfter)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].activeAfter.LookAtY={FormatFloat(call.ActiveLookAtYAfter)}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].status={call.Status}");
        lines.Add($"{prefix}.{callLabel}[{call.CallIndex}].error={FormatNone(call.Error)}");
    }

    private static TitleBackgroundResolverMode NormalizeResolverMode(TitleBackgroundResolverMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundResolverMode), mode)
            ? mode
            : TitleBackgroundResolverMode.AutoDiagnosticOnly;
    }

}

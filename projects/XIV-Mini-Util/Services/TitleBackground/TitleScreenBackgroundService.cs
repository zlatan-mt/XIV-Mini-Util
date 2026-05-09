// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs
// Description: キャラ選択画面背景差し替えの設定、診断、native hook lifecycleを管理する
// Reason: HaselTweaks相当のemote/pet/preload機能から背景差し替えを分離するため
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using System.Runtime.InteropServices;
using System.Text;

namespace XivMiniUtil.Services.TitleBackground;

public sealed unsafe class TitleScreenBackgroundService : IDisposable
{
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly ISigScanner _sigScanner;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;
    private readonly TitleBackgroundAddressResolver _addressResolver = new();

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
    private float? _lastObservedFixOnFovY;

    private GameLobbyType EffectiveLobbyType =>
        _loadingLobbyType != GameLobbyType.None
            ? _loadingLobbyType
            : _lastLobbyUpdateMapId;

    public TitleScreenBackgroundService(
        IGameInteropProvider gameInteropProvider,
        ISigScanner sigScanner,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration)
    {
        _gameInteropProvider = gameInteropProvider;
        _sigScanner = sigScanner;
        _dataManager = dataManager;
        _log = log;
        _configuration = configuration;

        InitializeHooks();
        ApplyFromConfiguration();
    }

    public void SetEnabled(bool enabled)
    {
        _configuration.TitleBackgroundOverrideEnabled = enabled;
        _configuration.Save();
        ReloadNativeIntegration();
    }

    public void ApplyFromConfiguration()
    {
        NormalizeConfiguration();
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

        if (!ValidateCurrentConfiguration(out var errorMessage))
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

        _state = TitleBackgroundServiceState.Ready;
        _stateReason = "準備完了";
    }

    public void ReloadNativeIntegration()
    {
        _cameraApplyPending = false;
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
        var hooksReady = sceneHooksReady && (!_configuration.TitleBackgroundCameraOverrideEnabled || cameraHookReady);
        var hooksEnabled = IsHookEnabled(_createSceneHook)
            || IsHookEnabled(_lobbyUpdateHook)
            || IsHookEnabled(_loadLobbySceneHook)
            || IsHookEnabled(_cameraFixOnHook);
        var lines = new List<string>
        {
            $"runtimeMode={_configuration.TitleBackgroundRuntimeMode}",
            $"hooksReady={hooksReady}",
            $"sceneHooksReady={sceneHooksReady}",
            $"cameraHookReady={cameraHookReady}",
            $"cameraHookEnabled={IsHookEnabled(_cameraFixOnHook)}",
            $"cameraOverrideEnabled={_configuration.TitleBackgroundCameraOverrideEnabled}",
            $"titleOverrideImplemented={TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(_configuration.TitleBackgroundRuntimeMode)}",
            "fixOnAbiVerified=False",
            $"hooksEnabled={hooksEnabled}",
            "",
            BuildAddressLine("CreateScene.configured", _configuration.TitleBackgroundCreateSceneSignature),
            $"CreateScene.match={FormatAddress(_addressResolver.CreateSceneMatch)}",
            $"CreateScene.resolvedTarget={FormatAddress(_addressResolver.CreateScene)}",
            "CreateScene.method=TryScanText+NearbyE8Rel32",
            $"CreateScene.targetWithinText={GetTargetWithinText("CreateScene")}",
            "",
            BuildAddressLine("LobbyUpdate.configured", _configuration.TitleBackgroundLobbyUpdateSignature),
            $"LobbyUpdate.match={FormatAddress(_addressResolver.LobbyUpdateMatch)}",
            $"LobbyUpdate.resolvedTarget={FormatAddress(_addressResolver.LobbyUpdate)}",
            "LobbyUpdate.method=TryScanText+NearbyE8Rel32",
            $"LobbyUpdate.targetWithinText={GetTargetWithinText("LobbyUpdate")}",
            "",
            BuildAddressLine("LoadLobbyScene.configured", _configuration.TitleBackgroundLoadLobbySceneSignature),
            $"LoadLobbyScene.address={FormatAddress(_addressResolver.LoadLobbyScene)}",
            "LoadLobbyScene.method=TryScanText",
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
            $"LobbyCameraFixOn.targetWithinText={GetTargetWithinText("LobbyCameraFixOn")}",
            "",
            "UpdateLobbyUIStage.optional=True",
            $"UpdateLobbyUIStage.address={FormatAddress(_addressResolver.UpdateLobbyUIStage)}",
            $"Focus.reservedForCameraPhase={!TitleBackgroundRuntimeModeHelper.IsFocusUsed(_configuration.TitleBackgroundCameraOverrideEnabled)}",
            $"lastLobbyUpdateMapId={_lastLobbyUpdateMapId}",
            $"loadingLobbyType={_loadingLobbyType}",
            $"effectiveLobbyType={EffectiveLobbyType}",
            $"lastCreateScenePath={(_lastObservedCreateScenePath.Length == 0 ? "none" : _lastObservedCreateScenePath)}",
            $"lastFixOnFovY={(_lastObservedFixOnFovY.HasValue ? _lastObservedFixOnFovY.Value.ToString("0.###") : "none")}",
            $"resolverError={(_addressResolver.LastError.Length == 0 ? "none" : _addressResolver.LastError)}",
        };

        foreach (var result in _addressResolver.ScanResults)
        {
            lines.Add($"signatureScan: name={result.Name}, method={result.Method}, status={result.Status}, address={FormatAddress(result.Address)}, resolvedTarget={FormatAddress(result.ResolvedTarget)}, targetWithinText={result.TargetWithinText}, message={(string.IsNullOrWhiteSpace(result.Message) ? "none" : result.Message)}");
        }

        return lines;
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
        _cameraApplyPending = false;
        _loadingLobbyType = GameLobbyType.None;
        _lastLobbyUpdateMapId = GameLobbyType.None;
        _currentMapWriteAttempted = false;
        _lastCurrentMapWriteSucceeded = false;
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
            if (TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(
                _configuration.TitleBackgroundRuntimeMode,
                _configuration.TitleBackgroundOverrideEnabled,
                _configuration.TitleBackgroundCameraOverrideEnabled))
            {
                _cameraFixOnHook = _gameInteropProvider.HookFromAddress<LobbyCameraFixOnDelegate>(_addressResolver.FixOn, LobbyCameraFixOnDetour);
            }

            _createSceneHook.Enable();
            _lobbyUpdateHook.Enable();
            _loadLobbySceneHook.Enable();
            _cameraFixOnHook?.Enable();
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
            _log.Debug("[XMU BG] CreateScene lobbyType={LobbyType}, path={Path}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", lobbyType, originalPath, territoryId, layerFilterKey);

            if (ShouldOverrideCharaSelect(lobbyType))
            {
                LayoutWorld.UnloadPrefetchLayout();
                var presetTerritoryId = _configuration.TitleBackgroundLayoutTerritoryTypeId != 0
                    ? _configuration.TitleBackgroundLayoutTerritoryTypeId
                    : _configuration.TitleBackgroundTerritoryTypeId;
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
                _log.Information("[XMU BG] Override CharaSelect scene path={Path}, territoryId={TerritoryId}, layerFilterKey={LayerFilterKey}", _validatedTerritoryPath, territoryId, layerFilterKey);
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
        try
        {
            _lastObservedFixOnFovY = fovY;
            if (ShouldOverrideCamera())
            {
                _cameraApplyPending = false;
                cameraOverride =
                [
                    _configuration.TitleBackgroundCameraX,
                    _configuration.TitleBackgroundCameraY,
                    _configuration.TitleBackgroundCameraZ,
                ];
                focusOverride =
                [
                    _configuration.TitleBackgroundCharacterPositionX,
                    _configuration.TitleBackgroundCharacterPositionY,
                    _configuration.TitleBackgroundCharacterPositionZ,
                ];
                overrideFovY = _configuration.TitleBackgroundFovY;
            }
        }
        catch (Exception ex)
        {
            MarkRuntimeError(ex, nameof(LobbyCameraFixOnDetour));
            cameraOverride = null;
            focusOverride = null;
            overrideFovY = fovY;
        }

        if (cameraOverride != null && focusOverride != null)
        {
            fixed (float* cameraPointer = cameraOverride)
            fixed (float* focusPointer = focusOverride)
            {
                return _cameraFixOnHook?.Original(self, cameraPointer, focusPointer, overrideFovY) ?? nint.Zero;
            }
        }

        return _cameraFixOnHook?.Original(self, cameraPos, focusPos, fovY) ?? nint.Zero;
    }

    private bool ShouldResetCurrentMapForReload(GameLobbyType nextMap)
    {
        return _state == TitleBackgroundServiceState.Ready
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && TryReadCurrentLobbyMap(out var currentMap)
            && GameLobbyTypeHelper.IsTitleCharaSelectTransition(currentMap, nextMap);
    }

    private bool ShouldOverrideCharaSelect(GameLobbyType lobbyType)
    {
        return _state == TitleBackgroundServiceState.Ready
            && _configuration.TitleBackgroundOverrideEnabled
            && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly
            && lobbyType == GameLobbyType.CharaSelect
            && !string.IsNullOrWhiteSpace(_validatedTerritoryPath);
    }

    private bool ShouldOverrideCamera()
    {
        return _configuration.TitleBackgroundCameraOverrideEnabled
            && _cameraApplyPending
            && _state == TitleBackgroundServiceState.Ready
            && TryReadCurrentLobbyMap(out var currentMap)
            && currentMap == GameLobbyType.CharaSelect;
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
        return _configuration.TitleBackgroundCameraOverrideEnabled
            && _cameraFixOnHook != null;
    }

    private bool AreNativeSceneAddressesReady()
    {
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

    private bool IsHookSetAlignedWithConfiguration()
    {
        var shouldCreateCameraHook = TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(
            _configuration.TitleBackgroundRuntimeMode,
            _configuration.TitleBackgroundOverrideEnabled,
            _configuration.TitleBackgroundCameraOverrideEnabled);
        return shouldCreateCameraHook == (_cameraFixOnHook != null);
    }

    private void MarkRuntimeError(Exception ex, string hookName)
    {
        _state = TitleBackgroundServiceState.RuntimeError;
        _stateReason = $"{hookName}: {ex.Message}";
        _cameraApplyPending = false;
        _log.Warning(ex, "TitleBackground runtime error in {HookName}.", hookName);
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

    private static string NormalizeSignature(string? signature)
    {
        return (signature ?? string.Empty).Trim();
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
}

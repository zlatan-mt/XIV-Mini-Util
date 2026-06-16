// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.cs
// Description: キャラ選択画面のエモート再生と記録状態を管理する
// Reason: unsafe hookとUI設定をPlugin本体から分離するため
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectLobbyStageProbe(
    bool Available,
    int MatchCount,
    CharaSelectLobbyCandidate Candidate0,
    int ResolvedPosition,
    bool Changed,
    string PositionVerdict,
    string ResolvedReason);

public sealed unsafe partial class CharaSelectService : IDisposable
{
    private const ushort IdleTimelineId = 3;
    private const uint ChangePoseEmoteId = 90;
    private const int DelayedReplayFrameDelay = 60;
    private static readonly uint[] ChangePoseEmoteIds = [91, 92, 93, 107, 108, 218, 219];
    private static readonly HashSet<uint> AlwaysExcludedEmoteIds =
    [
        50, // 座る
        52, // 居眠り
        88, // グループポーズ
    ];

    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPlayerState _playerState;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;

    private Hook<AgentLobby.Delegates.UpdateCharaSelectDisplay>? _updateCharaSelectDisplayHook;
    private Hook<EmoteManager.Delegates.ExecuteEmote>? _executeEmoteHook;
    private Hook<AgentLobby.Delegates.OpenLoginWaitDialog>? _openLoginWaitDialogHook;
    private Hook<AgentLobby.Delegates.UpdateLoginPosition>? _updateLoginPositionHook;
    private CharaSelectCharacterState? _currentEntry;
    private readonly CharaSelectReplayTracker _replayTracker = new();
    private bool _disposed;
    private bool _isRecordingEmote;
    private int _frameworkPollFrame;
    private int _delayedReplayFrames;
    private ulong _delayedReplayContentId;
    private uint _delayedReplayEmoteId;
    private nint _delayedReplayCharacterAddress;
    private CharaSelectPrefetchOwner _prefetchOwner;
    private ushort _loadedPrefetchTerritoryTypeId;
    private string _loadedPrefetchBg = string.Empty;
    private uint _loadedPrefetchLevelId;
    private byte _loadedPrefetchLayerEntryType;
    private int _dataCenterNamePollFrame;
    private int _lastLoginPosition;
    private int _lastOverrideLoginPosition;
    private IReadOnlyList<string> _lastVoiceDiagnosticLines = [];
    private CharaSelectSceneLastObservation _lastSceneObservation = CharaSelectSceneLastObservation.Empty;
    private CharaSelectStageProbeSnapshot _lastStageProbe = CharaSelectStageProbeSnapshot.Empty;
    private TitleBackgroundCharacterCompositionBridgeSnapshot _lastTitleBackgroundBridgeSnapshot = TitleBackgroundCharacterCompositionBridgeSnapshot.Empty;
    private CharaSelectCompositionRouteRuntimeSnapshot _lastLegacyCompositionRouteSnapshot = CharaSelectCompositionRouteRuntimeSnapshot.Empty;
    private CharaSelectCompositionRouteRuntimeSnapshot _lastTitleBackgroundBridgeRouteSnapshot = CharaSelectCompositionRouteRuntimeSnapshot.Empty;

    public CharaSelectService(
        IGameInteropProvider gameInteropProvider,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration)
    {
        _gameInteropProvider = gameInteropProvider;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _playerState = playerState;
        _dataManager = dataManager;
        _log = log;
        _configuration = configuration;

        InitializeHooks();
        _framework.Update += OnFrameworkUpdate;
    }

    public bool IsRecordingEmote => _isRecordingEmote;
    public ulong ActiveContentId => _clientState.IsLoggedIn
        ? _playerState.ContentId
        : _currentEntry?.ContentId ?? _playerState.ContentId;

    public uint? CurrentSelectedEmoteId
    {
        get
        {
            var contentId = ActiveContentId;
            return CharaSelectEmotePresetStore.GetActiveEmoteId(
                _configuration.CharaSelectEmotePresets,
                _configuration.CharaSelectActiveEmotePresetIndexes,
                contentId,
                _configuration.CharaSelectSelectedEmotes);
        }
    }

    public void SetEmoteEnabled(bool enabled)
    {
        _configuration.CharaSelectEmoteEnabled = enabled;
        _configuration.Save();

        if (!enabled)
        {
            StopRecordingEmote();
            ResetEmoteMode();
            ClearReplayState();
            return;
        }

        ReplaySelectedEmote();
    }

    public void SetPreloadTerritoryEnabled(bool enabled)
    {
        _configuration.CharaSelectPreloadTerritoryEnabled = enabled;
        _configuration.Save();

        ApplyLoginWaitHookState();
        if (!enabled)
        {
            TryUnloadPrefetchLayout(CharaSelectPrefetchOwner.LoginWait);
        }
    }

    public void SyncFromConfiguration()
    {
        if (!_configuration.CharaSelectEmoteEnabled)
        {
            StopRecordingEmote();
            ResetEmoteMode();
        }

        ApplyLoginWaitHookState();
        ApplyOverrideTerritoryPrefetch();
    }

    private void ApplyLoginWaitHookState()
    {
        try
        {
            var enabled = _configuration.CharaSelectPreloadTerritoryEnabled
                || _configuration.CharaSelectOverrideTerritoryEnabled;
            if (enabled)
            {
                _openLoginWaitDialogHook?.Enable();
            }
            else
            {
                _openLoginWaitDialogHook?.Disable();
                TryUnloadPrefetchLayout(CharaSelectPrefetchOwner.LoginWait);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to toggle CharaSelect login wait hook.");
        }
    }

    public void StartRecordingEmote()
    {
        _isRecordingEmote = true;
    }

    public void StopRecordingEmote()
    {
        _isRecordingEmote = false;
    }

    public void ClearSelectedEmote()
    {
        var contentId = ActiveContentId;
        if (contentId == 0)
        {
            return;
        }

        var removedPreset = CharaSelectEmotePresetStore.RemoveActive(
            _configuration.CharaSelectEmotePresets,
            _configuration.CharaSelectActiveEmotePresetIndexes,
            contentId);
        var removedLegacy = _configuration.CharaSelectSelectedEmotes.Remove(contentId);
        if (removedPreset || removedLegacy)
        {
            _configuration.Save();
        }

        ResetEmoteMode();
        ClearReplayState();
    }

    public void ReplaySelectedEmote()
    {
        if (_clientState.IsLoggedIn || !_configuration.CharaSelectEmoteEnabled || CurrentSelectedEmoteId is not { } emoteId)
        {
            return;
        }

        MarkSceneEmoteReplay(PlayEmote(emoteId));
    }

    public void SelectPreviousEmote()
    {
        SelectPreset(CharaSelectEmotePresetStore.SelectPrevious);
    }

    public void SelectNextEmote()
    {
        SelectPreset(CharaSelectEmotePresetStore.SelectNext);
    }

    public void SaveLastRecordedEmoteToActiveSlot()
    {
        var contentId = ActiveContentId;
        if (!TryGetLastRecordedEmote(contentId, out var emoteId))
        {
            return;
        }

        if (!IsRecordableEmote(emoteId))
        {
            _configuration.CharaSelectLastRecordedEmotes.Remove(contentId);
            _configuration.Save();
            return;
        }

        CharaSelectEmotePresetStore.SaveToActiveSlot(
            _configuration.CharaSelectEmotePresets,
            _configuration.CharaSelectActiveEmotePresetIndexes,
            contentId,
            emoteId);
        _configuration.Save();
        ReplayAfterActivePresetChanged();
    }

    public void AppendLastRecordedEmotePreset()
    {
        var contentId = ActiveContentId;
        if (!TryGetLastRecordedEmote(contentId, out var emoteId))
        {
            return;
        }

        if (!IsRecordableEmote(emoteId))
        {
            _configuration.CharaSelectLastRecordedEmotes.Remove(contentId);
            _configuration.Save();
            return;
        }

        CharaSelectEmotePresetStore.Append(
            _configuration.CharaSelectEmotePresets,
            _configuration.CharaSelectActiveEmotePresetIndexes,
            contentId,
            emoteId);
        _configuration.Save();
        ReplayAfterActivePresetChanged();
    }

    public string GetCurrentSelectedEmoteDisplayName()
    {
        var contentId = ActiveContentId;
        if (contentId == 0)
        {
            return "キャラクター未選択";
        }

        if (CurrentSelectedEmoteId is not { } emoteId)
        {
            return "なし";
        }

        var emotes = CharaSelectEmotePresetStore.GetEmotes(_configuration.CharaSelectEmotePresets, contentId);
        var prefix = emotes.Count > 0
            ? $"{CharaSelectEmotePresetStore.GetActiveIndex(_configuration.CharaSelectEmotePresets, _configuration.CharaSelectActiveEmotePresetIndexes, contentId) + 1}/{emotes.Count} "
            : string.Empty;
        return prefix + GetEmoteDisplayName(emoteId);
    }

    public string GetLastRecordedEmoteDisplayName()
    {
        return TryGetLastRecordedEmote(ActiveContentId, out var emoteId)
            ? GetEmoteDisplayName(emoteId)
            : "なし";
    }

    public string GetOverrideTerritoryDisplayName()
    {
        var territoryTypeId = _configuration.CharaSelectOverrideTerritoryTypeId;
        return territoryTypeId == 0 ? "未指定" : GetTerritoryDisplayName(territoryTypeId);
    }

    public string GetCurrentLoginTerritoryDisplayName()
    {
        var territoryTypeId = ResolveCurrentTerritoryTypeId();
        return territoryTypeId == 0 ? "未選択" : GetTerritoryDisplayName(territoryTypeId);
    }

    public string GetOverridePositionDisplayName()
    {
        if (!_configuration.CharaSelectOverridePositionEnabled)
        {
            return "未使用";
        }

        return $"X:{_configuration.CharaSelectOverridePositionX:0.00} Y:{_configuration.CharaSelectOverridePositionY:0.00} Z:{_configuration.CharaSelectOverridePositionZ:0.00}";
    }

    public string GetLoginPositionDisplayName()
    {
        return _lastLoginPosition == 0
            ? "未検出"
            : $"last={_lastLoginPosition}, override={(_lastOverrideLoginPosition == 0 ? "なし" : _lastOverrideLoginPosition)}";
    }

    public void SetOverrideTerritoryEnabled(bool enabled)
    {
        _configuration.CharaSelectOverrideTerritoryEnabled = enabled;
        _configuration.Save();

        if (_configuration.CharaSelectOverrideTerritoryEnabled)
        {
            ApplyLoginWaitHookState();
            ApplyOverrideTerritoryPrefetch();
            RefreshCharaSelectDisplay();
        }
        else
        {
            ApplyLoginWaitHookState();
            TryUnloadPrefetchLayout(CharaSelectPrefetchOwner.OverrideDisplay);
            RefreshCharaSelectDisplay();
        }
    }

    public void SetOverrideTerritoryTypeId(ushort territoryTypeId)
    {
        _configuration.CharaSelectOverrideTerritoryTypeId = territoryTypeId;
        if (territoryTypeId == 0)
        {
            _configuration.CharaSelectOverrideTerritoryEnabled = false;
        }

        _configuration.Save();
        ApplyLoginWaitHookState();
        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
    }

    public void UseCurrentLoginTerritoryForOverride()
    {
        var territoryTypeId = ResolveCurrentTerritoryTypeId();
        if (territoryTypeId == 0)
        {
            return;
        }

        _configuration.CharaSelectOverrideTerritoryTypeId = territoryTypeId;
        _configuration.CharaSelectOverrideTerritoryEnabled = true;
        _configuration.Save();
        ApplyLoginWaitHookState();
        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
    }

    public void ClearOverrideTerritory()
    {
        _configuration.CharaSelectOverrideTerritoryTypeId = 0;
        _configuration.CharaSelectOverrideTerritoryEnabled = false;
        _configuration.Save();
        ApplyLoginWaitHookState();
        TryUnloadPrefetchLayout(CharaSelectPrefetchOwner.OverrideDisplay);
        RefreshCharaSelectDisplay();
    }

    public void SetOverridePositionEnabled(bool enabled)
    {
        _configuration.CharaSelectOverridePositionEnabled = enabled;
        _configuration.Save();
        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
    }

    public void SetOverridePosition(float x, float y, float z)
    {
        _configuration.CharaSelectOverridePositionX = SanitizeCoordinate(x);
        _configuration.CharaSelectOverridePositionY = SanitizeCoordinate(y);
        _configuration.CharaSelectOverridePositionZ = SanitizeCoordinate(z);
        _configuration.Save();
        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
    }

    public void UseCurrentPlayerPositionForOverride()
    {
        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return;
        }

        var position = localPlayer.Position;
        SetOverridePosition(position.X, position.Y, position.Z);
        _configuration.CharaSelectOverridePositionEnabled = true;
        _configuration.Save();
        ApplyOverrideTerritoryPrefetch();
        RefreshCharaSelectDisplay();
    }

    public void SetShowLastDataCenterNameEnabled(bool enabled)
    {
        _configuration.CharaSelectShowLastDataCenterNameEnabled = enabled;
        _dataCenterNamePollFrame = enabled ? 299 : 0;
        _configuration.Save();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRecordingEmote();
        ResetEmoteMode();
        ClearReplayState();
        TryUnloadPrefetchLayout();
        _currentEntry = null;
        _framework.Update -= OnFrameworkUpdate;
        DisposeHook(_updateLoginPositionHook);
        DisposeHook(_openLoginWaitDialogHook);
        DisposeHook(_executeEmoteHook);
        DisposeHook(_updateCharaSelectDisplayHook);
    }

    private void UpdateCurrentEntry(AgentLobby* agent, sbyte index, bool scheduleDelayedReplay = false)
    {
        if (agent == null || index < 0)
        {
            RecordSceneObservation(false, 0, index, false);
            CleanupCharaSelect();
            return;
        }

        var normalizedIndex = index >= 100 ? index - 100 : index;
        var entry = agent->LobbyData.GetCharacterEntryByIndex(0, agent->WorldIndex, normalizedIndex);
        var character = CharaSelectCharacterList.GetCurrentCharacter();

        if (entry == null || character == null || entry->ContentId == 0)
        {
            CaptureStageProbe(
                agent,
                index,
                normalizedIndex,
                false,
                entry == null ? 0 : entry->ContentId,
                entry == null ? "entry-null" : "character-null");
            RecordSceneObservation(false, entry == null ? 0 : entry->ContentId, normalizedIndex, false);
            CleanupCharaSelect();
            return;
        }

        var voiceId = ResolveCharacterVoiceId(entry);
        CharaSelectCharacterApplier.ApplyVoice(character, voiceId);
        _lastVoiceDiagnosticLines = BuildVoiceDiagnosticLines(agent, index, normalizedIndex, entry, character, voiceId);
        CaptureStageProbe(
            agent,
            index,
            normalizedIndex,
            true,
            entry->ContentId,
            "read-only-observation",
            originalTerritoryType: entry->ClientSelectData.TerritoryType,
            originalZoneId: entry->ClientSelectData.ZoneId);
        MarkTitleBackgroundBridge(
            invoked: CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration),
            reason: "character-observed",
            appliedStage: _lastTitleBackgroundBridgeSnapshot.AppliedStage,
            appliedCharacter: true);
        RecordSceneObservation(true, entry->ContentId, normalizedIndex, false);

        var isFirstEntry = _currentEntry == null;
        var sameEntry = _currentEntry?.ContentId == entry->ContentId
            && _currentEntry.Character == character;
        if (sameEntry)
        {
            ApplyOverrideTerritoryPrefetch();
            if (IsDelayedReplayPending(entry->ContentId, character))
            {
                return;
            }

            ReplaySelectedEmoteIfNeeded();
            return;
        }

        _currentEntry = new CharaSelectCharacterState(
            character,
            entry->ContentId,
            entry->ClientSelectData.TerritoryType,
            entry->ClientSelectData.CurrentClass,
            voiceId);

        ApplyOverrideTerritoryPrefetch();

        if (scheduleDelayedReplay || isFirstEntry)
        {
            ScheduleDelayedReplay();
            return;
        }

        ReplaySelectedEmoteIfNeeded(force: true);
    }

    private void RecordSceneObservation(
        bool characterPointerResolved,
        ulong contentId,
        int selectedIndex,
        bool emoteReplayApplied)
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return;
        }

        var profile = CurrentSceneProfile;
        var territoryOverrideApplied = _configuration.CharaSelectSceneUseProfileTerritory
            && _configuration.CharaSelectOverrideTerritoryEnabled
            && _configuration.CharaSelectOverrideTerritoryTypeId == profile.TerritoryTypeId;
        var emoteReplayAttempted = _configuration.CharaSelectSceneUseSavedEmote
            && _configuration.CharaSelectEmoteEnabled
            && CurrentSelectedEmoteId.HasValue;
        _lastSceneObservation = new CharaSelectSceneLastObservation(
            true,
            characterPointerResolved,
            contentId,
            selectedIndex,
            profile.Id,
            profile.DisplayName,
            territoryOverrideApplied,
            emoteReplayAttempted || _lastSceneObservation.EmoteReplayAttempted,
            emoteReplayApplied || _lastSceneObservation.EmoteReplayApplied,
            _configuration.TitleBackgroundOverrideEnabled,
            DateTimeOffset.UtcNow.ToString("O"),
            _lastStageProbe);
    }

    private void MarkSceneEmoteReplay(bool applied)
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return;
        }

        var profile = CurrentSceneProfile;
        _lastSceneObservation = new CharaSelectSceneLastObservation(
            true,
            _lastSceneObservation.CharacterPointerResolved,
            _lastSceneObservation.ContentId,
            _lastSceneObservation.SelectedIndex,
            profile.Id,
            profile.DisplayName,
            _lastSceneObservation.TerritoryOverrideApplied,
            true,
            applied || _lastSceneObservation.EmoteReplayApplied,
            _configuration.TitleBackgroundOverrideEnabled,
            DateTimeOffset.UtcNow.ToString("O"),
            _lastStageProbe);
    }

    private void CaptureStageProbe(
        AgentLobby* agent,
        sbyte selectedIndex,
        int normalizedIndex,
        bool characterPointerResolved,
        ulong contentId,
        string reason,
        ushort originalTerritoryType = 0,
        ushort originalZoneId = 0,
        ushort patchedTerritoryType = 0,
        ushort patchedZoneId = 0,
        bool patchAttempted = false,
        bool patchApplied = false)
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return;
        }

        var profile = CurrentSceneProfile;
        var territoryTypeId = NormalizeHousingTerritory(profile.TerritoryTypeId);
        var lobbyProbe = BuildLobbyStageProbe(territoryTypeId);
        var layoutRequested = _configuration.CharaSelectOverrideTerritoryEnabled
            && _configuration.CharaSelectOverrideTerritoryTypeId != 0;
        var layoutVerdict = _prefetchOwner == CharaSelectPrefetchOwner.OverrideDisplay
            && _loadedPrefetchTerritoryTypeId == territoryTypeId
                ? "loaded"
                : layoutRequested ? "not-loaded" : "not-requested";
        var preservePreviousPatch = !patchAttempted
            && _lastStageProbe.Available
            && _lastStageProbe.ContentId == contentId
            && _lastStageProbe.ClientSelectDataPatchAttempted;
        var clientSelectVerdict = patchApplied
            ? "patch-applied"
            : patchAttempted ? "not-needed" : preservePreviousPatch ? _lastStageProbe.ClientSelectDataVerdict : "not-visible-stage-source";
        _lastStageProbe = new CharaSelectStageProbeSnapshot(
            true,
            string.IsNullOrWhiteSpace(reason) ? "none" : reason,
            DateTimeOffset.UtcNow.ToString("O"),
            selectedIndex,
            normalizedIndex,
            agent == null ? (short)0 : agent->WorldIndex,
            contentId,
            characterPointerResolved,
            preservePreviousPatch ? _lastStageProbe.ClientSelectDataOriginalTerritoryType : originalTerritoryType,
            preservePreviousPatch ? _lastStageProbe.ClientSelectDataOriginalZoneId : originalZoneId,
            preservePreviousPatch ? _lastStageProbe.ClientSelectDataPatchedTerritoryType : patchedTerritoryType,
            preservePreviousPatch ? _lastStageProbe.ClientSelectDataPatchedZoneId : patchedZoneId,
            patchAttempted || preservePreviousPatch,
            patchApplied || (preservePreviousPatch && _lastStageProbe.ClientSelectDataPatchApplied),
            preservePreviousPatch && _lastStageProbe.ClientSelectDataRestoreApplied,
            clientSelectVerdict,
            _lastLoginPosition,
            lobbyProbe.ResolvedPosition,
            lobbyProbe.Changed,
            _lastOverrideLoginPosition,
            lobbyProbe.PositionVerdict,
            lobbyProbe.Available,
            lobbyProbe.MatchCount,
            lobbyProbe.Candidate0.RowId,
            lobbyProbe.Candidate0.Type,
            lobbyProbe.Candidate0.Param,
            lobbyProbe.Candidate0.Link,
            lobbyProbe.ResolvedReason,
            layoutRequested,
            string.IsNullOrWhiteSpace(_loadedPrefetchBg) ? "none" : _loadedPrefetchBg,
            _loadedPrefetchLevelId,
            _loadedPrefetchLayerEntryType,
            _prefetchOwner.ToString(),
            layoutVerdict,
            _configuration.TitleBackgroundOverrideEnabled,
            false,
            _configuration.TitleBackgroundOverrideEnabled ? "conflict-disabled-by-final-mode" : "disabled-for-final-composition",
            CharaSelectSceneCompositionPlanner.BuildStageProbeRouteVerdict(_configuration),
            CharaSelectSceneCompositionPlanner.BuildNextAction(_configuration, _lastSceneObservation));
        _configuration.CharaSelectSceneStageStrategyLastResult = _lastStageProbe.RouteVerdict;
        _configuration.CharaSelectSceneStageStrategyLastReason = _lastStageProbe.Reason;
    }

    private CharaSelectLobbyStageProbe BuildLobbyStageProbe(ushort territoryTypeId)
    {
        if (territoryTypeId == 0)
        {
            return new CharaSelectLobbyStageProbe(false, 0, default, 0, false, "not-available", "territory-zero");
        }

        try
        {
            var lobbySheet = _dataManager.GetExcelSheet<Lobby>();
            var matches = lobbySheet
                .Select(lobby => new CharaSelectLobbyCandidate(lobby.RowId, lobby.TYPE, lobby.PARAM, lobby.LINK))
                .Where(candidate => candidate.Param == territoryTypeId || candidate.Link == territoryTypeId)
                .OrderBy(candidate => candidate.RowId)
                .Take(8)
                .ToList();
            var fallback = _lastLoginPosition;
            var resolved = CharaSelectLobbyPositionResolver.ResolveByTerritory(matches, territoryTypeId, fallback);
            var changed = resolved != fallback;
            return new CharaSelectLobbyStageProbe(
                true,
                matches.Count,
                matches.Count == 0 ? default : matches[0],
                resolved,
                changed,
                changed ? "changed" : "not-changed",
                matches.Count == 0 ? "no-lobby-row-match" : "lobby-row-candidate-found");
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to build CharaSelect lobby stage probe.");
            return new CharaSelectLobbyStageProbe(false, 0, default, _lastLoginPosition, false, "not-available", "error");
        }
    }

    private void RefreshCharaSelectDisplay()
    {
        if (_clientState.IsLoggedIn)
        {
            return;
        }

        try
        {
            var agent = AgentLobby.Instance();
            if (agent == null || agent->IsLoggedIn)
            {
                return;
            }

            var index = agent->SelectedCharacterIndex;
            var normalizedIndex = index >= 100 ? index - 100 : index;
            if (normalizedIndex < 0 || normalizedIndex >= 40)
            {
                return;
            }

            MarkCompositionRouteRefreshDisplayCalled();
            agent->UpdateCharaSelectDisplay((sbyte)index, true);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to refresh CharaSelect display.");
        }
    }

    private void MarkCompositionRouteUpdateDisplayDetourCalled()
    {
        if (_configuration.CharaSelectSceneCompositionEnabled)
        {
            _lastLegacyCompositionRouteSnapshot = _lastLegacyCompositionRouteSnapshot with
            {
                LegacyEnabled = true,
                UpdateDisplayDetourCalled = true,
                ApplyRoute = CharaSelectSceneCompositionPlanner.ResolveCompositionCaller(_configuration),
                ProfileId = _configuration.CharaSelectSceneProfileId,
                CameraSource = "LobbyCamera",
            };
        }

        if (CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            _lastTitleBackgroundBridgeRouteSnapshot = _lastTitleBackgroundBridgeRouteSnapshot with
            {
                BridgeEnabled = true,
                UpdateDisplayDetourCalled = true,
                ApplyRoute = "title-background-bridge",
                ProfileId = _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
                CameraSource = "Adapter",
            };
        }
    }

    private void MarkCompositionRouteClientSelectDataPatched(bool legacyRoute, bool titleBackgroundBridge)
    {
        if (legacyRoute)
        {
            _lastLegacyCompositionRouteSnapshot = _lastLegacyCompositionRouteSnapshot with
            {
                ClientSelectDataPatched = true,
                ApplyRoute = CharaSelectSceneCompositionPlanner.ClientSelectDataTerritoryPatchRoute,
                CameraSource = "LobbyCamera",
            };
        }

        if (titleBackgroundBridge)
        {
            _lastTitleBackgroundBridgeRouteSnapshot = _lastTitleBackgroundBridgeRouteSnapshot with
            {
                ClientSelectDataPatched = true,
                ApplyRoute = "title-background-bridge/client-select-data",
                CameraSource = "Adapter",
            };
        }
    }

    private void MarkCompositionRouteRefreshDisplayCalled()
    {
        if (_configuration.CharaSelectSceneCompositionEnabled)
        {
            _lastLegacyCompositionRouteSnapshot = _lastLegacyCompositionRouteSnapshot with
            {
                RefreshDisplayCalled = true,
            };
        }

        if (CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            _lastTitleBackgroundBridgeRouteSnapshot = _lastTitleBackgroundBridgeRouteSnapshot with
            {
                RefreshDisplayCalled = true,
            };
        }
    }

    private string GetTerritoryDisplayName(ushort territoryTypeId)
    {
        try
        {
            var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
            var territory = territorySheet.GetRow(territoryTypeId);
            var name = territory.PlaceName.Value.Name.ToString();
            return string.IsNullOrWhiteSpace(name)
                ? $"TerritoryTypeId: {territoryTypeId}"
                : $"{name} ({territoryTypeId})";
        }
        catch
        {
            return $"不明 (ID: {territoryTypeId})";
        }
    }

    private static float SanitizeCoordinate(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, -100000f, 100000f) : 0f;
    }

    private ushort ResolveCurrentTerritoryTypeId()
    {
        if (_clientState.IsLoggedIn)
        {
            var territoryTypeId = _clientState.TerritoryType;
            return territoryTypeId > ushort.MaxValue
                ? (ushort)0
                : NormalizeHousingTerritory((ushort)territoryTypeId);
        }

        return NormalizeHousingTerritory(_currentEntry?.TerritoryTypeId ?? 0);
    }

    private void ClearCharaSelectPointers()
    {
        ClearReplayState();
        _currentEntry = null;
        _lastVoiceDiagnosticLines = [];
    }

    private void TryUpdateLastDataCenterName()
    {
        if (!_configuration.CharaSelectShowLastDataCenterNameEnabled)
        {
            return;
        }

        _dataCenterNamePollFrame = (_dataCenterNamePollFrame + 1) % 300;
        if (_dataCenterNamePollFrame != 0)
        {
            return;
        }

        try
        {
            var world = _objectTable.LocalPlayer?.CurrentWorld.Value;
            var dataCenterName = world?.DataCenter.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(dataCenterName)
                || string.Equals(_configuration.CharaSelectLastDataCenterName, dataCenterName, StringComparison.Ordinal))
            {
                return;
            }

            _configuration.CharaSelectLastDataCenterName = dataCenterName;
            _configuration.Save();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to update CharaSelect last data center name.");
        }
    }

}

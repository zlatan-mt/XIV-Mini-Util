// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.NativeHooks.cs
// Description: CharaSelect の native hook と detour を管理する
// Reason: unsafe hook lifecycle を本体状態管理から分離するため
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    private void InitializeHooks()
    {
        try
        {
            _updateCharaSelectDisplayHook = _gameInteropProvider.HookFromAddress<AgentLobby.Delegates.UpdateCharaSelectDisplay>(
                AgentLobby.MemberFunctionPointers.UpdateCharaSelectDisplay,
                UpdateCharaSelectDisplayDetour);
            _executeEmoteHook = _gameInteropProvider.HookFromAddress<EmoteManager.Delegates.ExecuteEmote>(
                EmoteManager.MemberFunctionPointers.ExecuteEmote,
                ExecuteEmoteDetour);
            _openLoginWaitDialogHook = _gameInteropProvider.HookFromAddress<AgentLobby.Delegates.OpenLoginWaitDialog>(
                AgentLobby.MemberFunctionPointers.OpenLoginWaitDialog,
                OpenLoginWaitDialogDetour);
            _updateLoginPositionHook = _gameInteropProvider.HookFromAddress<AgentLobby.Delegates.UpdateLoginPosition>(
                AgentLobby.MemberFunctionPointers.UpdateLoginPosition,
                UpdateLoginPositionDetour);

            _updateCharaSelectDisplayHook.Enable();
            _executeEmoteHook.Enable();
            _updateLoginPositionHook.Enable();

            if (_configuration.CharaSelectPreloadTerritoryEnabled)
            {
                _openLoginWaitDialogHook.Enable();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize CharaSelect hooks.");
            DisposeHook(_updateLoginPositionHook);
            DisposeHook(_openLoginWaitDialogHook);
            DisposeHook(_executeEmoteHook);
            DisposeHook(_updateCharaSelectDisplayHook);
            _updateLoginPositionHook = null;
            _openLoginWaitDialogHook = null;
            _executeEmoteHook = null;
            _updateCharaSelectDisplayHook = null;
            _configuration.CharaSelectEmoteEnabled = false;
            _configuration.CharaSelectPreloadTerritoryEnabled = false;
            _configuration.Save();
        }
    }

    private bool UpdateCharaSelectDisplayDetour(AgentLobby* agent, sbyte index, bool a2)
    {
        var result = false;
        var originalClientSelectData = default(ClientSelectData);
        var patchedDisplayData = false;
        var patchedWorldIndex = (short)0;
        var patchedNormalizedIndex = -1;
        var patchedContentId = 0UL;

        try
        {
            MarkCompositionRouteUpdateDisplayDetourCalled();
            patchedDisplayData = TryPatchOverrideDisplayData(
                agent,
                index,
                out patchedWorldIndex,
                out patchedNormalizedIndex,
                out patchedContentId,
                out originalClientSelectData);
            result = _updateCharaSelectDisplayHook?.Original(agent, index, a2) ?? false;
        }
        finally
        {
            if (patchedDisplayData)
            {
                RestorePatchedOverrideDisplayData(
                    agent,
                    patchedWorldIndex,
                    patchedNormalizedIndex,
                    patchedContentId,
                    originalClientSelectData);
            }
        }

        try
        {
            UpdateCurrentEntry(agent, index);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to update CharaSelect character state.");
        }

        return result;
    }

    private bool TryPatchOverrideDisplayData(
        AgentLobby* agent,
        sbyte index,
        out short worldIndex,
        out int normalizedIndex,
        out ulong contentId,
        out ClientSelectData originalClientSelectData)
    {
        worldIndex = 0;
        normalizedIndex = -1;
        contentId = 0;
        originalClientSelectData = default;

        var hasLegacyRoute = CharaSelectSceneCompositionPlanner.UsesClientSelectDataTerritoryPatch(_configuration);
        var hasTitleBackgroundBridge = CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration);
        if (agent == null
            || _clientState.IsLoggedIn
            || (!hasLegacyRoute && !hasTitleBackgroundBridge)
            || !TryResolveBridgeTerritoryTypeId(hasTitleBackgroundBridge, out var territoryTypeId))
        {
            return false;
        }

        normalizedIndex = index >= 100 ? index - 100 : index;
        if (normalizedIndex < 0)
        {
            return false;
        }

        worldIndex = agent->WorldIndex;
        var entry = agent->LobbyData.GetCharacterEntryByIndex(0, worldIndex, normalizedIndex);
        if (entry == null || entry->ContentId == 0)
        {
            CaptureStageProbe(
                agent,
                index,
                normalizedIndex,
                false,
                entry == null ? 0 : entry->ContentId,
                "entry-null",
                patchAttempted: true);
            return false;
        }

        if (territoryTypeId == 0)
        {
            return false;
        }

        var resolvedLevel = ResolveOverrideLevel(territoryTypeId, CharaSelectPrefetchOwner.OverrideDisplay);
        var patchedZoneId = resolvedLevel.IsValid && resolvedLevel.RowId <= ushort.MaxValue
            ? (ushort)resolvedLevel.RowId
            : (ushort)0;
        CaptureStageProbe(
            agent,
            index,
            normalizedIndex,
            characterPointerResolved: CharaSelectCharacterList.GetCurrentCharacter() != null,
            contentId: entry->ContentId,
            reason: "client-select-data-probe",
            originalTerritoryType: entry->ClientSelectData.TerritoryType,
            originalZoneId: entry->ClientSelectData.ZoneId,
            patchedTerritoryType: territoryTypeId,
            patchedZoneId: patchedZoneId,
            patchAttempted: true);
        MarkTitleBackgroundBridge(
            invoked: hasTitleBackgroundBridge,
            reason: "client-select-data-probe",
            appliedStage: false,
            appliedCharacter: CharaSelectCharacterList.GetCurrentCharacter() != null);
        if (entry->ClientSelectData.TerritoryType == territoryTypeId
            && (patchedZoneId == 0 || entry->ClientSelectData.ZoneId == patchedZoneId))
        {
            CaptureStageProbe(
                agent,
                index,
                normalizedIndex,
                characterPointerResolved: CharaSelectCharacterList.GetCurrentCharacter() != null,
                contentId: entry->ContentId,
                reason: "client-select-data-not-needed",
                originalTerritoryType: entry->ClientSelectData.TerritoryType,
                originalZoneId: entry->ClientSelectData.ZoneId,
                patchedTerritoryType: territoryTypeId,
                patchedZoneId: patchedZoneId,
                patchAttempted: true,
                patchApplied: false);
            MarkTitleBackgroundBridge(
                invoked: hasTitleBackgroundBridge,
                reason: "client-select-data-not-needed",
                appliedStage: true,
                appliedCharacter: CharaSelectCharacterList.GetCurrentCharacter() != null);
            return false;
        }

        originalClientSelectData = entry->ClientSelectData;
        contentId = entry->ContentId;
        entry->ClientSelectData.TerritoryType = territoryTypeId;
        if (patchedZoneId != 0)
        {
            entry->ClientSelectData.ZoneId = patchedZoneId;
        }

        CaptureStageProbe(
            agent,
            index,
            normalizedIndex,
            characterPointerResolved: CharaSelectCharacterList.GetCurrentCharacter() != null,
            contentId: entry->ContentId,
            reason: "client-select-data-patched",
            originalTerritoryType: originalClientSelectData.TerritoryType,
            originalZoneId: originalClientSelectData.ZoneId,
            patchedTerritoryType: entry->ClientSelectData.TerritoryType,
            patchedZoneId: entry->ClientSelectData.ZoneId,
            patchAttempted: true,
            patchApplied: true);
        MarkCompositionRouteClientSelectDataPatched(hasLegacyRoute, hasTitleBackgroundBridge);
        MarkTitleBackgroundBridge(
            invoked: hasTitleBackgroundBridge,
            reason: CharaSelectSceneCompositionPlanner.TitleBackgroundCharacterVisibilityReason,
            appliedStage: true,
            appliedCharacter: CharaSelectCharacterList.GetCurrentCharacter() != null);
        return true;
    }

    private bool TryResolveBridgeTerritoryTypeId(bool titleBackgroundBridge, out ushort territoryTypeId)
    {
        territoryTypeId = 0;
        if (!titleBackgroundBridge)
        {
            if (!_configuration.CharaSelectOverrideTerritoryEnabled
                || _configuration.CharaSelectOverrideTerritoryTypeId == 0)
            {
                return false;
            }

            territoryTypeId = NormalizeHousingTerritory(_configuration.CharaSelectOverrideTerritoryTypeId);
            return territoryTypeId != 0;
        }

        var raw = _configuration.TitleBackgroundLayoutTerritoryTypeId != 0
            ? _configuration.TitleBackgroundLayoutTerritoryTypeId
            : _configuration.TitleBackgroundTerritoryTypeId;
        if (raw == 0 || raw > ushort.MaxValue)
        {
            MarkTitleBackgroundBridge(
                invoked: true,
                reason: "title-background-territory-missing",
                appliedStage: false,
                appliedCharacter: _currentEntry != null && _currentEntry.Character != null);
            return false;
        }

        territoryTypeId = NormalizeHousingTerritory((ushort)raw);
        return territoryTypeId != 0;
    }

    private void RestorePatchedOverrideDisplayData(
        AgentLobby* agent,
        short worldIndex,
        int normalizedIndex,
        ulong contentId,
        ClientSelectData originalClientSelectData)
    {
        if (agent == null || normalizedIndex < 0 || contentId == 0)
        {
            return;
        }

        var entry = agent->LobbyData.GetCharacterEntryByIndex(0, worldIndex, normalizedIndex);
        if (entry == null || entry->ContentId != contentId)
        {
            return;
        }

        entry->ClientSelectData = originalClientSelectData;
        _lastStageProbe = _lastStageProbe with
        {
            ClientSelectDataRestoreApplied = true,
            ClientSelectDataVerdict = _lastStageProbe.ClientSelectDataPatchApplied
                ? "patch-applied"
                : _lastStageProbe.ClientSelectDataVerdict,
        };
    }

    private bool ExecuteEmoteDetour(EmoteManager* manager, ushort emoteId, EmoteController.PlayEmoteOption* playEmoteOption)
    {
        var result = _executeEmoteHook?.Original(manager, emoteId, playEmoteOption) ?? false;

        try
        {
            if (result && _isRecordingEmote && _clientState.IsLoggedIn)
            {
                SaveLoggedInVoiceId();
                SaveExecutedEmote(emoteId);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to record CharaSelect emote.");
        }

        return result;
    }

    private void OpenLoginWaitDialogDetour(AgentLobby* agent, int position)
    {
        var resolvedPosition = ResolveOverrideLoginPosition(position);
        _openLoginWaitDialogHook?.Original(agent, resolvedPosition);

        try
        {
            if (_configuration.CharaSelectPreloadTerritoryEnabled)
            {
                PreloadLoginTerritory();
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to preload CharaSelect login territory.");
        }
    }

    private void UpdateLoginPositionDetour(AgentLobby* agent, int newPosition)
    {
        var resolvedPosition = ResolveOverrideLoginPosition(newPosition);
        _updateLoginPositionHook?.Original(agent, resolvedPosition);
    }

    private int ResolveOverrideLoginPosition(int fallbackPosition)
    {
        _lastLoginPosition = fallbackPosition;
        _lastOverrideLoginPosition = 0;

        if (_clientState.IsLoggedIn
            || !_configuration.CharaSelectOverrideTerritoryEnabled
            || _configuration.CharaSelectOverrideTerritoryTypeId == 0)
        {
            return fallbackPosition;
        }

        try
        {
            var territoryTypeId = NormalizeHousingTerritory(_configuration.CharaSelectOverrideTerritoryTypeId);
            var lobbySheet = _dataManager.GetExcelSheet<Lobby>();
            var candidates = lobbySheet.Select(lobby => new CharaSelectLobbyCandidate(
                lobby.RowId,
                lobby.TYPE,
                lobby.PARAM,
                lobby.LINK));
            var resolvedPosition = CharaSelectLobbyPositionResolver.ResolveByTerritory(
                candidates,
                territoryTypeId,
                fallbackPosition);

            if (resolvedPosition != fallbackPosition)
            {
                _lastOverrideLoginPosition = resolvedPosition;
            }

            return resolvedPosition;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to resolve CharaSelect override login position.");
            return fallbackPosition;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed)
        {
            return;
        }

        if (_clientState.IsLoggedIn)
        {
            ClearCharaSelectPointers();
            TryUpdateLastDataCenterName();
            return;
        }

        ProcessDelayedReplay();

        _frameworkPollFrame = (_frameworkPollFrame + 1) % 10;
        if (_frameworkPollFrame != 0)
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
            if (index < 40)
            {
                UpdateCurrentEntry(agent, (sbyte)index, scheduleDelayedReplay: true);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to poll CharaSelect character state.");
        }
    }

    private void DisposeHook<T>(Hook<T>? hook)
        where T : Delegate
    {
        try
        {
            hook?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to dispose CharaSelect hook.");
        }
    }
}


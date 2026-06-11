// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.Prefetch.cs
// Description: CharaSelect の territory prefetch と level 解決を管理する
// Reason: layout prefetch logic を本体状態管理から分離するため
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    private void PreloadLoginTerritory()
    {
        var territoryTypeId = NormalizeHousingTerritory(_currentEntry?.TerritoryTypeId ?? 0);
        TryLoadPrefetchLayout(territoryTypeId, CharaSelectPrefetchOwner.LoginWait);
    }

    private void ApplyOverrideTerritoryPrefetch()
    {
        if (CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            ApplyTitleBackgroundBridgePrefetch();
            return;
        }

        if (_configuration.CharaSelectSceneCompositionEnabled
            && !CharaSelectSceneCompositionPlanner.UsesClientSelectDataTerritoryPatch(_configuration))
        {
            return;
        }

        if (!_configuration.CharaSelectOverrideTerritoryEnabled)
        {
            return;
        }

        if (_prefetchOwner == CharaSelectPrefetchOwner.LoginWait)
        {
            return;
        }

        TryLoadPrefetchLayout(
            NormalizeHousingTerritory(_configuration.CharaSelectOverrideTerritoryTypeId),
            CharaSelectPrefetchOwner.OverrideDisplay);
    }

    private void ApplyTitleBackgroundBridgePrefetch()
    {
        if (!TryResolveBridgeTerritoryTypeId(titleBackgroundBridge: true, out var territoryTypeId))
        {
            return;
        }

        TryLoadPrefetchLayout(territoryTypeId, CharaSelectPrefetchOwner.OverrideDisplay);
    }

    private void MarkTitleBackgroundBridge(
        bool invoked,
        string reason,
        bool appliedStage,
        bool appliedCharacter)
    {
        if (!CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeEnabled(_configuration))
        {
            return;
        }

        var required = CharaSelectSceneCompositionPlanner.IsTitleBackgroundCharacterCompositionBridgeRequired(_configuration);
        var stage = appliedStage || _lastTitleBackgroundBridgeSnapshot.AppliedStage;
        var character = appliedCharacter || _lastTitleBackgroundBridgeSnapshot.AppliedCharacter;
        _lastTitleBackgroundBridgeSnapshot = new TitleBackgroundCharacterCompositionBridgeSnapshot(
            true,
            required,
            invoked || _lastTitleBackgroundBridgeSnapshot.Invoked,
            string.IsNullOrWhiteSpace(reason) ? "none" : reason,
            CharaSelectSceneCompositionPlanner.TitleBackgroundIntegratedCaller,
            stage,
            character,
            _lastTitleBackgroundBridgeSnapshot.AppliedCamera,
            required,
            stage && character);
    }

    private void TryLoadPrefetchLayout(ushort territoryTypeId, CharaSelectPrefetchOwner owner)
    {
        if (territoryTypeId == 0)
        {
            return;
        }

        try
        {
            var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
            var territory = territorySheet.GetRow(territoryTypeId);
            var bg = territory.Bg.ToString();
            if (string.IsNullOrWhiteSpace(bg))
            {
                return;
            }

            var resolvedLevel = ResolveOverrideLevel(territoryTypeId, owner);
            if (_prefetchOwner == owner
                && _loadedPrefetchTerritoryTypeId == territoryTypeId
                && string.Equals(_loadedPrefetchBg, bg, StringComparison.Ordinal)
                && _loadedPrefetchLevelId == resolvedLevel.RowId
                && _loadedPrefetchLayerEntryType == resolvedLevel.Type)
            {
                return;
            }

            var layoutWorld = LayoutWorld.Instance();
            TryUnloadPrefetchLayout();
            layoutWorld->LoadPrefetchLayout(0, bg, resolvedLevel.Type, resolvedLevel.RowId, territoryTypeId, null, 0);
            _prefetchOwner = owner;
            _loadedPrefetchTerritoryTypeId = territoryTypeId;
            _loadedPrefetchBg = bg;
            _loadedPrefetchLevelId = resolvedLevel.RowId;
            _loadedPrefetchLayerEntryType = resolvedLevel.Type;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load CharaSelect prefetch layout.");
        }
    }

    private CharaSelectResolvedLevel ResolveOverrideLevel(ushort territoryTypeId, CharaSelectPrefetchOwner owner)
    {
        if (owner != CharaSelectPrefetchOwner.OverrideDisplay || !_configuration.CharaSelectOverridePositionEnabled)
        {
            return default;
        }

        try
        {
            var levelSheet = _dataManager.GetExcelSheet<Level>();
            var candidates = levelSheet.Select(level => new CharaSelectLevelCandidate(
                level.RowId,
                level.Territory.RowId > ushort.MaxValue ? (ushort)0 : (ushort)level.Territory.RowId,
                level.Type,
                level.X,
                level.Y,
                level.Z));

            return CharaSelectLevelResolver.ResolveNearest(
                candidates,
                territoryTypeId,
                _configuration.CharaSelectOverridePositionX,
                _configuration.CharaSelectOverridePositionY,
                _configuration.CharaSelectOverridePositionZ);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to resolve CharaSelect override level.");
            return default;
        }
    }

    private static ushort NormalizeHousingTerritory(ushort territoryTypeId)
    {
        return territoryTypeId switch
        {
            282 or 384 or 608 or 609 => 339,
            283 or 385 or 610 or 611 => 340,
            284 or 386 or 612 or 613 => 341,
            649 or 650 or 651 or 652 => 641,
            980 or 981 or 982 or 983 => 979,
            _ => territoryTypeId,
        };
    }

    private bool TryGetEmote(uint emoteId, out Emote emote)
    {
        try
        {
            var emoteSheet = _dataManager.GetExcelSheet<Emote>();
            emote = emoteSheet.GetRow(emoteId);
            return true;
        }
        catch
        {
            emote = default;
            return false;
        }
    }

    private static ushort GetTimelineRowId(Emote emote, int index)
    {
        if (index < 0 || index >= emote.ActionTimeline.Count)
        {
            return 0;
        }

        var rowId = emote.ActionTimeline[index].RowId;
        return rowId > ushort.MaxValue ? (ushort)0 : (ushort)rowId;
    }

    private void TryUnloadPrefetchLayout(CharaSelectPrefetchOwner? owner = null)
    {
        if (_prefetchOwner == CharaSelectPrefetchOwner.None)
        {
            return;
        }

        if (owner.HasValue && _prefetchOwner != owner.Value)
        {
            return;
        }

        try
        {
            LayoutWorld.UnloadPrefetchLayout();
            _prefetchOwner = CharaSelectPrefetchOwner.None;
            _loadedPrefetchTerritoryTypeId = 0;
            _loadedPrefetchBg = string.Empty;
            _loadedPrefetchLevelId = 0;
            _loadedPrefetchLayerEntryType = 0;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to unload CharaSelect prefetch layout.");
        }
    }
}


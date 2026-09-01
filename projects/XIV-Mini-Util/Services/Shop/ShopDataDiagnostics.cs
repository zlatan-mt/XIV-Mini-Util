// Path: projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
// Description: ショップ検索時の詳細ログ出力を担当する
// Reason: ShopDataCacheから詳細ログ責務を分離し保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Services/ShopDataModels.cs
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using XivMiniUtil;

namespace XivMiniUtil.Services.Shop;

internal sealed class ShopDataDiagnostics
{
    private const string CategorySearch = "Search";
    private const string CategoryMissing = "Missing";
    private const string CategoryExcluded = "ExcludedNpc";

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly HashSet<uint> _loggedMissingItems = new();

    public ShopDataDiagnostics(
        IDataManager dataManager,
        IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;
    }

    public void Reset()
    {
        _loggedMissingItems.Clear();
    }

    public void LogSearchDiagnostics(
        uint itemId,
        bool isInitialized,
        IReadOnlyList<ShopLocationInfo> locations,
        string itemName,
        Dictionary<uint, List<NpcShopInfo>> gilShopNpcInfos,
        Func<NpcShopInfo, bool> isValidLocation)
    {
        if (itemId == 0 || !isInitialized)
        {
            return;
        }

        LogInfo(CategorySearch, $"検索診断: ItemId={itemId} Name={itemName} 検出数={locations.Count}");

        // 検出されたNPC情報を出力
        foreach (var loc in locations.Take(5))
        {
            LogInfo(CategorySearch, $"  検出: {loc.AreaName} / {loc.NpcName} ({loc.MapX:0.0}, {loc.MapY:0.0}) ShopId={loc.ShopId}");
        }

        // GilShopItem内でこのアイテムを持つ全ショップを調査（位置情報なしのNPCも含めて）
        var shopHits = new List<(uint ShopId, int NpcCount, int ValidCount)>();
        ScanGilShopItemHits(
            itemId,
            gilShopNpcInfos,
            true,
            (shopId, list) =>
            {
                var npcCount = list?.Count ?? 0;
                var validCount = list?.Count(isValidLocation) ?? 0;
                shopHits.Add((shopId, npcCount, validCount));

                // 位置情報なしのNPCがある場合は詳細を出力
                if (list != null && npcCount > validCount)
                {
                    foreach (var npc in list.Where(n => !isValidLocation(n)))
                    {
                        LogWarning(CategoryExcluded, $"  位置情報なしNPC: {npc.NpcName} (ID:{npc.NpcId}) ShopId={shopId} Territory={npc.TerritoryTypeId} Area={npc.AreaName}");
                    }
                }
            });

        if (shopHits.Count > 0)
        {
            LogInfo(CategorySearch, $"GilShopヒット: {string.Join(", ", shopHits.Select(h => $"Shop{h.ShopId}(NPC:{h.NpcCount}/Valid:{h.ValidCount})"))}");
        }
    }

    public void LogMissingItemDiagnostics(
        uint itemId,
        bool isInitialized,
        Dictionary<uint, List<NpcShopInfo>> gilShopNpcInfos,
        Dictionary<uint, List<NpcShopInfo>> specialShopNpcInfos,
        Func<NpcShopInfo, bool> isValidLocation)
    {
        if (itemId == 0 || !isInitialized)
        {
            return;
        }

        if (!_loggedMissingItems.Add(itemId))
        {
            return;
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        var itemName = string.Empty;
        if (itemSheet != null)
        {
            try
            {
                var row = itemSheet.GetRow(itemId);
                itemName = row.RowId != 0 ? row.Name.ToString() : string.Empty;
            }
            catch
            {
                itemName = string.Empty;
            }
        }

        LogWarning(CategoryMissing, $"未検出アイテム調査: ItemId={itemId} Name={itemName}");

        // GilShopItem内の出現を調査
        var gilLogged = 0;
        var gilHitCount = ScanGilShopItemHits(
            itemId,
            gilShopNpcInfos,
            false,
            (shopId, list) =>
            {
                if (gilLogged >= 5)
                {
                    return;
                }

                var npcInfoCount = list?.Count ?? 0;
                var validLocationCount = list?.Count(isValidLocation) ?? 0;
                LogWarning(CategoryMissing, $"GilShopItemヒット: ShopId={shopId} NpcCount={npcInfoCount} ValidLocation={validLocationCount}");

                // NPC詳細情報を出力
                if (list != null)
                {
                    foreach (var npc in list)
                    {
                        LogWarning(CategoryMissing, $"  NPC: {npc.NpcName} (ID:{npc.NpcId}) @ {npc.AreaName} (Territory:{npc.TerritoryTypeId}, Map:{npc.MapId}, X:{npc.MapX}, Y:{npc.MapY})");
                    }
                }

                gilLogged++;
            });

        LogWarning(CategoryMissing, $"GilShopItemヒット総数: {gilHitCount}");

        // SpecialShop内の出現を調査
        var specialLogged = 0;
        var specialHitCount = ScanSpecialShopItemHits(
            itemId,
            specialShopNpcInfos,
            (shopId, list) =>
            {
                if (specialLogged >= 5)
                {
                    return;
                }

                var npcInfoCount = list?.Count ?? 0;
                var validLocationCount = list?.Count(isValidLocation) ?? 0;
                LogWarning(CategoryMissing, $"SpecialShopヒット: ShopId={shopId} NpcCount={npcInfoCount} ValidLocation={validLocationCount}");
                specialLogged++;
            });

        LogWarning(CategoryMissing, $"SpecialShopヒット総数: {specialHitCount}");
    }

    private int ScanGilShopItemHits(
        uint itemId,
        Dictionary<uint, List<NpcShopInfo>> npcInfos,
        bool breakAfterFirstMatchPerShop,
        Action<uint, List<NpcShopInfo>?> onHit)
    {
        var gilShopItemSheet = _dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet == null)
        {
            return 0;
        }

        var hitCount = 0;
        foreach (var subrowCollection in gilShopItemSheet)
        {
            var shopId = subrowCollection.RowId;
            foreach (var shopItem in subrowCollection)
            {
                if (ShopDataExtractors.GetItemIdFromGilShopItem(shopItem) != itemId)
                {
                    continue;
                }

                hitCount++;
                npcInfos.TryGetValue(shopId, out var list);
                onHit(shopId, list);

                if (breakAfterFirstMatchPerShop)
                {
                    break;
                }
            }
        }

        return hitCount;
    }

    private int ScanSpecialShopItemHits(
        uint itemId,
        Dictionary<uint, List<NpcShopInfo>> npcInfos,
        Action<uint, List<NpcShopInfo>?> onHit)
    {
        var specialShopSheet = _dataManager.GetExcelSheet<SpecialShop>();
        if (specialShopSheet == null)
        {
            return 0;
        }

        var hitCount = 0;
        foreach (var shop in specialShopSheet)
        {
            if (shop.RowId == 0)
            {
                continue;
            }

            if (!SpecialShopContainsItem(shop, itemId))
            {
                continue;
            }

            hitCount++;
            npcInfos.TryGetValue(shop.RowId, out var list);
            onHit(shop.RowId, list);
        }

        return hitCount;
    }

    private static bool SpecialShopContainsItem(SpecialShop shop, uint itemId)
    {
        for (var entryIndex = 0; entryIndex < shop.Item.Count; entryIndex++)
        {
            var entry = shop.Item[entryIndex];
            foreach (var receiveItemId in ShopDataExtractors.GetReceiveItems(entry))
            {
                if (receiveItemId == itemId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void LogInfo(string category, string message)
    {
        _pluginLog.Information($"[ShopDiag:{category}] {message}");
    }

    private void LogWarning(string category, string message)
    {
        _pluginLog.Warning($"[ShopDiag:{category}] {message}");
    }

}

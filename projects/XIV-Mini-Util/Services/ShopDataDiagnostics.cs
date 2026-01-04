// Path: projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
// Description: ショップデータ構築時の診断収集とレポート出力を担当する
// Reason: ShopDataCacheから診断責務を分離し保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Services/ShopDataModels.cs
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using XivMiniUtil;

namespace XivMiniUtil.Services;

internal sealed class ShopDataDiagnostics
{
    private const string CategorySearch = "Search";
    private const string CategoryMissing = "Missing";
    private const string CategoryReport = "Report";
    private const string CategoryExcluded = "ExcludedNpc";
    private const string CategoryUnmatched = "UnmatchedShop";

    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly ShopDataDiagnosticsWriter _writer;
    private readonly ShopDiagnosticsSummary _summary;
    private readonly List<ExcludedNpcEntry> _excludedNpcs = new();
    private readonly Dictionary<uint, UnmatchedShopEntry> _unmatchedShopItems = new();
    private readonly HashSet<uint> _loggedMissingItems = new();

    public ShopDataDiagnostics(
        IDataManager dataManager,
        IPluginLog pluginLog,
        ShopDataDiagnosticsWriter writer)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;
        _writer = writer;
        _summary = new ShopDiagnosticsSummary(pluginLog);
    }

    public int ExcludedNpcCount => _excludedNpcs.Count;

    public int UnmatchedShopCount => _unmatchedShopItems.Count;

    public void Reset()
    {
        _excludedNpcs.Clear();
        _unmatchedShopItems.Clear();
        _loggedMissingItems.Clear();
    }

    public void RecordExcludedNpc(NpcShopInfo npcInfo)
    {
        if (!_excludedNpcs.Any(e => e.NpcId == npcInfo.NpcId && e.ShopId == npcInfo.ShopId))
        {
            _excludedNpcs.Add(new ExcludedNpcEntry(npcInfo.NpcId, npcInfo.NpcName, npcInfo.ShopId, npcInfo.ShopName));
        }
    }

    public void RecordUnmatchedShopItem(uint shopId, uint itemId)
    {
        if (!_unmatchedShopItems.TryGetValue(shopId, out var entry))
        {
            entry = new UnmatchedShopEntry(shopId, new List<uint>());
            _unmatchedShopItems[shopId] = entry;
        }

        if (entry.ItemIds.Count < 3)
        {
            entry.ItemIds.Add(itemId);
        }
    }

    public string GenerateDiagnosticsReport(string outputPath, int itemCount)
    {
        LogInfo(CategoryReport, $"診断レポート生成開始: output={outputPath}");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# ショップデータ診断レポート");
        sb.AppendLine($"生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // サマリー
        sb.AppendLine("## サマリー");
        sb.AppendLine($"- 登録アイテム数: {itemCount}");
        sb.AppendLine($"- 位置情報なしNPC数: {_excludedNpcs.Count}");
        sb.AppendLine($"- NPCマッチなしショップ数: {_unmatchedShopItems.Count}");
        sb.AppendLine();

        // 位置情報なしNPC（ユニークなNPC IDでグループ化）
        sb.AppendLine("## 位置情報なしNPC一覧");
        sb.AppendLine("| NPC ID | NPC名 | ショップ数 | ショップ例 |");
        sb.AppendLine("|--------|-------|------------|-----------|");

        var groupedByNpc = _excludedNpcs
            .GroupBy(e => e.NpcId)
            .OrderBy(g => g.First().NpcName)
            .ToList();

        foreach (var group in groupedByNpc)
        {
            var first = group.First();
            var shopCount = group.Count();
            var shopExample = group.Take(2).Select(e => e.ShopName).Distinct().Take(2);
            sb.AppendLine($"| {first.NpcId} | {first.NpcName} | {shopCount} | {string.Join(", ", shopExample)} |");
        }
        sb.AppendLine();

        // NPCマッチなしショップ
        sb.AppendLine("## NPCマッチなしショップ一覧（アイテム例付き）");
        sb.AppendLine("| ショップID | アイテム例 |");
        sb.AppendLine("|------------|-----------|");

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        foreach (var entry in _unmatchedShopItems.Values.OrderBy(entry => entry.ShopId).Take(100))
        {
            var itemNames = new List<string>();
            foreach (var itemId in entry.ItemIds)
            {
                if (itemSheet != null)
                {
                    var item = itemSheet.GetRow(itemId);
                    if (item.RowId != 0)
                    {
                        itemNames.Add(item.Name.ToString());
                    }
                }
            }
            sb.AppendLine($"| {entry.ShopId} | {string.Join(", ", itemNames)} |");
        }

        if (_unmatchedShopItems.Count > 100)
        {
            sb.AppendLine($"| ... | （他 {_unmatchedShopItems.Count - 100} ショップ省略） |");
        }

        var report = sb.ToString();
        LogInfo(CategoryReport, $"診断レポート生成完了: itemCount={itemCount}, excludedNpc={_excludedNpcs.Count}, unmatchedShop={_unmatchedShopItems.Count}");
        _summary.LogSummary(itemCount, _excludedNpcs.Count, _unmatchedShopItems.Count);
        return _writer.WriteDiagnosticsReport(outputPath, report);
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

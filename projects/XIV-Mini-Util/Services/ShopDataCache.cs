// Path: projects/XIV-Mini-Util/Services/ShopDataCache.cs
// Description: ショップ販売データを起動時に読み込みキャッシュする
// Reason: 検索を高速化しゲーム中の再読み込みを避けるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ContextMenuService.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class ShopDataCache
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly Dictionary<uint, List<ShopLocationInfo>> _itemToLocations = new();
    private readonly Dictionary<uint, string> _itemNames = new();
    private readonly Dictionary<uint, string> _territoryNames = new();
    private readonly HashSet<uint> _loggedMissingItems = new();

    private Dictionary<uint, List<NpcShopInfo>> _gilShopNpcInfos = new();
    private Dictionary<uint, List<NpcShopInfo>> _specialShopNpcInfos = new();

    private Task? _initializeTask;
    private bool _isInitialized;

    private sealed record NpcShopInfo(
        uint NpcId,
        string NpcName,
        uint ShopId,
        string ShopName,
        uint TerritoryTypeId,
        string AreaName,
        string SubAreaName,
        uint MapId,
        float MapX,
        float MapY);

    public ShopDataCache(IDataManager dataManager, IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;
    }

    public bool IsInitialized => _isInitialized;

    public Task InitializeAsync()
    {
        _initializeTask ??= Task.Run(BuildCacheSafeAsync);
        return _initializeTask;
    }

    public bool HasShopData(uint itemId)
    {
        if (!_isInitialized || itemId == 0)
        {
            return false;
        }

        return _itemToLocations.ContainsKey(itemId);
    }

    public IReadOnlyList<ShopLocationInfo> GetShopLocations(uint itemId)
    {
        if (!_isInitialized || itemId == 0)
        {
            return Array.Empty<ShopLocationInfo>();
        }

        return _itemToLocations.TryGetValue(itemId, out var locations)
            ? locations
            : Array.Empty<ShopLocationInfo>();
    }

    public string GetItemName(uint itemId)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        return _itemNames.TryGetValue(itemId, out var name) ? name : string.Empty;
    }

    public string GetTerritoryName(uint territoryTypeId)
    {
        if (territoryTypeId == 0)
        {
            return string.Empty;
        }

        if (_territoryNames.TryGetValue(territoryTypeId, out var cached))
        {
            return cached;
        }

        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        if (territorySheet == null)
        {
            return string.Empty;
        }

        var row = territorySheet.GetRow(territoryTypeId);
        if (row.RowId == 0)
        {
            return string.Empty;
        }

        var name = row.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            _territoryNames[territoryTypeId] = name;
        }

        return name;
    }

    public IReadOnlyList<ShopTerritoryInfo> GetAllShopTerritories()
    {
        if (!_isInitialized)
        {
            return Array.Empty<ShopTerritoryInfo>();
        }

        var ids = new HashSet<uint>();
        CollectTerritoryIds(_gilShopNpcInfos, ids);
        CollectTerritoryIds(_specialShopNpcInfos, ids);

        return ids
            .Select(id => new ShopTerritoryInfo(id, GetTerritoryName(id)))
            .Where(info => !string.IsNullOrWhiteSpace(info.TerritoryName))
            .OrderBy(info => info.TerritoryName, StringComparer.Ordinal)
            .ToList();
    }

    public void LogMissingItemDiagnostics(uint itemId)
    {
        if (itemId == 0 || !_isInitialized)
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

        _pluginLog.Warning($"未検出アイテム調査: ItemId={itemId} Name={itemName}");

        // GilShopItem内の出現を調査
        var gilShopItemSheet = _dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet != null)
        {
            var hitCount = 0;
            var logged = 0;

            foreach (var subrowCollection in gilShopItemSheet)
            {
                var shopId = subrowCollection.RowId;
                foreach (var shopItem in subrowCollection)
                {
                    if (GetItemIdFromGilShopItem(shopItem) != itemId)
                    {
                        continue;
                    }

                    hitCount++;

                    if (logged >= 5)
                    {
                        continue;
                    }

                    var npcInfoCount = _gilShopNpcInfos.TryGetValue(shopId, out var list) ? list.Count : 0;
                    var validLocationCount = list?.Count(IsValidLocation) ?? 0;
                    _pluginLog.Warning($"GilShopItemヒット: ShopId={shopId} NpcCount={npcInfoCount} ValidLocation={validLocationCount}");

                    // NPC詳細情報を出力
                    if (list != null)
                    {
                        foreach (var npc in list)
                        {
                            _pluginLog.Warning($"  NPC: {npc.NpcName} (ID:{npc.NpcId}) @ {npc.AreaName} (Territory:{npc.TerritoryTypeId}, Map:{npc.MapId}, X:{npc.MapX}, Y:{npc.MapY})");
                        }
                    }

                    logged++;
                }
            }

            _pluginLog.Warning($"GilShopItemヒット総数: {hitCount}");
        }

        // SpecialShop内の出現を調査
        var specialShopSheet = _dataManager.GetExcelSheet<SpecialShop>();
        if (specialShopSheet != null)
        {
            var hitCount = 0;
            var logged = 0;

            foreach (var shop in specialShopSheet)
            {
                if (shop.RowId == 0)
                {
                    continue;
                }

                var matched = false;
                for (var entryIndex = 0; entryIndex < shop.Item.Count; entryIndex++)
                {
                    var entry = shop.Item[entryIndex];
                    foreach (var receiveItemId in GetReceiveItems(entry))
                    {
                        if (receiveItemId != itemId)
                        {
                            continue;
                        }

                        matched = true;
                        hitCount++;
                        break;
                    }

                    if (matched)
                    {
                        break;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                if (logged >= 5)
                {
                    continue;
                }

                var npcInfoCount = _specialShopNpcInfos.TryGetValue(shop.RowId, out var list) ? list.Count : 0;
                var validLocationCount = list?.Count(IsValidLocation) ?? 0;
                _pluginLog.Warning($"SpecialShopヒット: ShopId={shop.RowId} NpcCount={npcInfoCount} ValidLocation={validLocationCount}");
                logged++;
            }

            _pluginLog.Warning($"SpecialShopヒット総数: {hitCount}");
        }
    }

    private async Task BuildCacheSafeAsync()
    {
        try
        {
            await BuildCacheAsync();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "ショップデータの初期化に失敗しました。");
        }
    }

    private Task BuildCacheAsync()
    {
        _pluginLog.Information("ショップデータ構築開始...");

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        var gilShopSheet = _dataManager.GetExcelSheet<GilShop>();
        var specialShopSheet = _dataManager.GetExcelSheet<SpecialShop>();
        var npcBaseSheet = _dataManager.GetExcelSheet<ENpcBase>();
        var npcResidentSheet = _dataManager.GetExcelSheet<ENpcResident>();
        var levelSheet = _dataManager.GetExcelSheet<Level>();
        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = _dataManager.GetExcelSheet<Map>();

        _pluginLog.Information($"シート取得: Item={itemSheet != null}, GilShop={gilShopSheet != null}, SpecialShop={specialShopSheet != null}, ENpcBase={npcBaseSheet != null}");

        if (itemSheet == null || gilShopSheet == null || specialShopSheet == null
            || npcBaseSheet == null || npcResidentSheet == null
            || levelSheet == null || territorySheet == null || mapSheet == null)
        {
            _pluginLog.Error("ショップデータ用のシート取得に失敗しました。");
            return Task.CompletedTask;
        }

        var questSheet = _dataManager.GetExcelSheet<Quest>();

        _pluginLog.Information("NPC-Shop マッピング構築開始...");

        // Step 1: ENpcBaseを走査してNPC → Shop（GilShop + SpecialShop）のマッピングを構築
        var (npcShopInfos, npcSpecialShopInfos) = BuildNpcToShopMappings(
            npcBaseSheet, npcResidentSheet, gilShopSheet, specialShopSheet,
            levelSheet, territorySheet, mapSheet);

        _gilShopNpcInfos = npcShopInfos;
        _specialShopNpcInfos = npcSpecialShopInfos;

        _pluginLog.Information($"NPC-Shop マッピング構築完了: GilShop={npcShopInfos.Count}件, SpecialShop={npcSpecialShopInfos.Count}件");

        CacheTerritoryNames(territorySheet);

        // Step 2: GilShopItem → Item の関係を構築し、逆引きインデックスを作成
        var processedItems = 0;
        var skippedItems = 0;
        var noItemId = 0;
        var noShopId = 0;
        var noNpcMatch = 0;
        var totalShopItems = 0;

        // SubrowExcelSheetを直接使用
        var gilShopItemSheet = _dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet == null)
        {
            _pluginLog.Error("GilShopItemシートの取得に失敗しました。");
            return Task.CompletedTask;
        }

        _pluginLog.Information($"GilShopItemシート取得成功");

        // SubrowExcelSheetを親行ごとに走査
        var loggedFirstItem = false;
        foreach (var subrowCollection in gilShopItemSheet)
        {
            var shopId = subrowCollection.RowId;

            // 最初のショップの情報をログ出力
            if (!loggedFirstItem)
            {
                _pluginLog.Information($"SubrowCollection型: {subrowCollection.GetType().FullName}");
                _pluginLog.Information($"最初のショップID: {shopId}");
            }

            foreach (var shopItem in subrowCollection)
            {
                totalShopItems++;

                // 最初のアイテムの型情報をログ出力
                if (!loggedFirstItem)
                {
                    var itemProps = shopItem.GetType().GetProperties()
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                        .ToArray();
                    _pluginLog.Information($"GilShopItem型: {shopItem.GetType().FullName}");
                    _pluginLog.Information($"GilShopItemプロパティ: {string.Join(", ", itemProps)}");
                    loggedFirstItem = true;
                }

                // Itemプロパティから直接アイテムIDを取得
                var itemId = GetItemIdFromGilShopItem(shopItem);

                if (itemId == 0)
                {
                    noItemId++;
                    skippedItems++;
                    continue;
                }

                if (shopId == 0)
                {
                    noShopId++;
                    skippedItems++;
                    continue;
                }

                // このショップを持つNPCを検索
                if (!npcShopInfos.TryGetValue(shopId, out var npcInfoList) || npcInfoList.Count == 0)
                {
                    noNpcMatch++;
                    skippedItems++;
                    continue;
                }

                var itemRow = itemSheet.GetRow(itemId);
                if (itemRow.RowId == 0)
                {
                    skippedItems++;
                    continue;
                }

                var itemName = itemRow.Name.ToString();
                if (!_itemNames.ContainsKey(itemId))
                {
                    _itemNames[itemId] = itemName;
                }

                var price = GetPriceFromGilShopItem(shopItem);
                var conditionNote = GetConditionFromGilShopItem(shopItem, questSheet);

                if (!_itemToLocations.TryGetValue(itemId, out var locations))
                {
                    locations = new List<ShopLocationInfo>();
                    _itemToLocations[itemId] = locations;
                }

                // 各NPCの販売場所を追加（位置情報があるもののみ）
                foreach (var npcInfo in npcInfoList)
                {
                    // 位置情報がないNPCはスキップ（期間限定イベントNPCなど）
                    if (!IsValidLocation(npcInfo))
                    {
                        continue;
                    }

                    // 重複チェック（同じショップ・同じNPCは追加しない）
                    if (locations.Any(existing =>
                        existing.ShopId == npcInfo.ShopId &&
                        existing.NpcName == npcInfo.NpcName &&
                        existing.TerritoryTypeId == npcInfo.TerritoryTypeId))
                    {
                        continue;
                    }

                    locations.Add(new ShopLocationInfo(
                        npcInfo.ShopId,
                        npcInfo.ShopName,
                        npcInfo.NpcName,
                        npcInfo.TerritoryTypeId,
                        npcInfo.AreaName,
                        npcInfo.SubAreaName,
                        npcInfo.MapId,
                        npcInfo.MapX,
                        npcInfo.MapY,
                        price,
                        conditionNote));

                    processedItems++;
                }
            }
        }

        _pluginLog.Information($"GilShopItem走査完了: 合計={totalShopItems}, noItemId={noItemId}, noShopId={noShopId}, noNpcMatch={noNpcMatch}");

        // Step 3: SpecialShop → Item の関係を構築し、逆引きインデックスに追加
        ProcessSpecialShops(itemSheet, npcSpecialShopInfos, ref processedItems);

        // 販売場所が0件のアイテムを削除（位置情報なしのNPCのみで販売されているアイテム）
        var emptyItems = _itemToLocations.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var itemId in emptyItems)
        {
            _itemToLocations.Remove(itemId);
        }

        _pluginLog.Information($"ショップデータ初期化完了: アイテム {_itemToLocations.Count}件 / 販売場所 {processedItems}件 / スキップ {skippedItems}件 / 位置不明除外 {emptyItems.Count}件");
        return Task.CompletedTask;
    }

    private void ProcessSpecialShops(
        ExcelSheet<Item> itemSheet,
        Dictionary<uint, List<NpcShopInfo>> npcSpecialShopInfos,
        ref int processedItems)
    {
        var specialShopSheet = _dataManager.GetExcelSheet<SpecialShop>();
        if (specialShopSheet == null)
        {
            _pluginLog.Warning("SpecialShopシートの取得に失敗しました。");
            return;
        }

        var specialShopItems = 0;
        var specialShopSkipped = 0;
        var loggedFirst = false;

        foreach (var shop in specialShopSheet)
        {
            if (shop.RowId == 0)
            {
                continue;
            }

            // このショップを持つNPCを検索
            if (!npcSpecialShopInfos.TryGetValue(shop.RowId, out var npcInfoList) || npcInfoList.Count == 0)
            {
                continue;
            }

            var shopName = shop.Name.ToString();
            if (string.IsNullOrEmpty(shopName))
            {
                shopName = $"特殊ショップ#{shop.RowId}";
            }

            // SpecialShopのItem配列を処理（受け取るアイテム）
            // SpecialShopは複数のアイテム交換を持つ
            for (var entryIndex = 0; entryIndex < shop.Item.Count; entryIndex++)
            {
                var entry = shop.Item[entryIndex];

                // 最初のエントリをログ出力
                if (!loggedFirst)
                {
                    var entryType = entry.GetType().FullName ?? "null";
                    var entryProps = entry.GetType().GetProperties()
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                        .ToArray();
                    _pluginLog.Information($"SpecialShopEntry型: {entryType}");
                    _pluginLog.Information($"SpecialShopEntryプロパティ: {string.Join(", ", entryProps)}");
                    loggedFirst = true;
                }

                // ReceiveItemsから受け取るアイテムを処理
                var receiveItems = GetReceiveItems(entry);
                foreach (var itemId in receiveItems)
                {
                    if (itemId == 0)
                    {
                        continue;
                    }

                    var itemRow = itemSheet.GetRow(itemId);
                    if (itemRow.RowId == 0)
                    {
                        specialShopSkipped++;
                        continue;
                    }

                    var itemName = itemRow.Name.ToString();
                    if (!_itemNames.ContainsKey(itemId))
                    {
                        _itemNames[itemId] = itemName;
                    }

                    // コスト情報を構築
                    var costNote = BuildCostNote(entry);

                    if (!_itemToLocations.TryGetValue(itemId, out var locations))
                    {
                        locations = new List<ShopLocationInfo>();
                        _itemToLocations[itemId] = locations;
                    }

                    // 各NPCの販売場所を追加（位置情報があるもののみ）
                    foreach (var npcInfo in npcInfoList)
                    {
                        if (!IsValidLocation(npcInfo))
                        {
                            continue;
                        }

                        // 重複チェック
                        if (locations.Any(existing =>
                            existing.ShopId == shop.RowId &&
                            existing.NpcName == npcInfo.NpcName &&
                            existing.TerritoryTypeId == npcInfo.TerritoryTypeId))
                        {
                            continue;
                        }

                        locations.Add(new ShopLocationInfo(
                            shop.RowId,
                            shopName,
                            npcInfo.NpcName,
                            npcInfo.TerritoryTypeId,
                            npcInfo.AreaName,
                            npcInfo.SubAreaName,
                            npcInfo.MapId,
                            npcInfo.MapX,
                            npcInfo.MapY,
                            0, // SpecialShopは通常ギル価格なし
                            costNote));

                        processedItems++;
                        specialShopItems++;
                    }
                }
            }
        }

        _pluginLog.Information($"SpecialShop走査完了: 追加={specialShopItems}件, スキップ={specialShopSkipped}件");
    }

    private static IEnumerable<uint> GetReceiveItems(SpecialShop.ItemStruct entry)
    {
        // リフレクションでReceive系プロパティを探す
        var candidates = new[] { "ItemReceive", "ReceiveItems", "OutputItem", "Item", "Receive" };
        foreach (var name in candidates)
        {
            var prop = entry.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(entry);
            if (value == null)
            {
                continue;
            }

            // コレクションの場合
            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var itemId = ExtractItemId(item);
                    if (itemId != 0)
                    {
                        yield return itemId;
                    }
                }
                yield break;
            }

            // 単一アイテムの場合
            var singleId = ExtractItemId(value);
            if (singleId != 0)
            {
                yield return singleId;
            }
        }
    }

    private static uint ExtractItemId(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        // 直接uint
        if (value is uint directId)
        {
            return directId;
        }

        // RowRef型
        var rowIdProp = value.GetType().GetProperty("RowId");
        if (rowIdProp != null)
        {
            var rowId = rowIdProp.GetValue(value);
            if (rowId is uint id)
            {
                return id;
            }
        }

        // Itemプロパティを持つ構造体
        var itemProp = value.GetType().GetProperty("Item");
        if (itemProp != null)
        {
            var itemValue = itemProp.GetValue(value);
            return ExtractItemId(itemValue);
        }

        return 0;
    }

    private static string BuildCostNote(SpecialShop.ItemStruct entry)
    {
        var costs = new List<string>();

        // リフレクションでCost系プロパティを探す
        var candidates = new[] { "ItemCost", "CostItems", "InputItem", "Cost", "CurrencyCost" };
        foreach (var name in candidates)
        {
            var prop = entry.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(entry);
            if (value is not System.Collections.IEnumerable enumerable)
            {
                continue;
            }

            foreach (var item in enumerable)
            {
                var itemId = ExtractItemId(item);
                if (itemId == 0)
                {
                    continue;
                }

                // Count/Quantity を取得
                var count = GetCount(item);
                var itemName = $"アイテム#{itemId}";

                // アイテム名を取得（可能なら）
                var itemItemProp = item?.GetType().GetProperty("Item");
                if (itemItemProp != null)
                {
                    var itemRef = itemItemProp.GetValue(item);
                    var valueNullableProp = itemRef?.GetType().GetProperty("ValueNullable");
                    if (valueNullableProp != null)
                    {
                        var actualItem = valueNullableProp.GetValue(itemRef);
                        var nameProp = actualItem?.GetType().GetProperty("Name");
                        if (nameProp != null)
                        {
                            var nameValue = nameProp.GetValue(actualItem);
                            if (nameValue != null)
                            {
                                itemName = nameValue.ToString() ?? itemName;
                            }
                        }
                    }
                }

                if (count > 0)
                {
                    costs.Add($"{itemName} x{count}");
                }
            }

            if (costs.Count > 0)
            {
                break;
            }
        }

        return costs.Count > 0 ? string.Join(", ", costs) : "条件なし";
    }

    private static int GetCount(object? item)
    {
        if (item == null)
        {
            return 0;
        }

        var countCandidates = new[] { "Count", "Quantity", "Amount" };
        foreach (var name in countCandidates)
        {
            var prop = item.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(item);
            if (value is int intCount)
            {
                return intCount;
            }

            if (value is uint uintCount)
            {
                return (int)uintCount;
            }

            if (value is ushort ushortCount)
            {
                return ushortCount;
            }
        }

        return 0;
    }

    private static uint GetItemIdFromGilShopItem(GilShopItem shopItem)
    {
        // GilShopItem.Item は RowRef<Item> 型
        var itemRef = shopItem.Item;
        return itemRef.RowId;
    }

    private static int GetPriceFromGilShopItem(GilShopItem shopItem)
    {
        // GilShopItemにはPrice系プロパティがないため、Item側のPriceを使用
        var itemRef = shopItem.Item;
        if (itemRef.RowId == 0)
        {
            return 0;
        }

        var item = itemRef.ValueNullable;
        if (item == null)
        {
            return 0;
        }

        // Item.PriceMid がNPCショップでの販売価格
        return (int)item.Value.PriceMid;
    }

    private static string GetConditionFromGilShopItem(GilShopItem shopItem, ExcelSheet<Quest>? questSheet)
    {
        // GilShopItemには直接クエスト条件がないため、状態クエストをチェック
        var stateRequired = shopItem.StateRequired;
        var patch = shopItem.Patch;

        if (stateRequired != 0)
        {
            return $"条件ID: {stateRequired}";
        }

        if (patch != 0)
        {
            return $"パッチ {patch / 100f:0.0}以降";
        }

        return "条件なし";
    }

    private (Dictionary<uint, List<NpcShopInfo>> GilShops, Dictionary<uint, List<NpcShopInfo>> SpecialShops) BuildNpcToShopMappings(
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<ENpcResident> npcResidentSheet,
        ExcelSheet<GilShop> gilShopSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        ExcelSheet<Level> levelSheet,
        ExcelSheet<TerritoryType> territorySheet,
        ExcelSheet<Map> mapSheet)
    {
        var gilShopResult = new Dictionary<uint, List<NpcShopInfo>>();
        var specialShopResult = new Dictionary<uint, List<NpcShopInfo>>();
        var npcCount = 0;
        var gilShopRefCount = 0;
        var specialShopRefCount = 0;
        var scannedNpcCount = 0;
        var npcWithNameCount = 0;

        // Step 0: NPC ID -> 位置情報のマッピングを事前構築
        var npcLocations = BuildNpcLocationMapping(levelSheet);
        _pluginLog.Information($"NPC位置情報: {npcLocations.Count}件");

        // Step 1: GilShopの全RowIdを収集
        var gilShopIds = new HashSet<uint>();
        var gilShopNames = new Dictionary<uint, string>();
        foreach (var shop in gilShopSheet)
        {
            if (shop.RowId != 0)
            {
                gilShopIds.Add(shop.RowId);
                gilShopNames[shop.RowId] = shop.Name.ToString();
            }
        }
        _pluginLog.Information($"GilShopシート: {gilShopIds.Count}件のショップを検出");

        // ショップIDの範囲をログ出力
        if (gilShopIds.Count > 0)
        {
            var minId = gilShopIds.Min();
            var maxId = gilShopIds.Max();
            _pluginLog.Information($"GilShop RowId範囲: {minId} - {maxId}");
        }

        // Step 2: SpecialShopの全RowIdを収集
        var specialShopIds = new HashSet<uint>();
        foreach (var shop in specialShopSheet)
        {
            if (shop.RowId != 0)
            {
                specialShopIds.Add(shop.RowId);
            }
        }
        _pluginLog.Information($"SpecialShopシート: {specialShopIds.Count}件のショップを検出");

        if (specialShopIds.Count > 0)
        {
            var minId = specialShopIds.Min();
            var maxId = specialShopIds.Max();
            _pluginLog.Information($"SpecialShop RowId範囲: {minId} - {maxId}");
        }

        _pluginLog.Information($"ENpcBase走査開始...");

        var loggedDataType = false;
        var loggedShopNpc = 0;

        foreach (var npcBase in npcBaseSheet)
        {
            scannedNpcCount++;

            if (npcBase.RowId == 0)
            {
                continue;
            }

            // 最初のNPCでENpcDataの型情報をログ出力
            if (!loggedDataType && npcBase.ENpcData.Count > 0)
            {
                var firstData = npcBase.ENpcData[0];
                var dataType = firstData.GetType().FullName ?? "null";
                var props = firstData.GetType().GetProperties()
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .Select(p => $"{p.Name}:{p.PropertyType.Name}")
                    .ToArray();
                _pluginLog.Information($"ENpcData型: {dataType}");
                _pluginLog.Information($"ENpcDataプロパティ: {string.Join(", ", props)}");
                loggedDataType = true;
            }

            // NPC名を取得
            var npcName = GetNpcName(npcResidentSheet, npcBase.RowId);
            if (string.IsNullOrEmpty(npcName))
            {
                continue;
            }

            npcWithNameCount++;
            npcCount++;

            // 位置情報を取得
            npcLocations.TryGetValue(npcBase.RowId, out var locInfo);

            // ENpcData[]からショップ参照を探す
            foreach (var dataValue in npcBase.ENpcData)
            {
                var rawValue = GetRawDataValue(dataValue);
                if (rawValue == 0)
                {
                    continue;
                }

                // GilShopチェック
                if (gilShopIds.Contains(rawValue))
                {
                    var shopId = rawValue;
                    var shopName = gilShopNames.TryGetValue(shopId, out var name) && !string.IsNullOrEmpty(name)
                        ? name
                        : $"ショップ#{shopId}";

                    if (loggedShopNpc < 5)
                    {
                        _pluginLog.Information($"GilShopNPC発見: {npcName} -> Shop {shopId} ({shopName}) @ {locInfo?.AreaName ?? "不明"}");
                        loggedShopNpc++;
                    }

                    AddNpcShopInfo(gilShopResult, npcBase.RowId, npcName, shopId, shopName, locInfo);
                    gilShopRefCount++;
                    continue;
                }

                // SpecialShopチェック
                if (specialShopIds.Contains(rawValue))
                {
                    AddNpcShopInfo(specialShopResult, npcBase.RowId, npcName, rawValue, $"特殊ショップ#{rawValue}", locInfo);
                    specialShopRefCount++;
                    continue;
                }

                // 下位16ビットでGilShopチェック
                var lowerId = rawValue & 0xFFFF;
                if (lowerId != 0 && gilShopIds.Contains(lowerId))
                {
                    var shopId = lowerId;
                    var shopName = gilShopNames.TryGetValue(shopId, out var name) && !string.IsNullOrEmpty(name)
                        ? name
                        : $"ショップ#{shopId}";

                    if (!gilShopResult.TryGetValue(shopId, out var existing) || !existing.Any(x => x.NpcId == npcBase.RowId))
                    {
                        AddNpcShopInfo(gilShopResult, npcBase.RowId, npcName, shopId, shopName, locInfo);
                        gilShopRefCount++;
                    }
                }
            }
        }

        _pluginLog.Information($"NPC走査完了: 走査={scannedNpcCount} / 名前あり={npcWithNameCount} / GilShop参照={gilShopRefCount}件 / SpecialShop参照={specialShopRefCount}件");
        return (gilShopResult, specialShopResult);
    }

    private static void AddNpcShopInfo(
        Dictionary<uint, List<NpcShopInfo>> result,
        uint npcId,
        string npcName,
        uint shopId,
        string shopName,
        NpcLocationInfo? locInfo)
    {
        var info = new NpcShopInfo(
            npcId,
            npcName,
            shopId,
            shopName,
            locInfo?.TerritoryTypeId ?? 0,
            locInfo?.AreaName ?? string.Empty,
            locInfo?.SubAreaName ?? string.Empty,
            locInfo?.MapId ?? 0,
            locInfo?.MapX ?? 0,
            locInfo?.MapY ?? 0);

        if (!result.TryGetValue(shopId, out var list))
        {
            list = new List<NpcShopInfo>();
            result[shopId] = list;
        }

        list.Add(info);
    }

    private sealed record NpcLocationInfo(
        uint TerritoryTypeId,
        string AreaName,
        string SubAreaName,
        uint MapId,
        float MapX,
        float MapY);

    private Dictionary<uint, NpcLocationInfo> BuildNpcLocationMapping(ExcelSheet<Level> levelSheet)
    {
        var result = new Dictionary<uint, NpcLocationInfo>();

        foreach (var level in levelSheet)
        {
            if (level.RowId == 0)
            {
                continue;
            }

            // Level.Type == 8 がENpc（NPC）を示す
            if (level.Type != 8)
            {
                continue;
            }

            var objectId = level.Object.RowId;
            if (objectId == 0 || result.ContainsKey(objectId))
            {
                continue;
            }

            var territory = level.Territory.ValueNullable;
            var map = level.Map.ValueNullable;
            if (territory == null || map == null)
            {
                continue;
            }

            var areaName = territory.Value.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
            var subAreaName = map.Value.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty;

            var mapX = ConvertToMapCoordinate(level.X, map.Value.OffsetX, map.Value.SizeFactor);
            var mapY = ConvertToMapCoordinate(level.Z, map.Value.OffsetY, map.Value.SizeFactor);

            result[objectId] = new NpcLocationInfo(
                territory.Value.RowId,
                areaName,
                subAreaName,
                map.Value.RowId,
                mapX,
                mapY);
        }

        return result;
    }

    private static uint GetRawDataValue(object dataValue)
    {
        // ENpcData要素から生の値を取得
        // RowRef<T>型の場合はRowIdプロパティから取得
        if (dataValue is uint directValue)
        {
            return directValue;
        }

        // RowIdプロパティを探す
        var rowIdProp = dataValue.GetType().GetProperty("RowId");
        if (rowIdProp != null)
        {
            var rowIdValue = rowIdProp.GetValue(dataValue);
            if (rowIdValue is uint rowId)
            {
                return rowId;
            }
        }

        // Idプロパティを探す（別名の可能性）
        var idProp = dataValue.GetType().GetProperty("Id");
        if (idProp != null)
        {
            var idValue = idProp.GetValue(dataValue);
            if (idValue is uint id)
            {
                return id;
            }
        }

        return 0;
    }

    private static string GetNpcName(ExcelSheet<ENpcResident> npcResidentSheet, uint npcId)
    {
        try
        {
            var resident = npcResidentSheet.GetRow(npcId);
            if (resident.RowId == 0)
            {
                return string.Empty;
            }

            return resident.Singular.ToString();
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static float ConvertToMapCoordinate(float rawPosition, float offset, ushort sizeFactor)
    {
        // FFXIV座標変換: c = 41.0 / (sizeFactor/100.0) * ((raw * sizeFactor / 100.0 + 1024.0) / 2048.0) + 1.0
        var scale = sizeFactor / 100f;
        var c = 41f / scale;
        var adjusted = (rawPosition * scale + 1024f) / 2048f;
        var result = c * adjusted + 1f;
        return MathF.Round(result, 1, MidpointRounding.AwayFromZero);
    }

    private static bool IsValidLocation(NpcShopInfo npcInfo)
    {
        if (npcInfo.TerritoryTypeId == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(npcInfo.AreaName))
        {
            return false;
        }

        // "不明"などのダミー地名は除外する
        var areaName = npcInfo.AreaName.Trim();
        if (areaName.Equals("不明", StringComparison.OrdinalIgnoreCase)
            || areaName.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || areaName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void CacheTerritoryNames(ExcelSheet<TerritoryType> territorySheet)
    {
        if (_territoryNames.Count > 0)
        {
            return;
        }

        foreach (var territory in territorySheet)
        {
            if (territory.RowId == 0)
            {
                continue;
            }

            var name = territory.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _territoryNames[territory.RowId] = name;
        }
    }

    private static void CollectTerritoryIds(
        Dictionary<uint, List<NpcShopInfo>> source,
        HashSet<uint> ids)
    {
        // キャッシュ済みNPC情報からユニークなエリアIDだけを抽出する
        foreach (var list in source.Values)
        {
            foreach (var info in list)
            {
                if (info.TerritoryTypeId == 0)
                {
                    continue;
                }

                ids.Add(info.TerritoryTypeId);
            }
        }
    }

}

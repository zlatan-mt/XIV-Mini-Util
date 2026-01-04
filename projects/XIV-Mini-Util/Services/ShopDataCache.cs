// Path: projects/XIV-Mini-Util/Services/ShopDataCache.cs
// Description: ショップ販売データを起動時に読み込みキャッシュする
// Reason: 検索を高速化しゲーム中の再読み込みを避けるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ContextMenuService.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Threading;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class ShopDataCache
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly Configuration _configuration;
    private readonly ShopDataDiagnostics _diagnostics;
    private readonly ShopNameIndex _nameIndex;
    private readonly NpcLocationResolver _npcLocationResolver;
    private readonly EnpcDataShopResolver _enpcDataShopResolver;
    private readonly NpcNameResolver _npcNameResolver;
    private readonly GilShopItemNotes _gilShopItemNotes;
    private readonly SpecialShopEntryExtractor _specialShopEntryExtractor;
    private readonly NpcShopInfoRegistry _npcShopInfoRegistry;
    private readonly ItemNameResolver _itemNameResolver;
    private readonly NpcShopMappingBuilder _npcShopMappingBuilder;
    private readonly object _buildLock = new();
    private readonly Dictionary<uint, List<ShopLocationInfo>> _itemToLocations = new();
    private readonly Dictionary<uint, string> _itemNames = new();
    private readonly Dictionary<uint, string> _territoryNames = new();
    private readonly Dictionary<byte, uint> _stainToItemId = new();
    private readonly HashSet<byte> _loggedStainDiagnostics = new();
    private readonly List<ShopTerritoryGroup> _territoryGroups = new();
    private HashSet<uint>? _stainItemIds;
    private ExcelSheet<Item>? _itemSheet;
    private ExcelSheet<TerritoryType>? _territorySheet;
    private ExcelSheet<Stain>? _stainSheet;

    private Dictionary<uint, List<NpcShopInfo>> _gilShopNpcInfos = new();
    private Dictionary<uint, List<NpcShopInfo>> _specialShopNpcInfos = new();

    private Task? _initializeTask;
    private CancellationTokenSource? _buildCts;
    private bool _isInitialized;
    private bool _territoryGroupsDirty = true;
    private int _buildGeneration;
    private ShopCacheBuildStatus _buildStatus = ShopCacheBuildStatus.Idle;
    private const int ProgressUpdateInterval = 500;

    public ShopDataCache(
        IDataManager dataManager,
        IPluginLog pluginLog,
        Configuration configuration)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;
        _configuration = configuration;
        _diagnostics = new ShopDataDiagnostics(dataManager, pluginLog, new ShopDataDiagnosticsWriter(pluginLog));
        _nameIndex = new ShopNameIndex(dataManager);
        _npcLocationResolver = new NpcLocationResolver(dataManager, pluginLog);
        _enpcDataShopResolver = new EnpcDataShopResolver();
        _npcNameResolver = new NpcNameResolver();
        _gilShopItemNotes = new GilShopItemNotes();
        _specialShopEntryExtractor = new SpecialShopEntryExtractor(new SpecialShopCostNoteBuilder());
        _npcShopInfoRegistry = new NpcShopInfoRegistry();
        _itemNameResolver = new ItemNameResolver();
        _npcShopMappingBuilder = new NpcShopMappingBuilder(
            pluginLog,
            _npcLocationResolver,
            _enpcDataShopResolver,
            _npcNameResolver,
            _npcShopInfoRegistry,
            configuration);
    }

    public bool IsInitialized => _isInitialized;
    private bool IsVerboseLogging => _configuration.ShopDataVerboseLogging;
    internal ShopCacheBuildStatus BuildStatus => _buildStatus;
    internal int BuildVersion => _buildGeneration;

    public Task InitializeAsync()
    {
        return StartBuildAsync(false, "初期化");
    }

    public Task RebuildAsync(string reason)
    {
        return StartBuildAsync(true, reason);
    }

    public void CancelBuild()
    {
        lock (_buildLock)
        {
            _buildCts?.Cancel();
        }
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
            : new List<ShopLocationInfo>();
    }

    public string GetItemName(uint itemId)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        return _itemNames.TryGetValue(itemId, out var name) ? name : string.Empty;
    }

    /// <summary>
    /// 染色ID（Stain）から対応するアイテムIDを取得する
    /// </summary>
    public uint GetItemIdFromStain(byte stainId)
    {
        if (stainId == 0)
        {
            return 0;
        }

        if (_stainToItemId.TryGetValue(stainId, out var cached))
        {
            return cached;
        }

        uint itemId = 0;
        var stainSheet = _stainSheet ??= _dataManager.GetExcelSheet<Stain>();
        if (stainSheet != null)
        {
            var stainRow = stainSheet.GetRow(stainId);
            if (stainRow.RowId != 0)
            {
                // StainシートのItem参照はバージョン差があるため反射で取得する
                itemId = TryGetItemIdFromStainRow(stainRow);
                if (itemId == 0)
                {
                    itemId = TryGetItemIdFromStainName(stainRow);
                }

                if (itemId == 0)
                {
                    LogStainRowDiagnostics(stainId, stainRow);
                }
            }
        }

        _stainToItemId[stainId] = itemId;
        return itemId;
    }

    /// <summary>
    /// アイテム名からIDを取得する（カララント名などの逆引き用）
    /// </summary>
    public uint GetItemIdFromName(string itemName)
    {
        return _nameIndex.GetItemIdFromName(itemName);
    }

    /// <summary>
    /// アイテムIDが染料（Stain）に紐づくか判定する
    /// </summary>
    public bool IsStainItemId(uint itemId)
    {
        if (itemId == 0)
        {
            return false;
        }

        EnsureStainItemIds();
        return _stainItemIds != null && _stainItemIds.Contains(itemId);
    }

    /// <summary>
    /// カララント系のアイテムかどうかを簡易判定する（EX含む）
    /// </summary>
    public bool IsLikelyColorantItemId(uint itemId)
    {
        if (itemId == 0)
        {
            return false;
        }

        if (IsStainItemId(itemId))
        {
            return true;
        }

        var name = GetItemNameFromSheet(itemId);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return ShopNameIndex.IsLikelyDyeItemName(name);
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

        var territorySheet = _territorySheet ??= _dataManager.GetExcelSheet<TerritoryType>();
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
        // 互換のため従来の戻り値を維持しつつ、内部は代表IDでまとめる
        return GetShopTerritoryGroups()
            .Select(group => new ShopTerritoryInfo(group.RepresentativeTerritoryTypeId, group.TerritoryName))
            .ToList();
    }

    public IReadOnlyList<ShopTerritoryGroup> GetShopTerritoryGroups()
    {
        if (!_isInitialized)
        {
            return Array.Empty<ShopTerritoryGroup>();
        }

        if (_territoryGroupsDirty)
        {
            _territoryGroups.Clear();
            _territoryGroups.AddRange(BuildTerritoryGroups());
            _territoryGroupsDirty = false;
        }

        return _territoryGroups;
    }

    /// <summary>
    /// 名前でアイテムを検索する（販売場所が判明しているアイテムのみ）
    /// </summary>
    /// <param name="query">検索クエリ（部分一致）</param>
    /// <param name="limit">最大件数</param>
    /// <returns>アイテムIDと名前のリスト</returns>
    public IEnumerable<(uint Id, string Name)> SearchItemsByName(string query, int limit = 50)
    {
        if (!_isInitialized || string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<(uint Id, string Name)>();
        }

        var trimmedQuery = query.Trim();
        return _itemNames
            .Where(kvp => kvp.Value.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(kvp => (kvp.Key, kvp.Value));
    }

    private static uint TryGetItemIdFromStainRow(Stain stainRow)
    {
        var rowType = stainRow.GetType();
        var candidates = new[] { "Item", "ItemId", "ItemID" };

        foreach (var name in candidates)
        {
            var prop = rowType.GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(stainRow);
            var rowId = TryGetRowId(value);
            if (rowId is { } id && id != 0)
            {
                return id;
            }
        }

        return 0;
    }

    private uint TryGetItemIdFromStainName(Stain stainRow)
    {
        var stainName = stainRow.Name.ToString();
        if (string.IsNullOrWhiteSpace(stainName))
        {
            return 0;
        }

        return _nameIndex.GetItemIdFromStainName(stainName);
    }

    private void LogStainRowDiagnostics(byte stainId, Stain stainRow)
    {
        if (!_loggedStainDiagnostics.Add(stainId))
        {
            return;
        }

        try
        {
            var rowType = stainRow.GetType();
            var properties = rowType.GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0)
                .ToArray();

            var propertyNames = string.Join(", ", properties.Select(p => p.Name));
                LogVerbose($"[StainDebug] StainRow#{stainId} properties: {propertyNames}");

            foreach (var property in properties)
            {
                if (!PropertyNameHasKeyword(property.Name))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(stainRow);
                }
                catch
                {
                    continue;
                }

                var rowId = TryGetRowId(value);
                if (rowId is { } id && id != 0)
                {
                    LogVerbose($"[StainDebug] StainRow#{stainId} {property.Name} RowId={id}");
                }
                else if (value != null)
                {
                    LogVerbose($"[StainDebug] StainRow#{stainId} {property.Name}={value}");
                }
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "StainRow診断の出力に失敗しました。");
        }
    }

    private static bool PropertyNameHasKeyword(string name)
    {
        return name.Contains("Item", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Stain", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Color", StringComparison.OrdinalIgnoreCase);
    }


    private string GetItemNameFromSheet(uint itemId)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        var itemSheet = _itemSheet ??= _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return string.Empty;
        }

        return _itemNameResolver.GetName(itemSheet, itemId, _itemNames);
    }

    private void EnsureStainItemIds()
    {
        if (_stainItemIds != null)
        {
            return;
        }

        _stainItemIds = new HashSet<uint>();
        var stainSheet = _stainSheet ??= _dataManager.GetExcelSheet<Stain>();
        if (stainSheet == null)
        {
            return;
        }

        foreach (var row in stainSheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            var itemId = TryGetItemIdFromStainRow(row);
            if (itemId == 0)
            {
                itemId = TryGetItemIdFromStainName(row);
            }

            if (itemId != 0)
            {
                _stainItemIds.Add(itemId);
            }
        }
    }

    private static uint? TryGetRowId(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is uint directUint)
        {
            return directUint;
        }

        if (value is int directInt && directInt >= 0)
        {
            return (uint)directInt;
        }

        var type = value.GetType();
        var rowIdProp = type.GetProperty("RowId") ?? type.GetProperty("Id") ?? type.GetProperty("ItemId") ?? type.GetProperty("ItemID");
        if (rowIdProp != null)
        {
            var rowIdValue = rowIdProp.GetValue(value);
            if (rowIdValue is uint rowIdUint)
            {
                return rowIdUint;
            }

            if (rowIdValue is int rowIdInt && rowIdInt >= 0)
            {
                return (uint)rowIdInt;
            }
        }

        // LazyRow/RowRefのValue/ValueNullableを辿る
        var valueNullableProp = type.GetProperty("ValueNullable");
        if (valueNullableProp != null)
        {
            var nestedValue = valueNullableProp.GetValue(value);
            var nestedRowId = TryGetRowId(nestedValue);
            if (nestedRowId is { } nested && nested != 0)
            {
                return nested;
            }
        }

        var valueProp = type.GetProperty("Value");
        if (valueProp != null)
        {
            var nestedValue = valueProp.GetValue(value);
            var nestedRowId = TryGetRowId(nestedValue);
            if (nestedRowId is { } nested && nested != 0)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 診断レポートを生成してファイルに出力
    /// </summary>
    public string GenerateDiagnosticsReport(string outputPath)
    {
        if (!_isInitialized)
        {
            return "ショップデータが初期化されていません。";
        }
        return _diagnostics.GenerateDiagnosticsReport(outputPath, _itemToLocations.Count);
    }

    /// <summary>
    /// 位置情報なしNPCの数を取得
    /// </summary>
    public int GetExcludedNpcCount() => _diagnostics.ExcludedNpcCount;

    /// <summary>
    /// NPCマッチなしショップの数を取得
    /// </summary>
    public int GetUnmatchedShopCount() => _diagnostics.UnmatchedShopCount;

    /// <summary>
    /// 検索実行時にアイテムの販売場所詳細をログ出力（デバッグ用）
    /// </summary>
    public void LogSearchDiagnostics(uint itemId)
    {
        var locations = GetShopLocations(itemId);
        var itemName = GetItemName(itemId);
        _diagnostics.LogSearchDiagnostics(
            itemId,
            _isInitialized,
            locations,
            itemName,
            _gilShopNpcInfos,
            ShopLocationValidator.IsValid);
    }

    public void LogMissingItemDiagnostics(uint itemId)
    {
        _diagnostics.LogMissingItemDiagnostics(
            itemId,
            _isInitialized,
            _gilShopNpcInfos,
            _specialShopNpcInfos,
            ShopLocationValidator.IsValid);
    }

    private Task StartBuildAsync(bool rebuild, string reason)
    {
        lock (_buildLock)
        {
            if (!rebuild && _initializeTask != null)
            {
                return _initializeTask;
            }

            _buildCts?.Cancel();
            _buildCts?.Dispose();
            _buildCts = new CancellationTokenSource();

            var generation = ++_buildGeneration;
            var token = _buildCts.Token;
            _initializeTask = Task.Run(() => BuildCacheSafeAsync(generation, token, reason), token);
            return _initializeTask;
        }
    }

    private async Task BuildCacheSafeAsync(int generation, CancellationToken cancellationToken, string reason)
    {
        try
        {
            UpdateBuildStatus(ShopCacheBuildState.Running, "Start", $"ショップデータ構築開始({reason})...");
            ResetCaches();
            await BuildCacheAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                UpdateBuildStatus(ShopCacheBuildState.Canceled, "Canceled", "ショップデータ構築をキャンセルしました。");
                return;
            }

            if (generation != _buildGeneration)
            {
                return;
            }

            _isInitialized = true;
            UpdateBuildStatus(ShopCacheBuildState.Completed, "Complete", "ショップデータ構築完了");
        }
        catch (OperationCanceledException)
        {
            UpdateBuildStatus(ShopCacheBuildState.Canceled, "Canceled", "ショップデータ構築をキャンセルしました。");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "ショップデータの初期化に失敗しました。");
            UpdateBuildStatus(ShopCacheBuildState.Failed, "Failed", "ショップデータの初期化に失敗しました。");
        }
    }

    private Task BuildCacheAsync(CancellationToken cancellationToken)
    {
        LogBuildPhase("Start", "ショップデータ構築開始...");

        if (!ShopDataSheets.TryLoad(_dataManager, _pluginLog, out var sheets))
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        _itemSheet = sheets.ItemSheet;
        _territorySheet = sheets.TerritorySheet;
        _stainSheet = _dataManager.GetExcelSheet<Stain>();
        var questSheet = _dataManager.GetExcelSheet<Quest>();

        LogBuildPhase("NpcMapping", "NPC-Shop マッピング構築開始...");

        var mappings = BuildNpcMappings(sheets, cancellationToken);
        _gilShopNpcInfos = mappings.GilShopInfos;
        _specialShopNpcInfos = mappings.SpecialShopInfos;

        LogBuildPhase("NpcMapping", $"NPC-Shop マッピング構築完了: GilShop={_gilShopNpcInfos.Count}件, SpecialShop={_specialShopNpcInfos.Count}件");

        CacheTerritoryNames(sheets.TerritorySheet);
        _territoryGroupsDirty = true;

        var stats = ProcessGilShopItems(sheets.ItemSheet, _gilShopNpcInfos, questSheet, cancellationToken);

        // Step 3: SpecialShop → Item の関係を構築し、逆引きインデックスに追加
        var specialShopAdded = ProcessSpecialShops(sheets.ItemSheet, _specialShopNpcInfos, cancellationToken);
        stats.AddProcessed(specialShopAdded);

        var emptyItems = RemoveEmptyItemLocations();

        LogBuildPhase("Complete", $"ショップデータ初期化完了: アイテム {_itemToLocations.Count}件 / 販売場所 {stats.ProcessedItems}件 / スキップ {stats.SkippedItems}件 / 位置不明除外 {emptyItems.Count}件");
        return Task.CompletedTask;
    }

    private (Dictionary<uint, List<NpcShopInfo>> GilShopInfos, Dictionary<uint, List<NpcShopInfo>> SpecialShopInfos) BuildNpcMappings(
        ShopDataSheets sheets,
        CancellationToken cancellationToken)
    {
        // Step 1: ENpcBaseを走査してNPC → Shop（GilShop + SpecialShop）のマッピングを構築
        return _npcShopMappingBuilder.Build(
            sheets.NpcBaseSheet,
            sheets.NpcResidentSheet,
            sheets.GilShopSheet,
            sheets.SpecialShopSheet,
            sheets.LevelSheet,
            sheets.TerritorySheet,
            sheets.MapSheet,
            cancellationToken);
    }

    private GilShopProcessStats ProcessGilShopItems(
        ExcelSheet<Item> itemSheet,
        Dictionary<uint, List<NpcShopInfo>> npcShopInfos,
        ExcelSheet<Quest>? questSheet,
        CancellationToken cancellationToken)
    {
        var stats = new GilShopProcessStats();

        // SubrowExcelSheetを直接使用
        var gilShopItemSheet = _dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet == null)
        {
            _pluginLog.Error("GilShopItemシートの取得に失敗しました。");
            return stats;
        }

        LogBuildPhase("GilShop", "GilShopItemシート取得成功");

        // SubrowExcelSheetを親行ごとに走査
        var loggedFirstItem = false;
        foreach (var subrowCollection in gilShopItemSheet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shopId = subrowCollection.RowId;

            // 最初のショップの情報をログ出力
            if (IsVerboseLogging && !loggedFirstItem)
            {
                LogVerbose($"SubrowCollection型: {subrowCollection.GetType().FullName}");
                LogVerbose($"最初のショップID: {shopId}");
            }

            foreach (var shopItem in subrowCollection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stats.IncrementTotal();
                if (stats.TotalShopItems % ProgressUpdateInterval == 0)
                {
                    UpdateBuildStatus(
                        ShopCacheBuildState.Running,
                        "GilShop",
                        $"GilShopItem走査中... {stats.TotalShopItems}件",
                        stats.TotalShopItems,
                        0);
                }

                // 最初のアイテムの型情報をログ出力
                if (IsVerboseLogging)
                {
                    ShopDataLogHelper.LogFirstTypeMetadata(_pluginLog, "GilShopItem", shopItem, ref loggedFirstItem);
                }

                // Itemプロパティから直接アイテムIDを取得
                var itemId = ShopDataExtractors.GetItemIdFromGilShopItem(shopItem);

                if (itemId == 0)
                {
                    stats.SkipNoItemId();
                    continue;
                }

                if (shopId == 0)
                {
                    stats.SkipNoShopId();
                    continue;
                }

                // このショップを持つNPCを検索
                if (!npcShopInfos.TryGetValue(shopId, out var npcInfoList) || npcInfoList.Count == 0)
                {
                    stats.SkipNoNpcMatch();

                    // 診断用：NPCマッチなしショップを記録
                    _diagnostics.RecordUnmatchedShopItem(shopId, itemId);

                    continue;
                }

                if (!TryCacheItemName(itemSheet, itemId, out _))
                {
                    stats.SkipOther();
                    continue;
                }

                var price = _gilShopItemNotes.GetPrice(shopItem);
                var conditionNote = _gilShopItemNotes.GetCondition(shopItem, questSheet);

                var addedCount = AddItemLocations(
                    itemId,
                    npcInfoList,
                    npcInfo => npcInfo.ShopId,
                    npcInfo => npcInfo.ShopName,
                    price,
                    conditionNote);

                stats.AddProcessed(addedCount);
            }
        }

        LogBuildPhase("GilShop", $"GilShopItem走査完了: {stats.GetSummary()}");
        return stats;
    }

    private List<uint> RemoveEmptyItemLocations()
    {
        // 販売場所が0件のアイテムを削除（位置情報なしのNPCのみで販売されているアイテム）
        var emptyItems = _itemToLocations.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var itemId in emptyItems)
        {
            _itemToLocations.Remove(itemId);
        }

        return emptyItems;
    }

    private int ProcessSpecialShops(
        ExcelSheet<Item> itemSheet,
        Dictionary<uint, List<NpcShopInfo>> npcSpecialShopInfos,
        CancellationToken cancellationToken)
    {
        var specialShopSheet = _dataManager.GetExcelSheet<SpecialShop>();
        if (specialShopSheet == null)
        {
            _pluginLog.Warning("SpecialShopシートの取得に失敗しました。");
            return 0;
        }

        var specialShopItems = 0;
        var specialShopSkipped = 0;
        var processedItems = 0;
        var loggedFirst = false;

        foreach (var shop in specialShopSheet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessSpecialShop(
                shop,
                itemSheet,
                npcSpecialShopInfos,
                ref processedItems,
                ref specialShopItems,
                ref specialShopSkipped,
                ref loggedFirst);

            if (processedItems > 0 && processedItems % ProgressUpdateInterval == 0)
            {
                UpdateBuildStatus(
                    ShopCacheBuildState.Running,
                    "SpecialShop",
                    $"SpecialShop走査中... {processedItems}件",
                    processedItems,
                    0);
            }
        }

        LogBuildPhase("SpecialShop", $"SpecialShop走査完了: 追加={specialShopItems}件, スキップ={specialShopSkipped}件");
        return processedItems;
    }

    private void ProcessSpecialShop(
        SpecialShop shop,
        ExcelSheet<Item> itemSheet,
        Dictionary<uint, List<NpcShopInfo>> npcSpecialShopInfos,
        ref int processedItems,
        ref int specialShopItems,
        ref int specialShopSkipped,
        ref bool loggedFirst)
    {
        if (shop.RowId == 0)
        {
            return;
        }

        if (!npcSpecialShopInfos.TryGetValue(shop.RowId, out var npcInfoList) || npcInfoList.Count == 0)
        {
            return;
        }

        var shopName = ShopNameFormatter.GetSpecialShopName(shop.RowId, shop.Name.ToString());

        for (var entryIndex = 0; entryIndex < shop.Item.Count; entryIndex++)
        {
            var entry = shop.Item[entryIndex];
            if (IsVerboseLogging)
            {
                ShopDataLogHelper.LogFirstTypeMetadata(_pluginLog, "SpecialShopEntry", entry, ref loggedFirst);
            }
            ProcessSpecialShopEntry(
                entry,
                itemSheet,
                npcInfoList,
                shop.RowId,
                shopName,
                ref processedItems,
                ref specialShopItems,
                ref specialShopSkipped);
        }
    }

    private void ProcessSpecialShopEntry(
        SpecialShop.ItemStruct entry,
        ExcelSheet<Item> itemSheet,
        IReadOnlyList<NpcShopInfo> npcInfoList,
        uint shopId,
        string shopName,
        ref int processedItems,
        ref int specialShopItems,
        ref int specialShopSkipped)
    {
        foreach (var extracted in _specialShopEntryExtractor.Extract(entry))
        {
            var itemId = extracted.ItemId;
            if (itemId == 0)
            {
                continue;
            }

            if (!TryCacheItemName(itemSheet, itemId, out _))
            {
                specialShopSkipped++;
                continue;
            }

            var addedCount = AddItemLocations(
                itemId,
                npcInfoList,
                _ => shopId,
                _ => shopName,
                0,
                extracted.CostNote);

            processedItems += addedCount;
            specialShopItems += addedCount;
        }
    }

    private bool TryCacheItemName(ExcelSheet<Item> itemSheet, uint itemId, out string itemName)
    {
        return _itemNameResolver.TryCacheName(itemSheet, itemId, _itemNames, out itemName);
    }

    private int AddItemLocations(
        uint itemId,
        IReadOnlyList<NpcShopInfo> npcInfoList,
        Func<NpcShopInfo, uint> shopIdSelector,
        Func<NpcShopInfo, string> shopNameSelector,
        int price,
        string conditionNote)
    {
        if (!_itemToLocations.TryGetValue(itemId, out var locations))
        {
            locations = new List<ShopLocationInfo>();
            _itemToLocations[itemId] = locations;
        }

        var addedCount = 0;

        // 各NPCの販売場所を追加（位置情報があるもののみ）
        foreach (var npcInfo in npcInfoList)
        {
            if (!ShopLocationValidator.IsValid(npcInfo))
            {
                // 診断用：位置情報なしNPCを記録（重複除外）
                _diagnostics.RecordExcludedNpc(npcInfo);
                continue;
            }

            var shopId = shopIdSelector(npcInfo);
            var shopName = shopNameSelector(npcInfo);

            // 重複チェック（同じショップ・同じNPCは追加しない）
            if (locations.Any(existing =>
                existing.ShopId == shopId &&
                existing.NpcName == npcInfo.NpcName &&
                existing.TerritoryTypeId == npcInfo.TerritoryTypeId))
            {
                continue;
            }

            locations.Add(new ShopLocationInfo(
                shopId,
                shopName,
                npcInfo.NpcName,
                npcInfo.TerritoryTypeId,
                npcInfo.AreaName,
                npcInfo.SubAreaName,
                npcInfo.MapId,
                npcInfo.MapX,
                npcInfo.MapY,
                price,
                conditionNote,
                npcInfo.IsManuallyAdded));

            addedCount++;
        }

        return addedCount;
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

    private void LogBuildPhase(string phase, string message)
    {
        _pluginLog.Information($"[ShopCache:{phase}] {message}");
        UpdateBuildStatus(ShopCacheBuildState.Running, phase, message);
    }

    private void LogVerbose(string message)
    {
        if (!IsVerboseLogging)
        {
            return;
        }

        _pluginLog.Information(message);
    }

    private IReadOnlyList<ShopTerritoryGroup> BuildTerritoryGroups()
    {
        var ids = new HashSet<uint>();
        CollectTerritoryIds(_gilShopNpcInfos, ids);
        CollectTerritoryIds(_specialShopNpcInfos, ids);

        // 同名エリアは代表IDにまとめる（最小IDで固定）
        return ids
            .Select(id =>
            {
                var name = GetTerritoryName(id);
                return new { Id = id, Name = name };
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                var territoryIds = group
                    .Select(entry => entry.Id)
                    .OrderBy(id => id)
                    .ToList();
                return new ShopTerritoryGroup(
                    group.Key,
                    territoryIds[0],
                    territoryIds);
            })
            .OrderBy(group => group.TerritoryName, StringComparer.Ordinal)
            .ToList();
    }

    private void ResetCaches()
    {
        _isInitialized = false;
        _itemToLocations.Clear();
        _itemNames.Clear();
        _territoryNames.Clear();
        _stainToItemId.Clear();
        _loggedStainDiagnostics.Clear();
        _stainItemIds = null;
        _gilShopNpcInfos = new Dictionary<uint, List<NpcShopInfo>>();
        _specialShopNpcInfos = new Dictionary<uint, List<NpcShopInfo>>();
        _itemSheet = null;
        _territorySheet = null;
        _stainSheet = null;
        _territoryGroupsDirty = true;
        _territoryGroups.Clear();
        _nameIndex.Reset();
        _diagnostics.Reset();
    }

    private void UpdateBuildStatus(ShopCacheBuildState state, string phase, string message, int processed = 0, int total = 0)
    {
        _buildStatus = new ShopCacheBuildStatus(state, phase, message, processed, total);
    }

}

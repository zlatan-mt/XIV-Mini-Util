// Path: projects/XIV-Mini-Util/Services/ShopDataCache.cs
// Description: ショップ販売データを起動時に読み込みキャッシュする
// Reason: 検索を高速化しゲーム中の再読み込みを避けるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ContextMenuService.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
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
    private readonly Dictionary<string, uint> _itemNameToId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _itemNameNormalizedToId = new(StringComparer.Ordinal);
    private readonly Dictionary<byte, uint> _stainToItemId = new();
    private readonly HashSet<byte> _loggedStainDiagnostics = new();
    private readonly Dictionary<string, uint> _stainNameToItemId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _stainNameNormalizedToItemId = new(StringComparer.Ordinal);
    private HashSet<uint>? _stainItemIds;

    private Dictionary<uint, List<NpcShopInfo>> _gilShopNpcInfos = new();
    private Dictionary<uint, List<NpcShopInfo>> _specialShopNpcInfos = new();

    // カスタムショップ（ハウジングNPC等）
    private readonly object _customShopLock = new();
    private Dictionary<uint, List<ShopLocationInfo>> _customShopLocations = new();

    // 診断用：位置情報なしで除外されたNPC
    private readonly List<(uint NpcId, string NpcName, uint ShopId, string ShopName)> _excludedNpcs = new();
    // 診断用：NPCマッチがなかったショップ
    private readonly Dictionary<uint, List<uint>> _unmatchedShopItems = new(); // ShopId -> ItemIds

    private Task? _initializeTask;
    private bool _isInitialized;

    /// <summary>
    /// ハウジングNPCの販売アイテムリスト（ハードコード）
    /// ゲームデータから取得できないため、既知のアイテムIDを直接定義
    /// </summary>
    public static class HousingNpcItems
    {
        /// <summary>素材屋の販売アイテム</summary>
        public static readonly uint[] MaterialSupplier = new uint[]
        {
            // 注意: シャード/クリスタル/クラスターは購入できるNPCが存在しないため含めない
            // 基礎素材
            5504,  // 獣脂
            5505,  // 蜜蝋
            5530,  // にかわ
            5339,  // 天然水
            5356,  // 霊銀砂
            5357,  // オーガニック肥料
        };

        /// <summary>よろず屋の販売アイテム</summary>
        public static readonly uint[] Junkmonger = new uint[]
        {
            // 基礎素材・消耗品
            4551,  // カーボンコート
            5594,  // グロースフォーミュラ・ガンマ
            7059,  // 亜鉛鉱
        };

        /// <summary>HousingNpcTypeに対応するアイテムリストを取得</summary>
        public static uint[] GetItems(HousingNpcType npcType) => npcType switch
        {
            HousingNpcType.MaterialSupplier => MaterialSupplier,
            HousingNpcType.Junkmonger => Junkmonger,
            _ => Array.Empty<uint>(),
        };
    }

    /// <summary>
    /// ハウジングエリア情報
    /// </summary>
    public static class HousingAreas
    {
        public static readonly (uint TerritoryTypeId, string Name)[] All = new[]
        {
            (339u, "ミスト・ヴィレッジ"),
            (340u, "ラベンダーベッド"),
            (341u, "ゴブレットビュート"),
            (641u, "シロガネ"),
            (979u, "エンピレアム"),
        };

        public static string GetName(uint territoryTypeId)
        {
            foreach (var (id, name) in All)
            {
                if (id == territoryTypeId)
                {
                    return name;
                }
            }
            return string.Empty;
        }

        public static bool IsHousingArea(uint territoryTypeId)
        {
            foreach (var (id, _) in All)
            {
                if (id == territoryTypeId)
                {
                    return true;
                }
            }
            return false;
        }
    }

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
        float MapY,
        bool IsManuallyAdded = false);

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

        if (_itemToLocations.ContainsKey(itemId))
        {
            return true;
        }

        // カスタムショップもチェック
        lock (_customShopLock)
        {
            return _customShopLocations.ContainsKey(itemId);
        }
    }

    public IReadOnlyList<ShopLocationInfo> GetShopLocations(uint itemId)
    {
        if (!_isInitialized || itemId == 0)
        {
            return Array.Empty<ShopLocationInfo>();
        }

        var baseLocations = _itemToLocations.TryGetValue(itemId, out var locations)
            ? locations
            : new List<ShopLocationInfo>();

        // カスタムショップからもマージ
        List<ShopLocationInfo>? customLocations = null;
        lock (_customShopLock)
        {
            if (_customShopLocations.TryGetValue(itemId, out var custom))
            {
                customLocations = custom.ToList();
            }
        }

        if (customLocations == null || customLocations.Count == 0)
        {
            return baseLocations;
        }

        if (baseLocations.Count == 0)
        {
            return customLocations;
        }

        // 両方ある場合はマージ
        var merged = new List<ShopLocationInfo>(baseLocations.Count + customLocations.Count);
        merged.AddRange(baseLocations);
        merged.AddRange(customLocations);
        return merged;
    }

    /// <summary>
    /// カスタムショップ設定からアイテム→販売場所のマッピングを再構築する
    /// </summary>
    public void RefreshCustomShops(IReadOnlyList<CustomShopConfig> customShops)
    {
        var newLocations = new Dictionary<uint, List<ShopLocationInfo>>();

        foreach (var shop in customShops)
        {
            if (!shop.IsEnabled || string.IsNullOrWhiteSpace(shop.Name))
            {
                continue;
            }

            // マップピン（MapLinkPayload）には MapId が必須。
            // ハウジングは設定側でMapIdを持たない運用のため、TerritoryTypeから解決して埋める。
            var resolvedMapId = GetDefaultMapId(shop.TerritoryTypeId);
            if (resolvedMapId == 0)
            {
                resolvedMapId = shop.MapId;
            }

            var areaName = GetTerritoryName(shop.TerritoryTypeId);
            if (string.IsNullOrWhiteSpace(areaName))
            {
                areaName = HousingAreas.GetName(shop.TerritoryTypeId);
            }
            if (string.IsNullOrWhiteSpace(areaName))
            {
                areaName = $"エリア#{shop.TerritoryTypeId}";
            }

            // 各NPCタイプのアイテムを追加
            foreach (var npcType in shop.Npcs)
            {
                var items = HousingNpcItems.GetItems(npcType);
                foreach (var itemId in items)
                {
                    // ここで不正なIDを落としておく（検索結果に「アイテム#xxxx」が混ざるのを防ぐ）
                    if (!IsValidItemId(itemId))
                    {
                        continue;
                    }

                    if (!newLocations.TryGetValue(itemId, out var list))
                    {
                        list = new List<ShopLocationInfo>();
                        newLocations[itemId] = list;
                    }

                    // 重複チェック
                    if (list.Any(l => l.CustomShopId == shop.Id))
                    {
                        continue;
                    }

                    var npcTypeName = npcType switch
                    {
                        HousingNpcType.MaterialSupplier => "素材屋",
                        HousingNpcType.Junkmonger => "よろず屋",
                        _ => "NPC",
                    };

                    list.Add(new ShopLocationInfo(
                        ShopId: 0, // カスタムショップにはShopIdなし
                        ShopName: shop.Name,
                        NpcName: $"{npcTypeName} ({shop.Name})",
                        TerritoryTypeId: shop.TerritoryTypeId,
                        AreaName: areaName,
                        SubAreaName: string.Empty,
                        MapId: resolvedMapId,
                        MapX: shop.X,
                        MapY: shop.Y,
                        Price: 0,
                        ConditionNote: "カスタム登録",
                        IsManuallyAdded: true,
                        CustomShopId: shop.Id,
                        IsCustomShop: true));
                }
            }
        }

        lock (_customShopLock)
        {
            _customShopLocations = newLocations;
        }

        _pluginLog.Information($"カスタムショップ更新: {customShops.Count}件 → {newLocations.Count}アイテム");
    }

    private bool IsValidItemId(uint itemId)
    {
        if (itemId == 0)
        {
            return false;
        }

        var name = GetItemName(itemId);
        return !string.IsNullOrWhiteSpace(name);
    }

    private uint GetDefaultMapId(uint territoryTypeId)
    {
        if (territoryTypeId == 0)
        {
            return 0;
        }

        var mapSheet = _dataManager.GetExcelSheet<Map>();
        if (mapSheet == null)
        {
            return 0;
        }

        // TerritoryType.Map は環境差/用途差で期待とズレる場合があるため、
        // TerritoryTypeId に紐づく Map を Mapシート側から選ぶ。
        // まずは PlaceNameSub が空の「メインマップ」を優先する。
        uint fallbackId = 0;
        foreach (var map in mapSheet)
        {
            if (map.RowId != 0 && map.TerritoryType.RowId == territoryTypeId)
            {
                if (fallbackId == 0)
                {
                    fallbackId = map.RowId;
                }

                var subName = map.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(subName))
                {
                    return map.RowId;
                }
            }
        }

        return fallbackId;
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
        var stainSheet = _dataManager.GetExcelSheet<Stain>();
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
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return 0;
        }

        EnsureItemNameIndex();
        if (_itemNameToId.TryGetValue(itemName, out var itemId))
        {
            return itemId;
        }

        var normalized = NormalizeName(itemName);
        return _itemNameNormalizedToId.TryGetValue(normalized, out var normalizedId) ? normalizedId : 0;
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

        return IsLikelyDyeItemName(name);
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

        var ids = new HashSet<uint>();
        CollectTerritoryIds(_gilShopNpcInfos, ids);
        CollectTerritoryIds(_specialShopNpcInfos, ids);

        // 固定ショップが存在しない場合でも、ハウジングエリアは候補として出したい
        foreach (var (territoryTypeId, _) in HousingAreas.All)
        {
            ids.Add(territoryTypeId);
        }

        // 同名エリアは代表IDにまとめる（最小IDで固定）
        return ids
            .Select(id =>
            {
                var name = GetTerritoryName(id);
                if (string.IsNullOrWhiteSpace(name) && HousingAreas.IsHousingArea(id))
                {
                    name = HousingAreas.GetName(id);
                }
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

        EnsureStainNameIndex();
        if (_stainNameToItemId.TryGetValue(stainName, out var itemId))
        {
            return itemId;
        }

        var normalized = NormalizeName(stainName);
        return _stainNameNormalizedToItemId.TryGetValue(normalized, out var normalizedItemId) ? normalizedItemId : 0;
    }

    private void EnsureItemNameIndex()
    {
        if (_itemNameToId.Count > 0 || _itemNameNormalizedToId.Count > 0)
        {
            return;
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return;
        }

        foreach (var itemRow in itemSheet)
        {
            if (itemRow.RowId == 0)
            {
                continue;
            }

            var name = itemRow.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!_itemNameToId.ContainsKey(name))
            {
                _itemNameToId[name] = itemRow.RowId;
            }

            var normalized = NormalizeName(name);
            if (!string.IsNullOrEmpty(normalized) && !_itemNameNormalizedToId.ContainsKey(normalized))
            {
                _itemNameNormalizedToId[normalized] = itemRow.RowId;
            }
        }
    }

    private void EnsureStainNameIndex()
    {
        if (_stainNameToItemId.Count > 0 || _stainNameNormalizedToItemId.Count > 0)
        {
            return;
        }

        var stainSheet = _dataManager.GetExcelSheet<Stain>();
        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (stainSheet == null || itemSheet == null)
        {
            return;
        }

        var stainNames = new HashSet<string>(StringComparer.Ordinal);
        var stainNamesNormalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stainRow in stainSheet)
        {
            if (stainRow.RowId == 0)
            {
                continue;
            }

            var name = stainRow.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                stainNames.Add(name);
                var normalized = NormalizeName(name);
                if (!string.IsNullOrEmpty(normalized))
                {
                    stainNamesNormalized.Add(normalized);
                }
            }
        }

        var stainNormalizedList = stainNamesNormalized.ToList();

        foreach (var itemRow in itemSheet)
        {
            if (itemRow.RowId == 0)
            {
                continue;
            }

            var name = itemRow.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (stainNames.Contains(name) && !_stainNameToItemId.ContainsKey(name))
            {
                _stainNameToItemId[name] = itemRow.RowId;
            }

            var normalized = NormalizeName(name);
            if (!string.IsNullOrEmpty(normalized) && stainNamesNormalized.Contains(normalized) && !_stainNameNormalizedToItemId.ContainsKey(normalized))
            {
                _stainNameNormalizedToItemId[normalized] = itemRow.RowId;
            }

            if (!IsLikelyDyeItemName(name) || string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            foreach (var stainNormalized in stainNormalizedList)
            {
                if (!_stainNameNormalizedToItemId.ContainsKey(stainNormalized) && normalized.Contains(stainNormalized, StringComparison.Ordinal))
                {
                    _stainNameNormalizedToItemId[stainNormalized] = itemRow.RowId;
                }
            }
        }
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
            _pluginLog.Information($"[StainDebug] StainRow#{stainId} properties: {propertyNames}");

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
                    _pluginLog.Information($"[StainDebug] StainRow#{stainId} {property.Name} RowId={id}");
                }
                else if (value != null)
                {
                    _pluginLog.Information($"[StainDebug] StainRow#{stainId} {property.Name}={value}");
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

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var buffer = new char[name.Length];
        var length = 0;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length] = ch;
                length++;
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static bool IsLikelyDyeItemName(string name)
    {
        return name.Contains("染料", StringComparison.Ordinal)
            || name.Contains("カララント", StringComparison.Ordinal)
            || name.Contains("Dye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Colorant", StringComparison.OrdinalIgnoreCase);
    }

    private string GetItemNameFromSheet(uint itemId)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        if (_itemNames.TryGetValue(itemId, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return string.Empty;
        }

        try
        {
            var row = itemSheet.GetRow(itemId);
            if (row.RowId == 0)
            {
                return string.Empty;
            }

            var name = row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _itemNames[itemId] = name;
            }

            return name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void EnsureStainItemIds()
    {
        if (_stainItemIds != null)
        {
            return;
        }

        _stainItemIds = new HashSet<uint>();
        var stainSheet = _dataManager.GetExcelSheet<Stain>();
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

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# ショップデータ診断レポート");
        sb.AppendLine($"生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // サマリー
        sb.AppendLine("## サマリー");
        sb.AppendLine($"- 登録アイテム数: {_itemToLocations.Count}");
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
        foreach (var (shopId, itemIds) in _unmatchedShopItems.OrderBy(kvp => kvp.Key).Take(100))
        {
            var itemNames = new List<string>();
            foreach (var itemId in itemIds)
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
            sb.AppendLine($"| {shopId} | {string.Join(", ", itemNames)} |");
        }

        if (_unmatchedShopItems.Count > 100)
        {
            sb.AppendLine($"| ... | （他 {_unmatchedShopItems.Count - 100} ショップ省略） |");
        }

        var report = sb.ToString();

        // ファイルに出力
        try
        {
            File.WriteAllText(outputPath, report, System.Text.Encoding.UTF8);
            _pluginLog.Information($"診断レポートを出力しました: {outputPath}");
            return $"診断レポートを出力しました: {outputPath}";
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "診断レポートの出力に失敗しました。");
            return $"診断レポートの出力に失敗しました: {ex.Message}";
        }
    }

    /// <summary>
    /// 位置情報なしNPCの数を取得
    /// </summary>
    public int GetExcludedNpcCount() => _excludedNpcs.Count;

    /// <summary>
    /// NPCマッチなしショップの数を取得
    /// </summary>
    public int GetUnmatchedShopCount() => _unmatchedShopItems.Count;

    /// <summary>
    /// 検索実行時にアイテムの販売場所詳細をログ出力（デバッグ用）
    /// </summary>
    public void LogSearchDiagnostics(uint itemId)
    {
        if (itemId == 0 || !_isInitialized)
        {
            return;
        }

        var locations = GetShopLocations(itemId);
        var itemName = GetItemName(itemId);
        _pluginLog.Information($"検索診断: ItemId={itemId} Name={itemName} 検出数={locations.Count}");

        // 検出されたNPC情報を出力
        foreach (var loc in locations.Take(5))
        {
            _pluginLog.Information($"  検出: {loc.AreaName} / {loc.NpcName} ({loc.MapX:0.0}, {loc.MapY:0.0}) ShopId={loc.ShopId}");
        }

        // GilShopItem内でこのアイテムを持つ全ショップを調査（位置情報なしのNPCも含めて）
        var gilShopItemSheet = _dataManager.GetSubrowExcelSheet<GilShopItem>();
        if (gilShopItemSheet != null)
        {
            var shopHits = new List<(uint ShopId, int NpcCount, int ValidCount)>();
            foreach (var subrowCollection in gilShopItemSheet)
            {
                var shopId = subrowCollection.RowId;
                foreach (var shopItem in subrowCollection)
                {
                    if (GetItemIdFromGilShopItem(shopItem) != itemId)
                    {
                        continue;
                    }

                    var npcCount = _gilShopNpcInfos.TryGetValue(shopId, out var list) ? list.Count : 0;
                    var validCount = list?.Count(IsValidLocation) ?? 0;
                    shopHits.Add((shopId, npcCount, validCount));

                    // 位置情報なしのNPCがある場合は詳細を出力
                    if (list != null && npcCount > validCount)
                    {
                        foreach (var npc in list.Where(n => !IsValidLocation(n)))
                        {
                            _pluginLog.Warning($"  位置情報なしNPC: {npc.NpcName} (ID:{npc.NpcId}) ShopId={shopId} Territory={npc.TerritoryTypeId} Area={npc.AreaName}");
                        }
                    }
                    break;
                }
            }

            if (shopHits.Count > 0)
            {
                _pluginLog.Information($"GilShopヒット: {string.Join(", ", shopHits.Select(h => $"Shop{h.ShopId}(NPC:{h.NpcCount}/Valid:{h.ValidCount})"))}");
            }
        }
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

                    // 診断用：NPCマッチなしショップを記録
                    if (!_unmatchedShopItems.TryGetValue(shopId, out var unmatchedItems))
                    {
                        unmatchedItems = new List<uint>();
                        _unmatchedShopItems[shopId] = unmatchedItems;
                    }
                    if (unmatchedItems.Count < 3) // 各ショップ3アイテムまで記録
                    {
                        unmatchedItems.Add(itemId);
                    }

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
                        // 診断用：位置情報なしNPCを記録（重複除外）
                        if (!_excludedNpcs.Any(e => e.NpcId == npcInfo.NpcId && e.ShopId == npcInfo.ShopId))
                        {
                            _excludedNpcs.Add((npcInfo.NpcId, npcInfo.NpcName, npcInfo.ShopId, npcInfo.ShopName));
                        }
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
                        conditionNote,
                        npcInfo.IsManuallyAdded));

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
                            // 診断用：位置情報なしNPCを記録（重複除外）
                            if (!_excludedNpcs.Any(e => e.NpcId == npcInfo.NpcId && e.ShopId == npcInfo.ShopId))
                            {
                                _excludedNpcs.Add((npcInfo.NpcId, npcInfo.NpcName, npcInfo.ShopId, npcInfo.ShopName));
                            }
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
                            costNote,
                            npcInfo.IsManuallyAdded));

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
            locInfo?.MapY ?? 0,
            locInfo?.IsManuallyAdded ?? false);

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
        float MapY,
        bool IsManuallyAdded = false);

    /// <summary>
    /// ゲームデータに位置情報がないNPCのフォールバック位置データ
    /// </summary>
    private static readonly Dictionary<uint, (uint TerritoryId, string AreaName, float X, float Y)> ManualNpcLocations = new()
    {
        // リムサ・ロミンサ下甲板のよろず屋（オーシャンフィッシング関連）
        // NPC ID 1005422 - Merchant & Mender at Limsa Lominsa Lower Decks (3.3, 12.9)
        { 1005422, (129, "リムサ・ロミンサ：下甲板層", 3.3f, 12.9f) },
    };

    private Dictionary<uint, NpcLocationInfo> BuildNpcLocationMapping(ExcelSheet<Level> levelSheet)
    {
        var result = new Dictionary<uint, NpcLocationInfo>();

        // Step 0: 手動登録のNPC位置を先に追加
        AddManualNpcLocations(result);

        // Step 1: Level sheetからNPC位置を取得
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

        _pluginLog.Information($"Level sheetからNPC位置取得: {result.Count}件");

        // Step 2: LGBファイルからNPC位置を補完
        var lgbAddedCount = BuildNpcLocationFromLgbFiles(result);
        _pluginLog.Information($"LGBファイルからNPC位置追加: {lgbAddedCount}件");

        return result;
    }

    private void AddManualNpcLocations(Dictionary<uint, NpcLocationInfo> result)
    {
        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = _dataManager.GetExcelSheet<Map>();
        if (territorySheet == null || mapSheet == null)
        {
            return;
        }

        foreach (var (npcId, locData) in ManualNpcLocations)
        {
            if (result.ContainsKey(npcId))
            {
                continue; // 既に位置情報がある場合はスキップ
            }

            var territory = territorySheet.GetRow(locData.TerritoryId);
            if (territory.RowId == 0)
            {
                _pluginLog.Warning($"手動NPC位置: Territory {locData.TerritoryId} が見つかりません (NPC:{npcId})");
                continue;
            }

            // このTerritoryに対応するMapを検索
            uint mapId = 0;
            foreach (var map in mapSheet)
            {
                if (map.TerritoryType.RowId == locData.TerritoryId)
                {
                    mapId = map.RowId;
                    break;
                }
            }

            if (mapId == 0)
            {
                _pluginLog.Warning($"手動NPC位置: Territory {locData.TerritoryId} のMapが見つかりません (NPC:{npcId})");
                continue;
            }

            result[npcId] = new NpcLocationInfo(
                locData.TerritoryId,
                locData.AreaName,
                string.Empty, // SubAreaName
                mapId,
                locData.X,
                locData.Y,
                IsManuallyAdded: true);

            _pluginLog.Information($"手動NPC位置追加: ID={npcId} @ {locData.AreaName} ({locData.X}, {locData.Y})");
        }
    }

    private int BuildNpcLocationFromLgbFiles(Dictionary<uint, NpcLocationInfo> result)
    {
        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = _dataManager.GetExcelSheet<Map>();
        if (territorySheet == null || mapSheet == null)
        {
            return 0;
        }

        var addedCount = 0;
        var processedTerritories = 0;
        var failedFiles = 0;

        // 調査対象のNPC ID（よろず屋など）
        var targetNpcIds = new HashSet<uint> { 1005422, 1032822 };

        foreach (var territory in territorySheet)
        {
            if (territory.RowId == 0)
            {
                continue;
            }

            var bg = territory.Bg.ToString();
            if (string.IsNullOrEmpty(bg))
            {
                continue;
            }

            // planevent.lgbとbg.lgbの両方を試行
            var lgbPaths = GetLgbFilePaths(bg);
            if (lgbPaths.Count == 0)
            {
                continue;
            }

            var territoryProcessed = false;
            foreach (var lgbPath in lgbPaths)
            {
                try
                {
                    var lgbFile = _dataManager.GetFile<LgbFile>(lgbPath);
                    if (lgbFile == null)
                    {
                        continue;
                    }

                    if (!territoryProcessed)
                    {
                        processedTerritories++;
                        territoryProcessed = true;
                    }

                    addedCount += ParseLgbFileForNpcLocations(lgbFile, territory, mapSheet, result, targetNpcIds);
                }
                catch (Exception ex)
                {
                    failedFiles++;
                    if (failedFiles <= 3)
                    {
                        _pluginLog.Debug($"LGBファイル解析エラー: {lgbPath} - {ex.Message}");
                    }
                }
            }
        }

        _pluginLog.Information($"LGB処理完了: Territory={processedTerritories}, 失敗={failedFiles}");
        return addedCount;
    }

    private static List<string> GetLgbFilePaths(string bg)
    {
        // パス例: ffxiv/sea_s1/twn/s1t1/level/s1t1
        // 出力: bg/ffxiv/sea_s1/twn/s1t1/level/planevent.lgb, bg.lgb
        var result = new List<string>();
        var levelIndex = bg.IndexOf("/level/", StringComparison.Ordinal);
        if (levelIndex < 0)
        {
            return result;
        }

        var basePath = $"bg/{bg[..(levelIndex + 7)]}";
        result.Add($"{basePath}planevent.lgb");
        result.Add($"{basePath}bg.lgb");
        return result;
    }

    private int ParseLgbFileForNpcLocations(
        LgbFile lgbFile,
        TerritoryType territory,
        ExcelSheet<Map> mapSheet,
        Dictionary<uint, NpcLocationInfo> result,
        HashSet<uint> targetNpcIds)
    {
        var addedCount = 0;

        // デフォルトマップを取得
        Map? defaultMap = null;
        foreach (var map in mapSheet)
        {
            if (map.TerritoryType.RowId == territory.RowId)
            {
                defaultMap = map;
                break;
            }
        }

        if (defaultMap == null)
        {
            return 0;
        }

        var areaName = territory.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
        var subAreaName = defaultMap.Value.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty;

        foreach (var layer in lgbFile.Layers)
        {
            foreach (var instanceObj in layer.InstanceObjects)
            {
                // EventNPCのみ処理
                if (instanceObj.AssetType != LayerEntryType.EventNPC)
                {
                    continue;
                }

                // NPC IDを取得
                var npcId = GetNpcIdFromInstanceObject(instanceObj);
                if (npcId == 0)
                {
                    continue;
                }

                // 調査対象のNPCが見つかった場合はログ出力
                if (targetNpcIds.Contains(npcId))
                {
                    var pos = instanceObj.Transform.Translation;
                    _pluginLog.Warning($"調査対象NPC発見: ID={npcId} @ {areaName} (Territory:{territory.RowId}, Pos:{pos.X:F1},{pos.Z:F1})");
                }

                // 既に位置情報がある場合はスキップ（Level sheetの方が正確）
                if (result.ContainsKey(npcId))
                {
                    continue;
                }

                // 座標を取得
                var pos2 = instanceObj.Transform.Translation;
                var mapX = ConvertToMapCoordinate(pos2.X, defaultMap.Value.OffsetX, defaultMap.Value.SizeFactor);
                var mapY = ConvertToMapCoordinate(pos2.Z, defaultMap.Value.OffsetY, defaultMap.Value.SizeFactor);

                result[npcId] = new NpcLocationInfo(
                    territory.RowId,
                    areaName,
                    subAreaName,
                    defaultMap.Value.RowId,
                    mapX,
                    mapY);

                addedCount++;
            }
        }

        return addedCount;
    }

    private static uint GetNpcIdFromInstanceObject(LayerCommon.InstanceObject instanceObj)
    {
        // ENPCInstanceObject -> NPCInstanceObject -> BaseId
        if (instanceObj.Object is not LayerCommon.ENPCInstanceObject eventNpc)
        {
            return 0;
        }

        // ParentData.ParentData.BaseId からNPC IDを取得
        try
        {
            return eventNpc.ParentData.ParentData.BaseId;
        }
        catch
        {
            return 0;
        }
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

    /// <summary>
    /// ワールド座標からマップ座標に変換する
    /// </summary>
    public (float X, float Y) WorldToMapCoordinates(uint territoryTypeId, float worldX, float worldZ)
    {
        var mapSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
        if (mapSheet == null)
        {
            return (0, 0);
        }

        var mapId = GetDefaultMapId(territoryTypeId);
        if (mapId == 0)
        {
            return (0, 0);
        }

        var map = mapSheet.GetRow(mapId);
        if (map.RowId == 0)
        {
            return (0, 0);
        }

        var mapX = ConvertToMapCoordinate(worldX, map.OffsetX, map.SizeFactor);
        var mapY = ConvertToMapCoordinate(worldZ, map.OffsetY, map.SizeFactor);
        return (mapX, mapY);
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

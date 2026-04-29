using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Reflection;

namespace ShopDataSnapshot.Core;

public sealed class ShopSnapshotBuilder
{
    private const int StainItem1ColumnIndex = 3;
    private const int StainItem2ColumnIndex = 4;
    private const int TerritoryPlaceNameColumnIndex = 5;
    private const int MapSubPlaceNameColumnIndex = 2;
    private const int MapSizeFactorColumnIndex = 7;
    private const int MapOffsetXColumnIndex = 8;
    private const int MapOffsetYColumnIndex = 9;

    public ShopSnapshotDocument Build(SnapshotOptions options)
    {
        var language = ParseLanguage(options.Language);
        var dataPath = ResolveDataPath(options.GamePath);
        var gameData = new GameData(dataPath, new LuminaOptions
        {
            DefaultExcelLanguage = language,
        });

        var stats = new RawSheetLoadStats(0, 0);
        var itemSheet = RequireSheet<Item>(gameData, language, "Item");
        var stainItemIds = LoadStainItemIds(gameData, language, out var stainRawFallbackUsed);
        var gilShopSheet = RequireSheet<GilShop>(gameData, language, "GilShop");
        var specialShopSheet = RequireSheet<SpecialShop>(gameData, language, "SpecialShop");
        var npcBaseSheet = RequireSheet<ENpcBase>(gameData, language, "ENpcBase");
        var npcResidentSheet = RequireSheet<ENpcResident>(gameData, language, "ENpcResident");
        var levelSheet = RequireSheet<Level>(gameData, language, "Level");
        var gilShopItemSheet = gameData.GetSubrowExcelSheet<GilShopItem>(language)
            ?? throw new InvalidOperationException("Required sheet was not found: GilShopItem");

        var itemNames = BuildItemNameIndex(itemSheet);
        var colorantDetections = BuildColorantDetectionIndex(stainItemIds, stainRawFallbackUsed, itemNames);
        var npcLocations = BuildNpcLocationIndex(gameData, language, levelSheet, ref stats);
        var mappings = BuildNpcShopMappings(
            npcBaseSheet,
            npcResidentSheet,
            gilShopSheet,
            specialShopSheet,
            npcLocations);

        var records = new List<ShopSnapshotRecord>();
        ProcessGilShopItems(gilShopItemSheet, mappings.GilShopInfos, itemNames, colorantDetections.Detections, records);
        ProcessSpecialShops(specialShopSheet, mappings.SpecialShopInfos, itemNames, colorantDetections.Detections, records);

        var sorted = records
            .OrderBy(record => record.ItemId)
            .ThenBy(record => record.ShopType, StringComparer.Ordinal)
            .ThenBy(record => record.ShopId)
            .ThenBy(record => record.NpcName, StringComparer.Ordinal)
            .ThenBy(record => record.TerritoryId)
            .ThenBy(record => record.ShopName, StringComparer.Ordinal)
            .ThenBy(record => record.MapId ?? 0)
            .ThenBy(record => record.MapX)
            .ThenBy(record => record.MapY)
            .ToList();

        var missingLocationRecords = sorted
            .Where(record => record.TerritoryId == 0 || record.MapX == 0 || record.MapY == 0)
            .ToList();
        var summary = new ShopSnapshotSummary(
            sorted.Count,
            sorted.Select(record => record.ItemId).Distinct().Count(),
            sorted.Where(record => record.IsColorant).Select(record => record.ItemId).Distinct().Count(),
            colorantDetections.StainSheetItemIds,
            colorantDetections.StainRawFallbackUsed,
            sorted
                .Where(record => record.ColorantDetection == "nameFallback")
                .Select(record => record.ItemId)
                .Distinct()
                .Count(),
            sorted.Count(record => record.ShopType == "gil"),
            sorted.Count(record => record.ShopType == "special"),
            missingLocationRecords.Count,
            missingLocationRecords.Select(record => record.NpcName).Distinct(StringComparer.Ordinal).Count(),
            missingLocationRecords.Select(record => (record.ShopType, record.ShopId)).Distinct().Count(),
            missingLocationRecords
                .GroupBy(record => record.ShopType, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            stats.MapInfoLoadedCount,
            stats.TerritoryNameLoadedCount,
            missingLocationRecords
                .Take(20)
                .Select(record => new MissingNpcLocationSample(
                    record.ShopType,
                    record.ShopId,
                    record.ShopName,
                    record.NpcName,
                    record.ItemId,
                    record.ItemName))
                .ToList());

        return new ShopSnapshotDocument(
            DateTimeOffset.UtcNow.ToString("O"),
            Path.GetFullPath(options.GamePath),
            LanguageToOption(language),
            typeof(GameData).Assembly.GetName().Version?.ToString() ?? "unknown",
            sorted.Count,
            summary,
            sorted);
    }

    private static string ResolveDataPath(string gamePath)
    {
        var fullPath = Path.GetFullPath(gamePath);
        if (Directory.Exists(Path.Combine(fullPath, "ffxiv")))
        {
            return fullPath;
        }

        var sqpackPath = Path.Combine(fullPath, "game", "sqpack");
        if (Directory.Exists(sqpackPath))
        {
            return sqpackPath;
        }

        throw new DirectoryNotFoundException($"Could not find sqpack directory under game path: {gamePath}");
    }

    private static ExcelSheet<T> RequireSheet<T>(GameData gameData, Language language, string sheetName)
        where T : struct, IExcelRow<T>
    {
        return gameData.GetExcelSheet<T>(language)
            ?? gameData.GetExcelSheet<T>()
            ?? throw new InvalidOperationException($"Required sheet was not found: {sheetName}");
    }

    private static IReadOnlySet<uint> LoadStainItemIds(
        GameData gameData,
        Language language,
        out bool rawFallbackUsed)
    {
        var typedSheet = gameData.GetExcelSheet<Stain>(language) ?? gameData.GetExcelSheet<Stain>();
        if (typedSheet != null)
        {
            rawFallbackUsed = false;
            var typedIds = new HashSet<uint>();
            foreach (var stain in typedSheet)
            {
                AddStainItemIdsFromObject(stain, typedIds);
            }

            if (typedIds.Count > 0)
            {
                return typedIds;
            }
        }

        rawFallbackUsed = true;
        // Stain changes frequently around dye updates. Use the raw sheet as a
        // snapshot fallback so colorant detection remains Stain-derived when checksums drift.
        var rawSheet = gameData.Excel.GetRawSheet("Stain", language)
            ?? throw new InvalidOperationException("Required sheet was not found: Stain");
        var createRowMethod = rawSheet.GetType().GetMethod(
            "UnsafeCreateRowAt",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (createRowMethod == null)
        {
            throw new InvalidOperationException("Lumina raw sheet fallback is unavailable for StainTransient.");
        }

        var genericCreateRow = createRowMethod.MakeGenericMethod(typeof(RawRow));
        var itemIds = new HashSet<uint>();
        for (var index = 0; index < rawSheet.Count; index++)
        {
            if (genericCreateRow.Invoke(rawSheet, new object[] { index }) is not RawRow row || row.RowId == 0)
            {
                continue;
            }

            AddRawColumnItemId(row, StainItem1ColumnIndex, itemIds);
            AddRawColumnItemId(row, StainItem2ColumnIndex, itemIds);
        }

        return itemIds;
    }

    private static void AddStainItemIdsFromObject(object stain, HashSet<uint> itemIds)
    {
        foreach (var propertyName in new[] { "Item", "ItemId", "ItemID", "Item1", "Item2" })
        {
            var property = stain.GetType().GetProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            var itemId = SnapshotReflection.GetRowId(property.GetValue(stain));
            if (itemId != 0)
            {
                itemIds.Add(itemId);
            }
        }
    }

    private static void AddRawColumnItemId(RawRow row, int columnIndex, HashSet<uint> itemIds)
    {
        var value = row.ReadColumn(columnIndex);
        switch (value)
        {
            case uint uintValue when uintValue != 0:
                itemIds.Add(uintValue);
                break;
            case int intValue when intValue > 0:
                itemIds.Add((uint)intValue);
                break;
        }
    }

    private static Language ParseLanguage(string language)
    {
        return language.Trim().ToLowerInvariant() switch
        {
            "ja" or "jp" or "japanese" => Language.Japanese,
            "en" or "english" => Language.English,
            "de" or "german" => Language.German,
            "fr" or "french" => Language.French,
            "zh" or "chs" or "chinese-simplified" => Language.ChineseSimplified,
            "zht" or "cht" or "tw" or "chinese-traditional" => Language.ChineseTraditional,
            "ko" or "kr" or "korean" => Language.Korean,
            _ => throw new ArgumentException($"Unsupported language: {language}"),
        };
    }

    private static string LanguageToOption(Language language)
    {
        return language switch
        {
            Language.Japanese => "ja",
            Language.English => "en",
            Language.German => "de",
            Language.French => "fr",
            Language.ChineseSimplified => "zh",
            Language.ChineseTraditional => "zht",
            Language.Korean => "ko",
            _ => language.ToString(),
        };
    }

    private static Dictionary<uint, string> BuildItemNameIndex(ExcelSheet<Item> itemSheet)
    {
        var result = new Dictionary<uint, string>();
        foreach (var item in itemSheet)
        {
            var name = item.Name.ToString();
            if (item.RowId != 0 && !string.IsNullOrWhiteSpace(name))
            {
                result[item.RowId] = name;
            }
        }

        return result;
    }

    private static ColorantDetectionIndex BuildColorantDetectionIndex(
        IReadOnlySet<uint> stainItemIds,
        bool stainRawFallbackUsed,
        IReadOnlyDictionary<uint, string> itemNames)
    {
        var result = new Dictionary<uint, string>();
        foreach (var itemId in stainItemIds)
        {
            if (itemId != 0)
            {
                result[itemId] = "stainSheet";
            }
        }

        foreach (var (itemId, name) in itemNames)
        {
            if (result.ContainsKey(itemId))
            {
                continue;
            }

            if (IsLikelyColorantItemName(name))
            {
                result[itemId] = "nameFallback";
            }
        }

        var nameFallbackCount = result.Count(kvp => kvp.Value == "nameFallback");
        return new ColorantDetectionIndex(result, stainItemIds.Count, stainRawFallbackUsed, nameFallbackCount);
    }

    private static bool IsLikelyColorantItemName(string name)
    {
        return name.Contains("染料", StringComparison.Ordinal)
            || name.Contains("カララント", StringComparison.Ordinal)
            || name.Contains("Dye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Colorant", StringComparison.OrdinalIgnoreCase);
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
                buffer[length++] = ch;
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static Dictionary<uint, NpcLocationInfo> BuildNpcLocationIndex(
        GameData gameData,
        Language language,
        ExcelSheet<Level> levelSheet,
        ref RawSheetLoadStats stats)
    {
        var result = new Dictionary<uint, NpcLocationInfo>();
        AddManualNpcLocations(result);
        var placeNames = LoadPlaceNameIndex(gameData, language);
        var territoryNames = LoadTerritoryNameIndex(gameData, language, placeNames);
        var mapInfos = LoadMapInfoIndex(gameData, language);
        stats = new RawSheetLoadStats(mapInfos.Count, territoryNames.Count);

        foreach (var level in levelSheet)
        {
            if (level.RowId == 0 || level.Type != 8)
            {
                continue;
            }

            var npcId = level.Object.RowId;
            if (npcId == 0 || result.ContainsKey(npcId))
            {
                continue;
            }

            var territoryId = level.Territory.RowId;
            var mapId = level.Map.RowId;
            if (territoryId == 0 || mapId == 0)
            {
                continue;
            }

            territoryNames.TryGetValue(territoryId, out var areaName);
            areaName ??= string.Empty;
            mapInfos.TryGetValue(mapId, out var mapInfo);
            var subAreaName = mapInfo?.SubAreaName ?? string.Empty;
            var mapX = mapInfo == null ? 0f : MapCoordinateConverter.ConvertFromFloat(level.X, mapInfo.OffsetX, mapInfo.SizeFactor);
            var mapY = mapInfo == null ? 0f : MapCoordinateConverter.ConvertFromFloat(level.Z, mapInfo.OffsetY, mapInfo.SizeFactor);

            result[npcId] = new NpcLocationInfo(
                territoryId,
                areaName,
                subAreaName,
                mapId,
                mapX,
                mapY);
        }

        return result;
    }

    private static Dictionary<uint, string> LoadPlaceNameIndex(GameData gameData, Language language)
    {
        var result = new Dictionary<uint, string>();
        var rawSheet = gameData.Excel.GetRawSheet("PlaceName", language);
        foreach (var row in EnumerateRawRows(rawSheet))
        {
            var name = row.ReadColumn(0)?.ToString() ?? string.Empty;
            if (row.RowId != 0 && !string.IsNullOrWhiteSpace(name))
            {
                result[row.RowId] = name;
            }
        }

        return result;
    }

    private static Dictionary<uint, string> LoadTerritoryNameIndex(
        GameData gameData,
        Language language,
        IReadOnlyDictionary<uint, string> placeNames)
    {
        var result = new Dictionary<uint, string>();
        var rawSheet = gameData.Excel.GetRawSheet("TerritoryType", language);
        foreach (var row in EnumerateRawRows(rawSheet))
        {
            var placeNameId = ConvertRawUInt(row.ReadColumn(TerritoryPlaceNameColumnIndex));
            if (row.RowId != 0 && placeNameId != 0 && placeNames.TryGetValue(placeNameId, out var name))
            {
                result[row.RowId] = name;
            }
        }

        return result;
    }

    private static Dictionary<uint, RawMapInfo> LoadMapInfoIndex(GameData gameData, Language language)
    {
        var placeNames = LoadPlaceNameIndex(gameData, language);
        var result = new Dictionary<uint, RawMapInfo>();
        var rawSheet = gameData.Excel.GetRawSheet("Map", language);
        foreach (var row in EnumerateRawRows(rawSheet))
        {
            if (row.RowId == 0)
            {
                continue;
            }

            var subPlaceNameId = ConvertRawUInt(row.ReadColumn(MapSubPlaceNameColumnIndex));
            placeNames.TryGetValue(subPlaceNameId, out var subAreaName);
            var sizeFactor = ConvertRawUShort(row.ReadColumn(MapSizeFactorColumnIndex));
            if (sizeFactor == 0)
            {
                sizeFactor = 100;
            }

            result[row.RowId] = new RawMapInfo(
                sizeFactor,
                ConvertRawShort(row.ReadColumn(MapOffsetXColumnIndex)),
                ConvertRawShort(row.ReadColumn(MapOffsetYColumnIndex)),
                subAreaName ?? string.Empty);
        }

        return result;
    }

    private static IEnumerable<RawRow> EnumerateRawRows(RawExcelSheet rawSheet)
    {
        var createRowMethod = rawSheet.GetType().GetMethod(
            "UnsafeCreateRowAt",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (createRowMethod == null)
        {
            yield break;
        }

        var genericCreateRow = createRowMethod.MakeGenericMethod(typeof(RawRow));
        for (var index = 0; index < rawSheet.Count; index++)
        {
            if (genericCreateRow.Invoke(rawSheet, new object[] { index }) is RawRow row)
            {
                yield return row;
            }
        }
    }

    private static uint ConvertRawUInt(object? value)
    {
        return value switch
        {
            uint uintValue => uintValue,
            int intValue when intValue >= 0 => (uint)intValue,
            ushort ushortValue => ushortValue,
            short shortValue when shortValue >= 0 => (uint)shortValue,
            byte byteValue => byteValue,
            _ => 0,
        };
    }

    private static ushort ConvertRawUShort(object? value)
    {
        var uintValue = ConvertRawUInt(value);
        return uintValue <= ushort.MaxValue ? (ushort)uintValue : (ushort)0;
    }

    private static short ConvertRawShort(object? value)
    {
        return value switch
        {
            short shortValue => shortValue,
            int intValue when intValue >= short.MinValue && intValue <= short.MaxValue => (short)intValue,
            ushort ushortValue when ushortValue <= short.MaxValue => (short)ushortValue,
            uint uintValue when uintValue <= short.MaxValue => (short)uintValue,
            _ => 0,
        };
    }

    private static void AddManualNpcLocations(Dictionary<uint, NpcLocationInfo> result)
    {
        result[1005422] = new NpcLocationInfo(
            129,
            "リムサ・ロミンサ：下甲板層",
            string.Empty,
            0,
            3.3f,
            12.9f,
            true);
    }

    private static (Dictionary<uint, List<NpcShopInfo>> GilShopInfos, Dictionary<uint, List<NpcShopInfo>> SpecialShopInfos) BuildNpcShopMappings(
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<ENpcResident> npcResidentSheet,
        ExcelSheet<GilShop> gilShopSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        IReadOnlyDictionary<uint, NpcLocationInfo> npcLocations)
    {
        var gilShopIds = new HashSet<uint>();
        var gilShopNames = new Dictionary<uint, string>();
        foreach (var shop in gilShopSheet)
        {
            if (shop.RowId == 0)
            {
                continue;
            }

            gilShopIds.Add(shop.RowId);
            gilShopNames[shop.RowId] = FormatGilShopName(shop.RowId, shop.Name.ToString());
        }

        var specialShopIds = new HashSet<uint>();
        var specialShopNames = new Dictionary<uint, string>();
        foreach (var shop in specialShopSheet)
        {
            if (shop.RowId == 0)
            {
                continue;
            }

            specialShopIds.Add(shop.RowId);
            specialShopNames[shop.RowId] = FormatSpecialShopName(shop.RowId, shop.Name.ToString());
        }

        var npcNames = new Dictionary<uint, string>();
        foreach (var npc in npcResidentSheet)
        {
            var name = npc.Singular.ToString();
            if (npc.RowId != 0 && !string.IsNullOrWhiteSpace(name))
            {
                npcNames[npc.RowId] = name;
            }
        }

        var gilResult = new Dictionary<uint, List<NpcShopInfo>>();
        var specialResult = new Dictionary<uint, List<NpcShopInfo>>();
        foreach (var npcBase in npcBaseSheet)
        {
            if (npcBase.RowId == 0 || !npcNames.TryGetValue(npcBase.RowId, out var npcName))
            {
                continue;
            }

            npcLocations.TryGetValue(npcBase.RowId, out var location);
            var seenGil = new HashSet<uint>();
            var seenSpecial = new HashSet<uint>();
            foreach (var dataValue in npcBase.ENpcData)
            {
                var rawValue = SnapshotReflection.GetRowId(dataValue);
                if (rawValue == 0)
                {
                    continue;
                }

                if (gilShopIds.Contains(rawValue))
                {
                    if (seenGil.Add(rawValue))
                    {
                        AddNpcShopInfo(gilResult, npcBase.RowId, npcName, rawValue, gilShopNames[rawValue], location);
                    }
                    continue;
                }

                if (specialShopIds.Contains(rawValue))
                {
                    if (seenSpecial.Add(rawValue))
                    {
                        AddNpcShopInfo(specialResult, npcBase.RowId, npcName, rawValue, specialShopNames[rawValue], location);
                    }
                    continue;
                }

                var lowerId = rawValue & 0xFFFF;
                if (lowerId != 0 && gilShopIds.Contains(lowerId) && seenGil.Add(lowerId))
                {
                    AddNpcShopInfo(gilResult, npcBase.RowId, npcName, lowerId, gilShopNames[lowerId], location);
                }
            }
        }

        return (gilResult, specialResult);
    }

    private static void AddNpcShopInfo(
        Dictionary<uint, List<NpcShopInfo>> result,
        uint npcId,
        string npcName,
        uint shopId,
        string shopName,
        NpcLocationInfo? location)
    {
        if (!result.TryGetValue(shopId, out var list))
        {
            list = new List<NpcShopInfo>();
            result[shopId] = list;
        }

        if (list.Any(info => info.NpcId == npcId))
        {
            return;
        }

        list.Add(new NpcShopInfo(npcId, npcName, shopId, shopName, location));
    }

    private static void ProcessGilShopItems(
        SubrowExcelSheet<GilShopItem> gilShopItemSheet,
        IReadOnlyDictionary<uint, List<NpcShopInfo>> shopInfos,
        IReadOnlyDictionary<uint, string> itemNames,
        IReadOnlyDictionary<uint, string> colorantDetections,
        List<ShopSnapshotRecord> records)
    {
        foreach (var subrowCollection in gilShopItemSheet)
        {
            var shopId = subrowCollection.RowId;
            if (shopId == 0 || !shopInfos.TryGetValue(shopId, out var npcInfoList))
            {
                continue;
            }

            foreach (var shopItem in subrowCollection)
            {
                var itemId = shopItem.Item.RowId;
                if (itemId == 0 || !itemNames.TryGetValue(itemId, out var itemName))
                {
                    continue;
                }

                var price = shopItem.Item.ValueNullable?.PriceMid ?? 0;
                var conditionNote = BuildGilShopConditionNote(shopItem);
                AddRecords(
                    records,
                    itemId,
                    itemName,
                    colorantDetections,
                    "gil",
                    shopId,
                    null,
                    npcInfoList,
                    (int)Math.Min(price, int.MaxValue),
                    conditionNote);
            }
        }
    }

    private static void ProcessSpecialShops(
        ExcelSheet<SpecialShop> specialShopSheet,
        IReadOnlyDictionary<uint, List<NpcShopInfo>> shopInfos,
        IReadOnlyDictionary<uint, string> itemNames,
        IReadOnlyDictionary<uint, string> colorantDetections,
        List<ShopSnapshotRecord> records)
    {
        foreach (var shop in specialShopSheet)
        {
            if (shop.RowId == 0 || !shopInfos.TryGetValue(shop.RowId, out var npcInfoList))
            {
                continue;
            }

            var shopName = FormatSpecialShopName(shop.RowId, shop.Name.ToString());
            foreach (var entry in shop.Item)
            {
                var conditionNote = BuildSpecialShopCostNote(entry, itemNames);
                foreach (var itemId in SnapshotReflection.GetItemIdsFromProperty(entry, "ReceiveItems", "ItemReceive", "OutputItem", "Item", "Receive"))
                {
                    if (itemId == 0 || !itemNames.TryGetValue(itemId, out var itemName))
                    {
                        continue;
                    }

                    AddRecords(
                        records,
                        itemId,
                        itemName,
                        colorantDetections,
                        "special",
                        shop.RowId,
                        shopName,
                        npcInfoList,
                        0,
                        conditionNote);
                }
            }
        }
    }

    private static void AddRecords(
        List<ShopSnapshotRecord> records,
        uint itemId,
        string itemName,
        IReadOnlyDictionary<uint, string> colorantDetections,
        string shopType,
        uint shopId,
        string? overrideShopName,
        IReadOnlyList<NpcShopInfo> npcInfoList,
        int price,
        string? conditionNote)
    {
        var colorantDetection = colorantDetections.TryGetValue(itemId, out var detection) ? detection : "none";
        foreach (var npcInfo in npcInfoList)
        {
            var location = npcInfo.Location;
            records.Add(new ShopSnapshotRecord(
                itemId,
                itemName,
                colorantDetection != "none",
                colorantDetection,
                shopType,
                shopId,
                overrideShopName ?? npcInfo.ShopName,
                npcInfo.NpcName,
                location?.TerritoryId ?? 0,
                location?.AreaName ?? string.Empty,
                location?.MapX ?? 0,
                location?.MapY ?? 0,
                price,
                conditionNote,
                location?.SubAreaName,
                location?.MapId,
                location?.IsManuallyAdded));
        }
    }

    private static string BuildGilShopConditionNote(GilShopItem shopItem)
    {
        if (shopItem.StateRequired != 0)
        {
            return $"条件ID: {shopItem.StateRequired}";
        }

        return shopItem.Patch != 0 ? $"パッチ {shopItem.Patch / 100f:0.0}以降" : "条件なし";
    }

    private static string BuildSpecialShopCostNote(object entry, IReadOnlyDictionary<uint, string> itemNames)
    {
        var costs = new List<string>();
        var property = entry.GetType().GetProperty("ItemCosts");
        var value = property?.GetValue(entry);
        if (value is not System.Collections.IEnumerable enumerable)
        {
            return "条件なし";
        }

        foreach (var cost in enumerable)
        {
            var itemId = SnapshotReflection.GetRowId(cost);
            var count = SnapshotReflection.GetCount(cost);
            if (itemId == 0 || count <= 0)
            {
                continue;
            }

            var itemName = itemNames.TryGetValue(itemId, out var name) ? name : $"アイテム#{itemId}";
            costs.Add($"{itemName} x{count}");
        }

        return costs.Count == 0 ? "条件なし" : string.Join(", ", costs);
    }

    private static string FormatGilShopName(uint shopId, string shopName)
    {
        return string.IsNullOrWhiteSpace(shopName) ? $"GilShop#{shopId}" : shopName;
    }

    private static string FormatSpecialShopName(uint shopId, string? shopName = null)
    {
        return string.IsNullOrWhiteSpace(shopName) ? $"SpecialShop#{shopId}" : shopName;
    }
}

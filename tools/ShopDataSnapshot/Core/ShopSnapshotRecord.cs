namespace ShopDataSnapshot.Core;

public sealed record ShopSnapshotDocument(
    string GeneratedAt,
    string GamePath,
    string ResolvedDataPath,
    string Language,
    string LuminaVersion,
    int RecordCount,
    ShopSnapshotSummary Summary,
    IReadOnlyList<ShopSnapshotRecord> Records);

public sealed record ShopSnapshotSummary(
    int TotalRecords,
    int UniqueItems,
    int ColorantItems,
    int StainSheetItemIds,
    bool StainRawFallbackUsed,
    int NameFallbackColorantItems,
    int GilShopRecords,
    int SpecialShopRecords,
    int MissingNpcLocationRecords,
    int MissingNpcLocationUniqueNpcs,
    int MissingNpcLocationUniqueShops,
    IReadOnlyDictionary<string, int> MissingNpcLocationByShopType,
    int MapInfoLoadedCount,
    int TerritoryNameLoadedCount,
    IReadOnlyList<MissingNpcLocationSample> MissingNpcLocationSamples);

public sealed record MissingNpcLocationSample(
    string ShopType,
    uint ShopId,
    string ShopName,
    string NpcName,
    uint ItemId,
    string ItemName);

public sealed record ShopSnapshotRecord(
    uint ItemId,
    string ItemName,
    bool IsColorant,
    string ColorantDetection,
    string ShopType,
    uint ShopId,
    string ShopName,
    string NpcName,
    uint TerritoryId,
    string AreaName,
    float MapX,
    float MapY,
    int Price,
    string? ConditionNote,
    string? SubAreaName,
    uint? MapId,
    bool? IsManuallyAdded);

internal sealed record NpcLocationInfo(
    uint TerritoryId,
    string AreaName,
    string SubAreaName,
    uint MapId,
    float MapX,
    float MapY,
    bool IsManuallyAdded = false);

internal sealed record NpcShopInfo(
    uint NpcId,
    string NpcName,
    uint ShopId,
    string ShopName,
    NpcLocationInfo? Location);

internal sealed record RawMapInfo(
    ushort SizeFactor,
    short OffsetX,
    short OffsetY,
    string SubAreaName);

internal sealed record ColorantDetectionIndex(
    IReadOnlyDictionary<uint, string> Detections,
    int StainSheetItemIds,
    bool StainRawFallbackUsed);

internal sealed record RawSheetLoadStats(
    int MapInfoLoadedCount,
    int TerritoryNameLoadedCount);

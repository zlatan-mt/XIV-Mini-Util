// Path: projects/XIV-Mini-Util/Models/DomainModels.cs
// Description: 設定とサービス間で共有するデータモデルを定義する
// Reason: 型の役割を明確にしてロジックの見通しを良くするため
// RELEVANT FILES: projects/XIV-Mini-Util/Configuration.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Services/InventoryService.cs
using FFXIVClientStructs.FFXIV.Client.Game;

namespace XivMiniUtil;

public enum JobCondition
{
    Any = 0,
    CrafterOnly = 1,
    BattleOnly = 2,
}

public enum DesynthTargetMode
{
    All = 0,
    Count = 1,
}

public sealed record InventoryItemInfo(
    uint ItemId,
    string Name,
    int ItemLevel,
    ushort Spiritbond,
    int Quantity,
    InventoryType Container,
    int Slot,
    bool CanExtractMateria,
    bool CanDesynth);

public sealed record DesynthWarningInfo(
    string ItemName,
    int ItemLevel,
    int MaxItemLevel);

public sealed record DesynthOptions(
    int MinLevel,
    int MaxLevel,
    bool SkipHighLevelWarning,
    DesynthTargetMode TargetMode,
    int TargetCount);

public sealed record DesynthResult(
    int ProcessedCount,
    int SkippedCount,
    List<string> Errors);

public sealed record ShopLocationInfo(
    uint ShopId,
    string ShopName,
    string NpcName,
    uint TerritoryTypeId,
    string AreaName,
    string SubAreaName,
    uint MapId,
    float MapX,
    float MapY,
    int Price,
    string ConditionNote,
    bool IsManuallyAdded = false);

public sealed record ShopTerritoryInfo(
    uint TerritoryTypeId,
    string TerritoryName);

public sealed record SearchResult(
    uint ItemId,
    string ItemName,
    IReadOnlyList<ShopLocationInfo> Locations,
    bool Success,
    string? ErrorMessage);

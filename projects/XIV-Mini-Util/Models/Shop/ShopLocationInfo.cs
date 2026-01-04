// Path: projects/XIV-Mini-Util/Models/Shop/ShopLocationInfo.cs
// Description: ショップ位置情報
namespace XivMiniUtil;

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

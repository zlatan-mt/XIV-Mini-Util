// Path: projects/XIV-Mini-Util/Services/ShopDataModels.cs
// Description: ショップデータ構築で共有する内部モデル
// Reason: ShopDataCacheと診断クラスで共通利用するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
namespace XivMiniUtil.Services;

internal sealed record NpcShopInfo(
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

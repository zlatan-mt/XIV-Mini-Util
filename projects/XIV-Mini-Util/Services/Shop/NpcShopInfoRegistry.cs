// Path: projects/XIV-Mini-Util/Services/NpcShopInfoRegistry.cs
// Description: NPCショップ情報の追加と重複管理を担当する
// Reason: ShopDataCacheから追加ロジックを分離するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using System.Collections.Generic;

namespace XivMiniUtil.Services.Shop;

internal sealed class NpcShopInfoRegistry
{
    public bool TryAdd(
        Dictionary<uint, List<NpcShopInfo>> result,
        uint npcId,
        string npcName,
        uint shopId,
        string shopName,
        NpcLocationInfo? locInfo)
    {
        if (!result.TryGetValue(shopId, out var list))
        {
            list = new List<NpcShopInfo>();
            result[shopId] = list;
        }

        if (list.Exists(info => info.NpcId == npcId))
        {
            return false;
        }

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

        list.Add(info);
        return true;
    }
}

// Path: projects/XIV-Mini-Util/Services/ShopLocationValidator.cs
// Description: NPC位置情報の妥当性チェックを提供する
// Reason: 位置検証ロジックの共通化のため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using System;

namespace XivMiniUtil.Services;

internal static class ShopLocationValidator
{
    public static bool IsValid(NpcShopInfo npcInfo)
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
}

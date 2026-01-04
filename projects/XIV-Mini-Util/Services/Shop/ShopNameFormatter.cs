// Path: projects/XIV-Mini-Util/Services/ShopNameFormatter.cs
// Description: ショップ名のフォーマットを共通化する
// Reason: 生成ロジックの重複を避けるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/EnpcDataShopResolver.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using System.Collections.Generic;

namespace XivMiniUtil.Services.Shop;

internal static class ShopNameFormatter
{
    public static string GetGilShopName(uint shopId, IReadOnlyDictionary<uint, string> gilShopNames)
    {
        return gilShopNames.TryGetValue(shopId, out var name) && !string.IsNullOrEmpty(name)
            ? name
            : $"ショップ#{shopId}";
    }

    public static string GetSpecialShopName(uint shopId, string? candidateName = null)
    {
        return !string.IsNullOrEmpty(candidateName)
            ? candidateName
            : $"特殊ショップ#{shopId}";
    }
}

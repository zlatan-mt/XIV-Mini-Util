// Path: projects/XIV-Mini-Util/Services/EnpcDataShopResolver.cs
// Description: ENpcDataからショップ参照を抽出する
// Reason: ShopDataCacheからENpcData解析責務を分離するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace XivMiniUtil.Services;

internal sealed class EnpcDataShopResolver
{
    public IReadOnlyList<NpcShopReference> ResolveShopReferences(
        ENpcBase npcBase,
        HashSet<uint> gilShopIds,
        HashSet<uint> specialShopIds,
        IReadOnlyDictionary<uint, string> gilShopNames)
    {
        var results = new List<NpcShopReference>();
        var seenGil = new HashSet<uint>();
        var seenSpecial = new HashSet<uint>();

        foreach (var dataValue in npcBase.ENpcData)
        {
            var rawValue = GetRawDataValue(dataValue);
            if (rawValue == 0)
            {
                continue;
            }

            if (gilShopIds.Contains(rawValue))
            {
                if (seenGil.Add(rawValue))
                {
                    results.Add(new NpcShopReference(rawValue, ShopNameFormatter.GetGilShopName(rawValue, gilShopNames), true));
                }
                continue;
            }

            if (specialShopIds.Contains(rawValue))
            {
                if (seenSpecial.Add(rawValue))
                {
                    results.Add(new NpcShopReference(rawValue, ShopNameFormatter.GetSpecialShopName(rawValue), false));
                }
                continue;
            }

            var lowerId = rawValue & 0xFFFF;
            if (lowerId != 0 && gilShopIds.Contains(lowerId))
            {
                if (seenGil.Add(lowerId))
                {
                    results.Add(new NpcShopReference(lowerId, ShopNameFormatter.GetGilShopName(lowerId, gilShopNames), true));
                }
            }
        }

        return results;
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
}

internal sealed record NpcShopReference(uint ShopId, string ShopName, bool IsGilShop);

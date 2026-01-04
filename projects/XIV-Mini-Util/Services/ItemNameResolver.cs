// Path: projects/XIV-Mini-Util/Services/ItemNameResolver.cs
// Description: Item名の取得と例外処理を共通化する
// Reason: 例外処理の重複を避け、取得ロジックを集約するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace XivMiniUtil.Services;

internal sealed class ItemNameResolver
{
    public bool TryCacheName(ExcelSheet<Item> itemSheet, uint itemId, Dictionary<uint, string> cache, out string itemName)
    {
        itemName = string.Empty;
        if (itemId == 0)
        {
            return false;
        }

        try
        {
            var row = itemSheet.GetRow(itemId);
            if (row.RowId == 0)
            {
                return false;
            }

            itemName = row.Name.ToString();
            if (!cache.ContainsKey(itemId))
            {
                cache[itemId] = itemName;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetName(ExcelSheet<Item> itemSheet, uint itemId, Dictionary<uint, string> cache)
    {
        if (itemId == 0)
        {
            return string.Empty;
        }

        if (cache.TryGetValue(itemId, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
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
                cache[itemId] = name;
            }

            return name;
        }
        catch
        {
            return string.Empty;
        }
    }
}

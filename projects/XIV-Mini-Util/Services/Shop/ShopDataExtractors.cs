// Path: projects/XIV-Mini-Util/Services/ShopDataExtractors.cs
// Description: ショップデータ解析の共通抽出ロジック
// Reason: ShopDataCacheと診断で共通処理を再利用するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace XivMiniUtil.Services.Shop;

internal static class ShopDataExtractors
{
    public static IEnumerable<uint> GetReceiveItems(SpecialShop.ItemStruct entry)
    {
        // リフレクションでReceive系プロパティを探す
        var candidates = new[] { "ItemReceive", "ReceiveItems", "OutputItem", "Item", "Receive" };
        foreach (var name in candidates)
        {
            var prop = entry.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(entry);
            if (value == null)
            {
                continue;
            }

            // コレクションの場合
            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var itemId = ExtractItemId(item);
                    if (itemId != 0)
                    {
                        yield return itemId;
                    }
                }
                yield break;
            }

            // 単一アイテムの場合
            var singleId = ExtractItemId(value);
            if (singleId != 0)
            {
                yield return singleId;
            }
        }
    }

    public static uint ExtractItemId(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        // 直接uint
        if (value is uint directId)
        {
            return directId;
        }

        // RowRef型
        var rowIdProp = value.GetType().GetProperty("RowId");
        if (rowIdProp != null)
        {
            var rowId = rowIdProp.GetValue(value);
            if (rowId is uint id)
            {
                return id;
            }
        }

        // Itemプロパティを持つ構造体
        var itemProp = value.GetType().GetProperty("Item");
        if (itemProp != null)
        {
            var itemValue = itemProp.GetValue(value);
            return ExtractItemId(itemValue);
        }

        return 0;
    }

    public static uint GetItemIdFromGilShopItem(GilShopItem shopItem)
    {
        // GilShopItem.Item は RowRef<Item> 型
        var itemRef = shopItem.Item;
        return itemRef.RowId;
    }
}

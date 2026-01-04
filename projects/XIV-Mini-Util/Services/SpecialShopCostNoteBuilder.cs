// Path: projects/XIV-Mini-Util/Services/SpecialShopCostNoteBuilder.cs
// Description: SpecialShopのコストノート生成を担当する
// Reason: ShopDataCacheからコスト生成責務を分離するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace XivMiniUtil.Services;

internal sealed class SpecialShopCostNoteBuilder
{
    public string BuildCostNote(SpecialShop.ItemStruct entry)
    {
        var costs = new List<string>();

        // リフレクションでCost系プロパティを探す
        var candidates = new[] { "ItemCost", "CostItems", "InputItem", "Cost", "CurrencyCost" };
        foreach (var name in candidates)
        {
            var prop = entry.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(entry);
            if (value is not System.Collections.IEnumerable enumerable)
            {
                continue;
            }

            foreach (var item in enumerable)
            {
                var itemId = ShopDataExtractors.ExtractItemId(item);
                if (itemId == 0)
                {
                    continue;
                }

                // Count/Quantity を取得
                var count = GetCount(item);
                var itemName = $"アイテム#{itemId}";

                // アイテム名を取得（可能なら）
                var itemItemProp = item?.GetType().GetProperty("Item");
                if (itemItemProp != null)
                {
                    var itemRef = itemItemProp.GetValue(item);
                    var valueNullableProp = itemRef?.GetType().GetProperty("ValueNullable");
                    if (valueNullableProp != null)
                    {
                        var actualItem = valueNullableProp.GetValue(itemRef);
                        var nameProp = actualItem?.GetType().GetProperty("Name");
                        if (nameProp != null)
                        {
                            var nameValue = nameProp.GetValue(actualItem);
                            if (nameValue != null)
                            {
                                itemName = nameValue.ToString() ?? itemName;
                            }
                        }
                    }
                }

                if (count > 0)
                {
                    costs.Add($"{itemName} x{count}");
                }
            }

            if (costs.Count > 0)
            {
                break;
            }
        }

        return costs.Count > 0 ? string.Join(", ", costs) : "条件なし";
    }

    private static int GetCount(object? item)
    {
        if (item == null)
        {
            return 0;
        }

        var countCandidates = new[] { "Count", "Quantity", "Amount" };
        foreach (var name in countCandidates)
        {
            var prop = item.GetType().GetProperty(name);
            if (prop == null)
            {
                continue;
            }

            var value = prop.GetValue(item);
            if (value is int intCount)
            {
                return intCount;
            }

            if (value is uint uintCount)
            {
                return (int)uintCount;
            }

            if (value is ushort ushortCount)
            {
                return ushortCount;
            }
        }

        return 0;
    }
}

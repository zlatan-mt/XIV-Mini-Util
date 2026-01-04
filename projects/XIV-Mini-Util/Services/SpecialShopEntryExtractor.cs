// Path: projects/XIV-Mini-Util/Services/SpecialShopEntryExtractor.cs
// Description: SpecialShop項目からアイテム情報を抽出する
// Reason: ShopDataCacheの責務を分離して保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace XivMiniUtil.Services;

internal sealed class SpecialShopEntryExtractor
{
    private readonly SpecialShopCostNoteBuilder _costNoteBuilder;

    public SpecialShopEntryExtractor(SpecialShopCostNoteBuilder costNoteBuilder)
    {
        _costNoteBuilder = costNoteBuilder;
    }

    public IEnumerable<SpecialShopItemEntry> Extract(SpecialShop.ItemStruct entry)
    {
        var costNote = _costNoteBuilder.BuildCostNote(entry);
        foreach (var itemId in ShopDataExtractors.GetReceiveItems(entry))
        {
            if (itemId == 0)
            {
                continue;
            }

            yield return new SpecialShopItemEntry(itemId, costNote);
        }
    }
}

internal readonly record struct SpecialShopItemEntry(uint ItemId, string CostNote);

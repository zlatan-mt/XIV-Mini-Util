// Path: projects/XIV-Mini-Util/Services/GilShopItemNotes.cs
// Description: GilShopItemの価格/条件ノートを生成する
// Reason: ShopDataCacheから責務を分離するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.Shop;

internal sealed class GilShopItemNotes
{
    public int GetPrice(GilShopItem shopItem)
    {
        // GilShopItemにはPrice系プロパティがないため、Item側のPriceを使用
        var itemRef = shopItem.Item;
        if (itemRef.RowId == 0)
        {
            return 0;
        }

        var item = itemRef.ValueNullable;
        if (item == null)
        {
            return 0;
        }

        // Item.PriceMid がNPCショップでの販売価格
        return (int)item.Value.PriceMid;
    }

    public string GetCondition(GilShopItem shopItem, ExcelSheet<Quest>? questSheet)
    {
        // GilShopItemには直接クエスト条件がないため、状態クエストをチェック
        var stateRequired = shopItem.StateRequired;
        var patch = shopItem.Patch;

        if (stateRequired != 0)
        {
            return $"条件ID: {stateRequired}";
        }

        if (patch != 0)
        {
            return $"パッチ {patch / 100f:0.0}以降";
        }

        return "条件なし";
    }
}

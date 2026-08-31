// Path: projects/XIV-Mini-Util/Services/GilShopProcessStats.cs
// Description: GilShop処理の統計を集約する
// Reason: ShopDataCache内の集計を構造化するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
namespace XivMiniUtil.Services.Shop;

internal sealed class GilShopProcessStats
{
    public int ProcessedItems { get; private set; }
    public int SkippedItems { get; private set; }
    public int NoItemId { get; private set; }
    public int NoShopId { get; private set; }
    public int NoNpcMatch { get; private set; }
    public int TotalShopItems { get; private set; }

    public void IncrementTotal()
    {
        TotalShopItems++;
    }

    public void AddProcessed(int count)
    {
        ProcessedItems += count;
    }

    public void SkipNoItemId()
    {
        NoItemId++;
        SkippedItems++;
    }

    public void SkipNoShopId()
    {
        NoShopId++;
        SkippedItems++;
    }

    public void SkipNoNpcMatch()
    {
        NoNpcMatch++;
        SkippedItems++;
    }

    public void SkipOther()
    {
        SkippedItems++;
    }

    public string GetSummary()
    {
        return $"合計={TotalShopItems}, noItemId={NoItemId}, noShopId={NoShopId}, noNpcMatch={NoNpcMatch}";
    }
}

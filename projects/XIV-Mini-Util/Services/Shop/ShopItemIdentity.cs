// Path: projects/XIV-Mini-Util/Services/Shop/ShopItemIdentity.cs
// Description: ContextMenu由来のitem idと品質をゲーム非依存で正規化する
// Reason: Dalamudのメニュー処理と純粋なID変換を分離するため

using XivMiniUtil.Services.Market;

namespace XivMiniUtil.Services.Shop;

internal static class ShopItemIdentity
{
    public const ulong VariantOffset = 500000;
    private const ulong HighQualityItemIdOffset = 1000000;

    public static uint Normalize(ulong itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        var normalized = itemId % VariantOffset;
        if (normalized != 0)
        {
            return (uint)normalized;
        }

        return itemId <= uint.MaxValue ? (uint)itemId : 0;
    }

    public static UniversalisItemQuality GetQuality(ulong itemId)
    {
        return itemId >= HighQualityItemIdOffset && Normalize(itemId) != 0
            ? UniversalisItemQuality.HighQuality
            : UniversalisItemQuality.Normal;
    }
}

// Path: projects/XIV-Mini-Util/Models/Common/InventoryPreviewSnapshot.cs
// Description: ホームタブ表示用のインベントリ集計結果
using XivMiniUtil;
using XivMiniUtil.Models.Desynth;

namespace XivMiniUtil.Models.Common;

public sealed record InventoryPreviewSnapshot(
    DateTime UpdatedAt,
    bool IsLoggedIn,
    IReadOnlyList<InventoryItemInfo> ExtractableItems,
    int ExtractableItemCount,
    int ExtractableQuantity,
    IReadOnlyList<InventoryItemInfo> DesynthableItems,
    int DesynthableItemCount,
    int DesynthableQuantity,
    int EffectiveDesynthQuantity,
    int MaxItemLevel,
    DesynthPreviewRequest Request)
{
    public static InventoryPreviewSnapshot Empty { get; } = new(
        DateTime.MinValue,
        false,
        Array.Empty<InventoryItemInfo>(),
        0,
        0,
        Array.Empty<InventoryItemInfo>(),
        0,
        0,
        0,
        0,
        new DesynthPreviewRequest(1, 999, DesynthTargetMode.All, 1));
}

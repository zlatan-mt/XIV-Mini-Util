// Path: projects/XIV-Mini-Util/Models/Common/MateriaScanSnapshot.cs
// Description: マテリア精製向けスキャン結果(集計付き)
// Reason: 解析ログで候補数と条件判定を分離するため
namespace XivMiniUtil.Models.Common;

public sealed record MateriaScanSnapshot(
    bool IsLoggedIn,
    int TotalItemCount,
    int SpiritbondReadyCount,
    int MateriaSlotCount,
    int EligibleItemCount,
    IReadOnlyList<InventoryItemInfo> EligibleItems)
{
    public static MateriaScanSnapshot Empty { get; } = new(
        false,
        0,
        0,
        0,
        0,
        Array.Empty<InventoryItemInfo>());
}

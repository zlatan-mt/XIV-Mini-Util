// Path: projects/XIV-Mini-Util/Models/Shop/ShopTerritoryGroup.cs
// Description: 同名エリアを代表IDでまとめた情報
namespace XivMiniUtil;

public sealed record ShopTerritoryGroup(
    string TerritoryName,
    uint RepresentativeTerritoryTypeId,
    IReadOnlyList<uint> TerritoryTypeIds);

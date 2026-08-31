// Path: projects/XIV-Mini-Util/Models/Shop/ShopTerritoryInfo.cs
// Description: ショップ対象テリトリ情報
namespace XivMiniUtil;

public sealed record ShopTerritoryInfo(
    uint TerritoryTypeId,
    string TerritoryName);

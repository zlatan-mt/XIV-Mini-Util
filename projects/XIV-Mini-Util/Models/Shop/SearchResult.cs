// Path: projects/XIV-Mini-Util/Models/Shop/SearchResult.cs
// Description: ショップ検索結果
namespace XivMiniUtil;

public sealed record SearchResult(
    uint ItemId,
    string ItemName,
    IReadOnlyList<ShopLocationInfo> Locations,
    bool Success,
    string? ErrorMessage);

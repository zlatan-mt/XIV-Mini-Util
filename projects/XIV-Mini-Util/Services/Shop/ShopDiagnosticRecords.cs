// Path: projects/XIV-Mini-Util/Services/ShopDiagnosticRecords.cs
// Description: ショップ診断用の内部レコードを定義する
// Reason: 診断データ構造の意味づけを明確化するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
using System.Collections.Generic;

namespace XivMiniUtil.Services.Shop;

internal sealed record ExcludedNpcEntry(
    uint NpcId,
    string NpcName,
    uint ShopId,
    string ShopName);

internal sealed record UnmatchedShopEntry(
    uint ShopId,
    List<uint> ItemIds);

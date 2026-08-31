// Path: projects/XIV-Mini-Util/Models/Common/InventoryItemInfo.cs
// Description: インベントリアイテム情報
using FFXIVClientStructs.FFXIV.Client.Game;

namespace XivMiniUtil;

public sealed record InventoryItemInfo(
    uint ItemId,
    string Name,
    int ItemLevel,
    ushort Spiritbond,
    int Quantity,
    InventoryType Container,
    int Slot,
    bool CanExtractMateria,
    bool CanDesynth);

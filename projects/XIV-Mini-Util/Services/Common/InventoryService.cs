// Path: projects/XIV-Mini-Util/Services/InventoryService.cs
// Description: インベントリとアーマリーチェストを走査してアイテム情報を提供する
// Reason: マテリア精製と分解の判定ロジックを集約するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/Materia/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/Desynth/DesynthService.cs, projects/XIV-Mini-Util/Models/Common/InventoryItemInfo.cs
using Dalamud.Game.ClientState;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XivMiniUtil;

namespace XivMiniUtil.Services.Common;

public sealed class InventoryService
{
    private static readonly InventoryType[] InventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly InventoryType[] ArmoryContainers =
    [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    ];

    private static readonly InventoryType[] EquippedContainers =
    [
        InventoryType.EquippedItems,
    ];

    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;

    public InventoryService(IClientState clientState, IDataManager dataManager, IPluginLog pluginLog)
    {
        _clientState = clientState;
        _dataManager = dataManager;
        _pluginLog = pluginLog;
    }

    public bool IsPlayerLoggedIn => _clientState.IsLoggedIn;

    public IEnumerable<InventoryItemInfo> GetExtractableItems()
    {
        if (!IsPlayerLoggedIn)
        {
            return Array.Empty<InventoryItemInfo>();
        }

        return GetItems(InventoryContainers.Concat(ArmoryContainers))
            .Where(item => item.Spiritbond >= 10000 && item.CanExtractMateria);
    }

    public IEnumerable<InventoryItemInfo> GetDesynthableItems(int minLevel, int maxLevel)
    {
        if (!IsPlayerLoggedIn)
        {
            return Array.Empty<InventoryItemInfo>();
        }

        // 分解対象は所持品のみとする（アーマリーチェスト/装備中は除外）
        return GetItems(InventoryContainers)
            .Where(item => item.CanDesynth && item.ItemLevel >= minLevel && item.ItemLevel <= maxLevel);
    }

    public int GetMaxItemLevel()
    {
        if (!IsPlayerLoggedIn)
        {
            return 0;
        }

        var maxLevel = 0;
        foreach (var item in GetItems(InventoryContainers.Concat(ArmoryContainers).Concat(EquippedContainers)))
        {
            if (item.ItemLevel > maxLevel)
            {
                maxLevel = item.ItemLevel;
            }
        }

        return maxLevel;
    }

    private unsafe List<InventoryItemInfo> GetItems(IEnumerable<InventoryType> containerTypes)
    {
        var results = new List<InventoryItemInfo>();
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            _pluginLog.Error("InventoryManagerが取得できません。");
            return results;
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            _pluginLog.Error("Itemシートが取得できません。");
            return results;
        }

        foreach (var containerType in containerTypes)
        {
            var container = manager->GetInventoryContainer(containerType);
            if (container == null)
            {
                continue;
            }

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                {
                    continue;
                }

                var row = itemSheet.GetRow(item->ItemId);
                if (row.RowId == 0)
                {
                    continue;
                }
                // itemRow.Desynthは分解可否の簡易判定として利用する
                var canDesynth = row.Desynth > 0;
                var itemLevel = (int)(row.LevelItem.ValueNullable?.RowId ?? 0);
                var canExtractMateria = row.MateriaSlotCount > 0;
                var name = row.Name.ToString();

                results.Add(new InventoryItemInfo(
                    item->ItemId,
                    name,
                    itemLevel,
                    item->GetSpiritbondOrCollectability(),
                    item->Quantity,
                    containerType,
                    slot,
                    canExtractMateria,
                    canDesynth));
            }
        }

        return results;
    }
}

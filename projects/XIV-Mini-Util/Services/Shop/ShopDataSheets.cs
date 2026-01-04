// Path: projects/XIV-Mini-Util/Services/ShopDataSheets.cs
// Description: ShopDataCacheが使用するシート群の取得を共通化する
// Reason: シート取得ロジックの重複を避けるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.Shop;

internal sealed class ShopDataSheets
{
    public ShopDataSheets(
        ExcelSheet<Item> itemSheet,
        ExcelSheet<GilShop> gilShopSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<ENpcResident> npcResidentSheet,
        ExcelSheet<Level> levelSheet,
        ExcelSheet<TerritoryType> territorySheet,
        ExcelSheet<Map> mapSheet)
    {
        ItemSheet = itemSheet;
        GilShopSheet = gilShopSheet;
        SpecialShopSheet = specialShopSheet;
        NpcBaseSheet = npcBaseSheet;
        NpcResidentSheet = npcResidentSheet;
        LevelSheet = levelSheet;
        TerritorySheet = territorySheet;
        MapSheet = mapSheet;
    }

    public ExcelSheet<Item> ItemSheet { get; }
    public ExcelSheet<GilShop> GilShopSheet { get; }
    public ExcelSheet<SpecialShop> SpecialShopSheet { get; }
    public ExcelSheet<ENpcBase> NpcBaseSheet { get; }
    public ExcelSheet<ENpcResident> NpcResidentSheet { get; }
    public ExcelSheet<Level> LevelSheet { get; }
    public ExcelSheet<TerritoryType> TerritorySheet { get; }
    public ExcelSheet<Map> MapSheet { get; }

    public static bool TryLoad(IDataManager dataManager, IPluginLog pluginLog, out ShopDataSheets sheets)
    {
        sheets = null!;

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var gilShopSheet = dataManager.GetExcelSheet<GilShop>();
        var specialShopSheet = dataManager.GetExcelSheet<SpecialShop>();
        var npcBaseSheet = dataManager.GetExcelSheet<ENpcBase>();
        var npcResidentSheet = dataManager.GetExcelSheet<ENpcResident>();
        var levelSheet = dataManager.GetExcelSheet<Level>();
        var territorySheet = dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = dataManager.GetExcelSheet<Map>();

        pluginLog.Information($"シート取得: Item={itemSheet != null}, GilShop={gilShopSheet != null}, SpecialShop={specialShopSheet != null}, ENpcBase={npcBaseSheet != null}");

        if (itemSheet == null || gilShopSheet == null || specialShopSheet == null
            || npcBaseSheet == null || npcResidentSheet == null
            || levelSheet == null || territorySheet == null || mapSheet == null)
        {
            pluginLog.Error("ショップデータ用のシート取得に失敗しました。");
            return false;
        }

        sheets = new ShopDataSheets(
            itemSheet,
            gilShopSheet,
            specialShopSheet,
            npcBaseSheet,
            npcResidentSheet,
            levelSheet,
            territorySheet,
            mapSheet);
        return true;
    }
}

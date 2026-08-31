// Path: projects/XIV-Mini-Util/Services/NpcShopMappingBuilder.cs
// Description: NPCとショップのマッピング構築を担当する
// Reason: ShopDataCacheから責務を分離し保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace XivMiniUtil.Services.Shop;

internal sealed class NpcShopMappingBuilder
{
    private readonly IPluginLog _pluginLog;
    private readonly NpcLocationResolver _npcLocationResolver;
    private readonly EnpcDataShopResolver _enpcDataShopResolver;
    private readonly NpcNameResolver _npcNameResolver;
    private readonly NpcShopInfoRegistry _npcShopInfoRegistry;
    private readonly Configuration _configuration;

    public NpcShopMappingBuilder(
        IPluginLog pluginLog,
        NpcLocationResolver npcLocationResolver,
        EnpcDataShopResolver enpcDataShopResolver,
        NpcNameResolver npcNameResolver,
        NpcShopInfoRegistry npcShopInfoRegistry,
        Configuration configuration)
    {
        _pluginLog = pluginLog;
        _npcLocationResolver = npcLocationResolver;
        _enpcDataShopResolver = enpcDataShopResolver;
        _npcNameResolver = npcNameResolver;
        _npcShopInfoRegistry = npcShopInfoRegistry;
        _configuration = configuration;
    }

    private bool IsVerboseLogging => _configuration.ShopDataVerboseLogging;

    public (Dictionary<uint, List<NpcShopInfo>> GilShops, Dictionary<uint, List<NpcShopInfo>> SpecialShops) Build(
        ExcelSheet<ENpcBase> npcBaseSheet,
        ExcelSheet<ENpcResident> npcResidentSheet,
        ExcelSheet<GilShop> gilShopSheet,
        ExcelSheet<SpecialShop> specialShopSheet,
        ExcelSheet<Level> levelSheet,
        ExcelSheet<TerritoryType> territorySheet,
        ExcelSheet<Map> mapSheet,
        CancellationToken cancellationToken)
    {
        var gilShopResult = new Dictionary<uint, List<NpcShopInfo>>();
        var specialShopResult = new Dictionary<uint, List<NpcShopInfo>>();
        var gilShopRefCount = 0;
        var specialShopRefCount = 0;
        var scannedNpcCount = 0;
        var npcWithNameCount = 0;

        // Step 0: NPC ID -> 位置情報のマッピングを事前構築
        var npcLocations = _npcLocationResolver.BuildNpcLocationMapping(levelSheet);
        LogVerbose($"NPC位置情報: {npcLocations.Count}件");

        // Step 1: GilShopの全RowIdを収集
        var gilShopIds = new HashSet<uint>();
        var gilShopNames = new Dictionary<uint, string>();
        foreach (var shop in gilShopSheet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shop.RowId != 0)
            {
                gilShopIds.Add(shop.RowId);
                gilShopNames[shop.RowId] = shop.Name.ToString();
            }
        }
        LogVerbose($"GilShopシート: {gilShopIds.Count}件のショップを検出");

        // ショップIDの範囲をログ出力
        if (gilShopIds.Count > 0)
        {
            var minId = gilShopIds.Min();
            var maxId = gilShopIds.Max();
            LogVerbose($"GilShop RowId範囲: {minId} - {maxId}");
        }

        // Step 2: SpecialShopの全RowIdを収集
        var specialShopIds = new HashSet<uint>();
        foreach (var shop in specialShopSheet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shop.RowId != 0)
            {
                specialShopIds.Add(shop.RowId);
            }
        }
        LogVerbose($"SpecialShopシート: {specialShopIds.Count}件のショップを検出");

        if (specialShopIds.Count > 0)
        {
            var minId = specialShopIds.Min();
            var maxId = specialShopIds.Max();
            LogVerbose($"SpecialShop RowId範囲: {minId} - {maxId}");
        }

        LogVerbose("ENpcBase走査開始...");

        var loggedDataType = false;
        var loggedShopNpc = 0;

        foreach (var npcBase in npcBaseSheet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedNpcCount++;

            if (npcBase.RowId == 0)
            {
                continue;
            }

            // 最初のNPCでENpcDataの型情報をログ出力
            if (IsVerboseLogging && npcBase.ENpcData.Count > 0)
            {
                ShopDataLogHelper.LogFirstTypeMetadata(_pluginLog, "ENpcData", npcBase.ENpcData[0], ref loggedDataType);
            }

            // NPC名を取得
            var npcName = _npcNameResolver.GetNpcName(npcResidentSheet, npcBase.RowId);
            if (string.IsNullOrEmpty(npcName))
            {
                continue;
            }

            npcWithNameCount++;

            // 位置情報を取得
            npcLocations.TryGetValue(npcBase.RowId, out var locInfo);

            // ENpcData[]からショップ参照を探す
            var shopReferences = _enpcDataShopResolver.ResolveShopReferences(
                npcBase,
                gilShopIds,
                specialShopIds,
                gilShopNames);

            foreach (var shopRef in shopReferences)
            {
                if (shopRef.IsGilShop)
                {
                    if (IsVerboseLogging && loggedShopNpc < 5)
                    {
                        LogVerbose($"GilShopNPC発見: {npcName} -> Shop {shopRef.ShopId} ({shopRef.ShopName}) @ {locInfo?.AreaName ?? "不明"}");
                        loggedShopNpc++;
                    }

                    _npcShopInfoRegistry.TryAdd(gilShopResult, npcBase.RowId, npcName, shopRef.ShopId, shopRef.ShopName, locInfo);
                    gilShopRefCount++;
                }
                else
                {
                    _npcShopInfoRegistry.TryAdd(specialShopResult, npcBase.RowId, npcName, shopRef.ShopId, shopRef.ShopName, locInfo);
                    specialShopRefCount++;
                }
            }
        }

        _pluginLog.Information($"NPC走査完了: 走査={scannedNpcCount} / 名前あり={npcWithNameCount} / GilShop参照={gilShopRefCount}件 / SpecialShop参照={specialShopRefCount}件");
        return (gilShopResult, specialShopResult);
    }

    private void LogVerbose(string message)
    {
        if (!IsVerboseLogging)
        {
            return;
        }

        _pluginLog.Information(message);
    }
}

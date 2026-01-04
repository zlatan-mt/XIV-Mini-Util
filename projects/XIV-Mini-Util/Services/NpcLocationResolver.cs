// Path: projects/XIV-Mini-Util/Services/NpcLocationResolver.cs
// Description: NPCの位置情報をLevel/LGB/手動データから解決する
// Reason: ShopDataCacheの責務を分離して保守性を高める
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services;

internal sealed class NpcLocationResolver
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;

    public NpcLocationResolver(IDataManager dataManager, IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;
    }

    public Dictionary<uint, NpcLocationInfo> BuildNpcLocationMapping(ExcelSheet<Level> levelSheet)
    {
        var result = new Dictionary<uint, NpcLocationInfo>();

        // Step 0: 手動登録のNPC位置を先に追加
        AddManualNpcLocations(result);

        // Step 1: Level sheetからNPC位置を取得
        foreach (var level in levelSheet)
        {
            if (level.RowId == 0)
            {
                continue;
            }

            // Level.Type == 8 がENpc（NPC）を示す
            if (level.Type != 8)
            {
                continue;
            }

            var objectId = level.Object.RowId;
            if (objectId == 0 || result.ContainsKey(objectId))
            {
                continue;
            }

            var territory = level.Territory.ValueNullable;
            var map = level.Map.ValueNullable;
            if (territory == null || map == null)
            {
                continue;
            }

            var areaName = territory.Value.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
            var subAreaName = map.Value.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty;

            var mapX = MapCoordinateConverter.ConvertFromFloat(level.X, map.Value.OffsetX, map.Value.SizeFactor, true);
            var mapY = MapCoordinateConverter.ConvertFromFloat(level.Z, map.Value.OffsetY, map.Value.SizeFactor, true);

            result[objectId] = new NpcLocationInfo(
                territory.Value.RowId,
                areaName,
                subAreaName,
                map.Value.RowId,
                mapX,
                mapY);
        }

        _pluginLog.Information($"Level sheetからNPC位置取得: {result.Count}件");

        // Step 2: LGBファイルからNPC位置を補完
        var lgbAddedCount = BuildNpcLocationFromLgbFiles(result);
        _pluginLog.Information($"LGBファイルからNPC位置追加: {lgbAddedCount}件");

        return result;
    }

    private void AddManualNpcLocations(Dictionary<uint, NpcLocationInfo> result)
    {
        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = _dataManager.GetExcelSheet<Map>();
        if (territorySheet == null || mapSheet == null)
        {
            return;
        }

        foreach (var (npcId, locData) in ManualNpcLocations)
        {
            if (result.ContainsKey(npcId))
            {
                continue; // 既に位置情報がある場合はスキップ
            }

            var territory = territorySheet.GetRow(locData.TerritoryId);
            if (territory.RowId == 0)
            {
                _pluginLog.Warning($"手動NPC位置: Territory {locData.TerritoryId} が見つかりません (NPC:{npcId})");
                continue;
            }

            // このTerritoryに対応するMapを検索
            uint mapId = 0;
            foreach (var map in mapSheet)
            {
                if (map.TerritoryType.RowId == locData.TerritoryId)
                {
                    mapId = map.RowId;
                    break;
                }
            }

            if (mapId == 0)
            {
                _pluginLog.Warning($"手動NPC位置: Territory {locData.TerritoryId} のMapが見つかりません (NPC:{npcId})");
                continue;
            }

            result[npcId] = new NpcLocationInfo(
                locData.TerritoryId,
                locData.AreaName,
                string.Empty, // SubAreaName
                mapId,
                locData.X,
                locData.Y,
                IsManuallyAdded: true);

            _pluginLog.Information($"手動NPC位置追加: ID={npcId} @ {locData.AreaName} ({locData.X}, {locData.Y})");
        }
    }

    private int BuildNpcLocationFromLgbFiles(Dictionary<uint, NpcLocationInfo> result)
    {
        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var mapSheet = _dataManager.GetExcelSheet<Map>();
        if (territorySheet == null || mapSheet == null)
        {
            return 0;
        }

        var addedCount = 0;
        var processedTerritories = 0;
        var failedFiles = 0;

        // 調査対象のNPC ID（よろず屋など）
        var targetNpcIds = new HashSet<uint> { 1005422, 1032822 };

        foreach (var territory in territorySheet)
        {
            if (territory.RowId == 0)
            {
                continue;
            }

            var bg = territory.Bg.ToString();
            if (string.IsNullOrEmpty(bg))
            {
                continue;
            }

            // planevent.lgbとbg.lgbの両方を試行
            var lgbPaths = GetLgbFilePaths(bg);
            if (lgbPaths.Count == 0)
            {
                continue;
            }

            var territoryProcessed = false;
            foreach (var lgbPath in lgbPaths)
            {
                try
                {
                    var lgbFile = _dataManager.GetFile<LgbFile>(lgbPath);
                    if (lgbFile == null)
                    {
                        continue;
                    }

                    if (!territoryProcessed)
                    {
                        processedTerritories++;
                        territoryProcessed = true;
                    }

                    addedCount += ParseLgbFileForNpcLocations(lgbFile, territory, mapSheet, result, targetNpcIds);
                }
                catch (Exception ex)
                {
                    failedFiles++;
                    if (failedFiles <= 3)
                    {
                        _pluginLog.Debug($"LGBファイル解析エラー: {lgbPath} - {ex.Message}");
                    }
                }
            }
        }

        _pluginLog.Information($"LGB処理完了: Territory={processedTerritories}, 失敗={failedFiles}");
        return addedCount;
    }

    private static List<string> GetLgbFilePaths(string bg)
    {
        // パス例: ffxiv/sea_s1/twn/s1t1/level/s1t1
        // 出力: bg/ffxiv/sea_s1/twn/s1t1/level/planevent.lgb, bg.lgb
        var result = new List<string>();
        var levelIndex = bg.IndexOf("/level/", StringComparison.Ordinal);
        if (levelIndex < 0)
        {
            return result;
        }

        var basePath = $"bg/{bg[..(levelIndex + 7)]}";
        result.Add($"{basePath}planevent.lgb");
        result.Add($"{basePath}bg.lgb");
        return result;
    }

    private int ParseLgbFileForNpcLocations(
        LgbFile lgbFile,
        TerritoryType territory,
        ExcelSheet<Map> mapSheet,
        Dictionary<uint, NpcLocationInfo> result,
        HashSet<uint> targetNpcIds)
    {
        var addedCount = 0;

        // デフォルトマップを取得
        Map? defaultMap = null;
        foreach (var map in mapSheet)
        {
            if (map.TerritoryType.RowId == territory.RowId)
            {
                defaultMap = map;
                break;
            }
        }

        if (defaultMap == null)
        {
            return 0;
        }

        var areaName = territory.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty;
        var subAreaName = defaultMap.Value.PlaceNameSub.ValueNullable?.Name.ToString() ?? string.Empty;

        foreach (var layer in lgbFile.Layers)
        {
            foreach (var instanceObj in layer.InstanceObjects)
            {
                // EventNPCのみ処理
                if (instanceObj.AssetType != LayerEntryType.EventNPC)
                {
                    continue;
                }

                // NPC IDを取得
                var npcId = GetNpcIdFromInstanceObject(instanceObj);
                if (npcId == 0)
                {
                    continue;
                }

                // 調査対象のNPCが見つかった場合はログ出力
                if (targetNpcIds.Contains(npcId))
                {
                    var pos = instanceObj.Transform.Translation;
                    _pluginLog.Warning($"調査対象NPC発見: ID={npcId} @ {areaName} (Territory:{territory.RowId}, Pos:{pos.X:F1},{pos.Z:F1})");
                }

                // 既に位置情報がある場合はスキップ（Level sheetの方が正確）
                if (result.ContainsKey(npcId))
                {
                    continue;
                }

                // 座標を取得
                var pos2 = instanceObj.Transform.Translation;
                var mapX = MapCoordinateConverter.ConvertFromFloat(pos2.X, defaultMap.Value.OffsetX, defaultMap.Value.SizeFactor, true);
                var mapY = MapCoordinateConverter.ConvertFromFloat(pos2.Z, defaultMap.Value.OffsetY, defaultMap.Value.SizeFactor, true);

                result[npcId] = new NpcLocationInfo(
                    territory.RowId,
                    areaName,
                    subAreaName,
                    defaultMap.Value.RowId,
                    mapX,
                    mapY);

                addedCount++;
            }
        }

        return addedCount;
    }

    private static uint GetNpcIdFromInstanceObject(LayerCommon.InstanceObject instanceObj)
    {
        // ENPCInstanceObject -> NPCInstanceObject -> BaseId
        if (instanceObj.Object is not LayerCommon.ENPCInstanceObject eventNpc)
        {
            return 0;
        }

        // ParentData.ParentData.BaseId からNPC IDを取得
        try
        {
            return eventNpc.ParentData.ParentData.BaseId;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// ゲームデータに位置情報がないNPCのフォールバック位置データ
    /// </summary>
    private static readonly Dictionary<uint, (uint TerritoryId, string AreaName, float X, float Y)> ManualNpcLocations = new()
    {
        // リムサ・ロミンサ下甲板のよろず屋（オーシャンフィッシング関連）
        // NPC ID 1005422 - Merchant & Mender at Limsa Lominsa Lower Decks (3.3, 12.9)
        { 1005422, (129, "リムサ・ロミンサ：下甲板層", 3.3f, 12.9f) },
    };
}

internal sealed record NpcLocationInfo(
    uint TerritoryTypeId,
    string AreaName,
    string SubAreaName,
    uint MapId,
    float MapX,
    float MapY,
    bool IsManuallyAdded = false);

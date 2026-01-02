// Path: projects/XIV-Mini-Util/Services/TeleportService.cs
// Description: マップ座標から最寄りのエーテライトを検索してテレポする
// Reason: 検索結果の場所へ素早く移動できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Windows/ShopSearchResultWindow.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using Dalamud.Game.ClientState.Aetherytes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using LuminaMap = Lumina.Excel.Sheets.Map;

namespace XivMiniUtil.Services;

public sealed class TeleportService
{
    private readonly IDataManager _dataManager;
    private readonly IAetheryteList _aetheryteList;
    private readonly IPluginLog _pluginLog;

    public TeleportService(IDataManager dataManager, IAetheryteList aetheryteList, IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _aetheryteList = aetheryteList;
        _pluginLog = pluginLog;
    }

    /// <summary>
    /// 指定した販売場所の最寄りエーテライトにテレポする
    /// </summary>
    public unsafe bool TeleportToNearestAetheryte(ShopLocationInfo location)
    {
        var aetheryte = FindNearestAetheryte(location.TerritoryTypeId, location.MapX, location.MapY);
        if (aetheryte == null)
        {
            _pluginLog.Warning($"最寄りエーテライトが見つかりません: {location.AreaName}");
            return false;
        }

        var aetheryteId = aetheryte.Value.RowId;
        var aetheryteName = aetheryte.Value.PlaceName.ValueNullable?.Name.ToString() ?? "不明";

        // テレポ実行
        var telepo = Telepo.Instance();
        if (telepo == null)
        {
            _pluginLog.Error("Telepoインスタンスの取得に失敗しました。");
            return false;
        }

        // テレポ可能かチェック（アンロック済み）
        if (!IsAetheryteUnlocked(aetheryteId))
        {
            _pluginLog.Warning($"エーテライト未解放: {aetheryteName} (ID:{aetheryteId})");
            return false;
        }

        var result = telepo->Teleport(aetheryteId, 0);
        if (result)
        {
            _pluginLog.Information($"テレポ開始: {aetheryteName} (ID:{aetheryteId})");
        }
        else
        {
            _pluginLog.Warning($"テレポに失敗しました: {aetheryteName} (ID:{aetheryteId})");
        }

        return result;
    }

    /// <summary>
    /// 指定した販売場所の最寄りエーテライト情報を取得
    /// </summary>
    public AetheryteInfo? GetNearestAetheryteInfo(ShopLocationInfo location)
    {
        var aetheryte = FindNearestAetheryte(location.TerritoryTypeId, location.MapX, location.MapY);
        if (aetheryte == null)
        {
            return null;
        }

        var name = aetheryte.Value.PlaceName.ValueNullable?.Name.ToString() ?? "不明";
        return new AetheryteInfo(aetheryte.Value.RowId, name);
    }

    /// <summary>
    /// 指定したエーテライトがアンロック済みかチェック
    /// </summary>
    public bool IsAetheryteUnlocked(uint aetheryteId)
    {
        // IAetheryteListを使用して解放状況を確認
        foreach (var entry in _aetheryteList)
        {
            if (entry.AetheryteId == aetheryteId)
            {
                return true; // リストに存在すれば解放済み
            }
        }

        return false;
    }

    private Aetheryte? FindNearestAetheryte(uint territoryTypeId, float mapX, float mapY)
    {
        var aetheryteSheet = _dataManager.GetExcelSheet<Aetheryte>();
        var mapSheet = _dataManager.GetExcelSheet<LuminaMap>();
        if (aetheryteSheet == null || mapSheet == null)
        {
            return null;
        }

        Aetheryte? nearest = null;
        var minDistance = float.MaxValue;

        foreach (var aetheryte in aetheryteSheet)
        {
            if (aetheryte.RowId == 0)
            {
                continue;
            }

            // 同じTerritoryTypeのエーテライトのみ
            var aetheryteTerritoryId = aetheryte.Territory.RowId;
            if (aetheryteTerritoryId != territoryTypeId)
            {
                continue;
            }

            // エーテライトプラザ（都市内テレポ）は除外
            if (aetheryte.IsAetheryte == false)
            {
                continue;
            }

            // エーテライトのマップ座標を取得
            var aetheryteMap = aetheryte.Map.ValueNullable;
            if (aetheryteMap == null)
            {
                continue;
            }

            var aetheryteX = ConvertToMapCoordinate(aetheryte.AetherstreamX, aetheryteMap.Value.OffsetX, aetheryteMap.Value.SizeFactor);
            var aetheryteY = ConvertToMapCoordinate(aetheryte.AetherstreamY, aetheryteMap.Value.OffsetY, aetheryteMap.Value.SizeFactor);

            // 距離計算
            var dx = aetheryteX - mapX;
            var dy = aetheryteY - mapY;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = aetheryte;
            }
        }

        // 同じTerritoryTypeで見つからなかった場合、関連するエーテライトを検索
        // （例：リムサ・ロミンサ上甲板層 → リムサ・ロミンサ・エーテライトプラザ）
        if (nearest == null)
        {
            nearest = FindRelatedAetheryte(territoryTypeId, aetheryteSheet);
        }

        return nearest;
    }

    private Aetheryte? FindRelatedAetheryte(uint territoryTypeId, Lumina.Excel.ExcelSheet<Aetheryte> aetheryteSheet)
    {
        // 三大都市の関連マッピング
        var relatedTerritories = new Dictionary<uint, uint>
        {
            { 128, 129 }, // リムサ上甲板層 → 下甲板層
            { 129, 129 }, // リムサ下甲板層
            { 130, 130 }, // ウルダハ・ナル回廊
            { 131, 130 }, // ウルダハ・ザル回廊 → ナル回廊
            { 132, 132 }, // グリダニア新市街
            { 133, 132 }, // グリダニア旧市街 → 新市街
        };

        if (!relatedTerritories.TryGetValue(territoryTypeId, out var mainTerritoryId))
        {
            return null;
        }

        foreach (var aetheryte in aetheryteSheet)
        {
            if (aetheryte.RowId == 0)
            {
                continue;
            }

            if (aetheryte.Territory.RowId == mainTerritoryId && aetheryte.IsAetheryte)
            {
                return aetheryte;
            }
        }

        return null;
    }

    private static float ConvertToMapCoordinate(short rawPosition, short offset, ushort sizeFactor)
    {
        // エーテライトの座標はshort型で格納されている
        var scale = sizeFactor / 100f;
        var c = 41f / scale;
        var adjusted = (rawPosition * scale + 1024f) / 2048f;
        return c * adjusted + 1f;
    }
}

public sealed record AetheryteInfo(uint AetheryteId, string Name);

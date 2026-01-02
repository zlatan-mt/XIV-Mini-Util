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
    private readonly Dictionary<uint, List<AetheryteEntry>> _aetheryteByTerritory = new();
    private bool _aetheryteCacheReady;
    private readonly HashSet<uint> _unlockedAetherytes = new();
    private bool _unlockedCacheReady;

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

        var aetheryteId = aetheryte.AetheryteId;
        var aetheryteName = aetheryte.Name;

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

        // サブインデックスが必要なケースに備えて、解放済みリストから補完する
        var subIndex = GetUnlockedAetheryteSubIndex(aetheryteId);
        var result = telepo->Teleport(aetheryteId, subIndex);
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

        return new AetheryteInfo(aetheryte.AetheryteId, aetheryte.Name);
    }

    /// <summary>
    /// 指定したエーテライトがアンロック済みかチェック
    /// </summary>
    public bool IsAetheryteUnlocked(uint aetheryteId)
    {
        EnsureUnlockedCache();
        return _unlockedAetherytes.Contains(aetheryteId);
    }

    private byte GetUnlockedAetheryteSubIndex(uint aetheryteId)
    {
        foreach (var entry in _aetheryteList)
        {
            if (entry.AetheryteId != aetheryteId)
            {
                continue;
            }

            var subIndex = GetSubIndexFromEntry(entry);
            if (subIndex.HasValue)
            {
                return subIndex.Value;
            }
        }

        return 0;
    }

    private static byte? GetSubIndexFromEntry(object entry)
    {
        var property = entry.GetType().GetProperty("SubIndex");
        if (property == null)
        {
            return null;
        }

        var value = property.GetValue(entry);
        return value switch
        {
            byte b => b,
            ushort s => (byte)Math.Clamp(s, byte.MinValue, byte.MaxValue),
            uint u => (byte)Math.Clamp(u, byte.MinValue, byte.MaxValue),
            int i when i >= 0 => (byte)Math.Clamp(i, byte.MinValue, byte.MaxValue),
            _ => null,
        };
    }

    private AetheryteEntry? FindNearestAetheryte(uint territoryTypeId, float mapX, float mapY)
    {
        EnsureAetheryteCache();
        if (!_aetheryteCacheReady)
        {
            return null;
        }

        if (!_aetheryteByTerritory.TryGetValue(territoryTypeId, out var entries) || entries.Count == 0)
        {
            return FindRelatedAetheryte(territoryTypeId);
        }

        AetheryteEntry? nearest = null;
        var minDistance = float.MaxValue;

        foreach (var entry in entries)
        {
            // 距離計算
            var dx = entry.MapX - mapX;
            var dy = entry.MapY - mapY;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = entry;
            }
        }

        // 同じTerritoryTypeで見つからなかった場合、関連するエーテライトを検索
        // （例：リムサ・ロミンサ上甲板層 → リムサ・ロミンサ・エーテライトプラザ）
        if (nearest == null)
        {
            nearest = FindRelatedAetheryte(territoryTypeId);
        }

        return nearest;
    }

    private AetheryteEntry? FindRelatedAetheryte(uint territoryTypeId)
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

        if (!_aetheryteByTerritory.TryGetValue(mainTerritoryId, out var entries) || entries.Count == 0)
        {
            return null;
        }

        return entries[0];
    }

    private static float ConvertToMapCoordinate(short rawPosition, short offset, ushort sizeFactor)
    {
        // エーテライトの座標はshort型で格納されている
        var scale = sizeFactor / 100f;
        var c = 41f / scale;
        var adjusted = (rawPosition * scale + 1024f) / 2048f;
        return c * adjusted + 1f;
    }

    private void EnsureAetheryteCache()
    {
        if (_aetheryteCacheReady)
        {
            return;
        }

        var aetheryteSheet = _dataManager.GetExcelSheet<Aetheryte>();
        if (aetheryteSheet == null)
        {
            return;
        }

        foreach (var aetheryte in aetheryteSheet)
        {
            if (aetheryte.RowId == 0)
            {
                continue;
            }

            if (!aetheryte.IsAetheryte)
            {
                continue;
            }

            var aetheryteMap = aetheryte.Map.ValueNullable;
            if (aetheryteMap == null)
            {
                continue;
            }

            var territoryId = aetheryte.Territory.RowId;
            if (territoryId == 0)
            {
                continue;
            }

            var mapX = ConvertToMapCoordinate(aetheryte.AetherstreamX, aetheryteMap.Value.OffsetX, aetheryteMap.Value.SizeFactor);
            var mapY = ConvertToMapCoordinate(aetheryte.AetherstreamY, aetheryteMap.Value.OffsetY, aetheryteMap.Value.SizeFactor);
            var name = aetheryte.PlaceName.ValueNullable?.Name.ToString() ?? "不明";

            if (!_aetheryteByTerritory.TryGetValue(territoryId, out var list))
            {
                list = new List<AetheryteEntry>();
                _aetheryteByTerritory[territoryId] = list;
            }

            list.Add(new AetheryteEntry(aetheryte.RowId, territoryId, name, mapX, mapY));
        }

        _aetheryteCacheReady = true;
    }

    private void EnsureUnlockedCache()
    {
        if (_unlockedCacheReady)
        {
            return;
        }

        _unlockedAetherytes.Clear();
        foreach (var entry in _aetheryteList)
        {
            _unlockedAetherytes.Add(entry.AetheryteId);
        }

        _unlockedCacheReady = true;
    }
}

public sealed record AetheryteInfo(uint AetheryteId, string Name);

public sealed record AetheryteEntry(
    uint AetheryteId,
    uint TerritoryTypeId,
    string Name,
    float MapX,
    float MapY);

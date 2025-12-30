// Path: projects/XIV-Mini-Util/Services/MapService.cs
// Description: マップピン設定とマップリンク生成を担当する
// Reason: マップ操作の責務を分離して再利用しやすくするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ChatService.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class MapService
{
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _pluginLog;

    public MapService(IGameGui gameGui, IPluginLog pluginLog)
    {
        _gameGui = gameGui;
        _pluginLog = pluginLog;
    }

    public void SetMapMarker(ShopLocationInfo location)
    {
        var payload = CreateMapLink(location);
        if (payload == null)
        {
            _pluginLog.Warning("マップリンク生成に失敗しました。");
            return;
        }

        _gameGui.OpenMapWithMapLink(payload);
    }

    public MapLinkPayload? CreateMapLink(ShopLocationInfo location)
    {
        if (location.TerritoryTypeId == 0 || location.MapId == 0)
        {
            return null;
        }

        return new MapLinkPayload(location.TerritoryTypeId, location.MapId, location.MapX, location.MapY);
    }
}

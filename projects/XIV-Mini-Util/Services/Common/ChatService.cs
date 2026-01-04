// Path: projects/XIV-Mini-Util/Services/ChatService.cs
// Description: Echo投稿とマップリンク付きメッセージ生成を担当する
// Reason: チャット出力の責務を分離し再利用性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/Shop/ShopSearchService.cs, projects/XIV-Mini-Util/Services/Common/MapService.cs, projects/XIV-Mini-Util/Models/Shop/ShopLocationInfo.cs
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services.Common;

public sealed class ChatService
{
    private readonly IChatGui _chatGui;
    private readonly MapService _mapService;
    public ChatService(IChatGui chatGui, MapService mapService)
    {
        _chatGui = chatGui;
        _mapService = mapService;
    }

    public void PostSearchResult(string itemName, IReadOnlyList<ShopLocationInfo> locations, int maxCount = 3)
    {
        if (locations.Count == 0)
        {
            PostError("販売場所が見つかりませんでした。");
            return;
        }

        _chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = new SeStringBuilder()
                .AddText($"[販売場所検索] {itemName}")
                .Build(),
        });

        var count = Math.Min(maxCount, locations.Count);
        for (var i = 0; i < count; i++)
        {
            var location = locations[i];
            var builder = new SeStringBuilder();
            builder.AddText($"{i + 1}. {FormatLocationLabel(location)} ");

            var payload = _mapService.CreateMapLink(location);
            if (payload != null)
            {
                // マップリンクを正しく表示するにはUiForegroundで囲む必要がある
                builder.AddUiForeground(0x01F4); // マップリンク用の色 (500)
                builder.Add(payload);
                builder.AddText($"({location.MapX:0.0}, {location.MapY:0.0})");
                builder.Add(RawPayload.LinkTerminator);
                builder.AddUiForegroundOff();
            }

            _chatGui.Print(new XivChatEntry
            {
                Type = XivChatType.Echo,
                Message = builder.Build(),
            });
        }
    }

    public void PostError(string message)
    {
        _chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = new SeStringBuilder()
                .AddText($"[販売場所検索] {message}")
                .Build(),
        });
    }

    private static string FormatLocationLabel(ShopLocationInfo location)
    {
        var areaLabel = string.IsNullOrWhiteSpace(location.SubAreaName)
            ? location.AreaName
            : $"{location.AreaName} {location.SubAreaName}";

        return $"{areaLabel} / {location.NpcName} ";
    }
}

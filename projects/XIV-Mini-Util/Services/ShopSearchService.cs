// Path: projects/XIV-Mini-Util/Services/ShopSearchService.cs
// Description: 販売場所検索の実行と出力を統括する
// Reason: 検索・ソート・出力の責務をまとめて扱いやすくするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Services/MapService.cs, projects/XIV-Mini-Util/Services/ChatService.cs
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class ShopSearchService
{
    private readonly ShopDataCache _shopDataCache;
    private readonly MapService _mapService;
    private readonly ChatService _chatService;
    private readonly Configuration _configuration;
    private readonly IPluginLog _pluginLog;

    public ShopSearchService(
        ShopDataCache shopDataCache,
        MapService mapService,
        ChatService chatService,
        Configuration configuration,
        IPluginLog pluginLog)
    {
        _shopDataCache = shopDataCache;
        _mapService = mapService;
        _chatService = chatService;
        _configuration = configuration;
        _pluginLog = pluginLog;
    }

    public event Action<SearchResult>? OnSearchCompleted;

    public SearchResult Search(uint itemId)
    {
        if (itemId == 0)
        {
            return NotifyResult(new SearchResult(0, string.Empty, Array.Empty<ShopLocationInfo>(), false, "アイテムIDが取得できませんでした。"));
        }

        if (!_shopDataCache.IsInitialized)
        {
            return NotifyResult(new SearchResult(itemId, _shopDataCache.GetItemName(itemId), Array.Empty<ShopLocationInfo>(), false, "ショップデータを準備中です。"));
        }

        // 検索診断ログを出力（デバッグ用）
        _shopDataCache.LogSearchDiagnostics(itemId);

        var locations = _shopDataCache.GetShopLocations(itemId);
        if (locations.Count == 0)
        {
            // 販売場所が見つからない場合は診断ログを出して原因特定を支援する
            _shopDataCache.LogMissingItemDiagnostics(itemId);
            return NotifyResult(new SearchResult(itemId, _shopDataCache.GetItemName(itemId), Array.Empty<ShopLocationInfo>(), false, "このアイテムはNPCショップでは販売されていません。"));
        }

        var sorted = SortByPriority(locations);
        var itemName = _shopDataCache.GetItemName(itemId);

        try
        {
            _mapService.SetMapMarker(sorted[0]);
            if (_configuration.ShopSearchEchoEnabled)
            {
                _chatService.PostSearchResult(itemName, sorted, 3);
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "検索結果の出力に失敗しました。");
        }

        return NotifyResult(new SearchResult(itemId, itemName, sorted, true, null));
    }

    private SearchResult NotifyResult(SearchResult result)
    {
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            _chatService.PostError(result.ErrorMessage);
        }

        OnSearchCompleted?.Invoke(result);
        return result;
    }

    private IReadOnlyList<ShopLocationInfo> SortByPriority(IReadOnlyList<ShopLocationInfo> locations)
    {
        var priority = _configuration.ShopSearchAreaPriority;
        var priorityIndex = new Dictionary<uint, int>(priority.Count);
        for (var i = 0; i < priority.Count; i++)
        {
            priorityIndex[priority[i]] = i;
        }

        // 優先度指定の有無でグループ化し、指定済みは順序、それ以外は名称順にする
        // 同じエリア内では手動登録NPCを最後に表示
        return locations
            .OrderBy(location => priorityIndex.ContainsKey(location.TerritoryTypeId) ? 0 : 1)
            .ThenBy(location => priorityIndex.TryGetValue(location.TerritoryTypeId, out var index) ? index : int.MaxValue)
            .ThenBy(location => location.AreaName, StringComparer.Ordinal)
            .ThenBy(location => location.IsManuallyAdded ? 1 : 0) // 手動登録は同エリア内で最後
            .ThenBy(location => location.NpcName, StringComparer.Ordinal)
            .ToList();
    }
}

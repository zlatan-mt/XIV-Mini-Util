// Path: projects/XIV-Mini-Util/Services/ContextMenuService.cs
// Description: アイテム右クリックメニューに販売場所検索を追加する
// Reason: コンテキストメニュー処理を独立させて保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivMiniUtil.Services;

public sealed class ContextMenuService : IDisposable
{
    private const string SearchLabel = "販売場所を検索";

    private readonly IContextMenu _contextMenu;
    private readonly IGameGui _gameGui;
    private readonly ShopSearchService _shopSearchService;
    private readonly ShopDataCache _shopDataCache;
    private readonly IPluginLog _pluginLog;

    public ContextMenuService(
        IContextMenu contextMenu,
        IGameGui gameGui,
        ShopSearchService shopSearchService,
        ShopDataCache shopDataCache,
        IPluginLog pluginLog)
    {
        _contextMenu = contextMenu;
        _gameGui = gameGui;
        _shopSearchService = shopSearchService;
        _shopDataCache = shopDataCache;
        _pluginLog = pluginLog;

        _contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        _contextMenu.OnMenuOpened -= OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        var addonName = args.AddonName ?? string.Empty;
        if (!TryGetItemId(args.Target, addonName, out var itemId))
        {
            return;
        }

        var isReady = _shopDataCache.IsInitialized;
        var hasData = _shopDataCache.HasShopData(itemId);
        var label = BuildMenuLabel(isReady, hasData);

        // 販売データがない場合は診断ログを出力
        if (isReady && !hasData)
        {
            _shopDataCache.LogMissingItemDiagnostics(itemId);
        }

        // MenuItemはNameとOnClickedを明示的に設定する
        // PrefixCharを設定して警告を回避（大文字でボックスレターアイコンになる）
        var menuItem = new MenuItem
        {
            Name = new SeStringBuilder().AddText(label).Build(),
            OnClicked = OnSearchClicked,
            PrefixChar = 'M', // M for Mini-Util
        };

        SetMenuItemEnabled(menuItem, isReady && hasData);
        args.AddMenuItem(menuItem);
    }

    private void OnSearchClicked(IMenuItemClickedArgs args)
    {
        var addonName = args.AddonName ?? string.Empty;
        if (!TryGetItemId(args.Target, addonName, out var itemId))
        {
            _pluginLog.Warning("販売場所検索の対象アイテムが取得できませんでした。");
            return;
        }

        if (!_shopDataCache.IsInitialized)
        {
            _shopSearchService.Search(itemId);
            return;
        }

        _shopSearchService.Search(itemId);
    }

    private bool TryGetItemId(object target, string addonName, out uint itemId)
    {
        // インベントリアイテム
        if (target is MenuTargetInventory inventoryTarget && inventoryTarget.TargetItem is { } inventoryItem)
        {
            itemId = inventoryItem.ItemId;
            return itemId != 0;
        }

        // Agent経由でItemIdを取得（チャットログ、レシピノートなど）
        var agentItemId = GetItemIdFromAgent(addonName);
        if (agentItemId != 0)
        {
            itemId = agentItemId;
            _pluginLog.Information($"Agent経由でItemId取得: {itemId} (Addon: {addonName})");
            return true;
        }

        // フォールバック: HoveredItemから取得
        var hoveredItem = _gameGui.HoveredItem;
        if (hoveredItem > 0)
        {
            var hoveredItemId = (uint)(hoveredItem % 500000);
            if (hoveredItemId != 0)
            {
                itemId = hoveredItemId;
                _pluginLog.Information($"HoveredItem経由でItemId取得: {itemId}");
                return true;
            }
        }

        // チャットリンク等の一般的なコンテキストメニュー（デバッグ用）
        if (target is MenuTargetDefault defaultTarget)
        {
            // デバッグ: MenuTargetDefaultのプロパティを出力
            var props = defaultTarget.GetType().GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p =>
                {
                    try
                    {
                        var val = p.GetValue(defaultTarget);
                        return $"{p.Name}={val}";
                    }
                    catch
                    {
                        return $"{p.Name}=<error>";
                    }
                })
                .ToArray();
            _pluginLog.Information($"MenuTargetDefault: {string.Join(", ", props)}");

            // TargetItemIdから直接取得
            if (TryGetProperty(defaultTarget, "TargetItemId", out var targetItemId) && targetItemId is uint chatItemId && chatItemId != 0)
            {
                itemId = chatItemId;
                return true;
            }

            // ContentIdOrItemIdから取得（アイテムリンクの場合）
            if (TryGetProperty(defaultTarget, "ContentIdOrItemId", out var contentId) && contentId is ulong contentIdVal && contentIdVal != 0)
            {
                // アイテムIDは下位32ビット
                var extractedId = (uint)(contentIdVal & 0xFFFFFFFF);
                if (extractedId != 0 && extractedId < 100000) // 妥当なアイテムID範囲
                {
                    itemId = extractedId;
                    return true;
                }
            }
        }

        if (TryGetProperty(target, "ItemId", out var rawItemId) && rawItemId is uint directItemId)
        {
            itemId = directItemId;
            return itemId != 0;
        }

        if (TryGetProperty(target, "ItemID", out var rawItemIdAlt) && rawItemIdAlt is uint directItemIdAlt)
        {
            itemId = directItemIdAlt;
            return itemId != 0;
        }

        if (TryGetProperty(target, "TargetItem", out var targetItem))
        {
            var reflectedItemId = TryGetRowId(targetItem);
            if (reflectedItemId is { } candidate && candidate != 0)
            {
                itemId = candidate;
                return true;
            }
        }

        itemId = 0;
        return false;
    }

    private unsafe uint GetItemIdFromAgent(string addonName)
    {
        try
        {
            uint itemId = 0;

            switch (addonName)
            {
                case "ChatLog":
                    var agentChatLog = AgentChatLog.Instance();
                    if (agentChatLog != null)
                    {
                        itemId = agentChatLog->ContextItemId;
                    }
                    break;

                case "RecipeNote":
                    var agentRecipeNote = AgentRecipeNote.Instance();
                    if (agentRecipeNote != null)
                    {
                        itemId = agentRecipeNote->ContextMenuResultItemId;
                    }
                    break;

                case "ItemSearch":
                    var agentContext = AgentContext.Instance();
                    if (agentContext != null)
                    {
                        itemId = (uint)agentContext->UpdateCheckerParam;
                    }
                    break;
            }

            // HQ品のIDを通常品に正規化
            return itemId % 500000;
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, $"Agent経由のItemId取得に失敗: {addonName}");
            return 0;
        }
    }

    private static string BuildMenuLabel(bool isReady, bool hasData)
    {
        if (!isReady)
        {
            return $"{SearchLabel} (準備中)";
        }

        if (!hasData)
        {
            return $"{SearchLabel} (販売なし)";
        }

        return SearchLabel;
    }

    private static void SetMenuItemEnabled(MenuItem menuItem, bool enabled)
    {
        // Dalamudバージョン差異に備えて反射で有効・無効を設定する
        var candidates = new[] { "IsEnabled", "Enabled", "IsDisabled", "Disabled" };
        foreach (var name in candidates)
        {
            var property = menuItem.GetType().GetProperty(name);
            if (property == null || !property.CanWrite)
            {
                continue;
            }

            if (property.PropertyType == typeof(bool))
            {
                var value = name.Contains("Disabled", StringComparison.OrdinalIgnoreCase) ? !enabled : enabled;
                property.SetValue(menuItem, value);
                return;
            }
        }
    }

    private static bool TryGetProperty(object target, string propertyName, out object? value)
    {
        value = null;
        var property = target.GetType().GetProperty(propertyName);
        if (property == null)
        {
            return false;
        }

        value = property.GetValue(target);
        return true;
    }

    private static uint? TryGetRowId(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is uint directUint)
        {
            return directUint;
        }

        if (value is int directInt && directInt >= 0)
        {
            return (uint)directInt;
        }

        var rowIdProperty = value.GetType().GetProperty("ItemId");
        if (rowIdProperty != null)
        {
            var rowIdValue = rowIdProperty.GetValue(value);
            if (rowIdValue is uint rowIdUint)
            {
                return rowIdUint;
            }

            if (rowIdValue is int rowIdInt && rowIdInt >= 0)
            {
                return (uint)rowIdInt;
            }
        }

        return null;
    }
}

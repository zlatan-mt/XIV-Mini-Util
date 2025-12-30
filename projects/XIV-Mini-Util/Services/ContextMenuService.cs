// Path: projects/XIV-Mini-Util/Services/ContextMenuService.cs
// Description: アイテム右クリックメニューに販売場所検索を追加する
// Reason: コンテキストメニュー処理を独立させて保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace XivMiniUtil.Services;

public sealed class ContextMenuService : IDisposable
{
    private const string SearchLabel = "販売場所を検索";

    private readonly IContextMenu _contextMenu;
    private readonly ShopSearchService _shopSearchService;
    private readonly ShopDataCache _shopDataCache;
    private readonly IPluginLog _pluginLog;

    public ContextMenuService(
        IContextMenu contextMenu,
        ShopSearchService shopSearchService,
        ShopDataCache shopDataCache,
        IPluginLog pluginLog)
    {
        _contextMenu = contextMenu;
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
        if (!TryGetItemId(args.Target, out var itemId))
        {
            return;
        }

        var isReady = _shopDataCache.IsInitialized;
        var hasData = _shopDataCache.HasShopData(itemId);
        var label = BuildMenuLabel(isReady, hasData);

        // MenuItemはNameとOnClickedを明示的に設定する
        var menuItem = new MenuItem
        {
            Name = new SeStringBuilder().AddText(label).Build(),
            OnClicked = OnSearchClicked,
        };

        SetMenuItemEnabled(menuItem, isReady && hasData);
        args.AddMenuItem(menuItem);
    }

    private void OnSearchClicked(IMenuItemClickedArgs args)
    {
        if (!TryGetItemId(args.Target, out var itemId))
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

    private static bool TryGetItemId(object target, out uint itemId)
    {
        // インベントリアイテム
        if (target is MenuTargetInventory inventoryTarget && inventoryTarget.TargetItem is { } inventoryItem)
        {
            itemId = inventoryItem.ItemId;
            return itemId != 0;
        }

        // チャットリンク等の一般的なコンテキストメニュー
        if (target is MenuTargetDefault defaultTarget)
        {
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

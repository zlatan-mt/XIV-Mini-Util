// Path: projects/XIV-Mini-Util/Services/ContextMenuService.cs
// Description: アイテム右クリックメニューに販売場所検索を追加する
// Reason: コンテキストメニュー処理を独立させて保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Reflection;
using XivMiniUtil.Services.Market;

namespace XivMiniUtil.Services.Shop;

public sealed class ContextMenuService : IDisposable
{
    private const ulong ItemIdVariantOffset = 500000;
    private const ulong HighQualityItemIdOffset = 1000000;
    private const string SearchLabel = "販売場所を検索";
    private const string UniversalisSearchLabel = "Universalisで価格確認";

    private readonly IContextMenu _contextMenu;
    private readonly IGameGui _gameGui;
    private readonly IDataManager _dataManager;
    private readonly ShopSearchService _shopSearchService;
    private readonly ShopDataCache _shopDataCache;
    private readonly UniversalisMarketService _universalisMarketService;
    private readonly IPluginLog _pluginLog;
    private readonly ColorantItemResolver _colorantItemResolver;

    public ContextMenuService(
        IContextMenu contextMenu,
        IGameGui gameGui,
        IDataManager dataManager,
        ShopSearchService shopSearchService,
        ShopDataCache shopDataCache,
        UniversalisMarketService universalisMarketService,
        IPluginLog pluginLog)
    {
        _contextMenu = contextMenu;
        _gameGui = gameGui;
        _dataManager = dataManager;
        _shopSearchService = shopSearchService;
        _shopDataCache = shopDataCache;
        _universalisMarketService = universalisMarketService;
        _pluginLog = pluginLog;
        _colorantItemResolver = new ColorantItemResolver(gameGui, shopDataCache, pluginLog);

        _contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose()
    {
        _contextMenu.OnMenuOpened -= OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        // 調査用：染色画面のコンテキスト情報を確認する
        var currentAddon = args.AddonName ?? string.Empty;
        if (currentAddon.Contains("Dye", StringComparison.OrdinalIgnoreCase) || currentAddon.Contains("Colorant", StringComparison.OrdinalIgnoreCase))
        {
            var targetType = args.Target?.GetType().Name ?? "null";
            var hovered = _gameGui.HoveredItem;
            _pluginLog.Information($"[ContextMenuDebug] Addon={currentAddon}, Target={targetType}, HoveredItem={hovered}");
        }

        var addonName = args.AddonName ?? string.Empty;
        if (addonName == "ColorantColoring")
        {
            // 右クリックごとに差分状態を初期化し、前回の差分が次回に持ち越されないようにする。
            _colorantItemResolver.ResetNumericDiff("ContextMenuOpened");
        }

        if (args.Target == null || !TryGetItemContext(args.Target, addonName, out var itemContext))
        {
            return;
        }

        var itemId = itemContext.ItemId;
        var isReady = _shopDataCache.IsInitialized;
        var hasData = _shopDataCache.HasShopData(itemId);
        var isMarketable = IsMarketableItem(itemId);
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

        var universalisMenuItem = new MenuItem
        {
            Name = new SeStringBuilder().AddText(BuildUniversalisMenuLabel(isMarketable, itemContext.Quality)).Build(),
            OnClicked = OnUniversalisSearchClicked,
            PrefixChar = 'U',
        };

        SetMenuItemEnabled(universalisMenuItem, isMarketable);
        args.AddMenuItem(universalisMenuItem);
    }

    private void OnSearchClicked(IMenuItemClickedArgs args)
    {
        var addonName = args.AddonName ?? string.Empty;
        if (args.Target == null || !TryGetItemContext(args.Target, addonName, out var itemContext))
        {
            _pluginLog.Warning("販売場所検索の対象アイテムが取得できませんでした。");
            return;
        }

        var itemId = itemContext.ItemId;
        if (!_shopDataCache.IsInitialized)
        {
            _shopSearchService.Search(itemId);
            return;
        }

        _shopSearchService.Search(itemId);
    }

    private void OnUniversalisSearchClicked(IMenuItemClickedArgs args)
    {
        var addonName = args.AddonName ?? string.Empty;
        if (args.Target == null || !TryGetItemContext(args.Target, addonName, out var itemContext))
        {
            _pluginLog.Warning("Universalis検索の対象アイテムが取得できませんでした。");
            return;
        }

        var itemId = itemContext.ItemId;
        if (!IsMarketableItem(itemId))
        {
            _pluginLog.Information($"Universalis検索対象外アイテム: {itemId}");
            return;
        }

        _universalisMarketService.CheckLowestPrice(itemId, itemContext.Quality);
    }

    private bool TryGetItemId(object target, string addonName, out uint itemId)
    {
        if (TryGetItemContext(target, addonName, out var itemContext))
        {
            itemId = itemContext.ItemId;
            return true;
        }

        itemId = 0;
        return false;
    }

    private bool TryGetItemContext(object target, string addonName, out ItemContext itemContext)
    {
        // 染色画面はAddonのAtkValuesからItemIdを取得する
        if (addonName == "ColorantColoring")
        {
            var colorantItemId = _colorantItemResolver.TryGetItemIdFromAddon();
            if (colorantItemId != 0)
            {
                itemContext = CreateItemContext(colorantItemId);
                _pluginLog.Information($"ColorantAddon経由でItemId取得: {itemContext.ItemId}");
                return true;
            }
        }

        // インベントリアイテム
        if (target is MenuTargetInventory inventoryTarget && inventoryTarget.TargetItem is { } inventoryItem)
        {
            itemContext = CreateItemContext(inventoryItem.ItemId);
            return itemContext.ItemId != 0;
        }

        // Agent経由でItemIdを取得（チャットログ、レシピノートなど）
        var agentItemId = GetRawItemIdFromAgent(addonName);
        if (agentItemId != 0)
        {
            itemContext = CreateItemContext(agentItemId);
            _pluginLog.Information($"Agent経由でItemId取得: {itemContext.ItemId} (Addon: {addonName}, Quality: {itemContext.Quality})");
            return true;
        }

        // フォールバック: HoveredItemから取得
        var hoveredItem = _gameGui.HoveredItem;
        if (hoveredItem > 0)
        {
            var hoveredItemContext = CreateItemContext(hoveredItem);
            if (hoveredItemContext.ItemId != 0)
            {
                itemContext = hoveredItemContext;
                _pluginLog.Information($"HoveredItem経由でItemId取得: {itemContext.ItemId} (Quality: {itemContext.Quality})");
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
                itemContext = CreateItemContext(chatItemId);
                return true;
            }

            // ContentIdOrItemIdから取得（アイテムリンクの場合）
            if (TryGetProperty(defaultTarget, "ContentIdOrItemId", out var contentId) && contentId is ulong contentIdVal && contentIdVal != 0)
            {
                // アイテムIDは下位32ビット
                var extractedId = (uint)(contentIdVal & 0xFFFFFFFF);
                var extractedContext = CreateItemContext(extractedId);
                if (extractedContext.ItemId != 0 && extractedContext.ItemId < ItemIdVariantOffset)
                {
                    itemContext = extractedContext;
                    return true;
                }
            }
        }

        if (TryGetProperty(target, "ItemId", out var rawItemId) && rawItemId is uint directItemId)
        {
            itemContext = CreateItemContext(directItemId);
            return itemContext.ItemId != 0;
        }

        if (TryGetProperty(target, "ItemID", out var rawItemIdAlt) && rawItemIdAlt is uint directItemIdAlt)
        {
            itemContext = CreateItemContext(directItemIdAlt);
            return itemContext.ItemId != 0;
        }

        if (TryGetProperty(target, "TargetItem", out var targetItem))
        {
            var reflectedItemId = TryGetRowId(targetItem);
            if (reflectedItemId is { } candidate && candidate != 0)
            {
                itemContext = CreateItemContext(candidate);
                return true;
            }
        }

        itemContext = default;
        return false;
    }

    private unsafe uint GetRawItemIdFromAgent(string addonName)
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
                case "ColorantColoring":
                    itemId = _colorantItemResolver.TryGetItemIdFromAgent();
                    break;
            }

            return itemId;
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

    private static string BuildUniversalisMenuLabel(bool isMarketable, UniversalisItemQuality quality)
    {
        var qualityLabel = quality == UniversalisItemQuality.HighQuality ? "HQ" : "NQ";
        var label = $"{UniversalisSearchLabel} ({qualityLabel})";
        return isMarketable ? label : $"{label} (取引不可)";
    }

    private bool IsMarketableItem(uint itemId)
    {
        var normalizedItemId = NormalizeItemId(itemId);
        var item = _dataManager.Excel.GetSheet<Item>().GetRowOrDefault(normalizedItemId);
        return item.HasValue && item.Value.ItemSearchCategory.RowId != 0;
    }

    internal static uint NormalizeItemId(uint itemId)
    {
        return NormalizeItemId((ulong)itemId);
    }

    internal static UniversalisItemQuality GetItemQuality(uint itemId)
    {
        return GetItemQuality((ulong)itemId);
    }

    private static uint NormalizeItemId(ulong itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        var normalized = itemId % ItemIdVariantOffset;
        if (normalized != 0)
        {
            return (uint)normalized;
        }

        return itemId <= uint.MaxValue ? (uint)itemId : 0;
    }

    private static UniversalisItemQuality GetItemQuality(ulong itemId)
    {
        return itemId >= HighQualityItemIdOffset && NormalizeItemId(itemId) != 0
            ? UniversalisItemQuality.HighQuality
            : UniversalisItemQuality.Normal;
    }

    private static ItemContext CreateItemContext(ulong rawItemId)
    {
        return new ItemContext(NormalizeItemId(rawItemId), GetItemQuality(rawItemId));
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

    private readonly record struct ItemContext(uint ItemId, UniversalisItemQuality Quality);
}

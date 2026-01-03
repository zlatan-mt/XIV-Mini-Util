// Path: projects/XIV-Mini-Util/Services/ContextMenuService.cs
// Description: アイテム右クリックメニューに販売場所検索を追加する
// Reason: コンテキストメニュー処理を独立させて保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Plugin.cs
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Reflection;

namespace XivMiniUtil.Services;

public sealed class ContextMenuService : IDisposable
{
    private const string SearchLabel = "販売場所を検索";

    private readonly IContextMenu _contextMenu;
    private readonly IGameGui _gameGui;
    private readonly ShopSearchService _shopSearchService;
    private readonly ShopDataCache _shopDataCache;
    private readonly IPluginLog _pluginLog;
    private bool _loggedColorantDebug;

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
        // 調査用：染色画面のコンテキスト情報を確認する
        var currentAddon = args.AddonName ?? string.Empty;
        if (currentAddon.Contains("Dye", StringComparison.OrdinalIgnoreCase) || currentAddon.Contains("Colorant", StringComparison.OrdinalIgnoreCase))
        {
            var targetType = args.Target?.GetType().Name ?? "null";
            var hovered = _gameGui.HoveredItem;
            _pluginLog.Information($"[ContextMenuDebug] Addon={currentAddon}, Target={targetType}, HoveredItem={hovered}");
        }

        var addonName = args.AddonName ?? string.Empty;
        if (args.Target == null || !TryGetItemId(args.Target, addonName, out var itemId))
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
        if (args.Target == null || !TryGetItemId(args.Target, addonName, out var itemId))
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
        // 染色画面はAddonのAtkValuesからItemIdを取得する
        if (addonName == "ColorantColoring")
        {
            var colorantItemId = GetColorantItemIdFromAddon();
            if (colorantItemId != 0)
            {
                itemId = colorantItemId;
                _pluginLog.Information($"ColorantAddon経由でItemId取得: {itemId}");
                return true;
            }
        }

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
                case "ColorantColoring":
                    itemId = GetItemIdFromColorantAgent();
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

    private unsafe uint GetItemIdFromColorantAgent()
    {
        try
        {
            var agentColorant = AgentColorant.Instance();
            if (agentColorant == null)
            {
                return 0;
            }

            var stainId = agentColorant->CharaView.SelectedStain;
            if (stainId == 0)
            {
                // 右クリックのみの場合はSelectedStainが入らないことがあるため、他候補を探す
                stainId = TryGetStainIdFromCharaView(agentColorant->CharaView);
            }

            if (stainId == 0)
            {
                LogColorantDebugOnce(agentColorant);
                return 0;
            }

            var itemId = _shopDataCache.GetItemIdFromStain(stainId);
            if (itemId != 0)
            {
                _pluginLog.Information($"染色取得: Stain={stainId} ItemId={itemId}");
            }

            return itemId;
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "ColorantAgentからのItemId取得に失敗しました。");
            return 0;
        }
    }

    private static byte TryGetStainIdFromCharaView<T>(T charaView) where T : struct
    {
        var candidateNames = new[]
        {
            "SelectedStain",
            "SelectedStainId",
            "SelectedColorant",
            "SelectedColorantId",
            "HoverStain",
            "HoveredStain",
            "HoveredColorant",
            "HoveredColorantId",
            "CurrentStain",
            "ActiveStain",
            "CurrentColorant",
            "ActiveColorant",
        };

        foreach (var name in candidateNames)
        {
            if (TryGetFieldValue(charaView, name, out var stainId) && stainId != 0)
            {
                return stainId;
            }
        }

        return 0;
    }

    private unsafe void LogColorantDebugOnce(AgentColorant* agentColorant)
    {
        if (_loggedColorantDebug)
        {
            return;
        }

        _loggedColorantDebug = true;

        try
        {
            var selected = agentColorant->CharaView.SelectedStain;
            _pluginLog.Information($"[ColorantDebug] SelectedStain={selected}");

            var charaView = agentColorant->CharaView;
            LogStructFields(charaView, "CharaView");
            LogStructNumericFields(charaView, "CharaView");
            LogStructFieldsRecursive(charaView, "CharaView", 2, false);

            // Agent本体にも手がかりがある可能性があるため、広めに調査する
            var agentValue = *agentColorant;
            LogStructFields(agentValue, "AgentColorant");
            LogStructNumericFields(agentValue, "AgentColorant");
            LogStructFieldsRecursive(agentValue, "AgentColorant", 2, false);
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "ColorantDebugの出力に失敗しました。");
        }
    }

    private void LogStructFields<T>(T value, string label) where T : struct
    {
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields.Where(field =>
            field.Name.Contains("Stain", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("Dye", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var fieldValue = field.GetValueDirect(__makeref(value));
                _pluginLog.Information($"[ColorantDebug] {label}.{field.Name}={fieldValue}");
            }
            catch
            {
                // 反射読み取りに失敗しても調査を継続する
            }
        }
    }

    private void LogStructNumericFields<T>(T value, string label) where T : struct
    {
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var logged = 0;

        foreach (var field in fields)
        {
            if (logged >= 60)
            {
                break;
            }

            var fieldType = field.FieldType;
            if (!IsNumericFieldType(fieldType))
            {
                continue;
            }

            try
            {
                var fieldValue = field.GetValueDirect(__makeref(value));
                _pluginLog.Information($"[ColorantDebug] {label}.{field.Name}={fieldValue}");
                logged++;
            }
            catch
            {
                // 反射読み取りに失敗しても調査を継続する
            }
        }
    }

    private static bool IsNumericFieldType(Type fieldType)
    {
        return fieldType == typeof(byte)
            || fieldType == typeof(sbyte)
            || fieldType == typeof(short)
            || fieldType == typeof(ushort)
            || fieldType == typeof(int)
            || fieldType == typeof(uint)
            || fieldType == typeof(long)
            || fieldType == typeof(ulong);
    }

    private void LogStructFieldsRecursive(object value, string label, int depth, bool parentHasKeyword)
    {
        if (depth < 0)
        {
            return;
        }

        var valueType = value.GetType();
        var fields = valueType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.FieldType.IsPointer)
            {
                continue;
            }

            var fieldName = field.Name;
            var hasKeyword = parentHasKeyword || FieldNameHasKeyword(fieldName);

            object? fieldValue;
            try
            {
                fieldValue = field.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (field.FieldType.IsEnum || IsNumericFieldType(field.FieldType))
            {
                if (hasKeyword || FieldNameHasKeyword(fieldName))
                {
                    _pluginLog.Information($"[ColorantDebug] {label}.{fieldName}={fieldValue}");
                }
                continue;
            }

            if (field.FieldType.IsValueType)
            {
                if (depth == 0)
                {
                    continue;
                }

                var nextLabel = $"{label}.{fieldName}";
                if (fieldValue != null)
                {
                    LogStructFieldsRecursive(fieldValue, nextLabel, depth - 1, hasKeyword);
                }
            }
        }
    }

    private static bool FieldNameHasKeyword(string fieldName)
    {
        return fieldName.Contains("Stain", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("Color", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("Dye", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("Colorant", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("Selected", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("Hover", StringComparison.OrdinalIgnoreCase);
    }

    private unsafe uint GetColorantItemIdFromAddon()
    {
        nint addonPtr = _gameGui.GetAddonByName("ColorantColoring", 1);
        if (addonPtr == nint.Zero)
        {
            return 0;
        }

        var addon = (AtkUnitBase*)addonPtr;
        if (addon->AtkValuesCount == 0)
        {
            return 0;
        }

        var matches = new List<(int Index, uint ItemId)>();
        var count = addon->AtkValuesCount;
        for (var i = 0; i < count; i++)
        {
            var value = addon->AtkValues[i];
            var candidate = ExtractItemId(value);
            if (candidate == 0)
            {
                continue;
            }

            if (_shopDataCache.IsStainItemId(candidate))
            {
                matches.Add((i, candidate));
                if (matches.Count >= 12)
                {
                    break;
                }
            }
        }

        if (matches.Count == 1)
        {
            return matches[0].ItemId;
        }

        if (matches.Count > 1)
        {
            var summary = string.Join(", ", matches.Select(m => $"[{m.Index}]={m.ItemId}"));
            _pluginLog.Information($"[ColorantDebug] 染料候補: {summary}");
        }
        else if (matches.Count == 0)
        {
            var stainCandidates = new List<(int Index, byte StainId, uint ItemId)>();
            var rawStainCandidates = new List<(int Index, byte StainId)>();
            for (var i = 0; i < count; i++)
            {
                var value = addon->AtkValues[i];
                var stainId = ExtractStainId(value);
                if (stainId == 0)
                {
                    continue;
                }

                if (rawStainCandidates.Count < 12)
                {
                    rawStainCandidates.Add((i, stainId));
                }

                var itemId = _shopDataCache.GetItemIdFromStain(stainId);
                if (itemId != 0)
                {
                    stainCandidates.Add((i, stainId, itemId));
                    if (stainCandidates.Count >= 12)
                    {
                        break;
                    }
                }
            }

            if (stainCandidates.Count == 1)
            {
                return stainCandidates[0].ItemId;
            }

            if (stainCandidates.Count > 1)
            {
                // 実測ログで、Index=14が右クリックした染料と連動して変化するため優先する
                var preferred = stainCandidates.FirstOrDefault(m => m.Index == 14);
                if (preferred.ItemId != 0)
                {
                    _pluginLog.Information($"[ColorantDebug] 染色ID候補からIndex=14を採用: Stain={preferred.StainId} ItemId={preferred.ItemId}");
                    return preferred.ItemId;
                }

                var summary = string.Join(", ", stainCandidates.Select(m => $"[{m.Index}]=Stain:{m.StainId} ItemId:{m.ItemId}"));
                _pluginLog.Information($"[ColorantDebug] 染色ID候補: {summary}");
            }
            else if (rawStainCandidates.Count > 0)
            {
                var summary = string.Join(", ", rawStainCandidates.Select(m => $"[{m.Index}]=Stain:{m.StainId}"));
                _pluginLog.Information($"[ColorantDebug] 染色ID候補(未解決): {summary}");
            }

            // 取得できる数値候補を限定的にログ出しして手掛かりを集める
            var numericCandidates = new List<string>();
            var limit = Math.Min((int)count, 60);
            for (var i = 0; i < limit; i++)
            {
                var value = addon->AtkValues[i];
                if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt && value.UInt != 0)
                {
                    numericCandidates.Add($"[{i}]=UInt:{value.UInt}");
                }
                else if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int && value.Int > 0)
                {
                    numericCandidates.Add($"[{i}]=Int:{value.Int}");
                }

                if (numericCandidates.Count >= 12)
                {
                    break;
                }
            }

            var numericSummary = numericCandidates.Count == 0 ? "<none>" : string.Join(", ", numericCandidates);
            _pluginLog.Information($"[ColorantDebug] 染料候補なし: AtkValuesCount={count}, NumericCandidates={numericSummary}");
        }

        return 0;
    }

    private static uint ExtractItemId(AtkValue value)
    {
        switch (value.Type)
        {
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt:
                return value.UInt % 500000;
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int:
                return value.Int > 0 ? (uint)value.Int % 500000 : 0;
            default:
                return 0;
        }
    }

    private static byte ExtractStainId(AtkValue value)
    {
        switch (value.Type)
        {
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt:
                return value.UInt <= byte.MaxValue ? (byte)value.UInt : (byte)0;
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int:
                return value.Int > 0 && value.Int <= byte.MaxValue ? (byte)value.Int : (byte)0;
            default:
                return 0;
        }
    }

    private static bool TryGetFieldValue<TStruct>(TStruct value, string fieldName, out byte result) where TStruct : struct
    {
        result = 0;
        var field = typeof(TStruct).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return false;
        }

        try
        {
            var raw = field.GetValueDirect(__makeref(value));
            if (raw is byte byteValue)
            {
                result = byteValue;
                return true;
            }

            if (raw is ushort ushortValue && ushortValue <= byte.MaxValue)
            {
                result = (byte)ushortValue;
                return true;
            }

            if (raw is uint uintValue && uintValue <= byte.MaxValue)
            {
                result = (byte)uintValue;
                return true;
            }
        }
        catch
        {
            return false;
        }

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

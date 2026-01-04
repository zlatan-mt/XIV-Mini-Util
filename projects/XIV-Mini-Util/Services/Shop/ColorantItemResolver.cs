// Path: projects/XIV-Mini-Util/Services/ColorantItemResolver.cs
// Description: 染色画面からのアイテムID取得ロジックを担当する
// Reason: ContextMenuServiceから巨大な染色解析ロジックを分離し保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ContextMenuService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace XivMiniUtil.Services.Shop;

public sealed class ColorantItemResolver
{
    private readonly IGameGui _gameGui;
    private readonly ShopDataCache _shopDataCache;
    private readonly IPluginLog _pluginLog;
    private bool _loggedColorantDebug;
    private bool _loggedColorantTextDebug;
    private Dictionary<int, int>? _lastColorantNumericSnapshot;
    private List<int> _lastColorantNumericDiffIndices = new();
    private int _lastColorantSelectedIndex = -1;
    private string _lastColorantSelectedLabel = string.Empty;
    private long _lastColorantDecisionTick;
    private uint _lastColorantDecisionItemId;

    public ColorantItemResolver(
        IGameGui gameGui,
        ShopDataCache shopDataCache,
        IPluginLog pluginLog)
    {
        _gameGui = gameGui;
        _shopDataCache = shopDataCache;
        _pluginLog = pluginLog;
    }

    public void ResetNumericDiff(string reason)
    {
        _lastColorantNumericSnapshot = null;
        _lastColorantNumericDiffIndices = new List<int>();
        _pluginLog.Information($"[ColorantDebug] 数値差分をリセット: Reason={reason}");
    }

    public unsafe uint TryGetItemIdFromAgent()
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

    public unsafe uint TryGetItemIdFromAddon()
    {
        nint addonPtr = _gameGui.GetAddonByName("ColorantColoring", 1);
        if (addonPtr == nint.Zero)
        {
            return 0;
        }

        var nowTick = Environment.TickCount64;
        if (_lastColorantDecisionItemId != 0 && nowTick - _lastColorantDecisionTick <= 800)
        {
            _pluginLog.Information($"ColorantAddon決定キャッシュを使用: {_lastColorantDecisionItemId}");
            return _lastColorantDecisionItemId;
        }

        var addon = (AtkUnitBase*)addonPtr;
        if (addon->AtkValuesCount == 0)
        {
            return 0;
        }

        var textItemId = GetItemIdFromAddonText(addon, addon->AtkValuesCount);
        if (textItemId != 0)
        {
            _lastColorantDecisionItemId = textItemId;
            _lastColorantDecisionTick = nowTick;
            return textItemId;
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
            _lastColorantDecisionItemId = matches[0].ItemId;
            _lastColorantDecisionTick = nowTick;
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
                _lastColorantDecisionItemId = stainCandidates[0].ItemId;
                _lastColorantDecisionTick = nowTick;
                return stainCandidates[0].ItemId;
            }

            if (stainCandidates.Count > 1)
            {
                // 実測ログで、Index=14が右クリックした染料と連動して変化するため優先する
                var preferred = stainCandidates.FirstOrDefault(m => m.Index == 14);
                if (preferred.ItemId != 0)
                {
                    _pluginLog.Information($"[ColorantDebug] 染色ID候補からIndex=14を採用: Stain={preferred.StainId} ItemId={preferred.ItemId}");
                    _lastColorantDecisionItemId = preferred.ItemId;
                    _lastColorantDecisionTick = nowTick;
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

    private unsafe uint GetItemIdFromAddonText(AtkUnitBase* addon, ushort count)
    {
        var labelCandidates = new List<string>();
        var textCandidates = new List<string>();
        var allStrings = new List<(int Index, string Text)>();
        var labelIndexMap = new Dictionary<int, string>();
        var labelIndexList = new List<(int Index, string Label)>();
        var itemLabelIndexList = new List<(int Index, string Label, uint ItemId)>();
        var itemTextCandidates = new List<string>();
        var numericCandidates = new List<(int Index, int Value)>();
        var colorantItemCandidates = new List<(int Index, uint ItemId)>();

        for (var i = 0; i < count; i++)
        {
            var value = addon->AtkValues[i];
            var itemCandidate = ExtractItemId(value);
            if (itemCandidate != 0)
            {
                // カララント判定に加え、テレビン油（Terebinth）も対象とする
                // 短絡評価により、カララントでない場合のみ名前チェックを行う
                if (_shopDataCache.IsLikelyColorantItemId(itemCandidate)
                    || IsTurpentineLabel(_shopDataCache.GetItemName(itemCandidate)))
                {
                    colorantItemCandidates.Add((i, itemCandidate));
                }
            }

            var text = ExtractString(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var sanitized = SanitizeAddonText(text);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                continue;
            }

            allStrings.Add((i, sanitized));

            if (!TryExtractColorantLabel(sanitized, out var label))
            {
                if (TryExtractItemLabel(sanitized, out var itemLabel, out var itemId))
                {
                    itemLabelIndexList.Add((i, itemLabel, itemId));
                    if (labelCandidates.Count < 20)
                    {
                        labelCandidates.Add($"[{i}] {itemLabel}");
                    }
                    continue;
                }

                if (!IsIgnorableColorantUiText(sanitized) && itemTextCandidates.Count < 20)
                {
                    itemTextCandidates.Add($"[{i}] {sanitized}");
                }

                if (textCandidates.Count < 20)
                {
                    textCandidates.Add($"[{i}] {value.Type} {sanitized}");
                }
                continue;
            }

            if (labelCandidates.Count < 20)
            {
                labelCandidates.Add($"[{i}] {label}");
            }

            if (!labelIndexMap.ContainsKey(i))
            {
                labelIndexMap[i] = label;
            }

            labelIndexList.Add((i, label));
        }

        CaptureColorantNumericDiff(addon, count, numericCandidates);

        var hasColorantSelection = false;
        var colorantSelectedIndex = -1;
        var colorantSelectedLabel = string.Empty;
        var colorantSelectedId = 0u;

        if (TryFindSelectedColorantLabelFromUsageSection(allStrings, out var selectedLabel))
        {
            var selectedId = _shopDataCache.GetItemIdFromName(selectedLabel);
            if (selectedId != 0)
            {
                hasColorantSelection = true;
                colorantSelectedLabel = selectedLabel;
                colorantSelectedId = selectedId;
            }
        }

        if (colorantSelectedId == 0
            && TryFindSelectedColorantLabelByIndex(addon, count, labelIndexMap, labelIndexList, _pluginLog, out var indexedLabel, out var indexedColorantIndex))
        {
            // 選択Indexが確定している場合は、数値候補ブロックからItemIdを対応付ける
            var mappedItemId = TryMapColorantItemIdByIndex(indexedColorantIndex, labelIndexList.Count, colorantItemCandidates);
            if (mappedItemId != 0)
            {
                hasColorantSelection = true;
                colorantSelectedIndex = indexedColorantIndex;
                colorantSelectedLabel = indexedLabel;
                colorantSelectedId = mappedItemId;
            }

            if (colorantSelectedId == 0)
            {
                var indexedId = _shopDataCache.GetItemIdFromName(indexedLabel);
                if (indexedId != 0)
                {
                    hasColorantSelection = true;
                    colorantSelectedIndex = indexedColorantIndex;
                    colorantSelectedLabel = indexedLabel;
                    colorantSelectedId = indexedId;
                }
            }
        }

        var hasItemSelection = false;
        var itemSelectedLabel = string.Empty;
        var itemSelectedId = 0u;
        var itemDiffIndex = -1;
        if (TryResolveItemIdFromChangedIndex(itemLabelIndexList, numericCandidates, out var changedLabel, out var changedId, out itemDiffIndex))
        {
            hasItemSelection = true;
            itemSelectedLabel = changedLabel;
            itemSelectedId = changedId;
        }

        var colorantChanged = HasColorantSelectionChanged(colorantSelectedIndex, colorantSelectedLabel);
        if (hasColorantSelection && hasItemSelection)
        {
            _pluginLog.Information($"[ColorantDebug] 両方候補: Colorant={colorantSelectedId}({colorantSelectedLabel}) Item={itemSelectedId}({itemSelectedLabel})");
        }

        if (hasItemSelection
            && itemSelectedId != 0
            && itemDiffIndex >= 200
            && (!colorantChanged || IsTurpentineLabel(itemSelectedLabel)))
        {
            _pluginLog.Information($"ColorantAddonアイテム選択(差分)経由でItemId取得: {itemSelectedId} (Text={itemSelectedLabel}, DiffIndex={itemDiffIndex})");
            // 高DiffIndexはアイテム優先。次の染色選択で変更判定が動くように状態をリセットする。
            _lastColorantSelectedIndex = -1;
            _lastColorantSelectedLabel = string.Empty;
            return itemSelectedId;
        }

        if (hasColorantSelection && colorantSelectedId != 0 && colorantChanged)
        {
            _pluginLog.Information($"ColorantAddon選択Index(数値)経由でItemId取得: {colorantSelectedId} (Index={colorantSelectedIndex})");
            UpdateLastColorantSelection(colorantSelectedIndex, colorantSelectedLabel);
            return colorantSelectedId;
        }

        if (hasItemSelection && itemSelectedId != 0)
        {
            _pluginLog.Information($"ColorantAddonアイテム選択(差分)経由でItemId取得: {itemSelectedId} (Text={itemSelectedLabel})");
            return itemSelectedId;
        }

        if (hasColorantSelection && colorantSelectedId != 0)
        {
            _pluginLog.Information($"ColorantAddon選択Index(数値)経由でItemId取得: {colorantSelectedId} (Index={colorantSelectedIndex})");
            UpdateLastColorantSelection(colorantSelectedIndex, colorantSelectedLabel);
            return colorantSelectedId;
        }

        if (itemLabelIndexList.Count > 0 && labelIndexList.Count == 0 && colorantItemCandidates.Count == 0)
        {
            if (TryResolveItemIdFromItemLabels(itemLabelIndexList, out var resolvedLabel, out var resolvedId))
            {
                _pluginLog.Information($"ColorantAddonアイテム候補経由でItemId取得: {resolvedId} (Text={resolvedLabel})");
                return resolvedId;
            }

            if (TryFindSelectedIndex(addon, count, itemLabelIndexList.Count, _pluginLog, out var itemSelectedIndex))
            {
                var adjustedIndex = itemSelectedIndex > 0 ? itemSelectedIndex - 1 : itemSelectedIndex;
                if (adjustedIndex != itemSelectedIndex)
                {
                    _pluginLog.Information($"[ColorantDebug] 選択Index補正: {itemSelectedIndex} -> {adjustedIndex}");
                }

                LogItemIndexWindow(_pluginLog, adjustedIndex, itemLabelIndexList);
                var mappedItemId = MapItemIdBySelectedIndex(adjustedIndex, itemLabelIndexList, out var mappedLabel);
                if (mappedItemId != 0)
                {
                    _pluginLog.Information($"ColorantAddon選択Index(数値)経由でItemId取得: {mappedItemId} (Text={mappedLabel})");
                    return mappedItemId;
                }
            }
        }
        if (itemLabelIndexList.Count > 0)
        {
            var labelSummary = string.Join(", ", itemLabelIndexList.Take(12).Select(entry => $"{entry.Index}:{entry.Label}"));
            _pluginLog.Information($"[ColorantDebug] アイテムラベル候補: {labelSummary}");
        }

        if (itemTextCandidates.Count > 0)
        {
            _pluginLog.Information($"[ColorantDebug] アイテム文字列候補: {string.Join(", ", itemTextCandidates)}");
        }

        if (numericCandidates.Count > 0)
        {
            var summary = string.Join(", ", numericCandidates.Take(12).Select(c => $"[{c.Index}]={c.Value}"));
            _pluginLog.Information($"[ColorantDebug] 数値候補: {summary}");
        }

        foreach (var candidate in labelCandidates.Select(c => c[(c.IndexOf(']') + 1)..].Trim()))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var itemId = _shopDataCache.GetItemIdFromName(candidate);
            if (itemId != 0)
            {
                _pluginLog.Information($"ColorantAddon文字列経由でItemId取得: {itemId} (Text={candidate})");
                return itemId;
            }
        }

        if (!_loggedColorantTextDebug)
        {
            _loggedColorantTextDebug = true;
            if (labelCandidates.Count > 0)
            {
                _pluginLog.Information($"[ColorantDebug] カララント文字列候補: {string.Join(", ", labelCandidates)}");
            }
            else if (textCandidates.Count > 0)
            {
                _pluginLog.Information($"[ColorantDebug] 文字列候補: {string.Join(", ", textCandidates)}");
            }
            else
            {
                _pluginLog.Information("[ColorantDebug] 文字列候補なし");
            }
        }

        return 0;
    }

    private static bool TryGetColorantLabelByListIndex(
        List<(int Index, string Label)> labelIndexList,
        int selectedIndex,
        out string label)
    {
        label = string.Empty;
        if (selectedIndex < 0 || labelIndexList.Count == 0)
        {
            return false;
        }

        labelIndexList.Sort((a, b) => a.Index.CompareTo(b.Index));
        if (selectedIndex >= labelIndexList.Count)
        {
            return false;
        }

        label = labelIndexList[selectedIndex].Label;
        return !string.IsNullOrWhiteSpace(label);
    }

    private bool TryExtractItemLabel(string text, out string label, out uint itemId)
    {
        label = string.Empty;
        itemId = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (IsIgnorableColorantUiText(text))
        {
            return false;
        }

        if (TryExtractColorantLabel(text, out _))
        {
            // カララント名は専用のリストで処理するため、ここでは除外
            return false;
        }

        var normalized = NormalizeItemLabel(text);
        if (string.IsNullOrWhiteSpace(normalized) || IsIgnorableColorantUiText(normalized))
        {
            return false;
        }

        if (TryExtractColorantLabel(normalized, out _))
        {
            return false;
        }

        var candidate = TrimItemLabelSuffixes(normalized);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var candidateId = _shopDataCache.GetItemIdFromName(candidate);
            if (candidateId != 0)
            {
                label = candidate;
                itemId = candidateId;
                return true;
            }
        }

        var fallbackId = _shopDataCache.GetItemIdFromName(normalized);
        if (fallbackId == 0)
        {
            return false;
        }

        label = normalized;
        itemId = fallbackId;
        return true;
    }

    private static string NormalizeItemLabel(string text)
    {
        var trimmed = TrimNonLabelSuffix(text);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        trimmed = TrimTrailingAsciiTag(trimmed);
        trimmed = StripLeadingMarkers(trimmed);
        return trimmed.Trim();
    }

    private static string StripLeadingMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // テレビン油・Terebinth・Turpentineを検出し、その位置から抽出する
        // 先頭にゴミがある場合も、ない場合も統一的に処理する
        if (text.IndexOf("テレビン油", StringComparison.Ordinal) is var jaIndex && jaIndex >= 0)
        {
            return text[jaIndex..].Trim();
        }

        if (text.IndexOf("Terebinth", StringComparison.OrdinalIgnoreCase) is var enIndex && enIndex >= 0)
        {
            return text[enIndex..].Trim();
        }

        if (text.IndexOf("Turpentine", StringComparison.OrdinalIgnoreCase) is var legacyIndex && legacyIndex >= 0)
        {
            return text[legacyIndex..].Trim();
        }

        var start = FindFirstPreferredChar(text, preferNonAscii: true);
        if (start < 0)
        {
            start = FindFirstPreferredChar(text, preferNonAscii: false);
        }

        if (start <= 0)
        {
            return text.Trim();
        }

        return text[start..].Trim();
    }

    private static int FindFirstPreferredChar(string text, bool preferNonAscii)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!IsPreferredItemChar(ch))
            {
                continue;
            }

            if (!preferNonAscii || ch > 0x7F)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsPreferredItemChar(char ch)
    {
        return char.IsLetterOrDigit(ch)
            || ch == '・'
            || ch == 'ー'
            || ch == '－'
            || ch == '-';
    }

    private static string TrimItemLabelSuffixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var separators = new[]
        {
            " 所持",
            " 必要",
            " 所要",
            " 個",
            " 枚",
            " x",
            " ×",
            "／",
            "/",
        };

        foreach (var separator in separators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                return text[..index].Trim();
            }
        }

        return text.Trim();
    }

    private static bool IsIgnorableColorantUiText(string text)
    {
        return text.Contains("このカララント", StringComparison.Ordinal)
            || text.Contains("染色1の使用カララント", StringComparison.Ordinal)
            || text.Contains("染色2の使用カララント", StringComparison.Ordinal)
            || text.Contains("EQUIPMENT", StringComparison.Ordinal);
    }

    private static unsafe string ExtractString(AtkValue value)
    {
        switch (value.Type)
        {
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String:
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.ManagedString:
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.String8:
                return value.String.ToString();
            case FFXIVClientStructs.FFXIV.Component.GUI.ValueType.WideString:
                return Marshal.PtrToStringUni((nint)value.WideString) ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    private static string SanitizeAddonText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var buffer = new char[text.Length];
        var length = 0;
        foreach (var ch in text)
        {
            if (char.IsControl(ch))
            {
                continue;
            }

            buffer[length] = ch;
            length++;
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length).Trim();
    }

    private static bool IsColorantOrRelatedItem(string text)
    {
        return text.Contains("カララント", StringComparison.Ordinal)
            || text.Contains("Colorant", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Dye", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTurpentineLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("テレビン油", StringComparison.Ordinal)
            || text.Contains("Terebinth", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Turpentine", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractColorantLabel(string text, out string label)
    {
        label = string.Empty;
        if (!IsColorantOrRelatedItem(text))
        {
            return false;
        }

        var prefixIndex = text.IndexOf("カララント:", StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            prefixIndex = text.IndexOf("Colorant:", StringComparison.OrdinalIgnoreCase);
        }

        if (prefixIndex >= 0)
        {
            var candidate = text[prefixIndex..];
            label = TrimNonLabelSuffix(candidate);
            return !string.IsNullOrWhiteSpace(label);
        }

        label = TrimNonLabelSuffix(text);
        return !string.IsNullOrWhiteSpace(label);
    }

    private static string TrimNonLabelSuffix(string text)
    {
        var end = text.Length - 1;
        while (end >= 0)
        {
            var ch = text[end];
            if (char.IsLetterOrDigit(ch)
                || ch == ':'
                || ch == ' '
                || ch == '・'
                || ch == 'ー'
                || ch == '－'
                || ch == '-')
            {
                break;
            }

            end--;
        }

        if (end < 0)
        {
            return string.Empty;
        }

        var trimmed = text[..(end + 1)].Trim();
        if (trimmed.StartsWith("カララント:", StringComparison.Ordinal))
        {
            trimmed = TrimTrailingAsciiTag(trimmed);
        }

        return trimmed;
    }

    private static string TrimTrailingAsciiTag(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.EndsWith("IH", StringComparison.Ordinal)
            || text.EndsWith("HQ", StringComparison.Ordinal))
        {
            return text[..^2].Trim();
        }

        return text.Trim();
    }

    private static bool TryFindSelectedColorantLabelFromUsageSection(
        List<(int Index, string Text)> allStrings,
        out string label)
    {
        label = string.Empty;
        if (allStrings.Count == 0)
        {
            return false;
        }

        var usageIndex = allStrings.FindIndex(entry =>
            entry.Text.Contains("染色1の使用カララント", StringComparison.Ordinal)
            || entry.Text.Contains("染色2の使用カララント", StringComparison.Ordinal));

        if (usageIndex < 0)
        {
            return false;
        }

        var start = usageIndex + 1;
        var end = Math.Min(allStrings.Count - 1, usageIndex + 8);

        for (var i = start; i <= end; i++)
        {
            var text = allStrings[i].Text;
            if (TryExtractColorantLabel(text, out var extracted))
            {
                label = extracted;
                return true;
            }
        }

        for (var i = start; i <= end; i++)
        {
            var text = allStrings[i].Text;
            if (text.Contains("カララント", StringComparison.Ordinal)
                || text.Contains("使用", StringComparison.Ordinal)
                || text.Contains("適用", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            label = $"カララント:{text}";
            return true;
        }

        return false;
    }

    private static unsafe bool TryFindSelectedColorantLabelByIndex(
        AtkUnitBase* addon,
        ushort count,
        Dictionary<int, string> labelIndexMap,
        List<(int Index, string Label)> labelIndexList,
        IPluginLog pluginLog,
        out string label,
        out int selectedIndex)
    {
        label = string.Empty;
        selectedIndex = -1;
        if (labelIndexMap.Count == 0 || addon == null || count == 0)
        {
            return false;
        }

        if (labelIndexList.Count > 0)
        {
            labelIndexList.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        for (var i = 0; i < count; i++)
        {
            var value = addon->AtkValues[i];
            int candidate;
            if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int)
            {
                candidate = value.Int;
            }
            else if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt)
            {
                if (value.UInt > int.MaxValue)
                {
                    continue;
                }
                candidate = (int)value.UInt;
            }
            else
            {
                continue;
            }

            if (labelIndexList.Count > 0 && candidate >= 0 && candidate < labelIndexList.Count)
            {
                // 実測ログで、Index=22が右クリックした染色と連動して変化するため優先する
                if (i == 22)
                {
                    label = labelIndexList[candidate].Label;
                    selectedIndex = candidate;
                    LogColorantIndexWindow(pluginLog, candidate, labelIndexList);
                    return true;
                }
            }

            if (labelIndexMap.TryGetValue(candidate, out var indexedLabel))
            {
                label = indexedLabel;
                selectedIndex = candidate;
                return true;
            }
        }

        return false;
    }

    private static unsafe bool TryFindSelectedIndex(
        AtkUnitBase* addon,
        ushort count,
        int listCount,
        IPluginLog pluginLog,
        out int selectedIndex)
    {
        selectedIndex = -1;
        if (listCount <= 0 || addon == null || count == 0)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var value = addon->AtkValues[i];
            int candidate;
            if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int)
            {
                candidate = value.Int;
            }
            else if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt)
            {
                if (value.UInt > int.MaxValue)
                {
                    continue;
                }
                candidate = (int)value.UInt;
            }
            else
            {
                continue;
            }

            if (candidate < 0 || candidate >= listCount)
            {
                continue;
            }

            if (i == 22)
            {
                selectedIndex = candidate;
                return true;
            }

            selectedIndex = candidate;
        }

        if (selectedIndex >= 0)
        {
            return true;
        }

        pluginLog.Warning("[ColorantDebug] 選択Indexの取得に失敗しました。");
        return false;
    }

    private static void LogColorantIndexWindow(IPluginLog pluginLog, int index, List<(int Index, string Label)> labelIndexList)
    {
        if (index < 0 || labelIndexList.Count == 0)
        {
            return;
        }

        var start = Math.Max(0, index - 2);
        var end = Math.Min(labelIndexList.Count - 1, index + 2);
        var window = new List<string>();
        for (var i = start; i <= end; i++)
        {
            window.Add($"{i}:{labelIndexList[i].Label}");
        }

        pluginLog.Information($"[ColorantDebug] 選択Index={index} 周辺ラベル: {string.Join(", ", window)}");
    }

    private static void LogItemIndexWindow(IPluginLog pluginLog, int index, List<(int Index, string Label, uint ItemId)> labelIndexList)
    {
        if (index < 0 || labelIndexList.Count == 0)
        {
            return;
        }

        var start = Math.Max(0, index - 2);
        var end = Math.Min(labelIndexList.Count - 1, index + 2);
        var window = new List<string>();
        for (var i = start; i <= end; i++)
        {
            window.Add($"{i}:{labelIndexList[i].Label}");
        }

        pluginLog.Information($"[ColorantDebug] 選択Index={index} 周辺アイテム: {string.Join(", ", window)}");
    }

    private static uint TryMapColorantItemIdByIndex(
        int selectedIndex,
        int labelCount,
        List<(int Index, uint ItemId)> itemCandidates)
    {
        if (selectedIndex < 0 || labelCount <= 0 || itemCandidates.Count == 0)
        {
            return 0;
        }

        itemCandidates.Sort((a, b) => a.Index.CompareTo(b.Index));

        var blocks = new List<List<uint>>();
        var current = new List<uint>();
        var lastIndex = -2;

        foreach (var (index, itemId) in itemCandidates)
        {
            if (index != lastIndex + 1 && current.Count > 0)
            {
                blocks.Add(current);
                current = new List<uint>();
            }

            current.Add(itemId);
            lastIndex = index;
        }

        if (current.Count > 0)
        {
            blocks.Add(current);
        }

        // ラベル数に最も近いブロックを採用する
        var bestBlock = blocks
            .OrderBy(block => Math.Abs(block.Count - labelCount))
            .FirstOrDefault();

        if (bestBlock == null || selectedIndex >= bestBlock.Count)
        {
            return 0;
        }

        return bestBlock[selectedIndex];
    }

    private static uint MapItemIdBySelectedIndex(
        int selectedIndex,
        List<(int Index, string Label, uint ItemId)> itemLabels,
        out string label)
    {
        label = string.Empty;
        if (selectedIndex < 0 || itemLabels.Count == 0)
        {
            return 0;
        }

        itemLabels.Sort((a, b) => a.Index.CompareTo(b.Index));
        if (selectedIndex >= itemLabels.Count)
        {
            return 0;
        }

        var entry = itemLabels[selectedIndex];
        label = entry.Label;
        return entry.ItemId;
    }

    private bool TryResolveItemIdFromItemLabels(
        List<(int Index, string Label, uint ItemId)> itemLabels,
        out string label,
        out uint itemId)
    {
        label = string.Empty;
        itemId = 0;
        if (itemLabels.Count == 0)
        {
            return false;
        }

        var withShop = itemLabels
            .Where(entry => _shopDataCache.HasShopData(entry.ItemId))
            .ToList();

        if (withShop.Count == 1)
        {
            label = withShop[0].Label;
            itemId = withShop[0].ItemId;
            return true;
        }

        if (itemLabels.Count == 1)
        {
            label = itemLabels[0].Label;
            itemId = itemLabels[0].ItemId;
            return true;
        }

        return false;
    }

    private bool TryResolveItemIdFromChangedIndex(
        List<(int Index, string Label, uint ItemId)> itemLabels,
        List<(int Index, int Value)> numericCandidates,
        out string label,
        out uint itemId,
        out int diffIndex)
    {
        label = string.Empty;
        itemId = 0;
        diffIndex = -1;

        if (itemLabels.Count == 0 || numericCandidates.Count == 0 || _lastColorantNumericDiffIndices.Count == 0)
        {
            return false;
        }

        var listCount = itemLabels.Count;
        var candidate = numericCandidates
            .Where(entry => entry.Value >= 0
                && entry.Value < listCount
                && _lastColorantNumericDiffIndices.Contains(entry.Index))
            .OrderByDescending(entry => entry.Index)
            .FirstOrDefault();

        if (candidate == default)
        {
            return false;
        }

        diffIndex = candidate.Index;
        _pluginLog.Information($"[ColorantDebug] アイテム選択候補: DiffIndex={candidate.Index} Value={candidate.Value} ListCount={listCount}");
        _pluginLog.Information($"[ColorantDebug] 差分Index一覧: {string.Join(", ", _lastColorantNumericDiffIndices)}");
        LogItemIndexWindow(_pluginLog, candidate.Value, itemLabels);
        var mappedId = MapItemIdBySelectedIndex(candidate.Value, itemLabels, out var mappedLabel);
        if (mappedId == 0
            || mappedLabel.StartsWith("カララント:", StringComparison.Ordinal)
            || !_shopDataCache.HasShopData(mappedId))
        {
            return false;
        }

        label = mappedLabel;
        itemId = mappedId;
        return true;
    }

    private bool HasColorantSelectionChanged(int index, string label)
    {
        if (index < 0 && string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (_lastColorantSelectedIndex != index)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(label)
            && !string.Equals(label, _lastColorantSelectedLabel, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private void UpdateLastColorantSelection(int index, string label)
    {
        if (index >= 0)
        {
            _lastColorantSelectedIndex = index;
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            _lastColorantSelectedLabel = label;
        }
    }

    private unsafe void CaptureColorantNumericDiff(
        AtkUnitBase* addon,
        ushort count,
        List<(int Index, int Value)> numericCandidates)
    {
        if (addon == null || count == 0)
        {
            return;
        }

        var snapshot = new Dictionary<int, int>();
        for (var i = 0; i < count; i++)
        {
            var value = addon->AtkValues[i];
            int candidate;
            if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int)
            {
                candidate = value.Int;
            }
            else if (value.Type == FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt)
            {
                if (value.UInt > int.MaxValue)
                {
                    continue;
                }
                candidate = (int)value.UInt;
            }
            else
            {
                continue;
            }

            snapshot[i] = candidate;
            if (candidate >= 0 && candidate <= 128)
            {
                numericCandidates.Add((i, candidate));
            }
        }

        if (_lastColorantNumericSnapshot == null)
        {
            _lastColorantNumericSnapshot = snapshot;
            _lastColorantNumericDiffIndices = new List<int>();
            return;
        }

        var diffs = new List<string>();
        var diffIndices = new List<int>();
        foreach (var (index, value) in snapshot)
        {
            if (_lastColorantNumericSnapshot.TryGetValue(index, out var previous) && previous == value)
            {
                continue;
            }

            diffs.Add($"[{index}] {(_lastColorantNumericSnapshot.TryGetValue(index, out previous) ? previous : 0)} -> {value}");
            diffIndices.Add(index);
            if (diffs.Count >= 12)
            {
                break;
            }
        }

        if (diffs.Count > 0)
        {
            _pluginLog.Information($"[ColorantDebug] 数値差分: {string.Join(", ", diffs)}");
        }

        _lastColorantNumericSnapshot = snapshot;
        _lastColorantNumericDiffIndices = diffIndices;
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
}

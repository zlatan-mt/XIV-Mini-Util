// Path: projects/XIV-Mini-Util/Services/ShopNameIndex.cs
// Description: アイテム名・染色名の索引生成と名前正規化を担当する
// Reason: ShopDataCacheの索引構築責務を分離して保守性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace XivMiniUtil.Services;

internal sealed class ShopNameIndex
{
    private readonly IDataManager _dataManager;
    private readonly Dictionary<string, uint> _itemNameToId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _itemNameNormalizedToId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _stainNameToItemId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _stainNameNormalizedToItemId = new(StringComparer.Ordinal);

    public ShopNameIndex(IDataManager dataManager)
    {
        _dataManager = dataManager;
    }

    public uint GetItemIdFromName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return 0;
        }

        EnsureItemNameIndex();
        if (_itemNameToId.TryGetValue(itemName, out var itemId))
        {
            return itemId;
        }

        var normalized = NormalizeName(itemName);
        return _itemNameNormalizedToId.TryGetValue(normalized, out var normalizedId) ? normalizedId : 0;
    }

    public uint GetItemIdFromStainName(string stainName)
    {
        if (string.IsNullOrWhiteSpace(stainName))
        {
            return 0;
        }

        EnsureStainNameIndex();
        if (_stainNameToItemId.TryGetValue(stainName, out var itemId))
        {
            return itemId;
        }

        var normalized = NormalizeName(stainName);
        return _stainNameNormalizedToItemId.TryGetValue(normalized, out var normalizedItemId) ? normalizedItemId : 0;
    }

    public static bool IsLikelyDyeItemName(string name)
    {
        return name.Contains("染料", StringComparison.Ordinal)
            || name.Contains("カララント", StringComparison.Ordinal)
            || name.Contains("Dye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Colorant", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureItemNameIndex()
    {
        if (_itemNameToId.Count > 0 || _itemNameNormalizedToId.Count > 0)
        {
            return;
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return;
        }

        foreach (var itemRow in itemSheet)
        {
            if (itemRow.RowId == 0)
            {
                continue;
            }

            var name = itemRow.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!_itemNameToId.ContainsKey(name))
            {
                _itemNameToId[name] = itemRow.RowId;
            }

            var normalized = NormalizeName(name);
            if (!string.IsNullOrEmpty(normalized) && !_itemNameNormalizedToId.ContainsKey(normalized))
            {
                _itemNameNormalizedToId[normalized] = itemRow.RowId;
            }
        }
    }

    private void EnsureStainNameIndex()
    {
        if (_stainNameToItemId.Count > 0 || _stainNameNormalizedToItemId.Count > 0)
        {
            return;
        }

        var stainSheet = _dataManager.GetExcelSheet<Stain>();
        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (stainSheet == null || itemSheet == null)
        {
            return;
        }

        var stainNames = new HashSet<string>(StringComparer.Ordinal);
        var stainNamesNormalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stainRow in stainSheet)
        {
            if (stainRow.RowId == 0)
            {
                continue;
            }

            var name = stainRow.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                stainNames.Add(name);
                var normalized = NormalizeName(name);
                if (!string.IsNullOrEmpty(normalized))
                {
                    stainNamesNormalized.Add(normalized);
                }
            }
        }

        var stainNormalizedList = new List<string>(stainNamesNormalized);

        foreach (var itemRow in itemSheet)
        {
            if (itemRow.RowId == 0)
            {
                continue;
            }

            var name = itemRow.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (stainNames.Contains(name) && !_stainNameToItemId.ContainsKey(name))
            {
                _stainNameToItemId[name] = itemRow.RowId;
            }

            var normalized = NormalizeName(name);
            if (!string.IsNullOrEmpty(normalized) && stainNamesNormalized.Contains(normalized) && !_stainNameNormalizedToItemId.ContainsKey(normalized))
            {
                _stainNameNormalizedToItemId[normalized] = itemRow.RowId;
            }

            if (!IsLikelyDyeItemName(name) || string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            foreach (var stainNormalized in stainNormalizedList)
            {
                if (!_stainNameNormalizedToItemId.ContainsKey(stainNormalized) && normalized.Contains(stainNormalized, StringComparison.Ordinal))
                {
                    _stainNameNormalizedToItemId[stainNormalized] = itemRow.RowId;
                }
            }
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var buffer = new char[name.Length];
        var length = 0;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length] = ch;
                length++;
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }
}

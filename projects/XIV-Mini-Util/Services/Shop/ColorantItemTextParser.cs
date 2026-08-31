// Path: projects/XIV-Mini-Util/Services/Shop/ColorantItemTextParser.cs
// Description: カララントUI由来のアイテム表示文字列を正規化する
// Reason: unsafeなAddon探索とゲーム非依存の文字列変換を分離するため

namespace XivMiniUtil.Services.Shop;

internal static class ColorantItemTextParser
{
    public static string NormalizeItemLabel(string text)
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

    public static string TrimItemLabelSuffixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] separators =
        [
            " 所持",
            " 必要",
            " 所要",
            " 個",
            " 枚",
            " x",
            " ×",
            "／",
            "/",
        ];

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

    public static bool IsIgnorableUiText(string text)
    {
        return text.Contains("このカララント", StringComparison.Ordinal)
            || text.Contains("染色1の使用カララント", StringComparison.Ordinal)
            || text.Contains("染色2の使用カララント", StringComparison.Ordinal)
            || text.Contains("EQUIPMENT", StringComparison.Ordinal);
    }

    public static string TrimNonLabelSuffix(string text)
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

    private static string StripLeadingMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

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

        return start <= 0 ? text.Trim() : text[start..].Trim();
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
}

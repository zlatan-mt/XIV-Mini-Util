// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPathHelper.cs
// Description: タイトル背景 TerritoryPath の正規化と validation path 生成を提供する
// Reason: hook/UI/設定から共通利用できる純粋ロジックとして退行を検出するため
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundPathHelper
{
    private const string BgPrefix = "bg/";
    private const string LvbExtension = ".lvb";

    public static string NormalizeTerritoryPathInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Trim().Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (normalized.StartsWith(BgPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[BgPrefix.Length..];
        }

        if (normalized.EndsWith(LvbExtension, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^LvbExtension.Length];
        }

        return normalized.Trim('/');
    }

    public static string BuildLvbPath(string normalizedTerritoryPath)
    {
        return string.IsNullOrWhiteSpace(normalizedTerritoryPath)
            ? string.Empty
            : $"{BgPrefix}{normalizedTerritoryPath}{LvbExtension}";
    }

    public static bool IsLikelyValidNormalizedTerritoryPath(string normalizedTerritoryPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedTerritoryPath)
            || normalizedTerritoryPath.Contains('\\', StringComparison.Ordinal)
            || normalizedTerritoryPath.Contains("//", StringComparison.Ordinal)
            || normalizedTerritoryPath.Contains("..", StringComparison.Ordinal)
            || normalizedTerritoryPath.Contains(':', StringComparison.Ordinal)
            || normalizedTerritoryPath.StartsWith(BgPrefix, StringComparison.OrdinalIgnoreCase)
            || normalizedTerritoryPath.EndsWith(LvbExtension, StringComparison.OrdinalIgnoreCase)
            || normalizedTerritoryPath.StartsWith("/", StringComparison.Ordinal)
            || normalizedTerritoryPath.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return HasValidPackRoot(normalizedTerritoryPath)
            && normalizedTerritoryPath.Contains("/level/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalizeAndValidateTerritoryPath(string? input, out string normalizedTerritoryPath, out string errorMessage)
    {
        normalizedTerritoryPath = NormalizeTerritoryPathInput(input);
        if (string.IsNullOrEmpty(normalizedTerritoryPath))
        {
            errorMessage = "TerritoryPath が空です。";
            return false;
        }

        if (!IsLikelyValidNormalizedTerritoryPath(normalizedTerritoryPath))
        {
            errorMessage = "TerritoryPath は <pack>/.../level/... 形式で指定してください。";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool HasValidPackRoot(string normalizedTerritoryPath)
    {
        var slashIndex = normalizedTerritoryPath.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex <= 0)
        {
            return false;
        }

        var root = normalizedTerritoryPath[..slashIndex];
        if (string.Equals(root, "ffxiv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!root.StartsWith("ex", StringComparison.OrdinalIgnoreCase) || root.Length == 2)
        {
            return false;
        }

        for (var i = 2; i < root.Length; i++)
        {
            if (!char.IsAsciiDigit(root[i]))
            {
                return false;
            }
        }

        return true;
    }
}

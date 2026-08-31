// Path: projects/XIV-Mini-Util/Services/Common/DiagnosticReportBuilder.cs
// Description: key=value 診断レポートの共通ヘルパー
namespace XivMiniUtil.Services.Common;

internal static class DiagnosticReportBuilder
{
    public static void AddPrefixAliasLines(
        List<string> lines,
        int startIndex,
        string oldPrefix,
        string newPrefix)
    {
        var count = lines.Count;
        for (var i = startIndex; i < count; i++)
        {
            var line = lines[i];
            if (line.StartsWith(oldPrefix, StringComparison.Ordinal))
            {
                lines.Add(newPrefix + line[oldPrefix.Length..]);
            }
        }
    }
}

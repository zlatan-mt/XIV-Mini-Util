// Path: projects/XIV-Mini-Util/Services/ShopDataDiagnosticsWriter.cs
// Description: ショップ診断レポートのI/Oとローテーションを担当する
// Reason: ShopDataDiagnosticsからI/O責務を分離しテスト容易性を高めるため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Linq;

namespace XivMiniUtil.Services.Shop;

internal sealed class ShopDataDiagnosticsWriter
{
    private readonly IPluginLog _pluginLog;

    public ShopDataDiagnosticsWriter(IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
    }

    public string WriteDiagnosticsReport(string outputPath, string report)
    {
        RotateDiagnosticsReports(outputPath);

        try
        {
            File.WriteAllText(outputPath, report, System.Text.Encoding.UTF8);
            _pluginLog.Information($"診断レポートを出力しました: {outputPath}");
            return $"診断レポートを出力しました: {outputPath}";
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "診断レポートの出力に失敗しました。");
            return $"診断レポートの出力に失敗しました: {ex.Message}";
        }
    }

    private void RotateDiagnosticsReports(string outputPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var files = Directory.GetFiles(directory, "shop-diagnostics-*.md")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            while (files.Count >= 5)
            {
                var target = files[0];
                files.RemoveAt(0);
                File.Delete(target);
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Warning($"診断レポートのローテーションに失敗しました: {ex.Message}");
        }
    }
}

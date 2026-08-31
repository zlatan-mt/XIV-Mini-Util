// Path: projects/XIV-Mini-Util/Services/ShopDiagnosticsSummary.cs
// Description: ShopDataDiagnosticsの要約ログ出力を担当する
// Reason: 診断の概況を短いログで把握できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataDiagnostics.cs
using Dalamud.Plugin.Services;

namespace XivMiniUtil.Services.Shop;

internal sealed class ShopDiagnosticsSummary
{
    private readonly IPluginLog _pluginLog;

    public ShopDiagnosticsSummary(IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
    }

    public void LogSummary(int itemCount, int excludedNpcCount, int unmatchedShopCount)
    {
        _pluginLog.Information($"[ShopDiag:Summary] Item={itemCount}, ExcludedNpc={excludedNpcCount}, UnmatchedShop={unmatchedShopCount}");
    }
}

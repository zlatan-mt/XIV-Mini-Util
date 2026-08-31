// Path: projects/XIV-Mini-Util/Plugin.UiEvents.cs
// Description: UIイベントとclipboard引き渡しを処理する
// Reason: コマンド処理とフレーム描画時の副作用を分離するため

using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.Shop;

namespace XivMiniUtil;

public sealed partial class Plugin
{
    private void CopyTitleBackgroundDiagnosticLines(IReadOnlyList<string> lines)
    {
        var text = string.Join(Environment.NewLine, lines.Select(line => $"[XIV Mini Util] {line}"));
        ImGui.SetClipboardText(text);
        _chatGui.Print($"[XIV Mini Util] title background diagnostic copied to clipboard. lines={lines.Count}");
        _pluginLog.Information("TitleBackground diag copied to clipboard. lines={LineCount}", lines.Count);
    }

    private void CopyPendingTitleBackgroundAutomaticCheckReport()
    {
        if (!_titleScreenBackgroundService.TryConsumeAutomaticCheckClipboardText(out var text))
        {
            return;
        }

        ImGui.SetClipboardText(text);
        _chatGui.Print("[XIV Mini Util] 自動確認が完了しました。ログをクリップボードへコピーしました。");
        _pluginLog.Information("TitleBackground automatic check copied to clipboard. chars={CharacterCount}", text.Length);
    }

    private void CopyTitleBackgroundCameraProbeLines(IReadOnlyList<string> lines)
    {
        var text = string.Join(Environment.NewLine, lines.Select(line => $"[XIV Mini Util] {line}"));
        ImGui.SetClipboardText(text);
        _chatGui.Print($"[XIV Mini Util] camera probe report copied to clipboard. lines={lines.Count}");
        _pluginLog.Information("TitleBackground camera probe copied to clipboard. lines={LineCount}", lines.Count);
    }

    private void OpenSettingsWindow()
    {
        _mainWindow.OpenSettingsTab();
    }

    private void OpenMainWindow()
    {
        _mainWindow.IsOpen = true;
    }

    private void OnShopSearchCompleted(SearchResult result)
    {
        _shopSearchResultWindow.SetResult(result);

        if (_configuration.ShopSearchWindowEnabled && result.Success && result.Locations.Count > 0)
        {
            _shopSearchResultWindow.IsOpen = true;
        }
    }
}

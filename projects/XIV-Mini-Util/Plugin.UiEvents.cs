// Path: projects/XIV-Mini-Util/Plugin.UiEvents.cs
// Description: UIイベントとclipboard引き渡しを処理する
// Reason: コマンド処理とフレーム描画時の副作用を分離するため

using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.Shop;

namespace XivMiniUtil;

public sealed partial class Plugin
{
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

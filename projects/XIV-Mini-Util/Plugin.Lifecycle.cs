// Path: projects/XIV-Mini-Util/Plugin.Lifecycle.cs
// Description: イベント解除とサービス破棄を既存順序で実行する
// Reason: 依存先を破棄する前に購読を解除する順序を明示するため

namespace XivMiniUtil;

public sealed partial class Plugin
{
    public void Dispose()
    {
        UnregisterCommands();
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw -= CopyPendingTitleBackgroundAutomaticCheckReport;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsWindow;

        _mainWindow.Dispose();
        _shopSearchResultWindow.Dispose();
        _titleScreenBackgroundService.SelfTestCompleted -= OnTitleBackgroundSelfTestCompleted;
        _titleScreenBackgroundService.Dispose();
        _charaSelectService.Dispose();
        _dutyReadyNotificationService.Dispose();
        _materiaService.Dispose();
        _desynthService.Dispose();
        _addonStateTracker.Dispose();
        _contextMenuService.Dispose();
        _universalisMarketService.Dispose();
        _submarineService.Dispose();
        _submarineDataStorage.Dispose();
        _checklistService.Dispose();
        _discordService.Dispose();
        _shopSearchService.OnSearchCompleted -= OnShopSearchCompleted;
        _shopDataCache.Dispose();
    }
}

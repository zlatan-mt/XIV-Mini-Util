// Path: projects/XIV-Mini-Util/Plugin.Lifecycle.cs
// Description: イベント解除とサービス破棄を既存順序で実行する
// Reason: 依存先を破棄する前に購読を解除する順序を明示するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/Materia/MateriaRetrievalService.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs

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
        // TitleScreenBackgroundService.Dispose() releases its own cold-start diagnostic subscription.
        _titleScreenBackgroundService.Dispose();
        _charaSelectService.Dispose();
        _dutyReadyNotificationService.Dispose();
        _materiaRetrievalService.Dispose();
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
// Path: projects/XIV-Mini-Util/Plugin.ServiceConstruction.cs
// Description: プラグインの依存サービスとUIを既存順序で構築する
// Reason: Dalamudのライフサイクルに沿ってサービスとUIを構成するため
// RELEVANT FILES: projects/XIV-Mini-Util/Configuration.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Services/InventoryService.cs
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System.Reflection;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.Common;
using XivMiniUtil.Services.Checklist;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Market;
using XivMiniUtil.Services.Materia;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.Submarine;
using XivMiniUtil.Services.TitleBackground;
using XivMiniUtil.Windows;

namespace XivMiniUtil;

public sealed partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly WindowSystem _windowSystem;

    private readonly Configuration _configuration;
    private readonly AddonStateTracker _addonStateTracker;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly ShopDataCache _shopDataCache;
    private readonly MapService _mapService;
    private readonly ChatService _chatService;
    private readonly ShopSearchService _shopSearchService;
    private readonly ContextMenuService _contextMenuService;
    private readonly UniversalisMarketService _universalisMarketService;
    private readonly TeleportService _teleportService;
    private readonly SubmarineDataStorage _submarineDataStorage;
    private readonly DiscordService _discordService;
    private readonly SubmarineService _submarineService;
    private readonly ChecklistService _checklistService;
    private readonly DutyReadyNotificationService _dutyReadyNotificationService;
    private readonly CharaSelectService _charaSelectService;
    private readonly TitleScreenBackgroundService _titleScreenBackgroundService;
    private readonly MainWindow _mainWindow;
    private readonly ShopSearchResultWindow _shopSearchResultWindow;


    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IGameGui gameGui,
        IAddonLifecycle addonLifecycle,
        IGameInteropProvider gameInteropProvider,
        ISigScanner sigScanner,
        ICondition condition,
        IPluginLog pluginLog,
        IDataManager dataManager,
        IChatGui chatGui,
        IContextMenu contextMenu,
        IAetheryteList aetheryteList)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;
        _dataManager = dataManager;
        _pluginLog = pluginLog;

        _configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(pluginInterface);

        var inventoryService = new InventoryService(clientState, dataManager, pluginLog);
        var inventoryCacheService = new InventoryCacheService(inventoryService, pluginLog);
        var jobService = new JobService(playerState, pluginLog);
        var gameUiService = new GameUiService(gameGui, pluginLog);
        _addonStateTracker = new AddonStateTracker(addonLifecycle, pluginLog);
        _addonStateTracker.Register(GameUiConstants.MaterializeAddonName);
        _addonStateTracker.Register(GameUiConstants.MaterializeDialogAddonName);
        foreach (var addonName in GameUiConstants.DutyReadyConfirmAddonNames)
        {
            _addonStateTracker.Register(addonName);
        }

        _submarineDataStorage = new SubmarineDataStorage(pluginInterface, pluginLog);
        _discordService = new DiscordService(_configuration, pluginLog, chatGui);
        _dutyReadyNotificationService = new DutyReadyNotificationService(
            framework,
            _configuration,
            _addonStateTracker,
            pluginLog);
        _submarineService = new SubmarineService(
            framework,
            clientState,
            objectTable,
            playerState,
            pluginLog,
            _configuration,
            _submarineDataStorage,
            _discordService);
        _checklistService = new ChecklistService(
            framework,
            _configuration,
            chatGui,
            _discordService,
            pluginLog);
        _charaSelectService = new CharaSelectService(
            gameInteropProvider,
            framework,
            clientState,
            objectTable,
            playerState,
            dataManager,
            pluginLog,
            _configuration);
        _titleScreenBackgroundService = new TitleScreenBackgroundService(
            gameInteropProvider,
            sigScanner,
            framework,
            clientState,
            objectTable,
            dataManager,
            pluginLog,
            pluginInterface.ConfigDirectory.FullName,
            _configuration,
            _charaSelectService);
        _titleScreenBackgroundService.SelfTestCompleted += OnTitleBackgroundSelfTestCompleted;

        _materiaService = new MateriaExtractService(
            framework,
            condition,
            pluginLog,
            inventoryService,
            gameUiService,
            _configuration,
            _addonStateTracker);

        _desynthService = new DesynthService(
            framework,
            pluginLog,
            inventoryService,
            jobService,
            gameUiService,
            _configuration);

        _shopDataCache = new ShopDataCache(dataManager, pluginLog, _configuration);
        _mapService = new MapService(gameGui, pluginLog);
        _chatService = new ChatService(chatGui, _mapService);
        _teleportService = new TeleportService(dataManager, aetheryteList, pluginLog);
        _shopSearchService = new ShopSearchService(_shopDataCache, _mapService, _chatService, _teleportService, _configuration, pluginLog);
        _universalisMarketService = new UniversalisMarketService(dataManager, objectTable, chatGui, pluginLog, _configuration);
        _contextMenuService = new ContextMenuService(contextMenu, gameGui, dataManager, _shopSearchService, _shopDataCache, _universalisMarketService, pluginLog);

        var materiaFeatureEnabled = _configuration.MateriaFeatureEnabled;
        var desynthFeatureEnabled = _configuration.DesynthFeatureEnabled;

        if (!materiaFeatureEnabled)
        {
            _materiaService.Disable();
        }

        if (!desynthFeatureEnabled)
        {
            _desynthService.Stop();
        }

        _mainWindow = new MainWindow(
            _configuration,
            _materiaService,
            _desynthService,
            inventoryCacheService,
            _shopDataCache,
            _shopSearchService,
            _checklistService,
            _submarineDataStorage,
            _discordService,
            _dutyReadyNotificationService,
            _charaSelectService,
            _titleScreenBackgroundService,
            materiaFeatureEnabled,
            desynthFeatureEnabled);
        _shopSearchResultWindow = new ShopSearchResultWindow(_mapService, _teleportService, _configuration);

        _windowSystem = new WindowSystem("XIV Mini Util");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_shopSearchResultWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.Draw += CopyPendingTitleBackgroundAutomaticCheckReport;
        _pluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;

        RegisterCommands();
        _shopSearchService.OnSearchCompleted += OnShopSearchCompleted;
        _ = InitializeShopDataAsync();
    }

    private async Task InitializeShopDataAsync()
    {
        try
        {
            await _shopDataCache.InitializeAsync();
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Shop data initialization failed.");
        }
    }
}

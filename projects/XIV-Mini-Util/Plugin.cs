// Path: projects/XIV-Mini-Util/Plugin.cs
// Description: プラグインのエントリーポイントとDI初期化を行う
// Reason: Dalamudのライフサイクルに沿ってサービスとUIを構成するため
// RELEVANT FILES: projects/XIV-Mini-Util/Configuration.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Services/InventoryService.cs
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using XivMiniUtil.Services;
using XivMiniUtil.Windows;

namespace XivMiniUtil;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivminiutil";
    private const string CommandAlias = "/xmu";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly WindowSystem _windowSystem;

    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly ShopDataCache _shopDataCache;
    private readonly MapService _mapService;
    private readonly ChatService _chatService;
    private readonly ShopSearchService _shopSearchService;
    private readonly ContextMenuService _contextMenuService;
    private readonly TeleportService _teleportService;
    private readonly MainWindow _mainWindow;
    private readonly ShopSearchResultWindow _shopSearchResultWindow;

    public string Name => "XIV Mini Util";

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        IGameGui gameGui,
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

        _configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(pluginInterface);

        var inventoryService = new InventoryService(clientState, dataManager, pluginLog);
        var jobService = new JobService(playerState, pluginLog);
        var gameUiService = new GameUiService(gameGui, pluginLog);

        _materiaService = new MateriaExtractService(
            framework,
            condition,
            pluginLog,
            inventoryService,
            gameUiService,
            _configuration);

        _desynthService = new DesynthService(
            framework,
            pluginLog,
            inventoryService,
            jobService,
            gameUiService,
            _configuration);

        _shopDataCache = new ShopDataCache(dataManager, pluginLog);
        _mapService = new MapService(gameGui, pluginLog);
        _chatService = new ChatService(chatGui, _mapService);
        _shopSearchService = new ShopSearchService(_shopDataCache, _mapService, _chatService, _configuration, pluginLog);
        _contextMenuService = new ContextMenuService(contextMenu, gameGui, _shopSearchService, _shopDataCache, pluginLog);
        _teleportService = new TeleportService(dataManager, aetheryteList, pluginLog);

        _mainWindow = new MainWindow(_configuration, _materiaService, _desynthService, _shopDataCache);
        _shopSearchResultWindow = new ShopSearchResultWindow(_mapService, _teleportService);

        _windowSystem = new WindowSystem("XIV Mini Util");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_shopSearchResultWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "メインウィンドウを開きます。サブコマンド: config / help",
        });
        _commandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "メインウィンドウを開きます。サブコマンド: config / help",
        });

        _shopSearchService.OnSearchCompleted += OnShopSearchCompleted;
        _ = _shopDataCache.InitializeAsync();
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _commandManager.RemoveHandler(CommandAlias);
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsWindow;

        _mainWindow.Dispose();
        _shopSearchResultWindow.Dispose();
        _materiaService.Dispose();
        _desynthService.Dispose();
        _contextMenuService.Dispose();
        _shopSearchService.OnSearchCompleted -= OnShopSearchCompleted;
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            _mainWindow.Toggle();
            return;
        }

        switch (trimmed)
        {
            case "config":
                OpenSettingsWindow();
                break;
            case "diag":
                GenerateDiagnosticsReport();
                break;
            case "help":
                PrintHelp();
                break;
            default:
                PrintHelp();
                break;
        }
    }

    private void GenerateDiagnosticsReport()
    {
        var configDir = _pluginInterface.ConfigDirectory.FullName;
        var outputPath = Path.Combine(configDir, $"shop-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        var result = _shopDataCache.GenerateDiagnosticsReport(outputPath);
        _chatGui.Print($"[XIV Mini Util] {result}");
        _chatGui.Print($"[XIV Mini Util] 位置情報なしNPC: {_shopDataCache.GetExcludedNpcCount()}件, NPCマッチなしショップ: {_shopDataCache.GetUnmatchedShopCount()}件");
    }

    private void OpenSettingsWindow()
    {
        _mainWindow.OpenSettingsTab();
    }

    private void OpenMainWindow()
    {
        _mainWindow.IsOpen = true;
    }

    private void PrintHelp()
    {
        _chatGui.Print("/xivminiutil : メインウィンドウを開きます。");
        _chatGui.Print("/xivminiutil config : 設定タブを開きます。");
        _chatGui.Print("/xivminiutil diag : ショップデータ診断レポートを出力します。");
        _chatGui.Print("/xmu : /xivminiutil のエイリアス");
    }

    private void OnShopSearchCompleted(SearchResult result)
    {
        _shopSearchResultWindow.SetResult(result);

        if (_configuration.ShopSearchWindowEnabled && result.Success && result.Locations.Count >= 4)
        {
            _shopSearchResultWindow.IsOpen = true;
        }
    }
}

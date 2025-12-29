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

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly WindowSystem _windowSystem;

    private readonly Configuration _configuration;
    private readonly MateriaExtractService _materiaService;
    private readonly DesynthService _desynthService;
    private readonly MainWindow _mainWindow;
    private readonly ConfigWindow _configWindow;

    public string Name => "XIV Mini Util";

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IGameGui gameGui,
        ICondition condition,
        IPluginLog pluginLog,
        IDataManager dataManager,
        IChatGui chatGui)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;

        _configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(pluginInterface);

        var inventoryService = new InventoryService(clientState, dataManager, pluginLog);
        var jobService = new JobService(objectTable, pluginLog);
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

        _mainWindow = new MainWindow(_configuration, _materiaService, _desynthService);
        _configWindow = new ConfigWindow(_configuration);

        _windowSystem = new WindowSystem("XIV Mini Util");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_configWindow);

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += OpenConfigWindow;

        _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "メインウィンドウを開きます。サブコマンド: config / help",
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigWindow;

        _mainWindow.Dispose();
        _configWindow.Dispose();
        _materiaService.Dispose();
        _desynthService.Dispose();
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
                OpenConfigWindow();
                break;
            case "help":
                PrintHelp();
                break;
            default:
                PrintHelp();
                break;
        }
    }

    private void OpenConfigWindow()
    {
        _configWindow.IsOpen = true;
    }

    private void PrintHelp()
    {
        _chatGui.Print("/xivminiutil : メインウィンドウを開きます。");
        _chatGui.Print("/xivminiutil config : 設定ウィンドウを開きます。");
    }
}

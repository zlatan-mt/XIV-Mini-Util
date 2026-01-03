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
    private const string DebugCommandName = "/xmudebug";
    // 公開版で一時的に無効化する機能の切り替え
    private const bool MateriaFeatureEnabled = false;
    private const bool DesynthFeatureEnabled = false;

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly ICommandManager _commandManager;
    private readonly IChatGui _chatGui;
    private readonly ITargetManager _targetManager;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
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
        IAetheryteList aetheryteList,
        ITargetManager targetManager,
        IObjectTable objectTable)
    {
        _pluginInterface = pluginInterface;
        _commandManager = commandManager;
        _chatGui = chatGui;
        _targetManager = targetManager;
        _dataManager = dataManager;
        _pluginLog = pluginLog;

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

        _shopDataCache = new ShopDataCache(pluginInterface, dataManager, pluginLog);
        _mapService = new MapService(gameGui, pluginLog);
        _chatService = new ChatService(chatGui, _mapService);
        _teleportService = new TeleportService(dataManager, aetheryteList, pluginLog);
        _shopSearchService = new ShopSearchService(_shopDataCache, _mapService, _chatService, _teleportService, _configuration, pluginLog);
        _contextMenuService = new ContextMenuService(contextMenu, gameGui, _shopSearchService, _shopDataCache, pluginLog);

        if (!MateriaFeatureEnabled)
        {
            _materiaService.Disable();
        }

        if (!DesynthFeatureEnabled)
        {
            _desynthService.Stop();
        }

        _mainWindow = new MainWindow(
            _configuration,
            _materiaService,
            _desynthService,
            _shopDataCache,
            _shopSearchService,
            clientState,
            objectTable,
            MateriaFeatureEnabled,
            DesynthFeatureEnabled);
        _shopSearchResultWindow = new ShopSearchResultWindow(_mapService, _teleportService, _configuration);

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
        _commandManager.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand)
        {
            HelpMessage = "デバッグコマンド。サブコマンド: housing",
        });

        _shopSearchService.OnSearchCompleted += OnShopSearchCompleted;
        _ = InitializeShopDataAsync();
    }

    private async Task InitializeShopDataAsync()
    {
        await _shopDataCache.InitializeAsync();
        _shopDataCache.RefreshCustomShops(_configuration.CustomShops);
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _commandManager.RemoveHandler(CommandAlias);
        _commandManager.RemoveHandler(DebugCommandName);
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

        if (_configuration.ShopSearchWindowEnabled && result.Success && result.Locations.Count > 0)
        {
            _shopSearchResultWindow.IsOpen = true;
        }
    }

    private void OnDebugCommand(string command, string args)
    {
        var trimmed = args?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            _chatGui.Print("[XMU Debug] 使用方法: /xmudebug housing");
            return;
        }

        switch (trimmed)
        {
            case "housing":
                InvestigateHousingNpc();
                break;
            default:
                _chatGui.Print($"[XMU Debug] 不明なサブコマンド: {trimmed}");
                break;
        }
    }

    private void InvestigateHousingNpc()
    {
        var target = _targetManager.Target;
        if (target == null)
        {
            _chatGui.Print("[XMU Debug] ターゲットがありません。NPCをターゲットしてください。");
            return;
        }

        var dataId = target.BaseId;
        var name = target.Name.TextValue;
        var objectKind = target.ObjectKind;

        _chatGui.Print($"[XMU Debug] === ターゲット情報 ===");
        _chatGui.Print($"[XMU Debug] Name: {name}");
        _chatGui.Print($"[XMU Debug] DataId (ENpcBaseId): {dataId}");
        _chatGui.Print($"[XMU Debug] ObjectKind: {objectKind}");
        _pluginLog.Information($"[Housing PoC] Target: Name={name}, DataId={dataId}, ObjectKind={objectKind}");

        // ENpcBaseシートからデータを取得
        try
        {
            var npcBaseSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcBase>();
            if (npcBaseSheet == null)
            {
                _chatGui.Print("[XMU Debug] ENpcBaseシートの取得に失敗しました。");
                return;
            }

            var npcBase = npcBaseSheet.GetRowOrDefault(dataId);
            if (npcBase == null)
            {
                _chatGui.Print($"[XMU Debug] ENpcBase (DataId={dataId}) が見つかりませんでした。");
                return;
            }

            _chatGui.Print($"[XMU Debug] === ENpcBase データ ===");

            // ENpcDataフィールドを調査（ShopID候補）
            var dataValues = new List<uint>();
            for (var i = 0; i < 32; i++)
            {
                var val = npcBase.Value.ENpcData[i].RowId;
                if (val != 0)
                {
                    dataValues.Add(val);
                }
            }

            if (dataValues.Count > 0)
            {
                _chatGui.Print($"[XMU Debug] ENpcData (非ゼロ): {string.Join(", ", dataValues)}");
                _pluginLog.Information($"[Housing PoC] ENpcData: {string.Join(", ", dataValues)}");

                // 各シートで検証
                CheckGilShop(dataValues);
                CheckTopicSelect(dataValues);
                CheckCustomTalk(dataValues);
                CheckInclusionShop(dataValues);
            }
            else
            {
                _chatGui.Print("[XMU Debug] ENpcData: すべてゼロ");
            }
        }
        catch (Exception ex)
        {
            _chatGui.Print($"[XMU Debug] エラー: {ex.Message}");
            _pluginLog.Error(ex, "[Housing PoC] Exception during NPC investigation");
        }
    }

    private void CheckGilShop(List<uint> dataValues)
    {
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.GilShop>();
        if (sheet == null) return;

        foreach (var val in dataValues)
        {
            var row = sheet.GetRowOrDefault(val);
            if (row != null)
            {
                var name = row.Value.Name.ExtractText();
                _chatGui.Print($"[XMU Debug] GilShop発見: ID={val}, Name={name}");
                _pluginLog.Information($"[Housing PoC] GilShop: ID={val}, Name={name}");
            }
        }
    }

    private void CheckTopicSelect(List<uint> dataValues)
    {
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TopicSelect>();
        if (sheet == null) return;

        foreach (var val in dataValues)
        {
            var row = sheet.GetRowOrDefault(val);
            if (row != null)
            {
                var name = row.Value.Name.ExtractText();
                _chatGui.Print($"[XMU Debug] TopicSelect発見: ID={val}, Name={name}");
                _pluginLog.Information($"[Housing PoC] TopicSelect: ID={val}, Name={name}");

                // TopicSelectのShop参照を調査
                for (var i = 0; i < 10; i++)
                {
                    var shopRef = row.Value.Shop[i].RowId;
                    if (shopRef != 0)
                    {
                        _chatGui.Print($"[XMU Debug]   Shop[{i}]: {shopRef}");
                        _pluginLog.Information($"[Housing PoC] TopicSelect.Shop[{i}]: {shopRef}");
                    }
                }
            }
        }
    }

    private void CheckCustomTalk(List<uint> dataValues)
    {
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.CustomTalk>();
        if (sheet == null) return;

        foreach (var val in dataValues)
        {
            var row = sheet.GetRowOrDefault(val);
            if (row != null)
            {
                var name = row.Value.Name.ExtractText();
                _chatGui.Print($"[XMU Debug] CustomTalk発見: ID={val}, Name={name}");
                _pluginLog.Information($"[Housing PoC] CustomTalk: ID={val}, Name={name}");
            }
        }
    }

    private void CheckInclusionShop(List<uint> dataValues)
    {
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.InclusionShop>();
        if (sheet == null) return;

        foreach (var val in dataValues)
        {
            var row = sheet.GetRowOrDefault(val);
            if (row != null)
            {
                _chatGui.Print($"[XMU Debug] InclusionShop発見: ID={val}");
                _pluginLog.Information($"[Housing PoC] InclusionShop: ID={val}");

                // InclusionShopのカテゴリを調査
                for (var i = 0; i < 30; i++)
                {
                    var catRef = row.Value.Category[i].RowId;
                    if (catRef != 0)
                    {
                        _chatGui.Print($"[XMU Debug]   Category[{i}]: {catRef}");
                        _pluginLog.Information($"[Housing PoC] InclusionShop.Category[{i}]: {catRef}");
                    }
                }
            }
        }
    }
}

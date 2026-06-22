// Path: projects/XIV-Mini-Util/Plugin.cs
// Description: プラグインのエントリーポイントとDI初期化を行う
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

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xivminiutil";
    private const string CommandAlias = "/xmu";
    private const string VersionCommandName = "/xmuversion";
    private const string VersionCommandAlias = "/xmuv";
    private const string CharaSelectDiagnosticCommandName = "/xmucdiag";
    private const string CharaSelectDiagnosticCommandAlias = "/xmuc";
    private const string TitleBackgroundDiagnosticCommandName = "/xmutbgdiag";
    private const string TitleBackgroundDiagnosticCommandAlias = "/xmutbg";
    private const string TitleBackgroundProbeCommandName = "/xmutbgprobe";
    private const string TitleBackgroundCameraProbeCommandName = "/xmutbgcamprobe";
    private const string TitleBackgroundSelfTestCommandName = "/xmutbgtest";
    private const string TitleBackgroundReloadCommandName = "/xmutbgreload";
    private const string TitleBackgroundQuickCheckCommandName = "/xmutbgcheck";
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

    private readonly record struct CommandRegistration(
        string Name,
        IReadOnlyCommandInfo.HandlerDelegate Handler,
        string HelpMessage);

    public string Name => "XIV Mini Util";

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
        await _shopDataCache.InitializeAsync();
    }

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
    }

    private IReadOnlyList<CommandRegistration> GetCommandRegistrations()
    {
        return
        [
            new(CommandName, OnCommand, "メインウィンドウを開きます。サブコマンド: config / diag / version / help"),
            new(CommandAlias, OnCommand, "メインウィンドウを開きます。サブコマンド: config / diag / version / help"),
            new(VersionCommandName, OnVersionCommand, "読み込み中のXIV Mini Util DLLとビルド時刻を表示します。"),
            new(VersionCommandAlias, OnVersionCommand, "読み込み中のXIV Mini Util DLLとビルド時刻を表示します。"),
            new(CharaSelectDiagnosticCommandName, OnCharaSelectDiagnosticCommand, "キャラ選択画面のエモート/声診断情報を表示します。"),
            new(CharaSelectDiagnosticCommandAlias, OnCharaSelectDiagnosticCommand, "キャラ選択画面のエモート/声診断情報を表示します。"),
            new(TitleBackgroundDiagnosticCommandName, OnTitleBackgroundDiagnosticCommand, "タイトル背景差し替えの診断情報を表示します。サブコマンド: copy"),
            new(TitleBackgroundDiagnosticCommandAlias, OnTitleBackgroundDiagnosticCommand, "タイトル背景差し替えの診断情報を表示します。サブコマンド: copy"),
            new(TitleBackgroundProbeCommandName, OnTitleBackgroundProbeCommand, "タイトル背景hook probeを開始/停止/表示します。サブコマンド: on / report / off"),
            new(TitleBackgroundCameraProbeCommandName, OnTitleBackgroundCameraProbeCommand, "タイトル背景camera Y probeを準備/表示/復元します。サブコマンド: arm-y / report / restore"),
            new(TitleBackgroundSelfTestCommandName, OnTitleBackgroundSelfTestCommand, "debug-only: タイトル背景差し替えのself-testを実行します。通常確認では使用しません。"),
            new(TitleBackgroundReloadCommandName, OnTitleBackgroundReloadCommand, "debug-only: キャラ選択ロビー中にタイトル背景とカメラを再適用します。通常確認では使用しません。"),
            new(TitleBackgroundQuickCheckCommandName, OnTitleBackgroundQuickCheckCommand, "Character Select 背景 QuickCheck を開始/評価/表示/リセットします。サブコマンド: start / status / reset"),
        ];
    }

    private void RegisterCommands()
    {
        foreach (var registration in GetCommandRegistrations())
        {
            _commandManager.AddHandler(registration.Name, new CommandInfo(registration.Handler)
            {
                HelpMessage = registration.HelpMessage,
            });
        }
    }

    private void UnregisterCommands()
    {
        foreach (var registration in GetCommandRegistrations())
        {
            _commandManager.RemoveHandler(registration.Name);
        }
    }

    private void OnCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        if (string.IsNullOrEmpty(subCommand))
        {
            _mainWindow.Toggle();
            return;
        }

        switch (subCommand)
        {
            case "config":
                OpenSettingsWindow();
                break;
            case "diag":
                GenerateDiagnosticsReport();
                break;
            case "version":
            case "ver":
            case "-v":
                PrintVersionInfo();
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

    private void PrintVersionInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();
        var assemblyFile = _pluginInterface.AssemblyLocation;
        var location = assemblyFile.FullName;
        var displayTimeZone = GetDisplayTimeZone();
        var writeTime = File.Exists(location)
            ? TimeZoneInfo.ConvertTimeFromUtc(File.GetLastWriteTimeUtc(location), displayTimeZone)
            : (DateTime?)null;
        var loadTime = TimeZoneInfo.ConvertTimeFromUtc(_pluginInterface.LoadTimeUTC, displayTimeZone);

        _chatGui.Print($"[XIV Mini Util] Assembly: {assemblyName.Name}");
        _chatGui.Print($"[XIV Mini Util] Version: {assemblyName.Version?.ToString(3) ?? "unknown"} / IsDev: {_pluginInterface.IsDev}");
        _chatGui.Print($"[XIV Mini Util] Loaded: {loadTime:yyyy-MM-dd HH:mm:ss} JST");
        _chatGui.Print($"[XIV Mini Util] DLL: {location}");

        if (writeTime.HasValue)
        {
            _chatGui.Print($"[XIV Mini Util] DLL updated: {writeTime.Value:yyyy-MM-dd HH:mm:ss} JST");
        }
    }

    private void OnVersionCommand(string command, string args)
    {
        PrintVersionInfo();
    }

    private void OnCharaSelectDiagnosticCommand(string command, string args)
    {
        foreach (var line in _charaSelectService.GetVoiceDiagnosticLines())
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
        }
    }

    private void OnTitleBackgroundDiagnosticCommand(string command, string args)
    {
        var lines = _titleScreenBackgroundService.GetDiagnosticLines();
        if (ShouldCopyCommandOutput(args))
        {
            CopyTitleBackgroundDiagnosticLines(lines);
            return;
        }

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground diag: {Line}", line);
        }
    }

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

    private void OnTitleBackgroundProbeCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "report" => _titleScreenBackgroundService.GetProbeReportLines(),
            "on" or "start" => _titleScreenBackgroundService.StartProbe(),
            "off" or "stop" => _titleScreenBackgroundService.StopProbe(),
            _ =>
            [
                "[Probe] usage: /xmutbgprobe on | report | off",
            ],
        };

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground probe: {Line}", line);
        }
    }

    private void OnTitleBackgroundCameraProbeCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "report" => _titleScreenBackgroundService.GetCameraProbeReportLines(),
            "arm-y" => _titleScreenBackgroundService.ArmCameraYProbe(),
            "restore" => _titleScreenBackgroundService.RestoreCameraProbe(),
            _ =>
            [
                "[CameraProbe] usage: /xmutbgcamprobe arm-y | report | restore",
            ],
        };

        if (subCommand is "" or "report")
        {
            CopyTitleBackgroundCameraProbeLines(lines);
        }

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground camera probe: {Line}", line);
        }
    }

    private void OnTitleBackgroundSelfTestCommand(string command, string args)
    {
        var startMessage = _titleScreenBackgroundService.StartSelfTest();
        if (!string.IsNullOrWhiteSpace(startMessage))
        {
            _chatGui.Print($"[XIV Mini Util] {startMessage}");
            _pluginLog.Information("TitleBackground self-test: {Line}", startMessage);
        }
    }

    private void OnTitleBackgroundReloadCommand(string command, string args)
    {
        var message = _titleScreenBackgroundService.RequestCharaSelectReload();
        _chatGui.Print($"[XIV Mini Util] {message}");
        _pluginLog.Information("TitleBackground reload: {Line}", message);
    }

    private void OnTitleBackgroundQuickCheckCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "run" => _titleScreenBackgroundService.RunQuickCheck(),
            "start" => _titleScreenBackgroundService.StartQuickCheck(),
            "status" => _titleScreenBackgroundService.GetQuickCheckStatusLines(),
            "reset" => _titleScreenBackgroundService.ResetQuickCheck(),
            _ =>
            [
                "[XMU QuickCheck] usage: /xmutbgcheck [start|status|reset]",
            ],
        };

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground quickcheck: {Line}", line);
        }
    }

    private void OnTitleBackgroundSelfTestCompleted(string message)
    {
        _chatGui.Print($"[XIV Mini Util] {message}");
        _pluginLog.Information("TitleBackground self-test: {Line}", message);
    }

    private static string GetSubCommand(string args)
    {
        var trimmed = args?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var separatorIndex = trimmed.IndexOfAny([' ', '\t', '　']);
        return (separatorIndex < 0 ? trimmed : trimmed[..separatorIndex]).ToLowerInvariant();
    }

    private static bool ShouldCopyCommandOutput(string args)
    {
        return GetSubCommand(args) is "copy" or "clip" or "clipboard";
    }

    private static TimeZoneInfo GetDisplayTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
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
        _chatGui.Print("/xivminiutil version : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuv : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuversion : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuc : キャラ選択画面のエモート/声診断情報を表示します。");
        _chatGui.Print("/xmutbg : タイトル背景差し替えの診断情報を表示します。");
        _chatGui.Print("/xmutbg copy : タイトル背景差し替えの診断情報をクリップボードへコピーします。");
        _chatGui.Print("/xmutbgcheck : Character Select 背景 QuickCheck を表示します。");
        _chatGui.Print("/xmutbgcheck start : QuickCheck のrun-scoped確認を開始します。");
        _chatGui.Print("/xmutbgcamprobe arm-y : CameraY / FocusY one-shot probeを準備します。");
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
}

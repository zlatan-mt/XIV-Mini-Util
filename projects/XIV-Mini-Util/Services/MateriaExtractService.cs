// Path: projects/XIV-Mini-Util/Services/MateriaExtractService.cs
// Description: スピリットボンド100%のアイテムを検出して精製を自動実行する
// Reason: マテリア精製処理をUIから分離し、安全に状態管理するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/InventoryService.cs, projects/XIV-Mini-Util/Services/GameUiService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class MateriaExtractService : IDisposable
{
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly IPluginLog _pluginLog;
    private readonly InventoryService _inventoryService;
    private readonly GameUiService _gameUiService;
    private readonly Configuration _configuration;

    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _cooldownInterval = TimeSpan.FromSeconds(1);

    private DateTime _nextActionAt = DateTime.UtcNow;
    private ExtractState _state = ExtractState.Disabled;
    private InventoryItemInfo? _currentItem;

    public MateriaExtractService(
        IFramework framework,
        ICondition condition,
        IPluginLog pluginLog,
        InventoryService inventoryService,
        GameUiService gameUiService,
        Configuration configuration)
    {
        _framework = framework;
        _condition = condition;
        _pluginLog = pluginLog;
        _inventoryService = inventoryService;
        _gameUiService = gameUiService;
        _configuration = configuration;

        _framework.Update += OnFrameworkUpdate;

        if (_configuration.MateriaExtractEnabled)
        {
            Enable();
        }
    }

    public bool IsEnabled => _state != ExtractState.Disabled;
    public bool IsProcessing => _state is ExtractState.Scanning or ExtractState.Extracting or ExtractState.Waiting;

    public void Enable()
    {
        _state = ExtractState.Scanning;
        _configuration.MateriaExtractEnabled = true;
        _configuration.Save();
    }

    public void Disable()
    {
        _state = ExtractState.Disabled;
        _currentItem = null;
        _configuration.MateriaExtractEnabled = false;
        _configuration.Save();
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_state == ExtractState.Disabled)
        {
            return;
        }

        if (!_inventoryService.IsPlayerLoggedIn)
        {
            return;
        }

        if (DateTime.UtcNow < _nextActionAt)
        {
            return;
        }

        // 戦闘中などの操作不可状態はスキップする
        if (_condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.Occupied])
        {
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        switch (_state)
        {
            case ExtractState.Scanning:
                ScanForItem();
                break;
            case ExtractState.Extracting:
                ExecuteExtract();
                break;
            case ExtractState.Waiting:
                _state = ExtractState.Scanning;
                break;
            case ExtractState.Idle:
                _state = ExtractState.Scanning;
                break;
        }
    }

    private void ScanForItem()
    {
        _currentItem = _inventoryService.GetExtractableItems().FirstOrDefault();
        if (_currentItem == null)
        {
            _state = ExtractState.Idle;
            _nextActionAt = DateTime.UtcNow.Add(_scanInterval);
            return;
        }

        _state = ExtractState.Extracting;
    }

    private void ExecuteExtract()
    {
        if (_currentItem == null)
        {
            _state = ExtractState.Scanning;
            return;
        }

        if (_gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName))
        {
            // UI操作はパッチ依存のため、失敗時もループが止まらないようにする
            if (_gameUiService.TryConfirmMaterializeDialog())
            {
                _pluginLog.Information($"マテリア精製を実行: {_currentItem.Name}");
            }
            else
            {
                _pluginLog.Warning($"マテリア精製の実行に失敗: {_currentItem.Name}");
            }
        }
        else if (_gameUiService.IsAddonVisible(GameUiConstants.MaterializeAddonName))
        {
            if (_gameUiService.TrySelectMaterializeFirstItem())
            {
                _pluginLog.Information($"マテリア精製の選択を実行: {_currentItem.Name}");
            }
            else
            {
                _pluginLog.Warning($"マテリア精製の選択に失敗: {_currentItem.Name}");
            }
        }
        else
        {
            _pluginLog.Warning("マテリア精製ウィンドウが開いていません。");
            _state = ExtractState.Waiting;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        _currentItem = null;
        _state = ExtractState.Waiting;
        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private enum ExtractState
    {
        Disabled,
        Idle,
        Scanning,
        Extracting,
        Waiting,
    }
}

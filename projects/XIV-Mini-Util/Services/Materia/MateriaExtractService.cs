// Path: projects/XIV-Mini-Util/Services/MateriaExtractService.cs
// Description: スピリットボンド100%のアイテムを検出して精製を自動実行する
// Reason: マテリア精製処理をUIから分離し、安全に状態管理するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/InventoryService.cs, projects/XIV-Mini-Util/Services/GameUiService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using XivMiniUtil;
using XivMiniUtil.Models.Common;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.Materia;

public sealed class MateriaExtractService : IDisposable
{
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly IPluginLog _pluginLog;
    private readonly InventoryService _inventoryService;
    private readonly GameUiService _gameUiService;
    private readonly Configuration _configuration;
    private readonly AddonStateTracker _addonStateTracker;

    private readonly TimeSpan _scanInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _cooldownInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _failureBackoffInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _dialogOpenTimeout = TimeSpan.FromSeconds(4);
    private const int MaxConsecutiveUiFailures = 3;

    private DateTime _nextActionAt = DateTime.UtcNow;
    private DateTime _dialogExpectedBy = DateTime.MinValue;
    private ExtractState _state = ExtractState.Disabled;
    private InventoryItemInfo? _currentItem;
    private long _scanId;
    private long _currentScanId;
    private int _consecutiveUiFailures;
    private bool _waitingForMaterializeDialog;

    public MateriaExtractService(
        IFramework framework,
        ICondition condition,
        IPluginLog pluginLog,
        InventoryService inventoryService,
        GameUiService gameUiService,
        Configuration configuration,
        AddonStateTracker addonStateTracker)
    {
        _framework = framework;
        _condition = condition;
        _pluginLog = pluginLog;
        _inventoryService = inventoryService;
        _gameUiService = gameUiService;
        _configuration = configuration;
        _addonStateTracker = addonStateTracker;

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
        _consecutiveUiFailures = 0;
        _waitingForMaterializeDialog = false;
        _configuration.Save();
    }

    public void Disable()
    {
        _state = ExtractState.Disabled;
        _currentItem = null;
        _consecutiveUiFailures = 0;
        _waitingForMaterializeDialog = false;
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

        var now = DateTime.UtcNow;
        if (_waitingForMaterializeDialog)
        {
            if (_gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName))
            {
                _waitingForMaterializeDialog = false;
            }
            else if (now < _dialogExpectedBy)
            {
                _nextActionAt = _dialogExpectedBy;
                return;
            }
            else
            {
                HandleUiFailure("マテリア精製の確認ダイアログが開きません。対象がない可能性があります。");
                return;
            }
        }

        var isMaterializeOpen =
            _gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName)
            || _gameUiService.IsAddonVisible(GameUiConstants.MaterializeAddonName);
        var blockedReason = GetBlockedReason(isMaterializeOpen);

        switch (_state)
        {
            case ExtractState.Scanning:
                ScanForItem(now, blockedReason, isMaterializeOpen);
                break;
            case ExtractState.Extracting:
                if (blockedReason != "None")
                {
                    LogBlocked(now, blockedReason, isMaterializeOpen, GetActiveScanId());
                    _state = ExtractState.Waiting;
                    _nextActionAt = now.Add(_cooldownInterval);
                    break;
                }

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

    private void ScanForItem(DateTime now, string blockedReason, bool isMaterializeOpen)
    {
        _currentScanId = Interlocked.Increment(ref _scanId);
        var snapshot = _inventoryService.GetMateriaScanSnapshot();
        _currentItem = snapshot.EligibleItems.FirstOrDefault();
        LogScan(now, snapshot, blockedReason, isMaterializeOpen, _currentScanId);

        if (_currentItem == null)
        {
            _state = ExtractState.Idle;
            _nextActionAt = now.Add(_scanInterval);
            _consecutiveUiFailures = 0;
            return;
        }

        if (blockedReason != "None")
        {
            _state = ExtractState.Waiting;
            _nextActionAt = now.Add(_cooldownInterval);
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

        if (!_inventoryService.IsSameInventoryItemAvailable(_currentItem, out var currentItem)
            || currentItem.Spiritbond < 10000
            || !currentItem.CanExtractMateria)
        {
            _pluginLog.Information($"マテリア精製対象がなくなったため停止: {_currentItem.Name}");
            _currentItem = null;
            _state = ExtractState.Idle;
            _nextActionAt = DateTime.UtcNow.Add(_scanInterval);
            return;
        }

        if (_gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName))
        {
            // UI操作はパッチ依存のため、失敗時もループが止まらないようにする
            if (_gameUiService.TryConfirmMaterializeDialog())
            {
                _pluginLog.Information($"マテリア精製を実行: {_currentItem.Name}");
                _consecutiveUiFailures = 0;
            }
            else
            {
                HandleUiFailure($"マテリア精製の実行に失敗: {_currentItem.Name}");
                return;
            }
        }
        else if (_gameUiService.IsAddonVisible(GameUiConstants.MaterializeAddonName))
        {
            if (_gameUiService.TrySelectMaterializeFirstItem())
            {
                _pluginLog.Information($"マテリア精製の選択を実行: {_currentItem.Name}");
                _waitingForMaterializeDialog = true;
                _dialogExpectedBy = DateTime.UtcNow.Add(_dialogOpenTimeout);
                _state = ExtractState.Waiting;
                _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
                return;
            }
            else
            {
                HandleUiFailure($"マテリア精製の選択に失敗: {_currentItem.Name}");
                return;
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

    private void HandleUiFailure(string message)
    {
        _consecutiveUiFailures++;
        _waitingForMaterializeDialog = false;
        _pluginLog.Warning($"{message} 連続失敗={_consecutiveUiFailures}/{MaxConsecutiveUiFailures}");

        if (_consecutiveUiFailures >= MaxConsecutiveUiFailures)
        {
            _pluginLog.Warning("マテリア精製UI操作が連続で失敗したため、自動精製を停止しました。");
            Disable();
            return;
        }

        _currentItem = null;
        _state = ExtractState.Waiting;
        _nextActionAt = DateTime.UtcNow.Add(_failureBackoffInterval);
    }

    private void LogScan(DateTime now, MateriaScanSnapshot snapshot, string blockedReason, bool isMaterializeOpen, long scanId)
    {
        var matState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeAddonName, now);
        var dlgState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeDialogAddonName, now);

        _gameUiService.TryGetAddonInfo(GameUiConstants.MaterializeAddonName, out var matPtr, out var matVisible);
        _gameUiService.TryGetAddonInfo(GameUiConstants.MaterializeDialogAddonName, out var dlgPtr, out var dlgVisible);

        var flags = string.Join(",", GetActiveConditionFlags());

        _pluginLog.Debug(
            "[Extract] scanId={0} total={1} sb100={2} slot>0={3} eligible={4} " +
            "addon(Materialize) loaded={5} visible={6} ptr=0x{7:X} guiVisible={8} " +
            "addon(MaterializeDialog) loaded={9} visible={10} ptr=0x{11:X} guiVisible={12} " +
            "blocked={13} uiOpen={14} flags={15}",
            scanId,
            snapshot.TotalItemCount,
            snapshot.SpiritbondReadyCount,
            snapshot.MateriaSlotCount,
            snapshot.EligibleItemCount,
            matState.Loaded,
            matState.Visible,
            matPtr.ToInt64(),
            matVisible,
            dlgState.Loaded,
            dlgState.Visible,
            dlgPtr.ToInt64(),
            dlgVisible,
            blockedReason,
            isMaterializeOpen,
            flags);
    }

    private void LogBlocked(DateTime now, string blockedReason, bool isMaterializeOpen, long scanId)
    {
        var matState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeAddonName, now);
        var dlgState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeDialogAddonName, now);
        var flags = string.Join(",", GetActiveConditionFlags());

        _pluginLog.Debug(
            "[Extract] scanId={0} blocked={1} uiOpen={2} addon(Materialize) loaded={3} visible={4} " +
            "addon(MaterializeDialog) loaded={5} visible={6} flags={7}",
            scanId,
            blockedReason,
            isMaterializeOpen,
            matState.Loaded,
            matState.Visible,
            dlgState.Loaded,
            dlgState.Visible,
            flags);
    }

    private string GetBlockedReason(bool isMaterializeOpen)
    {
        if (_condition[ConditionFlag.BetweenAreas])
        {
            return "BetweenAreas";
        }

        if (_condition[ConditionFlag.Occupied] && !isMaterializeOpen)
        {
            return "Occupied";
        }

        return "None";
    }

    private long GetActiveScanId()
    {
        if (_currentScanId == 0)
        {
            _currentScanId = Interlocked.Increment(ref _scanId);
        }

        return _currentScanId;
    }

    private IEnumerable<string> GetActiveConditionFlags()
    {
        foreach (ConditionFlag flag in Enum.GetValues(typeof(ConditionFlag)))
        {
            if (_condition[flag])
            {
                yield return flag.ToString();
            }
        }
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

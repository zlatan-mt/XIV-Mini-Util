// Path: projects/XIV-Mini-Util/Services/DesynthService.cs
// Description: 分解対象アイテムを順次処理する
// Reason: 分解の安全性とジョブ条件をサービス層で担保するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/InventoryService.cs, projects/XIV-Mini-Util/Services/JobService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using Dalamud.Plugin.Services;
using XivMiniUtil;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.Desynth;

public sealed class DesynthService : IDisposable
{
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly InventoryService _inventoryService;
    private readonly JobService _jobService;
    private readonly GameUiService _gameUiService;
    private readonly Configuration _configuration;

    private readonly TimeSpan _cooldownInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _confirmCooldownInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _dialogOpenTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _resultOpenTimeout = TimeSpan.FromSeconds(10);

    private DateTime _nextActionAt = DateTime.UtcNow;
    private DateTime _dialogExpectedBy = DateTime.MinValue;
    private DateTime _resultExpectedBy = DateTime.MinValue;
    private Queue<InventoryItemInfo> _pendingItems = new();
    private InventoryItemInfo? _currentItem;
    private int _currentItemRemaining;
    private int _activeMinLevel;
    private int _activeMaxLevel;
    private bool _resultObserved;
    private TaskCompletionSource<DesynthResult>? _completionSource;
    private List<string> _errors = new();
    private int _processedCount;
    private int _skippedCount;
    private int _remainingTargetCount;
    private DesynthState _state = DesynthState.Idle;

    public DesynthService(
        IFramework framework,
        IPluginLog pluginLog,
        InventoryService inventoryService,
        JobService jobService,
        GameUiService gameUiService,
        Configuration configuration)
    {
        _framework = framework;
        _pluginLog = pluginLog;
        _inventoryService = inventoryService;
        _jobService = jobService;
        _gameUiService = gameUiService;
        _configuration = configuration;

        _framework.Update += OnFrameworkUpdate;
    }

    public bool IsProcessing { get; private set; }


    public Task<DesynthResult> StartDesynthAsync(DesynthOptions options)
    {
        if (IsProcessing)
        {
            return Task.FromResult(new DesynthResult(0, 0, ["既に分解処理中です。"]));
        }

        if (!_inventoryService.IsPlayerLoggedIn)
        {
            return Task.FromResult(new DesynthResult(0, 0, ["ログイン状態ではありません。"]));
        }

        if (!_jobService.CheckJobCondition(_configuration.DesynthJobCondition))
        {
            return Task.FromResult(new DesynthResult(0, 0, ["ジョブ条件を満たしていません。"]));
        }

        _processedCount = 0;
        _skippedCount = 0;
        _errors = new List<string>();
        _remainingTargetCount = options.TargetMode == DesynthTargetMode.Count
            ? Math.Clamp(options.TargetCount, 1, 999)
            : int.MaxValue;
        _activeMinLevel = options.MinLevel;
        _activeMaxLevel = options.MaxLevel;

        var items = _inventoryService.GetDesynthableItems(options.MinLevel, options.MaxLevel).ToList();
        if (items.Count == 0)
        {
            return Task.FromResult(new DesynthResult(0, 0, ["分解対象がありません。"]));
        }

        _pendingItems = new Queue<InventoryItemInfo>(items);

        _completionSource = new TaskCompletionSource<DesynthResult>();
        IsProcessing = true;
        _state = DesynthState.Selecting;
        _currentItem = null;
        _currentItemRemaining = 0;
        _nextActionAt = DateTime.UtcNow;

        return _completionSource.Task;
    }

    public void Stop()
    {
        if (!IsProcessing)
        {
            return;
        }

        _errors.Add("ユーザー操作で分解を停止しました。");
        FinishProcessing();
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsProcessing)
        {
            return;
        }

        if (DateTime.UtcNow < _nextActionAt)
        {
            return;
        }

        switch (_state)
        {
            case DesynthState.Selecting:
                ProcessSelecting();
                break;
            case DesynthState.WaitingForDialog:
                ProcessWaitingForDialog();
                break;
            case DesynthState.Confirming:
                ProcessConfirming();
                break;
            case DesynthState.WaitingForResult:
                ProcessWaitingForResult();
                break;
            case DesynthState.Idle:
                FinishProcessing();
                break;
        }
    }

    private void ProcessSelecting()
    {
        if (_remainingTargetCount <= 0)
        {
            _state = DesynthState.Idle;
            return;
        }

        if (_gameUiService.IsAddonVisible(GameUiConstants.SalvageDialogAddonName))
        {
            _errors.Add("分解確認ダイアログが想定外に残っているため停止しました。");
            _pluginLog.Warning("分解確認ダイアログが想定外に残っているため、自動分解を停止します。");
            _state = DesynthState.Idle;
            return;
        }

        if (_currentItem != null && _currentItemRemaining > 0)
        {
            if (!TryRefreshCurrentDesynthItem())
            {
                _skippedCount++;
                _errors.Add($"対象がなくなったためスキップ: {_currentItem.Name}");
                _currentItem = null;
                _currentItemRemaining = 0;
                _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
                return;
            }

            SelectItemForDesynth(_currentItem);
            return;
        }

        if (_pendingItems.Count == 0)
        {
            _state = DesynthState.Idle;
            return;
        }

        if (!_gameUiService.IsAddonVisible(GameUiConstants.SalvageItemSelectorAddonName))
        {
            if (_gameUiService.TryOpenSalvageItemSelector())
            {
                _pluginLog.Information("分解アイテム選択ウィンドウを開きました。");
                _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
                return;
            }

            _pluginLog.Warning("分解アイテム選択ウィンドウが開いていません。");
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        var item = _pendingItems.Dequeue();
        if (!TryGetAvailableDesynthItem(item, out var currentItem))
        {
            _skippedCount++;
            _errors.Add($"対象外または消失のためスキップ: {item.Name}");
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        _currentItemRemaining = currentItem.Quantity;
        SelectItemForDesynth(currentItem);
    }

    private void SelectItemForDesynth(InventoryItemInfo item)
    {
        if (!TryGetAvailableDesynthItem(item, out var currentItem))
        {
            _pluginLog.Warning($"分解対象が現在のインベントリにありません: {item.Name}");
            _skippedCount++;
            _errors.Add($"対象消失: {item.Name}");
            _currentItem = null;
            _currentItemRemaining = 0;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        if (_gameUiService.TrySelectSalvageItem(currentItem))
        {
            _pluginLog.Information($"分解アイテムを選択: {currentItem.Name}");
            _currentItem = currentItem;
            _currentItemRemaining = Math.Min(_currentItemRemaining, currentItem.Quantity);
            _state = DesynthState.WaitingForDialog;
            _dialogExpectedBy = DateTime.UtcNow.Add(_dialogOpenTimeout);
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
        }
        else
        {
            _pluginLog.Warning($"分解アイテムの選択に失敗: {item.Name}");
            _skippedCount++;
            _errors.Add($"選択失敗: {item.Name}");
            _currentItem = null;
            _currentItemRemaining = 0;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
        }
    }

    private void ProcessWaitingForDialog()
    {
        if (_gameUiService.IsAddonVisible(GameUiConstants.SalvageDialogAddonName))
        {
            _state = DesynthState.Confirming;
            return;
        }

        if (DateTime.UtcNow >= _dialogExpectedBy)
        {
            _skippedCount++;
            _errors.Add($"確認ダイアログ未表示のためスキップ: {_currentItem?.Name ?? "不明"}");
            _pluginLog.Warning($"分解確認ダイアログが開かないためスキップ: {_currentItem?.Name ?? "不明"}");
            _currentItem = null;
            _currentItemRemaining = 0;
            _state = DesynthState.Selecting;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private void ProcessConfirming()
    {
        if (!_gameUiService.IsAddonVisible(GameUiConstants.SalvageDialogAddonName))
        {
            _errors.Add($"確認前に分解ダイアログが閉じたため停止: {_currentItem?.Name ?? "不明"}");
            _pluginLog.Warning($"確認前に分解ダイアログが閉じたため、自動分解を停止します: {_currentItem?.Name ?? "不明"}");
            _currentItem = null;
            _currentItemRemaining = 0;
            _state = DesynthState.Idle;
            return;
        }

        if (_currentItem == null || !TryRefreshCurrentDesynthItem())
        {
            _skippedCount++;
            _errors.Add("分解対象が確認前に消失したためスキップしました。");
            _pluginLog.Warning("分解対象が確認前に消失したため、確認操作を中止します。");
            _currentItem = null;
            _currentItemRemaining = 0;
            _state = DesynthState.Selecting;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        if (_gameUiService.TryConfirmDesynth())
        {
            _processedCount++;
            _remainingTargetCount--;
            _currentItemRemaining = Math.Max(0, _currentItemRemaining - 1);
            _pluginLog.Information($"分解を実行: {_currentItem?.Name ?? "不明"}");
            _state = DesynthState.WaitingForResult;
            _resultObserved = false;
            _resultExpectedBy = DateTime.UtcNow.Add(_resultOpenTimeout);
            _nextActionAt = DateTime.UtcNow.Add(_confirmCooldownInterval);
            return;
        }
        else
        {
            _skippedCount++;
            _errors.Add($"分解失敗: {_currentItem?.Name ?? "不明"}");
            _pluginLog.Warning($"分解の実行に失敗: {_currentItem?.Name ?? "不明"}");
        }

        if (_remainingTargetCount <= 0)
        {
            _state = DesynthState.Idle;
            return;
        }

        if (_currentItemRemaining <= 0)
        {
            _currentItem = null;
        }

        _state = DesynthState.Selecting;
        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private void ProcessWaitingForResult()
    {
        var now = DateTime.UtcNow;
        if (_gameUiService.IsSalvageResultOpen())
        {
            _resultObserved = true;
            _gameUiService.TryCloseSalvageResult();
            _gameUiService.TryRefreshSalvageItemList();
            _nextActionAt = now.Add(_cooldownInterval);
            return;
        }

        if (now < _nextActionAt)
        {
            return;
        }

        if (!_resultObserved)
        {
            if (now >= _resultExpectedBy)
            {
                _errors.Add($"分解結果を確認できないため停止: {_currentItem?.Name ?? "不明"}");
                _pluginLog.Warning($"分解結果を確認できないため、自動分解を停止します: {_currentItem?.Name ?? "不明"}");
                _state = DesynthState.Idle;
                return;
            }

            _nextActionAt = now.Add(_cooldownInterval);
            return;
        }

        if (_remainingTargetCount <= 0)
        {
            _state = DesynthState.Idle;
            return;
        }

        if (_currentItemRemaining <= 0)
        {
            _currentItem = null;
        }

        _state = DesynthState.Selecting;
        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private void FinishProcessing()
    {
        IsProcessing = false;
        _state = DesynthState.Idle;
        _currentItem = null;
        _currentItemRemaining = 0;
        _resultObserved = false;
        _pendingItems.Clear();

        _completionSource?.TrySetResult(new DesynthResult(_processedCount, _skippedCount, _errors));
        _completionSource = null;
    }

    private bool TryRefreshCurrentDesynthItem()
    {
        if (_currentItem == null)
        {
            return false;
        }

        if (!TryGetAvailableDesynthItem(_currentItem, out var currentItem))
        {
            return false;
        }

        _currentItem = currentItem;
        _currentItemRemaining = Math.Min(_currentItemRemaining, currentItem.Quantity);
        return _currentItemRemaining > 0;
    }

    private bool TryGetAvailableDesynthItem(InventoryItemInfo item, out InventoryItemInfo currentItem)
    {
        if (!_inventoryService.IsSameInventoryItemAvailable(item, out currentItem))
        {
            return false;
        }

        return currentItem.CanDesynth
            && currentItem.ItemLevel >= _activeMinLevel
            && currentItem.ItemLevel <= _activeMaxLevel;
    }

    private enum DesynthState
    {
        Idle,
        Selecting,
        WaitingForDialog,
        Confirming,
        WaitingForResult,
    }
}

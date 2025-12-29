// Path: projects/XIV-Mini-Util/Services/DesynthService.cs
// Description: 分解対象アイテムを順次処理し警告確認を行う
// Reason: 分解の安全性とジョブ条件をサービス層で担保するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/InventoryService.cs, projects/XIV-Mini-Util/Services/JobService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class DesynthService : IDisposable
{
    private readonly IFramework _framework;
    private readonly IPluginLog _pluginLog;
    private readonly InventoryService _inventoryService;
    private readonly JobService _jobService;
    private readonly GameUiService _gameUiService;
    private readonly Configuration _configuration;

    private readonly TimeSpan _cooldownInterval = TimeSpan.FromSeconds(1);

    private DateTime _nextActionAt = DateTime.UtcNow;
    private Queue<InventoryItemInfo> _pendingItems = new();
    private InventoryItemInfo? _currentItem;
    private InventoryItemInfo? _pendingWarningItem;
    private DesynthOptions? _options;
    private TaskCompletionSource<DesynthResult>? _completionSource;
    private List<string> _errors = new();
    private int _processedCount;
    private int _skippedCount;
    private int _maxItemLevel;
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

    public event Action<DesynthWarningInfo>? OnWarningRequired;

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

        _options = options;
        _processedCount = 0;
        _skippedCount = 0;
        _errors = new List<string>();
        _maxItemLevel = _inventoryService.GetMaxItemLevel();

        var items = _inventoryService.GetDesynthableItems(options.MinLevel, options.MaxLevel).ToList();
        _pendingItems = new Queue<InventoryItemInfo>(items);

        _completionSource = new TaskCompletionSource<DesynthResult>();
        IsProcessing = true;
        _state = DesynthState.Selecting;
        _currentItem = null;
        _nextActionAt = DateTime.UtcNow;

        if (_pendingItems.Count == 0)
        {
            FinishProcessing();
        }

        return _completionSource.Task;
    }

    public void ConfirmWarning(bool proceed)
    {
        if (!IsProcessing || _pendingWarningItem == null)
        {
            return;
        }

        var item = _pendingWarningItem;
        _pendingWarningItem = null;

        if (!proceed)
        {
            _skippedCount++;
            _currentItem = null;
            _pluginLog.Information($"警告により分解をスキップ: {item.Name}");
            return;
        }

        SelectItemForDesynth(item);
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
        if (!IsProcessing || _pendingWarningItem != null)
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
            case DesynthState.Idle:
                FinishProcessing();
                break;
        }
    }

    private void ProcessSelecting()
    {
        if (_pendingItems.Count == 0)
        {
            _state = DesynthState.Idle;
            return;
        }

        if (!_gameUiService.IsAddonVisible(GameUiConstants.SalvageItemSelectorAddonName))
        {
            _pluginLog.Warning("分解アイテム選択ウィンドウが開いていません。");
            _state = DesynthState.Idle;
            return;
        }

        var item = _pendingItems.Dequeue();
        if (ShouldWarn(item))
        {
            _pendingWarningItem = item;
            _currentItem = item;
            OnWarningRequired?.Invoke(new DesynthWarningInfo(item.Name, item.ItemLevel, _maxItemLevel));
            return;
        }

        SelectItemForDesynth(item);
    }

    private void SelectItemForDesynth(InventoryItemInfo item)
    {
        if (_gameUiService.TrySelectSalvageItem(item))
        {
            _pluginLog.Information($"分解アイテムを選択: {item.Name}");
            _currentItem = item;
            _state = DesynthState.WaitingForDialog;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
        }
        else
        {
            _pluginLog.Warning($"分解アイテムの選択に失敗: {item.Name}");
            _skippedCount++;
            _errors.Add($"選択失敗: {item.Name}");
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

        // ダイアログが開くまで待機（タイムアウトは設けない）
        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private void ProcessConfirming()
    {
        if (!_gameUiService.IsAddonVisible(GameUiConstants.SalvageDialogAddonName))
        {
            // ダイアログが閉じた = 完了または中断
            _state = DesynthState.Selecting;
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        if (_gameUiService.TryConfirmDesynth())
        {
            _processedCount++;
            _pluginLog.Information($"分解を実行: {_currentItem?.Name ?? "不明"}");
        }
        else
        {
            _skippedCount++;
            _errors.Add($"分解失敗: {_currentItem?.Name ?? "不明"}");
            _pluginLog.Warning($"分解の実行に失敗: {_currentItem?.Name ?? "不明"}");
        }

        _currentItem = null;
        _state = DesynthState.Selecting;
        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private bool ShouldWarn(InventoryItemInfo item)
    {
        if (_options == null)
        {
            return false;
        }

        if (_options.SkipHighLevelWarning || !_configuration.DesynthWarningEnabled)
        {
            return false;
        }

        var threshold = Math.Max(0, _maxItemLevel - _configuration.DesynthWarningThreshold);
        return item.ItemLevel >= threshold;
    }

    private void FinishProcessing()
    {
        IsProcessing = false;
        _state = DesynthState.Idle;
        _currentItem = null;
        _pendingWarningItem = null;
        _pendingItems.Clear();

        _completionSource?.TrySetResult(new DesynthResult(_processedCount, _skippedCount, _errors));
        _completionSource = null;
    }

    private enum DesynthState
    {
        Idle,
        Selecting,
        WaitingForDialog,
        Confirming,
    }
}

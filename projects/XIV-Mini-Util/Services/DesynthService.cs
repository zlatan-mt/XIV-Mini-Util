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
    private InventoryItemInfo? _pendingWarningItem;
    private DesynthOptions? _options;
    private TaskCompletionSource<DesynthResult>? _completionSource;
    private List<string> _errors = new();
    private int _processedCount;
    private int _skippedCount;
    private int _maxItemLevel;

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
            _pluginLog.Information($"警告により分解をスキップ: {item.Name}");
            return;
        }

        ExecuteDesynth(item);
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

        if (_pendingItems.Count == 0)
        {
            FinishProcessing();
            return;
        }

        var item = _pendingItems.Dequeue();
        if (ShouldWarn(item))
        {
            _pendingWarningItem = item;
            OnWarningRequired?.Invoke(new DesynthWarningInfo(item.Name, item.ItemLevel, _maxItemLevel));
            return;
        }

        ExecuteDesynth(item);
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

    private void ExecuteDesynth(InventoryItemInfo item)
    {
        if (!_gameUiService.IsAddonVisible(GameUiConstants.DesynthAddonName))
        {
            _pluginLog.Warning("分解ダイアログが開いていません。");
            _skippedCount++;
            _errors.Add($"分解スキップ: {item.Name}");
            _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
            return;
        }

        // UI操作はパッチ依存のため、失敗時も処理を継続できるようにする
        if (_gameUiService.TryConfirmDesynth())
        {
            _processedCount++;
            _pluginLog.Information($"分解を実行: {item.Name}");
        }
        else
        {
            _skippedCount++;
            _errors.Add($"分解失敗: {item.Name}");
            _pluginLog.Warning($"分解の実行に失敗: {item.Name}");
        }

        _nextActionAt = DateTime.UtcNow.Add(_cooldownInterval);
    }

    private void FinishProcessing()
    {
        IsProcessing = false;
        _pendingWarningItem = null;
        _pendingItems.Clear();

        _completionSource?.TrySetResult(new DesynthResult(_processedCount, _skippedCount, _errors));
        _completionSource = null;
    }
}

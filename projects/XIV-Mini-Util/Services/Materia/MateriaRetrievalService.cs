// Path: projects/XIV-Mini-Util/Services/Materia/MateriaRetrievalService.cs
// Description: 装着マテリアの候補診断、FIFO回収、通常所持品一括追加、進捗表示を管理する
// Reason: 精製サービスとMaterialize系UIの操作権を分離し、対象を毎回再解決して誤操作を防ぐため
// RELEVANT FILES: projects/XIV-Mini-Util/Models/Materia/MateriaRetrievalQueueItem.cs, projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Windows/Components/HomeTab.cs
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Inventory;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using XivMiniUtil.Models.Materia;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.Materia;

public sealed unsafe class MateriaRetrievalService : IDisposable
{
    private static readonly InventoryType[] HandInventoryContainers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private const int MaxAttempts = 3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan BlockedDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan BetweenActionsDelay = TimeSpan.FromMilliseconds(750);

    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IContextMenu _contextMenu;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly InventoryService _inventoryService;
    private readonly GameUiService _gameUiService;
    private readonly MateriaExtractService _materiaExtractService;
    private readonly Configuration _configuration;
    private readonly AddonStateTracker _addonStateTracker;
    private readonly MateriaRetrievalQueue _queue = new();
    private readonly IAddonLifecycle.AddonEventDelegate _addonLifecycleHandler;
    private readonly HashSet<string> _visibleAddonNames = new(StringComparer.Ordinal);

    private MateriaRetrievalRunState _runState = MateriaRetrievalRunState.Idle;
    private MateriaRetrievalBatchPreview? _pendingBatchPreview;
    private IDisposable? _extractPauseLease;
    private DateTime _nextActionAt = DateTime.UtcNow;
    private DateTime _requestDeadline = DateTime.MinValue;
    private bool _requestWaiting;
    private bool _retrievalDialogObserved;
    private bool _retrievalDialogConfirmationSubmitted;
    private HashSet<string> _requestBaselineAddonNames = [];
    private bool _requestNoChangeLogged;
    private bool _unverifiedAddonObserved;
    private string? _unverifiedAddonName;
    private bool _disposed;
    private string? _lastMessage;

    public MateriaRetrievalService(
        IFramework framework,
        ICondition condition,
        IClientState clientState,
        IAddonLifecycle addonLifecycle,
        IContextMenu contextMenu,
        IDataManager dataManager,
        IPluginLog pluginLog,
        InventoryService inventoryService,
        GameUiService gameUiService,
        MateriaExtractService materiaExtractService,
        Configuration configuration,
        AddonStateTracker addonStateTracker)
    {
        _framework = framework;
        _condition = condition;
        _clientState = clientState;
        _addonLifecycle = addonLifecycle;
        _contextMenu = contextMenu;
        _dataManager = dataManager;
        _pluginLog = pluginLog;
        _inventoryService = inventoryService;
        _gameUiService = gameUiService;
        _materiaExtractService = materiaExtractService;
        _configuration = configuration;
        _addonStateTracker = addonStateTracker;
        _addonLifecycleHandler = OnAddonLifecycleEvent;

        _framework.Update += OnFrameworkUpdate;
        _addonLifecycle.RegisterListener(AddonEvent.PreShow, _addonLifecycleHandler);
        _addonLifecycle.RegisterListener(AddonEvent.PreDraw, _addonLifecycleHandler);
        _addonLifecycle.RegisterListener(AddonEvent.PreHide, _addonLifecycleHandler);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, _addonLifecycleHandler);
        _contextMenu.OnMenuOpened += OnMenuOpened;
        _clientState.Logout += OnLogout;
    }

    public MateriaRetrievalRunState RunState => _runState;

    public bool IsProcessing => _runState == MateriaRetrievalRunState.Running;

    public int WaitingCount => _queue.PendingCount;

    public int RemainingCount => _queue.RemainingCount;

    public MateriaRetrievalQueueSnapshot QueueSnapshot => _queue.GetSnapshot();

    public int SuccessCount => _queue.SuccessCount;

    public int FailureCount => _queue.FailureCount;

    public string? CurrentItemName => _queue.CurrentItem?.DisplayName;

    public string? LastMessage => _lastMessage;

    public string StatusText
    {
        get
        {
            if (!_configuration.MateriaFeatureEnabled)
            {
                return "無効中";
            }

            if (!_inventoryService.IsPlayerLoggedIn)
            {
                return "ログインしていません";
            }

            return _runState switch
            {
                MateriaRetrievalRunState.Running => "実行中",
                MateriaRetrievalRunState.Completed => "完了",
                MateriaRetrievalRunState.Failed => "失敗",
                MateriaRetrievalRunState.Aborted => "中止",
                _ => "待機中",
            };
        }
    }

    public void Stop()
    {
        if (!IsProcessing && _extractPauseLease == null)
        {
            return;
        }

        AbortRun("ユーザー操作で回収を中止しました。", "回収を中止しました。");
    }

    public MateriaRetrievalBatchPreview BuildHandInventoryBatchPreview()
    {
        var candidates = new List<MateriaRetrievalCandidate>();
        var rejectedByReason = new Dictionary<MateriaRetrievalCandidateRejectionReason, int>();
        var duplicateCount = 0;

        if (!_configuration.MateriaFeatureEnabled)
        {
            AddBatchRejection(
                rejectedByReason,
                MateriaRetrievalCandidateRejectionReason.FeatureDisabled,
                "マテリア機能が無効です。",
                new MateriaRetrievalCandidateDiagnostics(
                    "InventoryBatch",
                    "InventoryContainer",
                    null,
                    null,
                    null,
                    null,
                    null));
            return SetPendingBatchPreview(new MateriaRetrievalBatchPreview(candidates, rejectedByReason, duplicateCount));
        }

        if (!IsPlayerLoggedIn())
        {
            AddBatchRejection(
                rejectedByReason,
                MateriaRetrievalCandidateRejectionReason.NotLoggedIn,
                "ログイン状態ではありません。",
                new MateriaRetrievalCandidateDiagnostics(
                    "InventoryBatch",
                    "InventoryContainer",
                    null,
                    null,
                    null,
                    null,
                    null));
            return SetPendingBatchPreview(new MateriaRetrievalBatchPreview(candidates, rejectedByReason, duplicateCount));
        }

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            AddBatchRejection(
                rejectedByReason,
                MateriaRetrievalCandidateRejectionReason.InventoryManagerUnavailable,
                "InventoryManagerを取得できません。",
                new MateriaRetrievalCandidateDiagnostics(
                    "InventoryBatch",
                    "InventoryContainer",
                    null,
                    null,
                    null,
                    null,
                    null));
            return SetPendingBatchPreview(new MateriaRetrievalBatchPreview(candidates, rejectedByReason, duplicateCount));
        }

        foreach (var containerType in HandInventoryContainers)
        {
            var container = manager->GetInventoryContainer(containerType);
            if (container == null)
            {
                AddBatchRejection(
                    rejectedByReason,
                    MateriaRetrievalCandidateRejectionReason.ContainerUnavailable,
                    "通常所持品コンテナを取得できません。",
                    new MateriaRetrievalCandidateDiagnostics(
                        "InventoryBatch",
                        "InventoryContainer",
                        containerType,
                        null,
                        null,
                        null,
                        null));
                continue;
            }

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                {
                    continue;
                }

                var resolution = ResolveCandidate(
                    containerType,
                    slot,
                    item->ItemId,
                    "InventoryBatch",
                    "InventoryBatch",
                    "InventoryContainer");
                if (!resolution.IsAccepted)
                {
                    AddBatchRejection(rejectedByReason, resolution);
                    continue;
                }

                var candidate = resolution.Candidate!;
                if (_runState == MateriaRetrievalRunState.Running
                    && _queue.ContainsIdentity(candidate.InventoryType, candidate.Slot, candidate.ItemId))
                {
                    duplicateCount++;
                    LogCandidateRejection(
                        "InventoryBatch",
                        new MateriaRetrievalCandidateResolution(
                            candidate,
                            MateriaRetrievalCandidateRejectionReason.Duplicate,
                            "既存キューに登録済みです。",
                            resolution.Diagnostics));
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return SetPendingBatchPreview(new MateriaRetrievalBatchPreview(candidates, rejectedByReason, duplicateCount));
    }

    public MateriaRetrievalBatchQueueResult QueueHandInventoryBatch()
    {
        var preview = _pendingBatchPreview;
        _pendingBatchPreview = null;

        if (preview == null || preview.CandidateCount == 0)
        {
            const string message = "一括回収の候補がありません。";
            SetLastMessage(message);
            return new MateriaRetrievalBatchQueueResult(false, 0, preview?.DuplicateCount ?? 0, message);
        }

        try
        {
            if (!_configuration.MateriaFeatureEnabled)
            {
                const string message = "マテリア機能が無効のため一括回収を開始できません。";
                SetLastMessage(message);
                return new MateriaRetrievalBatchQueueResult(false, 0, 0, message);
            }

            if (!IsPlayerLoggedIn())
            {
                const string message = "ログイン状態ではないため一括回収を開始できません。";
                SetLastMessage(message);
                return new MateriaRetrievalBatchQueueResult(false, 0, 0, message);
            }

            if (IsProcessing)
            {
                var appended = EnqueueBatchCandidates(preview);
                var duplicateCount = preview.DuplicateCount + appended.DuplicateCount;
                var message = $"一括候補をキュー末尾へ追加しました（追加 {appended.AddedCount}件 / 重複 {duplicateCount}件）。";
                SetLastMessage(message);
                return new MateriaRetrievalBatchQueueResult(false, appended.AddedCount, duplicateCount, message);
            }

            if (!CanStartRetrieval(out var reason))
            {
                SetLastMessage(reason);
                _pluginLog.Warning("装着マテリア一括回収の開始を拒否: {0}", reason);
                return new MateriaRetrievalBatchQueueResult(false, 0, 0, reason);
            }

            if (!_materiaExtractService.TryPauseForRetrieval(out var pauseLease) || pauseLease == null)
            {
                const string message = "マテリア精製が回収の操作権を解放できないため開始できません。";
                SetLastMessage(message);
                _pluginLog.Warning("装着マテリア一括回収の精製一時停止を取得できませんでした。");
                return new MateriaRetrievalBatchQueueResult(false, 0, 0, message);
            }

            _extractPauseLease = pauseLease;
            if (_queue.HasItems)
            {
                _queue.Clear();
            }

            var appendedBatch = EnqueueBatchCandidates(preview);
            if (appendedBatch.AddedCount == 0)
            {
                ReleaseExtractionPause();
                const string message = "一括回収に追加できる候補がありません。";
                SetLastMessage(message);
                return new MateriaRetrievalBatchQueueResult(false, 0, preview.DuplicateCount + appendedBatch.DuplicateCount, message);
            }

            _runState = MateriaRetrievalRunState.Running;
            _nextActionAt = DateTime.UtcNow;
            var startedMessage = $"手持ちの装着マテリア回収を開始しました（追加 {appendedBatch.AddedCount}件）。";
            SetLastMessage(startedMessage);
            _pluginLog.Information("装着マテリア一括回収を開始: {0}件", appendedBatch.AddedCount);
            return new MateriaRetrievalBatchQueueResult(true, appendedBatch.AddedCount, preview.DuplicateCount + appendedBatch.DuplicateCount, startedMessage);
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "装着マテリア一括回収の開始中に例外が発生しました。");
            AbortRun("一括回収開始中の例外のため停止しました。", "一括回収を停止しました。");
            return new MateriaRetrievalBatchQueueResult(false, 0, 0, "一括回収を停止しました。");
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _addonLifecycle.UnregisterListener(AddonEvent.PreShow, _addonLifecycleHandler);
        _addonLifecycle.UnregisterListener(AddonEvent.PreDraw, _addonLifecycleHandler);
        _addonLifecycle.UnregisterListener(AddonEvent.PreHide, _addonLifecycleHandler);
        _addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, _addonLifecycleHandler);
        _contextMenu.OnMenuOpened -= OnMenuOpened;
        _clientState.Logout -= OnLogout;
        _disposed = true;

        try
        {
            if (IsProcessing || _extractPauseLease != null)
            {
                AbortRun("プラグイン終了のため回収を中止しました。", "回収を中止しました。");
            }
            else
            {
                ReleaseExtractionPause();
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "装着マテリア回収の終了処理に失敗しました。");
            ReleaseExtractionPause();
        }
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!_configuration.MateriaFeatureEnabled)
        {
            return;
        }

        var resolution = ResolveMenuCandidate(args);
        if (!resolution.IsAccepted)
        {
            LogCandidateRejection("Menu", resolution);
            return;
        }

        LogCandidateAccepted("Menu", resolution);
        var queueItem = resolution.Candidate!.ToQueueItem();

        var duplicate = _runState == MateriaRetrievalRunState.Running
            && _queue.ContainsIdentity(queueItem.InventoryType, queueItem.Slot, queueItem.ItemId);
        var label = duplicate
            ? "装着マテリアをすべて回収（登録済み）"
            : "装着マテリアをすべて回収";

        var menuItem = new MenuItem
        {
            Name = new SeStringBuilder().AddText(label).Build(),
            PrefixChar = 'R',
            OnClicked = _ => TryQueueItem(queueItem),
            IsEnabled = !duplicate,
        };

        args.AddMenuItem(menuItem);
    }

    private void TryQueueItem(MateriaRetrievalQueueItem queueItem)
    {
        try
        {
            if (!_configuration.MateriaFeatureEnabled)
            {
                SetLastMessage("マテリア機能が無効のため回収を開始できません。");
                return;
            }

            if (!IsPlayerLoggedIn())
            {
                SetLastMessage("ログイン状態ではないため回収を開始できません。");
                return;
            }

            if (IsProcessing)
            {
                if (!_queue.TryEnqueue(queueItem))
                {
                    SetLastMessage("同じ対象は二重登録できません。");
                    _pluginLog.Information("装着マテリア回収の二重登録を拒否: {0}:{1}:{2}", queueItem.InventoryType, queueItem.Slot, queueItem.ItemId);
                    return;
                }

                _pluginLog.Information("装着マテリア回収をキューへ追加: {0} ({1}:{2})", queueItem.DisplayName, queueItem.InventoryType, queueItem.Slot);
                return;
            }

            if (!CanStartRetrieval(out var reason))
            {
                SetLastMessage(reason);
                _pluginLog.Warning("装着マテリア回収の開始を拒否: {0}", reason);
                return;
            }

            if (!_materiaExtractService.TryPauseForRetrieval(out var pauseLease) || pauseLease == null)
            {
                SetLastMessage("マテリア精製が回収の操作権を解放できないため開始できません。");
                _pluginLog.Warning("装着マテリア回収の精製一時停止を取得できませんでした。");
                return;
            }

            _extractPauseLease = pauseLease;
            if (_queue.HasItems)
            {
                _queue.Clear();
            }

            if (!_queue.TryEnqueue(queueItem))
            {
                ReleaseExtractionPause();
                SetLastMessage("同じ対象は二重登録できません。");
                return;
            }

            _runState = MateriaRetrievalRunState.Running;
            _nextActionAt = DateTime.UtcNow;
            _lastMessage = null;
            _pluginLog.Information("装着マテリア回収を開始: {0} ({1}:{2})", queueItem.DisplayName, queueItem.InventoryType, queueItem.Slot);
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "装着マテリア回収の開始中に例外が発生しました。");
            AbortRun("回収開始中の例外のため停止しました。", "回収を停止しました。");
        }
    }

    private void OnLogout(int type, int code)
    {
        if (IsProcessing || _extractPauseLease != null)
        {
            AbortRun("ログアウトのため回収を中止しました。", "ログアウトのため回収を中止しました。");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsProcessing || (_queue.PendingCount == 0 && _queue.CurrentItem == null))
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            if (!_configuration.MateriaFeatureEnabled)
            {
                FailRun("マテリア機能が無効になったため停止しました。", "機能無効のため回収を停止しました。");
                return;
            }

            if (!IsPlayerLoggedIn())
            {
                AbortRun("ログアウトのため回収を中止しました。", "ログアウトのため回収を中止しました。");
                return;
            }

            if (!_materiaExtractService.IsPausedForRetrieval)
            {
                FailRun("マテリア精製の操作権が回収中に失われました。", "精製との相互排他を確認できないため停止しました。");
                return;
            }

            var now = DateTime.UtcNow;
            if (now < _nextActionAt)
            {
                return;
            }

            var materializeVisible = _gameUiService.IsAddonVisible(GameUiConstants.MaterializeAddonName);
            var materializeDialogVisible = _gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName);
            var retrievalDialogVisible = _gameUiService.IsAddonVisible(GameUiConstants.MateriaRetrieveDialogAddonName);

            if (materializeVisible || materializeDialogVisible)
            {
                FailRun("回収中にマテリア精製UIが開いたため停止しました。", "精製UIが開いたため回収を停止しました。");
                return;
            }

            if (retrievalDialogVisible && !_requestWaiting)
            {
                FailRun("回収要求に紐づかない回収確認ダイアログを検出しました。", "未所有の回収確認を操作せず停止しました。");
                return;
            }

            if (_condition[ConditionFlag.BetweenAreas])
            {
                FailRun("エリア移動中のため停止しました。", "エリア移動中のため回収を停止しました。");
                return;
            }

            if (_requestWaiting && _unverifiedAddonObserved)
            {
                var addonName = _unverifiedAddonName ?? "unknown";
                FailRun(
                    $"回収要求後に未検証のアドオン {addonName} を検出しました。確認UIの型・callbackが未検証のため全キューを停止しました。",
                    "未検証の回収UIを検出したため回収を停止しました。");
                return;
            }

            if (_queue.CurrentItem == null && !_queue.TryBeginNext())
            {
                CompleteRun();
                return;
            }

            if (_requestWaiting)
            {
                ProcessWaiting(now, retrievalDialogVisible);
            }
            else
            {
                RequestCurrent(now, materializeVisible, materializeDialogVisible, retrievalDialogVisible);
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "装着マテリア回収のフレーム処理中に例外が発生しました。");
            FailRun("回収処理中の例外のため停止しました。", "回収処理中の例外のため停止しました。");
        }
    }

    private void RequestCurrent(
        DateTime now,
        bool materializeVisible,
        bool materializeDialogVisible,
        bool retrievalDialogVisible)
    {
        var current = _queue.CurrentItem;
        if (current == null)
        {
            _nextActionAt = now;
            return;
        }

        if (_condition[ConditionFlag.Occupied])
        {
            _nextActionAt = now.Add(BlockedDelay);
            _pluginLog.Debug("[Retrieval] Occupied待機中 item={0}", current.DisplayName);
            return;
        }

        if (!MateriaRetrievalSafety.CanIssueRequest(
                false,
                _condition[ConditionFlag.BetweenAreas],
                materializeVisible,
                materializeDialogVisible,
                retrievalDialogVisible))
        {
            _nextActionAt = now.Add(BlockedDelay);
            return;
        }

        if (!TryResolveTarget(
                current,
                out var item,
                out var materiaCount,
                out var itemSheetAvailable,
                out var itemIdentity,
                out var reason))
        {
            FailCurrent(reason);
            return;
        }

        if (!MateriaRetrievalSafety.IsValidTarget(
                current,
                true,
                item->ItemId,
                materiaCount,
                itemSheetAvailable,
                itemIdentity))
        {
            FailCurrent("対象のitem ID、スロット、個体情報、またはマテリア数が変化しました。");
            return;
        }

        if (materiaCount < current.LastMateriaCount)
        {
            FailCurrent("回収要求前に対象のマテリア数が変化したため、安全のため停止しました。");
            return;
        }

        if (materiaCount > current.LastMateriaCount)
        {
            FailCurrent("マテリア数が増加したため停止しました。");
            return;
        }

        var eventFramework = EventFramework.Instance();
        if (eventFramework == null)
        {
            FailCurrent("EventFrameworkを取得できません。");
            return;
        }

        current.RecordAttempt(DateTimeOffset.UtcNow, materiaCount, itemIdentity);
        _requestBaselineAddonNames = new(_visibleAddonNames);
        _requestNoChangeLogged = false;
        _unverifiedAddonObserved = false;
        _unverifiedAddonName = null;
        _retrievalDialogObserved = false;
        _retrievalDialogConfirmationSubmitted = false;
        _requestWaiting = true;
        _requestDeadline = now.Add(RequestTimeout);
        // 回収要求はAPI 15のEventFramework経由で、対象ポインタはこの呼び出し中だけ保持する。
        eventFramework->MaterializeItem(item, MaterializeEntryId.Retrieve);

        _nextActionAt = now.Add(TimeSpan.FromMilliseconds(100));
        _pluginLog.Information(
            "[Retrieval] request item={0} container={1} slot={2} itemId={3} materia={4} attempt={5} entry=Retrieve " +
            "behavior=owned-request observation=count-decrease-after-confirmation confirmation=owned-gated",
            current.DisplayName,
            current.InventoryType,
            current.Slot,
            current.ItemId,
            materiaCount,
            current.Attempts);
    }

    private void ProcessWaiting(DateTime now, bool retrievalDialogVisible)
    {
        var current = _queue.CurrentItem;
        if (current == null)
        {
            ClearRequestState();
            _nextActionAt = now;
            return;
        }

        if (!TryResolveTarget(
                current,
                out _,
                out var materiaCount,
                out var itemSheetAvailable,
                out var itemIdentity,
                out var reason))
        {
            FailCurrent(reason);
            return;
        }

        if (!itemSheetAvailable || current.ItemId == 0)
        {
            FailCurrent("回収対象のシート情報が不正です。");
            return;
        }

        if (retrievalDialogVisible)
        {
            if (!_retrievalDialogObserved)
            {
                if (now >= _requestDeadline)
                {
                    FailRun(
                        "回収確認ダイアログの要求後生成を確認できませんでした。",
                        "回収確認を安全に所有できないため停止しました。");
                }
                else
                {
                    _nextActionAt = now.Add(TimeSpan.FromMilliseconds(100));
                }

                return;
            }

            if (MateriaRetrievalSafety.CanConfirmRetrievalDialog(
                    _requestWaiting,
                    true,
                    _retrievalDialogObserved,
                    _retrievalDialogConfirmationSubmitted))
            {
                if (!_gameUiService.TryConfirmMateriaRetrieveDialog())
                {
                    FailRun("回収確認ダイアログの確定に失敗しました。", "回収確認を安全に確定できないため停止しました。");
                    return;
                }

                _retrievalDialogConfirmationSubmitted = true;
                _nextActionAt = now.Add(TimeSpan.FromMilliseconds(100));
                _pluginLog.Information("[Retrieval] confirmation submitted item={0}", current.DisplayName);
                return;
            }
        }

        var observation = _queue.ObserveCurrentState(itemIdentity, materiaCount);
        if (observation is MateriaRetrievalObservation.Increased or MateriaRetrievalObservation.Invalid)
        {
            FailCurrent("回収中の対象状態を安全に判定できませんでした。");
            return;
        }

        if (observation is MateriaRetrievalObservation.Progressed or MateriaRetrievalObservation.Completed)
        {
            LogObservation(current, observation, materiaCount, "マテリア数の減少を検出");
            ClearRequestState();
            ScheduleNextOrComplete(now);
            return;
        }

        if (observation == MateriaRetrievalObservation.NoChange && !_requestNoChangeLogged)
        {
            _requestNoChangeLogged = true;
            LogObservation(
                current,
                observation,
                materiaCount,
                "要求後もマテリア数変化なし。確認UIの完了または対象状態を待機");
        }

        if (now < _requestDeadline)
        {
            _nextActionAt = now.Add(TimeSpan.FromMilliseconds(100));
            return;
        }

        if (!MateriaRetrievalSafety.CanRetryAfterTimeout(
                _condition[ConditionFlag.Occupied],
                _unverifiedAddonObserved,
                current.Attempts,
                MaxAttempts))
        {
            var timeoutReason = _condition[ConditionFlag.Occupied]
                ? "5秒以内に回収結果を確認できず、占有状態のため停止しました。"
                : "5秒以内に回収結果を確認できず、試行上限に達しました。";
            if (_unverifiedAddonObserved)
            {
                FailRun(
                    $"要求後もマテリア数の変化を確認できず、未検証アドオン {_unverifiedAddonName ?? "unknown"} の確認UIを操作せず全キューを停止しました。",
                    "未検証の回収確認UIを操作せず回収を停止しました。");
            }
            else
            {
                FailCurrent(timeoutReason);
            }
            return;
        }

        _requestWaiting = false;
        _requestNoChangeLogged = false;
        _unverifiedAddonObserved = false;
        _unverifiedAddonName = null;
        _retrievalDialogObserved = false;
        _retrievalDialogConfirmationSubmitted = false;
        _requestBaselineAddonNames.Clear();
        _nextActionAt = now.Add(RetryDelay);
        _pluginLog.Warning(
            "[Retrieval] timeout retry item={0} attempts={1}/{2} lastMateria={3} " +
            "behavior=owned-request observation=count-decrease-after-confirmation confirmation=owned-gated",
            current.DisplayName,
            current.Attempts,
            MaxAttempts,
            current.LastMateriaCount);
    }

    private bool CanStartRetrieval(out string reason)
    {
        var materializeVisible = _gameUiService.IsAddonVisible(GameUiConstants.MaterializeAddonName);
        var materializeDialogVisible = _gameUiService.IsAddonVisible(GameUiConstants.MaterializeDialogAddonName);
        var retrievalDialogVisible = _gameUiService.IsAddonVisible(GameUiConstants.MateriaRetrieveDialogAddonName);
        var loggedIn = IsPlayerLoggedIn();
        var extractionWaiting = _materiaExtractService.HasPendingMaterializeDialog;

        if (!loggedIn)
        {
            reason = "ログイン状態ではないため回収を開始できません。";
            return false;
        }

        if (materializeVisible || materializeDialogVisible)
        {
            reason = "マテリア精製UIが開いているため回収を開始できません。";
            return false;
        }

        if (retrievalDialogVisible)
        {
            reason = "既存のマテリア回収確認ダイアログが開いているため回収を開始できません。";
            return false;
        }

        if (extractionWaiting)
        {
            reason = "マテリア精製が確認待機中のため回収を開始できません。";
            return false;
        }

        if (!MateriaRetrievalSafety.CanStart(
                _configuration.MateriaFeatureEnabled,
                loggedIn,
                materializeVisible,
                materializeDialogVisible,
                retrievalDialogVisible,
                extractionWaiting,
                _condition[ConditionFlag.Occupied],
                _condition[ConditionFlag.BetweenAreas]))
        {
            reason = "ゲームUIまたは占有状態が安全な開始条件を満たしていません。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void FailCurrent(string reason)
    {
        var current = _queue.CurrentItem;
        if (current == null)
        {
            FailRun(reason, "回収対象を安全に判定できないため停止しました。");
            return;
        }

        _pluginLog.Warning("[Retrieval] item failed name={0} container={1} slot={2} reason={3}", current.DisplayName, current.InventoryType, current.Slot, reason);
        _queue.MarkCurrentFailed(reason);
        ClearRequestState();
        SetLastMessage($"回収失敗: {current.DisplayName} / {reason}");
        ScheduleNextOrComplete(DateTime.UtcNow);
    }

    private void ScheduleNextOrComplete(DateTime now)
    {
        if (_queue.PendingCount == 0 && _queue.CurrentItem == null)
        {
            CompleteRun();
            return;
        }

        _nextActionAt = now.Add(BetweenActionsDelay);
    }

    private void CompleteRun()
    {
        ClearRequestState();
        _runState = _queue.FailureCount == 0
            ? MateriaRetrievalRunState.Completed
            : MateriaRetrievalRunState.Failed;
        SetLastMessage(_runState == MateriaRetrievalRunState.Completed
            ? "装着マテリア回収が完了しました。"
            : "装着マテリア回収が完了しました（一部失敗）。");
        _pluginLog.Information(
            "[Retrieval] run completed state={0} success={1} failure={2}",
            _runState,
            _queue.SuccessCount,
            _queue.FailureCount);
        ReleaseExtractionPause();
    }

    private void FailRun(string reason, string userMessage)
    {
        if (!IsProcessing && _extractPauseLease == null)
        {
            return;
        }

        _queue.MarkAllFailed(reason);
        ClearRequestState();
        _runState = MateriaRetrievalRunState.Failed;
        SetLastMessage(userMessage);
        _pluginLog.Warning("[Retrieval] run failed: {0}", reason);
        ReleaseExtractionPause();
    }

    private void AbortRun(string reason, string userMessage)
    {
        _queue.MarkAllAborted(reason);
        ClearRequestState();
        _runState = MateriaRetrievalRunState.Aborted;
        SetLastMessage(userMessage);
        _pluginLog.Information("[Retrieval] run aborted: {0}", reason);
        ReleaseExtractionPause();
    }

    private void ReleaseExtractionPause()
    {
        var pauseLease = _extractPauseLease;
        _extractPauseLease = null;
        if (pauseLease == null)
        {
            return;
        }

        try
        {
            pauseLease.Dispose();
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "マテリア精製の操作権返却に失敗しました。");
        }
    }

    private void ClearRequestState()
    {
        _requestWaiting = false;
        _requestDeadline = DateTime.MinValue;
        _requestBaselineAddonNames.Clear();
        _requestNoChangeLogged = false;
        _unverifiedAddonObserved = false;
        _unverifiedAddonName = null;
        _retrievalDialogObserved = false;
        _retrievalDialogConfirmationSubmitted = false;
    }

    private void OnAddonLifecycleEvent(AddonEvent addonEvent, AddonArgs args)
    {
        var addonName = args.AddonName;
        if (string.IsNullOrWhiteSpace(addonName))
        {
            return;
        }

        if ((addonEvent is AddonEvent.PreShow or AddonEvent.PreDraw)
            && _requestWaiting
            && string.Equals(addonName, GameUiConstants.MateriaRetrieveDialogAddonName, StringComparison.Ordinal))
        {
            if (!_retrievalDialogObserved)
            {
                _pluginLog.Information(
                    "[Retrieval] owned confirmation dialog observed item={0}",
                    _queue.CurrentItem?.DisplayName ?? "unknown");
            }

            _retrievalDialogObserved = true;
        }

        if ((addonEvent is AddonEvent.PreShow or AddonEvent.PreDraw)
            && _requestWaiting
            && !_requestBaselineAddonNames.Contains(addonName)
            && !string.Equals(addonName, GameUiConstants.MaterializeAddonName, StringComparison.Ordinal)
            && !string.Equals(addonName, GameUiConstants.MaterializeDialogAddonName, StringComparison.Ordinal)
            && !string.Equals(addonName, GameUiConstants.MateriaRetrieveDialogAddonName, StringComparison.Ordinal))
        {
            if (!_unverifiedAddonObserved)
            {
                _pluginLog.Warning(
                    "[Retrieval] post-request addon observed addon={0} behavior=unknown observation=addon-lifecycle confirmation=disabled-unverified",
                    addonName);
            }

            _unverifiedAddonObserved = true;
            _unverifiedAddonName ??= addonName;
        }

        if (addonEvent is AddonEvent.PreShow or AddonEvent.PreDraw)
        {
            _visibleAddonNames.Add(addonName);
        }
        else if (addonEvent is AddonEvent.PreHide or AddonEvent.PreFinalize)
        {
            _visibleAddonNames.Remove(addonName);
        }
    }

    private bool IsPlayerLoggedIn()
    {
        return _clientState.IsLoggedIn && _inventoryService.IsPlayerLoggedIn;
    }

    private void SetLastMessage(string message)
    {
        _lastMessage = message;
    }

    private void LogObservation(
        MateriaRetrievalQueueItem item,
        MateriaRetrievalObservation observation,
        int materiaCount,
        string reason)
    {
        var now = DateTime.UtcNow;
        var materializeState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeAddonName, now);
        var materializeDialogState = _addonStateTracker.GetSnapshot(GameUiConstants.MaterializeDialogAddonName, now);
        var retrievalDialogState = _addonStateTracker.GetSnapshot(GameUiConstants.MateriaRetrieveDialogAddonName, now);

        _pluginLog.Debug(
            "[Retrieval] observe item={0} observation={1} reason={2} materia={3} " +
            "behavior=owned-request observation=count-decrease-after-confirmation confirmation=owned-gated " +
            "addon(Materialize)={4}/{5} addon(MaterializeDialog)={6}/{7} addon(MateriaRetrieveDialog)={8}/{9} occupied={10} betweenAreas={11}",
            item.DisplayName,
            observation,
            reason,
            materiaCount,
            materializeState.Loaded,
            materializeState.Visible,
            materializeDialogState.Loaded,
            materializeDialogState.Visible,
            retrievalDialogState.Loaded,
            retrievalDialogState.Visible,
            _condition[ConditionFlag.Occupied],
            _condition[ConditionFlag.BetweenAreas]);
    }

    private MateriaRetrievalCandidateResolution ResolveMenuCandidate(IMenuOpenedArgs args)
    {
        var addonName = args.AddonName;
        var targetType = args.Target?.GetType().Name;

        if (args.Target is MenuTargetInventory inventoryTarget
            && inventoryTarget.TargetItem is { } inventoryItem)
        {
            return ResolveCandidate(
                (InventoryType)inventoryItem.ContainerType,
                (int)inventoryItem.InventorySlot,
                inventoryItem.ItemId,
                "MenuTargetInventory",
                addonName,
                targetType);
        }

        if (args.Target is MenuTargetInventory)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.TargetUnavailable,
                "インベントリ対象の実体位置を取得できません。",
                new MateriaRetrievalCandidateDiagnostics(
                    addonName,
                    targetType,
                    null,
                    null,
                    null,
                    null,
                    null));
        }

        if (string.Equals(addonName, "MateriaAttach", StringComparison.Ordinal))
        {
            if (!TryGetMateriaAttachSelectedItem(
                    out var containerType,
                    out var slot,
                    out var itemId,
                    out var attachReason))
            {
                return RejectCandidate(
                    MateriaRetrievalCandidateRejectionReason.MateriaAttachUnavailable,
                    attachReason,
                    new MateriaRetrievalCandidateDiagnostics(
                        addonName,
                        targetType,
                        containerType,
                        slot,
                        itemId,
                        null,
                        null));
            }

            return ResolveCandidate(
                containerType!.Value,
                slot,
                itemId,
                "MateriaAttach",
                addonName,
                targetType);
        }

        return RejectCandidate(
            MateriaRetrievalCandidateRejectionReason.UnsupportedTarget,
            "実インベントリ位置を取得できない対象です。",
            new MateriaRetrievalCandidateDiagnostics(
                addonName,
                targetType,
                null,
                null,
                null,
                null,
                null));
    }

    private static bool TryGetMateriaAttachSelectedItem(
        out InventoryType? containerType,
        out int slot,
        out uint itemId,
        out string reason)
    {
        containerType = null;
        slot = -1;
        itemId = 0;
        reason = string.Empty;

        var agent = AgentMateriaAttach.Instance();
        if (agent == null || agent->Data == null || agent->Data->ItemArraySorted == null)
        {
            reason = "MateriaAttach Agentまたはデータが未準備です。";
            return false;
        }

        var selectedIndex = agent->SelectedItemIndex;
        if (selectedIndex < 0 || selectedIndex >= agent->ItemCount)
        {
            reason = "MateriaAttachで装備が選択されていません。";
            return false;
        }

        var entry = agent->Data->ItemArraySorted[selectedIndex];
        if (entry == null || entry->Item == null)
        {
            reason = "MateriaAttachの選択装備を取得できません。";
            return false;
        }

        var item = entry->Item;
        containerType = item->Container;
        slot = item->Slot;
        itemId = item->ItemId;
        if (itemId == 0)
        {
            reason = "MateriaAttachの選択装備のitem IDを取得できません。";
            return false;
        }

        return true;
    }

    private MateriaRetrievalCandidateResolution ResolveCandidate(
        InventoryType containerType,
        int slot,
        uint expectedItemId,
        string source,
        string? addonName,
        string? targetType)
    {
        var diagnostics = new MateriaRetrievalCandidateDiagnostics(
            addonName,
            targetType,
            containerType,
            slot,
            expectedItemId == 0 ? null : expectedItemId,
            null,
            null)
        {
            ExpectedItemId = expectedItemId == 0 ? null : expectedItemId,
        };

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.InventoryManagerUnavailable,
                "InventoryManagerを取得できません。",
                diagnostics,
                source);
        }

        if (slot < 0)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.SlotUnavailable,
                "対象slotが不正です。",
                diagnostics,
                source);
        }

        var container = manager->GetInventoryContainer(containerType);
        if (container == null)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.ContainerUnavailable,
                "対象コンテナが存在しません。",
                diagnostics,
                source);
        }

        if (slot >= container->Size)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.SlotUnavailable,
                "対象slotがコンテナ範囲外です。",
                diagnostics,
                source);
        }

        var item = container->GetInventorySlot(slot);
        if (item == null)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.TargetUnavailable,
                "対象アイテムが存在しません。",
                diagnostics,
                source);
        }

        var currentItemId = item->ItemId;
        var currentMateriaCount = item->GetMateriaCount();
        diagnostics = diagnostics with
        {
            ItemId = currentItemId == 0 ? null : currentItemId,
            MateriaCount = currentMateriaCount,
        };

        if (currentItemId == 0)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.ItemIdZero,
                "対象item IDが0です。",
                diagnostics,
                source);
        }

        if (expectedItemId != 0 && currentItemId != expectedItemId)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.ItemIdMismatch,
                "対象item IDが一致しません。",
                diagnostics,
                source);
        }

        if (item->Container != containerType || item->Slot != slot)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.TargetPositionMismatch,
                "対象のコンテナまたはslotが一致しません。",
                diagnostics,
                source);
        }

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.ItemSheetUnavailable,
                "Itemシートを取得できません。",
                diagnostics,
                source);
        }

        var row = itemSheet.GetRowOrDefault(currentItemId);
        if (!row.HasValue || row.Value.RowId == 0)
        {
            return RejectCandidate(
                MateriaRetrievalCandidateRejectionReason.ItemSheetRowUnavailable,
                "対象Itemの行を取得できません。",
                diagnostics,
                source);
        }

        diagnostics = diagnostics with { MateriaSlotCount = row.Value.MateriaSlotCount };
        var isEquippable = row.Value.EquipSlotCategory.IsValid
            && row.Value.EquipSlotCategory.RowId > 0;
        if (!MateriaRetrievalSafety.CanUseCandidate(
                currentMateriaCount,
                row.Value.MateriaSlotCount,
                true,
                isEquippable))
        {
            var rejectionReason = isEquippable
                ? MateriaRetrievalCandidateRejectionReason.MateriaCountZero
                : MateriaRetrievalCandidateRejectionReason.NotEquippable;
            var message = isEquippable
                ? "装着マテリア数が0です。"
                : "装備可能なItemではないため対象外です。";
            return RejectCandidate(
                rejectionReason,
                message,
                diagnostics,
                source);
        }

        var itemIdentity = CaptureItemIdentity(item);
        var candidate = new MateriaRetrievalCandidate(
            containerType,
            slot,
            currentItemId,
            currentMateriaCount,
            row.Value.MateriaSlotCount,
            row.Value.Name.ToString())
        {
            ItemIdentity = itemIdentity,
        };
        return new MateriaRetrievalCandidateResolution(
            candidate,
            MateriaRetrievalCandidateRejectionReason.None,
            string.Empty,
            diagnostics);
    }

    private MateriaRetrievalBatchPreview SetPendingBatchPreview(MateriaRetrievalBatchPreview preview)
    {
        _pendingBatchPreview = preview;
        return preview;
    }

    private (int AddedCount, int DuplicateCount) EnqueueBatchCandidates(MateriaRetrievalBatchPreview preview)
    {
        var addedCount = 0;
        var duplicateCount = 0;
        foreach (var candidate in preview.Candidates)
        {
            var queueItem = candidate.ToQueueItem();
            if (_queue.TryEnqueue(queueItem))
            {
                addedCount++;
                continue;
            }

            duplicateCount++;
            LogCandidateRejection(
                "InventoryBatch",
                new MateriaRetrievalCandidateResolution(
                    candidate,
                    MateriaRetrievalCandidateRejectionReason.Duplicate,
                    "既存キューに登録済みです。",
                    new MateriaRetrievalCandidateDiagnostics(
                        "InventoryBatch",
                        "InventoryContainer",
                        candidate.InventoryType,
                        candidate.Slot,
                        candidate.ItemId,
                        candidate.StartingMateriaCount,
                        candidate.MateriaSlotCount)));
        }

        return (addedCount, duplicateCount);
    }

    private MateriaRetrievalCandidateResolution RejectCandidate(
        MateriaRetrievalCandidateRejectionReason rejectionReason,
        string userMessage,
        MateriaRetrievalCandidateDiagnostics diagnostics)
    {
        return new MateriaRetrievalCandidateResolution(null, rejectionReason, userMessage, diagnostics);
    }

    private MateriaRetrievalCandidateResolution RejectCandidate(
        MateriaRetrievalCandidateRejectionReason rejectionReason,
        string userMessage,
        MateriaRetrievalCandidateDiagnostics diagnostics,
        string source)
    {
        var resolution = RejectCandidate(rejectionReason, userMessage, diagnostics);
        LogCandidateRejection(source, resolution);
        return resolution;
    }

    private void AddBatchRejection(
        IDictionary<MateriaRetrievalCandidateRejectionReason, int> rejectedByReason,
        MateriaRetrievalCandidateRejectionReason rejectionReason,
        string userMessage,
        MateriaRetrievalCandidateDiagnostics diagnostics)
    {
        rejectedByReason[rejectionReason] = rejectedByReason.TryGetValue(rejectionReason, out var count)
            ? count + 1
            : 1;
        LogCandidateRejection(
            "InventoryBatch",
            RejectCandidate(rejectionReason, userMessage, diagnostics));
    }

    private void AddBatchRejection(
        IDictionary<MateriaRetrievalCandidateRejectionReason, int> rejectedByReason,
        MateriaRetrievalCandidateResolution resolution)
    {
        rejectedByReason[resolution.RejectionReason] = rejectedByReason.TryGetValue(resolution.RejectionReason, out var count)
            ? count + 1
            : 1;
        LogCandidateRejection("InventoryBatch", resolution);
    }

    private void LogCandidateRejection(string source, MateriaRetrievalCandidateResolution resolution)
    {
        var diagnostics = resolution.Diagnostics;
        _pluginLog.Debug(
            "[Retrieval] candidate rejected source={0} addon={1} target={2} container={3} slot={4} itemId={5} expectedItemId={6} materia={7} materiaSlotCount={8} reason={9}",
            source,
            diagnostics.AddonName ?? "none",
            diagnostics.TargetType ?? "none",
            diagnostics.Container?.ToString() ?? "none",
            diagnostics.Slot?.ToString() ?? "none",
            diagnostics.ItemId?.ToString() ?? "none",
            diagnostics.ExpectedItemId?.ToString() ?? "none",
            diagnostics.MateriaCount?.ToString() ?? "none",
            diagnostics.MateriaSlotCount?.ToString() ?? "none",
            resolution.RejectionReason);
    }

    private void LogCandidateAccepted(string source, MateriaRetrievalCandidateResolution resolution)
    {
        var diagnostics = resolution.Diagnostics;
        _pluginLog.Debug(
            "[Retrieval] candidate accepted source={0} addon={1} target={2} container={3} slot={4} itemId={5} expectedItemId={6} materia={7} materiaSlotCount={8}",
            source,
            diagnostics.AddonName ?? "none",
            diagnostics.TargetType ?? "none",
            diagnostics.Container?.ToString() ?? "none",
            diagnostics.Slot?.ToString() ?? "none",
            diagnostics.ItemId?.ToString() ?? "none",
            diagnostics.ExpectedItemId?.ToString() ?? "none",
            diagnostics.MateriaCount?.ToString() ?? "none",
            diagnostics.MateriaSlotCount?.ToString() ?? "none");
    }

    private bool TryResolveTarget(
        MateriaRetrievalQueueItem expected,
        out InventoryItem* item,
        out int materiaCount,
        out bool itemSheetAvailable,
        out MateriaRetrievalItemIdentity? itemIdentity,
        out string reason)
    {
        item = null;
        materiaCount = 0;
        itemSheetAvailable = false;
        itemIdentity = null;

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            reason = "InventoryManagerを取得できません。";
            return false;
        }

        var container = manager->GetInventoryContainer(expected.InventoryType);
        if (container == null || expected.Slot < 0 || expected.Slot >= container->Size)
        {
            reason = "対象コンテナまたはスロットが存在しません。";
            return false;
        }

        item = container->GetInventorySlot(expected.Slot);
        if (item == null || item->ItemId == 0)
        {
            reason = "回収対象が消失しました。";
            return false;
        }

        if (item->Container != expected.InventoryType || item->Slot != expected.Slot)
        {
            reason = "回収対象のコンテナまたはスロットが置換されました。";
            return false;
        }

        if (item->ItemId != expected.ItemId)
        {
            reason = "回収対象のitem IDが一致しません。";
            return false;
        }

        itemIdentity = CaptureItemIdentity(item);

        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            reason = "Itemシートを取得できません。";
            return false;
        }

        var row = itemSheet.GetRowOrDefault(item->ItemId);
        if (!row.HasValue || row.Value.RowId == 0)
        {
            reason = "回収対象のItemシート行を取得できません。";
            return false;
        }

        // Itemシート行が取得できれば判定可能。MateriaSlotCount=0だけで禁断装備を除外しない。
        itemSheetAvailable = true;
        materiaCount = item->GetMateriaCount();
        reason = materiaCount == 0
            ? "回収対象のマテリア数が0です。"
            : string.Empty;
        return true;
    }

    private static MateriaRetrievalItemIdentity CaptureItemIdentity(InventoryItem* item)
    {
        var materiaIds = new ushort[5];
        var materiaGrades = new byte[5];
        var stains = new byte[2];
        for (byte index = 0; index < materiaIds.Length; index++)
        {
            materiaIds[index] = item->GetMateriaId(index);
            materiaGrades[index] = item->GetMateriaGrade(index);
        }

        for (var index = 0; index < stains.Length; index++)
        {
            stains[index] = item->GetStain(index);
        }

        return new MateriaRetrievalItemIdentity(
            item->IsSymbolic,
            item->LinkedItemSlot,
            item->LinkedInventoryType,
            item->Quantity,
            item->SpiritbondOrCollectability,
            item->Condition,
            (byte)item->Flags,
            item->CrafterContentId,
            item->GlamourId,
            item->EventId.Id,
            materiaIds,
            materiaGrades,
            stains);
    }
}

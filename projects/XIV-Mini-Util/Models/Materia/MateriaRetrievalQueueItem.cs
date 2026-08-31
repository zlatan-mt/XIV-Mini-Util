// Path: projects/XIV-Mini-Util/Models/Materia/MateriaRetrievalQueueItem.cs
// Description: 装着マテリア回収の候補診断、プレビュー、キュー集計、純粋な状態判定を定義する
// Reason: ゲーム内ポインタを保持せず、候補範囲・FIFO・進捗・安全境界をテスト可能にするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/Materia/MateriaRetrievalService.cs, projects/XIV-Mini-Util/Windows/Components/HomeTab.cs, tools/CharaSelectLogicTests/Tests/MateriaRetrievalTests.cs
using FFXIVClientStructs.FFXIV.Client.Game;

namespace XivMiniUtil.Models.Materia;

public enum MateriaRetrievalItemState
{
    Queued,
    Processing,
    Succeeded,
    Failed,
    Aborted,
}

public enum MateriaRetrievalObservation
{
    NoChange,
    Progressed,
    Completed,
    Increased,
    Invalid,
}

public enum MateriaRetrievalRunState
{
    Idle,
    Running,
    Completed,
    Failed,
    Aborted,
}

public enum MateriaRetrievalCandidateRejectionReason
{
    None,
    FeatureDisabled,
    NotLoggedIn,
    UnsupportedTarget,
    MateriaAttachUnavailable,
    InventoryManagerUnavailable,
    ContainerUnavailable,
    SlotUnavailable,
    TargetUnavailable,
    ItemIdZero,
    ItemIdMismatch,
    TargetPositionMismatch,
    NotEquippable,
    MateriaCountZero,
    ItemSheetUnavailable,
    ItemSheetRowUnavailable,
    Duplicate,
}

public sealed class MateriaRetrievalItemIdentity
{
    public MateriaRetrievalItemIdentity(
        bool isSymbolic,
        ushort linkedItemSlot,
        ushort linkedInventoryType,
        int quantity,
        ushort spiritbondOrCollectability,
        ushort condition,
        byte flags,
        ulong crafterContentId,
        uint glamourId,
        uint eventId,
        IReadOnlyList<ushort> materiaIds,
        IReadOnlyList<byte> materiaGrades,
        IReadOnlyList<byte> stains)
    {
        ArgumentNullException.ThrowIfNull(materiaIds);
        ArgumentNullException.ThrowIfNull(materiaGrades);
        ArgumentNullException.ThrowIfNull(stains);

        IsSymbolic = isSymbolic;
        LinkedItemSlot = linkedItemSlot;
        LinkedInventoryType = linkedInventoryType;
        Quantity = quantity;
        SpiritbondOrCollectability = spiritbondOrCollectability;
        Condition = condition;
        Flags = flags;
        CrafterContentId = crafterContentId;
        GlamourId = glamourId;
        EventId = eventId;
        MateriaIds = materiaIds.ToArray();
        MateriaGrades = materiaGrades.ToArray();
        Stains = stains.ToArray();
    }

    public bool IsSymbolic { get; }

    public ushort LinkedItemSlot { get; }

    public ushort LinkedInventoryType { get; }

    public int Quantity { get; }

    public ushort SpiritbondOrCollectability { get; }

    public ushort Condition { get; }

    public byte Flags { get; }

    public ulong CrafterContentId { get; }

    public uint GlamourId { get; }

    public uint EventId { get; }

    public IReadOnlyList<ushort> MateriaIds { get; }

    public IReadOnlyList<byte> MateriaGrades { get; }

    public IReadOnlyList<byte> Stains { get; }

    public int MateriaCount => MateriaIds.Count(id => id != 0);

    public bool MatchesStableState(MateriaRetrievalItemIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsSymbolic == other.IsSymbolic
            && LinkedItemSlot == other.LinkedItemSlot
            && LinkedInventoryType == other.LinkedInventoryType
            && Quantity == other.Quantity
            && SpiritbondOrCollectability == other.SpiritbondOrCollectability
            && Condition == other.Condition
            && Flags == other.Flags
            && CrafterContentId == other.CrafterContentId
            && GlamourId == other.GlamourId
            && EventId == other.EventId
            && Stains.SequenceEqual(other.Stains);
    }

    public bool MatchesMateriaState(MateriaRetrievalItemIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return MateriaIds.SequenceEqual(other.MateriaIds)
            && MateriaGrades.SequenceEqual(other.MateriaGrades);
    }

    public bool IsMateriaReductionFrom(MateriaRetrievalItemIdentity previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (!MatchesStableState(previous) || MateriaCount >= previous.MateriaCount)
        {
            return false;
        }

        var remaining = new Dictionary<(ushort Id, byte Grade), int>();
        for (var index = 0; index < previous.MateriaIds.Count; index++)
        {
            var id = previous.MateriaIds[index];
            if (id == 0)
            {
                continue;
            }

            var grade = index < previous.MateriaGrades.Count ? previous.MateriaGrades[index] : (byte)0;
            var key = (id, grade);
            remaining[key] = remaining.GetValueOrDefault(key) + 1;
        }

        for (var index = 0; index < MateriaIds.Count; index++)
        {
            var id = MateriaIds[index];
            if (id == 0)
            {
                continue;
            }

            var grade = index < MateriaGrades.Count ? MateriaGrades[index] : (byte)0;
            var key = (id, grade);
            if (!remaining.TryGetValue(key, out var count) || count == 0)
            {
                return false;
            }

            remaining[key] = count - 1;
        }

        return true;
    }
}

public sealed record MateriaRetrievalCandidateDiagnostics(
    string? AddonName,
    string? TargetType,
    InventoryType? Container,
    int? Slot,
    uint? ItemId,
    int? MateriaCount,
    int? MateriaSlotCount)
{
    public uint? ExpectedItemId { get; init; }
}

public sealed record MateriaRetrievalCandidate(
    InventoryType InventoryType,
    int Slot,
    uint ItemId,
    int StartingMateriaCount,
    int MateriaSlotCount,
    string DisplayName)
{
    public MateriaRetrievalItemIdentity? ItemIdentity { get; init; }

    public MateriaRetrievalQueueItem ToQueueItem()
    {
        return new MateriaRetrievalQueueItem(
            InventoryType,
            Slot,
            ItemId,
            StartingMateriaCount,
            DisplayName,
            ItemIdentity);
    }
}

public sealed record MateriaRetrievalCandidateResolution(
    MateriaRetrievalCandidate? Candidate,
    MateriaRetrievalCandidateRejectionReason RejectionReason,
    string UserMessage,
    MateriaRetrievalCandidateDiagnostics Diagnostics)
{
    public bool IsAccepted => Candidate != null && RejectionReason == MateriaRetrievalCandidateRejectionReason.None;
}

public sealed class MateriaRetrievalBatchPreview
{
    public MateriaRetrievalBatchPreview(
        IEnumerable<MateriaRetrievalCandidate> candidates,
        IReadOnlyDictionary<MateriaRetrievalCandidateRejectionReason, int> rejectedByReason,
        int duplicateCount)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rejectedByReason);
        if (duplicateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duplicateCount));
        }

        Candidates = candidates.ToArray();
        RejectedByReason = new System.Collections.ObjectModel.ReadOnlyDictionary<MateriaRetrievalCandidateRejectionReason, int>(
            new Dictionary<MateriaRetrievalCandidateRejectionReason, int>(rejectedByReason));
        DuplicateCount = duplicateCount;
    }

    public IReadOnlyList<MateriaRetrievalCandidate> Candidates { get; }

    public IReadOnlyDictionary<MateriaRetrievalCandidateRejectionReason, int> RejectedByReason { get; }

    public int DuplicateCount { get; }

    public int CandidateCount => Candidates.Count;

    public int TotalMateriaCount => Candidates.Sum(candidate => candidate.StartingMateriaCount);

    public int ExcludedCount => RejectedByReason.Values.Sum() + DuplicateCount;
}

public sealed record MateriaRetrievalQueueSnapshot(
    int RemainingCount,
    int RunningCount,
    int WaitingCount,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    string RunningItemName,
    string WaitingHeadName,
    IReadOnlyList<string> WaitingItemNames,
    int AdditionalWaitingCount,
    IReadOnlyList<string> FailureReasonLines,
    int AdditionalFailureReasonCount);

public sealed record MateriaRetrievalBatchQueueResult(
    bool Started,
    int AddedCount,
    int DuplicateCount,
    string Message);

public sealed class MateriaRetrievalQueueItem
{
    public MateriaRetrievalQueueItem(
        InventoryType inventoryType,
        int slot,
        uint itemId,
        int startingMateriaCount,
        string displayName,
        MateriaRetrievalItemIdentity? itemIdentity = null)
    {
        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        if (itemId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        if (startingMateriaCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startingMateriaCount));
        }

        InventoryType = inventoryType;
        Slot = slot;
        ItemId = itemId;
        StartingMateriaCount = startingMateriaCount;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Item {itemId}" : displayName;
        ItemIdentity = itemIdentity;
        LastObservedIdentity = itemIdentity;
        LastMateriaCount = startingMateriaCount;
        State = MateriaRetrievalItemState.Queued;
    }

    public InventoryType InventoryType { get; }

    public int Slot { get; }

    public uint ItemId { get; }

    public int StartingMateriaCount { get; }

    public string DisplayName { get; }

    public MateriaRetrievalItemIdentity? ItemIdentity { get; }

    public MateriaRetrievalItemIdentity? LastObservedIdentity { get; private set; }

    public MateriaRetrievalItemState State { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public int LastMateriaCount { get; private set; }

    public string? FailureReason { get; private set; }

    public bool MatchesIdentity(InventoryType inventoryType, int slot, uint itemId)
    {
        return InventoryType == inventoryType && Slot == slot && ItemId == itemId;
    }

    public void MarkProcessing()
    {
        EnsureState(MateriaRetrievalItemState.Queued);
        State = MateriaRetrievalItemState.Processing;
    }

    public void RecordAttempt(
        DateTimeOffset attemptedAt,
        int materiaCount,
        MateriaRetrievalItemIdentity? itemIdentity = null)
    {
        EnsureState(MateriaRetrievalItemState.Processing);
        if (materiaCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materiaCount));
        }

        Attempts++;
        LastAttemptAt = attemptedAt;
        LastMateriaCount = materiaCount;
        if (itemIdentity != null)
        {
            if (LastObservedIdentity != null && !LastObservedIdentity.MatchesStableState(itemIdentity))
            {
                throw new InvalidOperationException("回収対象の個体情報が変化しました。");
            }

            LastObservedIdentity = itemIdentity;
        }
    }

    public void RecordObservedIdentity(MateriaRetrievalItemIdentity itemIdentity, int materiaCount)
    {
        EnsureState(MateriaRetrievalItemState.Processing);
        ArgumentNullException.ThrowIfNull(itemIdentity);
        if (materiaCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materiaCount));
        }

        if (LastObservedIdentity != null && !LastObservedIdentity.MatchesStableState(itemIdentity))
        {
            throw new InvalidOperationException("回収対象の個体情報が変化しました。");
        }

        LastObservedIdentity = itemIdentity;
        LastMateriaCount = materiaCount;
    }

    public bool MatchesCurrentIdentity(MateriaRetrievalItemIdentity? itemIdentity)
    {
        return itemIdentity != null
            && LastObservedIdentity != null
            && LastObservedIdentity.MatchesStableState(itemIdentity);
    }

    public bool MatchesCurrentPreRequestIdentity(MateriaRetrievalItemIdentity? itemIdentity)
    {
        return itemIdentity != null
            && LastObservedIdentity != null
            && LastObservedIdentity.MatchesStableState(itemIdentity)
            && LastObservedIdentity.MatchesMateriaState(itemIdentity);
    }

    public void RecordObservedCount(int materiaCount)
    {
        EnsureState(MateriaRetrievalItemState.Processing);
        if (materiaCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(materiaCount));
        }

        LastMateriaCount = materiaCount;
    }

    public void MarkSucceeded()
    {
        EnsureState(MateriaRetrievalItemState.Processing);
        State = MateriaRetrievalItemState.Succeeded;
        LastMateriaCount = 0;
    }

    public void MarkFailed(string reason)
    {
        if (State is MateriaRetrievalItemState.Succeeded or MateriaRetrievalItemState.Aborted)
        {
            return;
        }

        State = MateriaRetrievalItemState.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "不明な理由" : reason;
    }

    public void MarkAborted(string reason)
    {
        if (State is MateriaRetrievalItemState.Succeeded or MateriaRetrievalItemState.Failed)
        {
            return;
        }

        State = MateriaRetrievalItemState.Aborted;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "中止" : reason;
    }

    private void EnsureState(MateriaRetrievalItemState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"対象状態が不正です: expected={expected}, actual={State}");
        }
    }
}

public sealed class MateriaRetrievalQueue
{
    private readonly List<MateriaRetrievalQueueItem> _items = [];

    public IReadOnlyList<MateriaRetrievalQueueItem> Items => _items;

    public MateriaRetrievalQueueItem? CurrentItem { get; private set; }

    public int PendingCount => _items.Count(item => item.State == MateriaRetrievalItemState.Queued);

    public int RemainingCount => PendingCount + (CurrentItem == null ? 0 : 1);

    public int SuccessCount => _items.Count(item => item.State == MateriaRetrievalItemState.Succeeded);

    public int FailureCount => _items.Count(item => item.State == MateriaRetrievalItemState.Failed);

    public int SkippedCount => _items.Count(item => item.State == MateriaRetrievalItemState.Aborted);

    public bool HasItems => _items.Count > 0;

    public MateriaRetrievalQueueSnapshot GetSnapshot(int maxWaitingItems = 5)
    {
        if (maxWaitingItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWaitingItems));
        }

        var waitingNames = _items
            .Where(item => item.State == MateriaRetrievalItemState.Queued)
            .Select(item => item.DisplayName)
            .ToArray();
        var visibleWaitingNames = waitingNames.Take(maxWaitingItems).ToArray();
        var failureReasonLines = _items
            .Where(item => item.State is MateriaRetrievalItemState.Failed or MateriaRetrievalItemState.Aborted)
            .Where(item => !string.IsNullOrWhiteSpace(item.FailureReason))
            .Select(item => $"{item.DisplayName}: {item.FailureReason}")
            .ToArray();
        var visibleFailureReasonLines = failureReasonLines.Take(maxWaitingItems).ToArray();

        return new MateriaRetrievalQueueSnapshot(
            RemainingCount,
            CurrentItem == null ? 0 : 1,
            PendingCount,
            SuccessCount,
            FailureCount,
            SkippedCount,
            CurrentItem?.DisplayName ?? "なし",
            visibleWaitingNames.FirstOrDefault() ?? "なし",
            visibleWaitingNames,
            Math.Max(0, waitingNames.Length - visibleWaitingNames.Length),
            visibleFailureReasonLines,
            Math.Max(0, failureReasonLines.Length - visibleFailureReasonLines.Length));
    }

    public bool ContainsIdentity(InventoryType inventoryType, int slot, uint itemId)
    {
        return _items.Any(item => item.MatchesIdentity(inventoryType, slot, itemId));
    }

    public bool TryEnqueue(MateriaRetrievalQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.State != MateriaRetrievalItemState.Queued || ContainsIdentity(item.InventoryType, item.Slot, item.ItemId))
        {
            return false;
        }

        _items.Add(item);
        return true;
    }

    public bool TryBeginNext()
    {
        if (CurrentItem != null)
        {
            return true;
        }

        var next = _items.FirstOrDefault(item => item.State == MateriaRetrievalItemState.Queued);
        if (next == null)
        {
            return false;
        }

        next.MarkProcessing();
        CurrentItem = next;
        return true;
    }

    public MateriaRetrievalObservation ObserveCurrentCount(int materiaCount)
    {
        if (CurrentItem == null || materiaCount < 0)
        {
            return MateriaRetrievalObservation.Invalid;
        }

        if (materiaCount > CurrentItem.LastMateriaCount)
        {
            return MateriaRetrievalObservation.Increased;
        }

        if (materiaCount == CurrentItem.LastMateriaCount)
        {
            return MateriaRetrievalObservation.NoChange;
        }

        CurrentItem.RecordObservedCount(materiaCount);
        if (materiaCount == 0)
        {
            CurrentItem.MarkSucceeded();
            CurrentItem = null;
            return MateriaRetrievalObservation.Completed;
        }

        return MateriaRetrievalObservation.Progressed;
    }

    public MateriaRetrievalObservation ObserveCurrentState(
        MateriaRetrievalItemIdentity? itemIdentity,
        int materiaCount)
    {
        if (CurrentItem == null
            || itemIdentity == null
            || materiaCount < 0
            || itemIdentity.MateriaCount != materiaCount
            || !CurrentItem.MatchesCurrentIdentity(itemIdentity))
        {
            return MateriaRetrievalObservation.Invalid;
        }

        if (materiaCount > CurrentItem.LastMateriaCount)
        {
            return MateriaRetrievalObservation.Increased;
        }

        if (materiaCount == CurrentItem.LastMateriaCount)
        {
            return CurrentItem.LastObservedIdentity!.MatchesMateriaState(itemIdentity)
                ? MateriaRetrievalObservation.NoChange
                : MateriaRetrievalObservation.Invalid;
        }

        if (!itemIdentity.IsMateriaReductionFrom(CurrentItem.LastObservedIdentity!))
        {
            return MateriaRetrievalObservation.Invalid;
        }

        CurrentItem.RecordObservedIdentity(itemIdentity, materiaCount);
        if (materiaCount == 0)
        {
            CurrentItem.MarkSucceeded();
            CurrentItem = null;
            return MateriaRetrievalObservation.Completed;
        }

        return MateriaRetrievalObservation.Progressed;
    }

    public void MarkCurrentSucceeded()
    {
        if (CurrentItem == null)
        {
            return;
        }

        CurrentItem.MarkSucceeded();
        CurrentItem = null;
    }

    public void MarkCurrentFailed(string reason)
    {
        if (CurrentItem == null)
        {
            return;
        }

        CurrentItem.MarkFailed(reason);
        CurrentItem = null;
    }

    public void MarkAllFailed(string reason)
    {
        CurrentItem?.MarkFailed(reason);
        CurrentItem = null;
        foreach (var item in _items.Where(item => item.State == MateriaRetrievalItemState.Queued))
        {
            item.MarkFailed(reason);
        }
    }

    public void MarkAllAborted(string reason)
    {
        CurrentItem?.MarkAborted(reason);
        CurrentItem = null;
        foreach (var item in _items.Where(item => item.State == MateriaRetrievalItemState.Queued))
        {
            item.MarkAborted(reason);
        }
    }

    public void Clear()
    {
        _items.Clear();
        CurrentItem = null;
    }
}

public static class MateriaRetrievalSafety
{
    public static bool CanStart(
        bool featureEnabled,
        bool loggedIn,
        bool materializeVisible,
        bool materializeDialogVisible,
        bool retrievalDialogVisible,
        bool extractionWaiting,
        bool occupied,
        bool betweenAreas)
    {
        return featureEnabled
            && loggedIn
            && !materializeVisible
            && !materializeDialogVisible
            && !retrievalDialogVisible
            && !extractionWaiting
            && !occupied
            && !betweenAreas;
    }

    public static bool CanIssueRequest(
        bool occupied,
        bool betweenAreas,
        bool materializeVisible,
        bool materializeDialogVisible,
        bool retrievalDialogVisible)
    {
        return !occupied
            && !betweenAreas
            && !materializeVisible
            && !materializeDialogVisible
            && !retrievalDialogVisible;
    }

    public static bool CanConfirmRetrievalDialog(
        bool ownRequestWaiting,
        bool dialogVisible,
        bool dialogObservedAfterRequest,
        bool confirmationSubmitted)
    {
        return ownRequestWaiting
            && dialogVisible
            && dialogObservedAfterRequest
            && !confirmationSubmitted;
    }

    public static bool IsValidTarget(
        MateriaRetrievalQueueItem expected,
        bool targetExists,
        uint currentItemId,
        int currentMateriaCount,
        bool itemSheetAvailable)
    {
        return targetExists
            && currentItemId == expected.ItemId
            && currentMateriaCount > 0
            && itemSheetAvailable;
    }

    public static bool IsValidTarget(
        MateriaRetrievalQueueItem expected,
        bool targetExists,
        uint currentItemId,
        int currentMateriaCount,
        bool itemSheetAvailable,
        MateriaRetrievalItemIdentity? currentIdentity)
    {
        return IsValidTarget(expected, targetExists, currentItemId, currentMateriaCount, itemSheetAvailable)
            && expected.MatchesCurrentPreRequestIdentity(currentIdentity);
    }

    public static bool CanUseCandidate(int materiaCount, int materiaSlotCount, bool itemSheetAvailable)
    {
        return CanUseCandidate(materiaCount, materiaSlotCount, itemSheetAvailable, true);
    }

    public static bool CanUseCandidate(
        int materiaCount,
        int materiaSlotCount,
        bool itemSheetAvailable,
        bool isEquippable)
    {
        _ = materiaSlotCount;
        return itemSheetAvailable && isEquippable && materiaCount > 0;
    }

    public static bool IsHandInventoryContainer(InventoryType inventoryType)
    {
        return inventoryType is InventoryType.Inventory1
            or InventoryType.Inventory2
            or InventoryType.Inventory3
            or InventoryType.Inventory4;
    }

    public static bool CanRetry(int attempts, int maxAttempts)
    {
        return attempts >= 0 && maxAttempts > 0 && attempts < maxAttempts;
    }

    public static bool CanRetryAfterTimeout(bool occupied, bool unverifiedUiObserved, int attempts, int maxAttempts)
    {
        return !occupied && !unverifiedUiObserved && CanRetry(attempts, maxAttempts);
    }
}

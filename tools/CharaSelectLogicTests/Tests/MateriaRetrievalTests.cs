// Path: tools/CharaSelectLogicTests/Tests/MateriaRetrievalTests.cs
// Description: 装着マテリア回収の候補判定、キュー集計、一括範囲、相互排他を検証する
// Reason: ゲーム内ポインタに依存しない回収の候補・FIFO・進捗・終了条件を固定するため
// RELEVANT FILES: projects/XIV-Mini-Util/Models/Materia/MateriaRetrievalQueueItem.cs, projects/XIV-Mini-Util/Services/Materia/MateriaRetrievalService.cs, tools/CharaSelectLogicTests/TestRunner.cs
using FFXIVClientStructs.FFXIV.Client.Game;
using XivMiniUtil.Models.Materia;

internal static partial class TestRunner
{
    private static void AddMateriaRetrievalTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

        Test(70, "materia retrieval observes 3 to 2 to 1 to 0 as success", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var item = RetrievalItem(0, 3, 3001);
            var now = DateTimeOffset.UtcNow;

            return queue.TryEnqueue(item)
                && queue.TryBeginNext()
                && queue.CurrentItem == item
                && RecordAndObserve(queue, item, now, 3, 2) == MateriaRetrievalObservation.Progressed
                && RecordAndObserve(queue, item, now, 2, 1) == MateriaRetrievalObservation.Progressed
                && RecordAndObserve(queue, item, now, 1, 0) == MateriaRetrievalObservation.Completed
                && queue.CurrentItem == null
                && queue.SuccessCount == 1
                && item.State == MateriaRetrievalItemState.Succeeded;
        });

        Test(71, "materia retrieval processes multiple queued items in registration order", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var first = RetrievalItem(0, 1, 3002);
            var second = RetrievalItem(1, 2, 3003);

            if (!queue.TryEnqueue(first) || !queue.TryEnqueue(second) || !queue.TryBeginNext())
            {
                return false;
            }

            if (queue.CurrentItem != first)
            {
                return false;
            }

            first.RecordAttempt(DateTimeOffset.UtcNow, 1);
            if (queue.ObserveCurrentCount(0) != MateriaRetrievalObservation.Completed
                || !queue.TryBeginNext()
                || queue.CurrentItem != second)
            {
                return false;
            }

            second.RecordAttempt(DateTimeOffset.UtcNow, 2);
            return queue.ObserveCurrentCount(0) == MateriaRetrievalObservation.Completed
                && queue.SuccessCount == 2
                && queue.FailureCount == 0;
        });

        Test(72, "materia retrieval rejects duplicate target registration", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var first = RetrievalItem(4, 2, 3004);
            var duplicate = RetrievalItem(4, 2, 3004, "別名");

            return queue.TryEnqueue(first)
                && !queue.TryEnqueue(duplicate)
                && queue.Items.Count == 1;
        });

        Test(73, "materia retrieval refuses disappeared replaced empty and slotless targets", () =>
        {
            var expected = RetrievalItem(5, 2, 3005);

            return !MateriaRetrievalSafety.IsValidTarget(expected, false, expected.ItemId, 2, true)
                && !MateriaRetrievalSafety.IsValidTarget(expected, true, expected.ItemId + 1, 2, true)
                && !MateriaRetrievalSafety.IsValidTarget(expected, true, expected.ItemId, 0, true)
                && !MateriaRetrievalSafety.IsValidTarget(expected, true, expected.ItemId, 2, false)
                && MateriaRetrievalSafety.IsValidTarget(expected, true, expected.ItemId, 2, true);
        });

        Test(95, "materia retrieval rejects same item id replacement using the item snapshot", () =>
        {
            var originalIdentity = RetrievalIdentity(1, 1001, 1002);
            var replacedIdentity = RetrievalIdentity(2, 1001, 1002);
            var differentMateriaIdentity = RetrievalIdentity(1, 2001, 2002);
            var reducedIdentity = RetrievalIdentity(1, 1001);
            var expected = RetrievalItem(15, 2, 3095, "個体識別対象", originalIdentity);
            var queue = new MateriaRetrievalQueue();

            if (!queue.TryEnqueue(expected)
                || !queue.TryBeginNext()
                || !MateriaRetrievalSafety.IsValidTarget(
                    expected,
                    true,
                    expected.ItemId,
                    2,
                    true,
                    originalIdentity)
                || MateriaRetrievalSafety.IsValidTarget(
                    expected,
                    true,
                    expected.ItemId,
                    2,
                    true,
                    replacedIdentity)
                || expected.MatchesCurrentIdentity(replacedIdentity))
            {
                return false;
            }

            expected.RecordAttempt(DateTimeOffset.UtcNow, 2, originalIdentity);
            return queue.ObserveCurrentState(replacedIdentity, 2) == MateriaRetrievalObservation.Invalid
                && queue.ObserveCurrentState(differentMateriaIdentity, 2) == MateriaRetrievalObservation.Invalid
                && queue.ObserveCurrentState(reducedIdentity, 1) == MateriaRetrievalObservation.Progressed;
        });

        Test(97, "materia retrieval rejects same item id and count replacement before request", () =>
        {
            var originalIdentity = RetrievalIdentity(7, 1001, 1002);
            var replacedIdentity = RetrievalIdentity(7, 2001, 2002);
            var expected = RetrievalItem(16, 2, 3096, "要求前個体識別対象", originalIdentity);
            var queue = new MateriaRetrievalQueue();

            return queue.TryEnqueue(expected)
                && queue.TryBeginNext()
                && MateriaRetrievalSafety.IsValidTarget(
                    expected,
                    true,
                    expected.ItemId,
                    2,
                    true,
                    originalIdentity)
                && !MateriaRetrievalSafety.IsValidTarget(
                    expected,
                    true,
                    expected.ItemId,
                    2,
                    true,
                    replacedIdentity)
                && expected.Attempts == 0;
        });

        Test(74, "materia retrieval waits without issuing while occupied", () =>
        {
            return !MateriaRetrievalSafety.CanIssueRequest(true, false, false, false, false)
                && !MateriaRetrievalSafety.CanIssueRequest(false, true, false, false, false)
                && !MateriaRetrievalSafety.CanIssueRequest(false, false, false, false, true)
                && MateriaRetrievalSafety.CanIssueRequest(false, false, false, false, false);
        });

        Test(75, "materia retrieval timeout stops after three attempts", () =>
        {
            return MateriaRetrievalSafety.CanRetry(0, 3)
                && MateriaRetrievalSafety.CanRetry(1, 3)
                && MateriaRetrievalSafety.CanRetry(2, 3)
                && !MateriaRetrievalSafety.CanRetry(3, 3)
                && !MateriaRetrievalSafety.CanRetry(-1, 3);
        });

        Test(94, "materia retrieval never waits indefinitely after an occupied timeout", () =>
        {
            return MateriaRetrievalSafety.CanRetryAfterTimeout(false, false, 0, 3)
                && MateriaRetrievalSafety.CanRetryAfterTimeout(false, false, 2, 3)
                && !MateriaRetrievalSafety.CanRetryAfterTimeout(false, false, 3, 3)
                && !MateriaRetrievalSafety.CanRetryAfterTimeout(true, false, 0, 3)
                && !MateriaRetrievalSafety.CanRetryAfterTimeout(false, true, 0, 3);
        });

        Test(76, "materia retrieval abort clears pending work", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var first = RetrievalItem(6, 2, 3006);
            var second = RetrievalItem(7, 1, 3007);

            return queue.TryEnqueue(first)
                && queue.TryEnqueue(second)
                && queue.TryBeginNext()
                && queue.RemainingCount == 2
                && AbortQueue(queue, "中止")
                && queue.RemainingCount == 0
                && queue.PendingCount == 0
                && queue.Items.All(item => item.State == MateriaRetrievalItemState.Aborted);
        });

        Test(77, "materia retrieval logout returns the same cleanup boundary", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var item = RetrievalItem(8, 1, 3008);
            return queue.TryEnqueue(item)
                && queue.TryBeginNext()
                && AbortQueue(queue, "ログアウト")
                && item.State == MateriaRetrievalItemState.Aborted
                && queue.PendingCount == 0;
        });

        Test(78, "materia retrieval dispose returns the same cleanup boundary", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var item = RetrievalItem(9, 1, 3009);
            return queue.TryEnqueue(item)
                && queue.TryBeginNext()
                && AbortQueue(queue, "Dispose")
                && item.State == MateriaRetrievalItemState.Aborted
                && queue.PendingCount == 0;
        });

        Test(79, "materia retrieval and extraction are mutually exclusive", () =>
        {
            var safe = MateriaRetrievalSafety.CanStart(true, true, false, false, false, false, false, false);
            return safe
                && !MateriaRetrievalSafety.CanStart(true, true, true, false, false, false, false, false)
                && !MateriaRetrievalSafety.CanStart(true, true, false, true, false, false, false, false)
                && !MateriaRetrievalSafety.CanStart(true, true, false, false, true, false, false, false)
                && !MateriaRetrievalSafety.CanStart(true, true, false, false, false, true, false, false);
        });

        Test(80, "materia retrieval feature disabled refuses start", () =>
        {
            return !MateriaRetrievalSafety.CanStart(false, true, false, false, false, false, false, false);
        });

        Test(81, "materia retrieval never relies on unverified occupied39", () =>
        {
            var root = FindRepositoryRoot();
            var servicePath = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "Materia", "MateriaRetrievalService.cs");
            return !File.ReadAllText(servicePath).Contains("Occupied39", StringComparison.Ordinal);
        });

        Test(82, "materia retrieval uses materia count as the primary candidate condition", () =>
        {
            return MateriaRetrievalSafety.CanUseCandidate(2, 0, true)
                && MateriaRetrievalSafety.CanUseCandidate(2, 5, true)
                && !MateriaRetrievalSafety.CanUseCandidate(0, 5, true)
                && !MateriaRetrievalSafety.CanUseCandidate(2, 0, false);
        });

        Test(98, "materia retrieval requires an equippable item with attached materia", () =>
        {
            return !MateriaRetrievalSafety.CanUseCandidate(2, 0, true, false)
                && MateriaRetrievalSafety.CanUseCandidate(2, 0, true, true)
                && !MateriaRetrievalSafety.CanUseCandidate(0, 5, true, true)
                && !MateriaRetrievalSafety.CanUseCandidate(2, 0, false, true);
        });

        Test(83, "materia retrieval candidate rejection keeps structured diagnostics", () =>
        {
            var diagnostics = new MateriaRetrievalCandidateDiagnostics(
                "MateriaAttach",
                "MenuTargetDefault",
                InventoryType.Inventory2,
                7,
                3010,
                2,
                0);
            var resolution = new MateriaRetrievalCandidateResolution(
                null,
                MateriaRetrievalCandidateRejectionReason.ItemIdMismatch,
                "item ID不一致",
                diagnostics);

            return !resolution.IsAccepted
                && resolution.RejectionReason == MateriaRetrievalCandidateRejectionReason.ItemIdMismatch
                && resolution.Diagnostics.AddonName == "MateriaAttach"
                && resolution.Diagnostics.TargetType == "MenuTargetDefault"
                && resolution.Diagnostics.Container == InventoryType.Inventory2
                && resolution.Diagnostics.Slot == 7
                && resolution.Diagnostics.ItemId == 3010
                && resolution.Diagnostics.MateriaCount == 2
                && resolution.Diagnostics.MateriaSlotCount == 0;
        });

        Test(84, "materia retrieval queue aggregates current and waiting items", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var first = RetrievalItem(10, 1, 3011, "実行中装備");
            var second = RetrievalItem(11, 1, 3012, "待機装備A");
            var third = RetrievalItem(12, 1, 3013, "待機装備B");
            var fourth = RetrievalItem(13, 1, 3014, "待機装備C");

            return queue.TryEnqueue(first)
                && queue.TryEnqueue(second)
                && queue.TryEnqueue(third)
                && queue.TryEnqueue(fourth)
                && queue.TryBeginNext()
                && queue.GetSnapshot() is var snapshot
                && snapshot.RemainingCount == 4
                && snapshot.RunningCount == 1
                && snapshot.WaitingCount == 3
                && snapshot.SuccessCount == 0
                && snapshot.FailureCount == 0
                && snapshot.SkippedCount == 0
                && snapshot.RunningItemName == "実行中装備"
                && snapshot.WaitingHeadName == "待機装備A"
                && snapshot.WaitingItemNames.SequenceEqual(["待機装備A", "待機装備B", "待機装備C"])
                && snapshot.AdditionalWaitingCount == 0;
        });

        Test(85, "materia retrieval waiting list is capped and reports overflow", () =>
        {
            var queue = new MateriaRetrievalQueue();
            for (var index = 0; index < 7; index++)
            {
                if (!queue.TryEnqueue(RetrievalItem(20 + index, 1, (uint)(3020 + index), $"待機{index + 1}")))
                {
                    return false;
                }
            }

            var snapshot = queue.GetSnapshot(5);
            return snapshot.RemainingCount == 7
                && snapshot.RunningCount == 0
                && snapshot.WaitingCount == 7
                && snapshot.WaitingHeadName == "待機1"
                && snapshot.WaitingItemNames.SequenceEqual(["待機1", "待機2", "待機3", "待機4", "待機5"])
                && snapshot.AdditionalWaitingCount == 2
                && snapshot.FailureReasonLines.Count == 0
                && snapshot.AdditionalFailureReasonCount == 0;
        });

        Test(86, "materia retrieval individual and batch candidates share FIFO order", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var individual = RetrievalItem(30, 1, 3030, "個別");
            var batchCandidate = new MateriaRetrievalCandidate(
                InventoryType.Inventory1,
                31,
                3031,
                2,
                0,
                "一括");

            return queue.TryEnqueue(individual)
                && queue.TryEnqueue(batchCandidate.ToQueueItem())
                && queue.Items.Select(item => item.DisplayName).SequenceEqual(["個別", "一括"]);
        });

        Test(87, "materia retrieval batch scope is limited to normal inventory containers", () =>
        {
            return MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.Inventory1)
                && MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.Inventory2)
                && MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.Inventory3)
                && MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.Inventory4)
                && !MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.ArmoryBody)
                && !MateriaRetrievalSafety.IsHandInventoryContainer(InventoryType.EquippedItems);
        });

        Test(88, "materia retrieval batch rejects duplicate identities", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var candidate = new MateriaRetrievalCandidate(
                InventoryType.Inventory2,
                32,
                3032,
                1,
                0,
                "重複対象");

            return queue.TryEnqueue(candidate.ToQueueItem())
                && !queue.TryEnqueue(candidate.ToQueueItem())
                && queue.Items.Count == 1;
        });

        Test(89, "materia retrieval continues after one item fails", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var failed = RetrievalItem(33, 1, 3033, "失敗対象");
            var succeeded = RetrievalItem(34, 1, 3034, "後続対象");

            if (!queue.TryEnqueue(failed)
                || !queue.TryEnqueue(succeeded)
                || !queue.TryBeginNext())
            {
                return false;
            }

            queue.MarkCurrentFailed("対象消失");
            if (!queue.TryBeginNext() || queue.CurrentItem != succeeded)
            {
                return false;
            }

            succeeded.RecordAttempt(DateTimeOffset.UtcNow, 1);
            return queue.ObserveCurrentCount(0) == MateriaRetrievalObservation.Completed
                && queue.SuccessCount == 1
                && queue.FailureCount == 1
                && queue.GetSnapshot().FailureReasonLines.SequenceEqual(["失敗対象: 対象消失"]);
        });

        Test(90, "materia retrieval batch preview aggregates candidates exclusions and duplicates", () =>
        {
            var candidates = new[]
            {
                new MateriaRetrievalCandidate(InventoryType.Inventory1, 35, 3035, 3, 0, "一括A"),
                new MateriaRetrievalCandidate(InventoryType.Inventory2, 36, 3036, 2, 5, "一括B"),
            };
            var rejected = new Dictionary<MateriaRetrievalCandidateRejectionReason, int>
            {
                [MateriaRetrievalCandidateRejectionReason.MateriaCountZero] = 2,
                [MateriaRetrievalCandidateRejectionReason.ItemSheetRowUnavailable] = 1,
            };
            var preview = new MateriaRetrievalBatchPreview(candidates, rejected, 1);

            return preview.CandidateCount == 2
                && preview.TotalMateriaCount == 5
                && preview.ExcludedCount == 4
                && preview.DuplicateCount == 1
                && preview.RejectedByReason[MateriaRetrievalCandidateRejectionReason.MateriaCountZero] == 2;
        });

        Test(91, "materia retrieval keeps the existing fail closed boundaries", () =>
        {
            var root = FindRepositoryRoot();
            var servicePath = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "Materia", "MateriaRetrievalService.cs");
            var source = File.ReadAllText(servicePath);
            return !source.Contains("Occupied39", StringComparison.Ordinal)
                && source.Contains("MaterializeEntryId.Retrieve", StringComparison.Ordinal)
                && source.Contains("RequestTimeout = TimeSpan.FromSeconds(5)", StringComparison.Ordinal)
                && source.Contains("MaxAttempts = 3", StringComparison.Ordinal)
                && source.Contains("MateriaAttach", StringComparison.Ordinal)
                && !source.Contains("MateriaSlotCount == 0", StringComparison.Ordinal);
        });

        Test(92, "materia retrieval exposes failure and abort reasons in the queue snapshot", () =>
        {
            var queue = new MateriaRetrievalQueue();
            var failed = RetrievalItem(40, 1, 3040, "失敗装備");
            var aborted = RetrievalItem(41, 1, 3041, "中止装備");

            return queue.TryEnqueue(failed)
                && queue.TryEnqueue(aborted)
                && queue.TryBeginNext()
                && MarkFailedThenAbort(queue, "対象消失", "ユーザー中止")
                && queue.FailureCount == 1
                && queue.SkippedCount == 1
                && queue.GetSnapshot().FailureReasonLines.SequenceEqual(["失敗装備: 対象消失", "中止装備: ユーザー中止"]);
        });

        Test(93, "materia retrieval confirmation requires owned pending request and one submission", () =>
        {
            return !MateriaRetrievalSafety.CanConfirmRetrievalDialog(false, true, true, false)
                && !MateriaRetrievalSafety.CanConfirmRetrievalDialog(true, false, true, false)
                && !MateriaRetrievalSafety.CanConfirmRetrievalDialog(true, true, false, false)
                && MateriaRetrievalSafety.CanConfirmRetrievalDialog(true, true, true, false)
                && !MateriaRetrievalSafety.CanConfirmRetrievalDialog(true, true, true, true);
        });

        Test(96, "materia retrieval wires the owned confirmation path into the service", () =>
        {
            var root = FindRepositoryRoot();
            var serviceSource = File.ReadAllText(Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "Materia",
                "MateriaRetrievalService.cs"));
            var gameUiSource = File.ReadAllText(Path.Combine(
                root,
                "projects",
                "XIV-Mini-Util",
                "Services",
                "Common",
                "GameUiService.cs"));

            return serviceSource.Contains("if (retrievalDialogVisible && !_requestWaiting)", StringComparison.Ordinal)
                && serviceSource.Contains("ProcessWaiting(now, retrievalDialogVisible)", StringComparison.Ordinal)
                && serviceSource.Contains("MateriaRetrievalSafety.CanConfirmRetrievalDialog(", StringComparison.Ordinal)
                && serviceSource.Contains("_gameUiService.TryConfirmMateriaRetrieveDialog()", StringComparison.Ordinal)
                && serviceSource.Contains("ReleaseExtractionPause();", StringComparison.Ordinal)
                && gameUiSource.Contains("TryConfirmMateriaRetrieveDialog", StringComparison.Ordinal)
                && gameUiSource.Contains("FireCallbackInt", StringComparison.Ordinal);
        });
    }

    private static MateriaRetrievalQueueItem RetrievalItem(
        int slot,
        int materiaCount,
        uint itemId,
        string displayName = "テスト装備",
        MateriaRetrievalItemIdentity? itemIdentity = null)
    {
        return new MateriaRetrievalQueueItem(
            InventoryType.Inventory1,
            slot,
            itemId,
            materiaCount,
            displayName,
            itemIdentity);
    }

    private static MateriaRetrievalItemIdentity RetrievalIdentity(
        uint instanceToken,
        params ushort[] materiaIds)
    {
        var normalizedMateriaIds = materiaIds
            .Concat(Enumerable.Repeat((ushort)0, Math.Max(0, 5 - materiaIds.Length)))
            .Take(5)
            .ToArray();
        var materiaGrades = normalizedMateriaIds
            .Select(id => id == 0 ? (byte)0 : (byte)1)
            .ToArray();

        return new MateriaRetrievalItemIdentity(
            false,
            0,
            0,
            1,
            10000,
            30000,
            0,
            instanceToken,
            0,
            instanceToken,
            normalizedMateriaIds,
            materiaGrades,
            [0, 0]);
    }

    private static MateriaRetrievalObservation RecordAndObserve(
        MateriaRetrievalQueue queue,
        MateriaRetrievalQueueItem item,
        DateTimeOffset now,
        int currentCount,
        int observedCount)
    {
        item.RecordAttempt(now, currentCount);
        return queue.ObserveCurrentCount(observedCount);
    }

    private static bool AbortQueue(MateriaRetrievalQueue queue, string reason)
    {
        queue.MarkAllAborted(reason);
        return true;
    }

    private static bool MarkFailedThenAbort(MateriaRetrievalQueue queue, string failureReason, string abortReason)
    {
        queue.MarkCurrentFailed(failureReason);
        if (!queue.TryBeginNext())
        {
            return false;
        }

        queue.MarkAllAborted(abortReason);
        return true;
    }
}

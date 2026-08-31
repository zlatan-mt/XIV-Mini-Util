// Path: tools/CharaSelectLogicTests/Tests/SubmarineTests.cs
// Description: 潜水艦のキャラクター横断サマリー計算を検証する
// Reason: UI描画から分離した純粋な集計条件を固定するため
using XivMiniUtil.Models.Submarine;
using XivMiniUtil.Windows.Components;

internal static partial class TestRunner
{
    private static void AddSubmarineTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

        var nowUtc = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

        Test(519, "submarine summary reports no next return when all entries are non-future", () =>
        {
            var summary = SubmarineSummaryCalculator.Calculate(
                new Dictionary<ulong, CharacterSubmarines>
                {
                    [1] = new()
                    {
                        CharacterName = "キャラクターA",
                        Submarines =
                        [
                            new() { Name = "完了", Status = SubmarineStatus.Completed, ReturnTime = nowUtc.AddHours(-2) },
                            new() { Name = "期限超過", Status = SubmarineStatus.Exploring, ReturnTime = nowUtc.AddMinutes(-1) },
                            new() { Name = "未出港", Status = SubmarineStatus.Unknown, ReturnTime = DateTime.UnixEpoch },
                        ],
                    },
                },
                nowUtc);

            return summary.ReturnedCount == 2 && summary.NextReturn is null;
        });

        Test(520, "submarine summary counts stale exploring entries as returned", () =>
        {
            var summary = SubmarineSummaryCalculator.Calculate(
                new Dictionary<ulong, CharacterSubmarines>
                {
                    [1] = new()
                    {
                        CharacterName = "キャラクターA",
                        Submarines =
                        [
                            new() { Name = "期限超過", Status = SubmarineStatus.Exploring, ReturnTime = nowUtc.AddSeconds(-1) },
                            new() { Name = "探索中", Status = SubmarineStatus.Exploring, ReturnTime = nowUtc.AddHours(1) },
                        ],
                    },
                },
                nowUtc);

            return summary.ReturnedCount == 1
                && summary.NextReturn is { SubmarineName: "探索中" };
        });

        Test(521, "submarine summary selects the earliest future return across characters", () =>
        {
            var earliest = nowUtc.AddMinutes(30);
            var summary = SubmarineSummaryCalculator.Calculate(
                new Dictionary<ulong, CharacterSubmarines>
                {
                    [1] = new()
                    {
                        CharacterName = string.Empty,
                        Submarines =
                        [
                            new() { Name = "後の潜水艦", Status = SubmarineStatus.Exploring, ReturnTime = nowUtc.AddHours(2) },
                        ],
                    },
                    [2] = new()
                    {
                        CharacterName = "キャラクターB",
                        Submarines =
                        [
                            new() { Name = "最初の潜水艦", Status = SubmarineStatus.Exploring, ReturnTime = earliest },
                        ],
                    },
                },
                nowUtc);

            return summary.ReturnedCount == 0
                && summary.NextReturn is
                {
                    CharacterName: "キャラクターB",
                    SubmarineName: "最初の潜水艦",
                    ReturnTimeUtc: var returnTimeUtc,
                }
                && returnTimeUtc == earliest;
        });
    }
}

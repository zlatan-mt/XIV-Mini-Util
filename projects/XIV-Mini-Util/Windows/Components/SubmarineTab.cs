// Path: projects/XIV-Mini-Util/Windows/Components/SubmarineTab.cs
// Description: 潜水艦タブのUI描画を担当する
// Reason: MainWindowから潜水艦表示ロジックを分離するため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTableColumnFlags = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using XivMiniUtil.Models.Submarine;
using XivMiniUtil.Services.Submarine;

namespace XivMiniUtil.Windows.Components;

public sealed class SubmarineTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly SubmarineDataStorage _submarineDataStorage;

    public SubmarineTab(Configuration configuration, SubmarineDataStorage submarineDataStorage)
    {
        _configuration = configuration;
        _submarineDataStorage = submarineDataStorage;
    }

    public void Draw()
    {
        ImGui.Text("潜水艦探索状況");
        ImGui.Separator();

        if (!_configuration.SubmarineTrackerEnabled)
        {
            ImGui.Text("この機能は現在無効化されています。設定タブで有効にしてください。");
            return;
        }

        var allData = _submarineDataStorage.GetAll();
        if (allData.Count == 0)
        {
            ImGui.Text("潜水艦情報がありません。FCハウスに入室してください。");
            return;
        }

        var nowUtc = DateTime.UtcNow;
        DrawSummary(allData, nowUtc);

        if (ImGui.BeginTabBar("SubmarineChars"))
        {
            foreach (var kvp in allData)
            {
                var charInfo = kvp.Value;
                var charName = string.IsNullOrEmpty(charInfo.CharacterName) ? $"ID: {kvp.Key}" : charInfo.CharacterName;

                if (ImGui.BeginTabItem($"{charName}##{kvp.Key}"))
                {
                    DrawSubmarineList(charInfo.Submarines);
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }

    private static void DrawSummary(IReadOnlyDictionary<ulong, CharacterSubmarines> allData, DateTime nowUtc)
    {
        var summary = SubmarineSummaryCalculator.Calculate(allData, nowUtc);
        ImGui.Text($"帰還済み（全キャラクター）: {summary.ReturnedCount}隻");

        if (summary.NextReturn is not { } nextReturn)
        {
            ImGui.Text("次の帰還: なし");
            return;
        }

        var localTime = nextReturn.ReturnTimeUtc.ToLocalTime();
        var remaining = nextReturn.ReturnTimeUtc - nowUtc;
        ImGui.Text($"次の帰還: {nextReturn.CharacterName} / {nextReturn.SubmarineName} ({localTime:MM/dd HH:mm}, あと {FormatTimeSpan(remaining)})");
    }

    public void Dispose()
    {
    }

    private void DrawSubmarineList(List<SubmarineData> submarines)
    {
        if (submarines.Count == 0)
        {
            ImGui.Text("登録されている潜水艦がありません。");
            return;
        }

        if (ImGui.BeginTable("SubmarineTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("名称");
            ImGui.TableSetupColumn("ランク", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("状態", ImGuiTableColumnFlags.WidthFixed, 100f);
            ImGui.TableSetupColumn("帰還時刻 (Local)");
            ImGui.TableHeadersRow();

            foreach (var sub in submarines)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(sub.Name);

                ImGui.TableSetColumnIndex(1);
                ImGui.Text(sub.Rank.ToString());

                ImGui.TableSetColumnIndex(2);
                var statusText = sub.Status switch
                {
                    SubmarineStatus.Exploring => "探索中",
                    SubmarineStatus.Completed => "完了",
                    _ => "不明"
                };

                // 探索中でも時間が過ぎていれば完了扱い（メモリ上のステータス更新がまだの場合など）
                // ただしStatusプロパティはメモリ読み取り時に判定されているはず
                var now = DateTime.UtcNow;
                if (sub.Status == SubmarineStatus.Exploring && sub.ReturnTime <= now)
                {
                    statusText = "完了 (未更新)";
                    ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), statusText);
                }
                else if (sub.Status == SubmarineStatus.Completed)
                {
                    ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), statusText);
                }
                else
                {
                    ImGui.Text(statusText);
                }

                ImGui.TableSetColumnIndex(3);
                if (sub.ReturnTime > DateTime.UnixEpoch.AddSeconds(1))
                {
                    var localTime = sub.ReturnTime.ToLocalTime();
                    var timeStr = localTime.ToString("MM/dd HH:mm");
                    var remaining = sub.ReturnTime - now;

                    if (remaining.TotalSeconds > 0)
                    {
                        ImGui.Text($"{timeStr} (あと {FormatTimeSpan(remaining)})");
                    }
                    else
                    {
                        ImGui.Text(timeStr);
                    }
                }
                else
                {
                    ImGui.Text("-");
                }
            }

            ImGui.EndTable();
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{ts.Days}日{ts.Hours}時間";
        if (ts.TotalHours >= 1) return $"{ts.Hours}時間{ts.Minutes}分";
        return $"{ts.Minutes}分";
    }
}

internal readonly record struct SubmarineSummary(
    int ReturnedCount,
    SubmarineSummaryNextReturn? NextReturn);

internal readonly record struct SubmarineSummaryNextReturn(
    string CharacterName,
    string SubmarineName,
    DateTime ReturnTimeUtc);

internal static class SubmarineSummaryCalculator
{
    private static readonly DateTime MinimumValidReturnTimeUtc = DateTime.UnixEpoch.AddSeconds(1);

    public static SubmarineSummary Calculate(
        IReadOnlyDictionary<ulong, CharacterSubmarines> allData,
        DateTime nowUtc)
    {
        var evaluationTimeUtc = AsUtc(nowUtc);
        var returnedCount = 0;
        SubmarineSummaryNextReturn? nextReturn = null;

        foreach (var characterEntry in allData)
        {
            var characterInfo = characterEntry.Value;
            var characterName = string.IsNullOrWhiteSpace(characterInfo.CharacterName)
                ? "キャラクター名不明"
                : characterInfo.CharacterName;

            foreach (var submarine in characterInfo.Submarines)
            {
                var returnTimeUtc = AsUtc(submarine.ReturnTime);
                if (submarine.Status == SubmarineStatus.Completed
                    || (submarine.Status == SubmarineStatus.Exploring && returnTimeUtc <= evaluationTimeUtc))
                {
                    returnedCount++;
                }

                if (returnTimeUtc <= MinimumValidReturnTimeUtc || returnTimeUtc <= evaluationTimeUtc)
                {
                    continue;
                }

                if (nextReturn is null || returnTimeUtc < nextReturn.Value.ReturnTimeUtc)
                {
                    nextReturn = new SubmarineSummaryNextReturn(
                        characterName,
                        submarine.Name,
                        returnTimeUtc);
                }
            }
        }

        return new SubmarineSummary(returnedCount, nextReturn);
    }

    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}

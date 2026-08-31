using Dalamud.Bindings.ImGui;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using XivMiniUtil.Models.Checklist;
using XivMiniUtil.Services.Checklist;

namespace XivMiniUtil.Windows.Components;

public sealed class ChecklistTab : ITabComponent
{
    private readonly Configuration _configuration;
    private readonly ChecklistService _checklistService;

    private string _newTitle = string.Empty;
    private ChecklistFrequency _newFrequency = ChecklistFrequency.Daily;
    private int _filterIndex;

    public ChecklistTab(Configuration configuration, ChecklistService checklistService)
    {
        _configuration = configuration;
        _checklistService = checklistService;
    }

    public void Draw()
    {
        if (!_configuration.ChecklistFeatureEnabled)
        {
            ImGui.Text("チェックリスト機能は無効です。SettingsのChecklistから有効化してください。");
            return;
        }

        DrawAddSection();
        ImGui.Separator();
        DrawToolbar();
        ImGui.Separator();
        DrawItems();
    }

    public void Dispose()
    {
    }

    private void DrawAddSection()
    {
        ImGui.Text("項目追加");

        ImGui.SetNextItemWidth(280f);
        ImGui.InputTextWithHint("##ChecklistNewTitle", "例: デイリールーレット", ref _newTitle, 120);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(140f);
        if (ImGui.BeginCombo("##ChecklistNewFrequency", GetFrequencyLabel(_newFrequency)))
        {
            if (ImGui.Selectable("Daily", _newFrequency == ChecklistFrequency.Daily))
            {
                _newFrequency = ChecklistFrequency.Daily;
            }

            if (ImGui.Selectable("Weekly", _newFrequency == ChecklistFrequency.Weekly))
            {
                _newFrequency = ChecklistFrequency.Weekly;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("追加"))
        {
            _checklistService.AddItem(_newTitle, _newFrequency);
            _newTitle = string.Empty;
        }
    }

    private void DrawToolbar()
    {
        ImGui.Text("表示フィルタ");
        ImGui.SameLine();

        var filters = new[] { "すべて", "未完了", "Daily", "Weekly" };
        ImGui.SetNextItemWidth(140f);
        if (ImGui.BeginCombo("##ChecklistFilter", filters[_filterIndex]))
        {
            for (var i = 0; i < filters.Length; i++)
            {
                if (ImGui.Selectable(filters[i], _filterIndex == i))
                {
                    _filterIndex = i;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Dailyをリセット"))
        {
            _checklistService.ResetItems(ChecklistFrequency.Daily);
        }

        ImGui.SameLine();
        if (ImGui.Button("Weeklyをリセット"))
        {
            _checklistService.ResetItems(ChecklistFrequency.Weekly);
        }
    }

    private void DrawItems()
    {
        var items = _checklistService.GetItems();
        if (items.Count == 0)
        {
            ImGui.TextDisabled("項目がありません。");
            return;
        }

        if (!ImGui.BeginTable("ChecklistTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            return;
        }

        ImGui.TableSetupColumn("完了");
        ImGui.TableSetupColumn("項目");
        ImGui.TableSetupColumn("周期");
        ImGui.TableSetupColumn("有効");
        ImGui.TableSetupColumn("通知");
        ImGui.TableSetupColumn("Discord");
        ImGui.TableSetupColumn("時刻");
        ImGui.TableSetupColumn("操作");
        ImGui.TableHeadersRow();

        foreach (var item in items)
        {
            if (!ShouldShow(item))
            {
                continue;
            }

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var isDone = item.IsDone;
            if (ImGui.Checkbox($"##done-{item.Id}", ref isDone))
            {
                _checklistService.SetDone(item.Id, isDone);
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.Text(item.Title);

            ImGui.TableSetColumnIndex(2);
            ImGui.Text(GetFrequencyLabel(item.Frequency));

            ImGui.TableSetColumnIndex(3);
            var isEnabled = item.IsEnabled;
            if (ImGui.Checkbox($"##enabled-{item.Id}", ref isEnabled))
            {
                _checklistService.SetEnabled(item.Id, isEnabled);
            }

            ImGui.TableSetColumnIndex(4);
            var notifyInGame = item.NotifyInGame;
            if (ImGui.Checkbox($"##notify-in-game-{item.Id}", ref notifyInGame))
            {
                _checklistService.SetNotificationChannels(item.Id, notifyInGame, item.NotifyDiscord);
            }

            ImGui.TableSetColumnIndex(5);
            var notifyDiscord = item.NotifyDiscord;
            if (ImGui.Checkbox($"##notify-discord-{item.Id}", ref notifyDiscord))
            {
                _checklistService.SetNotificationChannels(item.Id, item.NotifyInGame, notifyDiscord);
            }

            ImGui.TableSetColumnIndex(6);
            var hour = item.ReminderHour;
            var minute = item.ReminderMinute;
            ImGui.SetNextItemWidth(40f);
            var changedHour = ImGui.InputInt($"##hour-{item.Id}", ref hour);
            ImGui.SameLine();
            ImGui.Text(":");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(40f);
            var changedMinute = ImGui.InputInt($"##minute-{item.Id}", ref minute);
            if (changedHour || changedMinute)
            {
                _checklistService.SetReminder(item.Id, hour, minute);
            }

            ImGui.TableSetColumnIndex(7);
            if (ImGui.SmallButton($"削除##delete-{item.Id}"))
            {
                _checklistService.DeleteItem(item.Id);
            }
        }

        ImGui.EndTable();
    }

    private bool ShouldShow(ChecklistItem item)
    {
        return _filterIndex switch
        {
            1 => !item.IsDone,
            2 => item.Frequency == ChecklistFrequency.Daily,
            3 => item.Frequency == ChecklistFrequency.Weekly,
            _ => true,
        };
    }

    private static string GetFrequencyLabel(ChecklistFrequency frequency)
    {
        return frequency == ChecklistFrequency.Daily ? "Daily" : "Weekly";
    }
}

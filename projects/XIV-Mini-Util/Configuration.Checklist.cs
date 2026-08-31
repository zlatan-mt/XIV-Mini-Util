// Path: projects/XIV-Mini-Util/Configuration.Checklist.cs
// Description: Checklist 関連の保存設定を保持する
// Reason: Configuration の巨大化を抑え、JSON プロパティ互換を維持したまま機能別に分割するため
using XivMiniUtil.Models.Checklist;

namespace XivMiniUtil;

public sealed partial class Configuration
{
    // 日課チェックリスト設定
    public bool ChecklistFeatureEnabled { get; set; } = true;
    public bool ChecklistDiscordNotificationEnabled { get; set; } = false;
    public DayOfWeek ChecklistWeeklyResetDay { get; set; } = DayOfWeek.Tuesday;
    public List<ChecklistItem> ChecklistItems { get; set; } = new();
    public List<Guid> ChecklistDisabledItemIds { get; set; } = new();

    private void ApplyChecklistFrom(Configuration source)
    {
        ChecklistFeatureEnabled = source.ChecklistFeatureEnabled;
        ChecklistDiscordNotificationEnabled = source.ChecklistDiscordNotificationEnabled;
        ChecklistWeeklyResetDay = source.ChecklistWeeklyResetDay;
        ChecklistItems = source.ChecklistItems?
            .Select(CloneChecklistItem)
            .ToList() ?? new List<ChecklistItem>();
        ChecklistDisabledItemIds = source.ChecklistDisabledItemIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();
    }

    private bool NormalizeChecklistSettings()
    {
        var changed = false;

        if (ChecklistItems == null)
        {
            ChecklistItems = new List<ChecklistItem>();
            changed = true;
        }
        else
        {
            var normalizedItems = ChecklistItems
                .Select(CloneChecklistItem)
                .ToList();

            if (ChecklistItems.Count != normalizedItems.Count
                || ChecklistItems.Where((item, index) => !ChecklistItemEquals(item, normalizedItems[index])).Any())
            {
                ChecklistItems = normalizedItems;
                changed = true;
            }
        }

        var disabledIds = ChecklistDisabledItemIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToHashSet() ?? new HashSet<Guid>();
        foreach (var item in ChecklistItems)
        {
            if (!item.IsEnabled)
            {
                disabledIds.Add(item.Id);
            }

            if (disabledIds.Contains(item.Id) && item.IsEnabled)
            {
                item.IsEnabled = false;
                changed = true;
            }
        }

        var normalizedDisabledIds = ChecklistItems
            .Where(item => !item.IsEnabled)
            .Select(item => item.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        if (ChecklistDisabledItemIds == null || !ChecklistDisabledItemIds.OrderBy(id => id).SequenceEqual(normalizedDisabledIds))
        {
            ChecklistDisabledItemIds = normalizedDisabledIds;
            changed = true;
        }

        return changed;
    }

    private static ChecklistItem CloneChecklistItem(ChecklistItem source)
    {
        return new ChecklistItem
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Title = source.Title ?? string.Empty,
            Frequency = source.Frequency,
            IsEnabled = source.IsEnabled,
            IsDone = source.IsDone,
            NotifyInGame = source.NotifyInGame,
            NotifyDiscord = source.NotifyDiscord,
            ReminderHour = Math.Clamp(source.ReminderHour, 0, 23),
            ReminderMinute = Math.Clamp(source.ReminderMinute, 0, 59),
            LastResetKey = source.LastResetKey,
            LastReminderKey = source.LastReminderKey,
        };
    }

    private static bool ChecklistItemEquals(ChecklistItem left, ChecklistItem right)
    {
        return left.Id == right.Id
            && (left.Title ?? string.Empty) == right.Title
            && left.Frequency == right.Frequency
            && left.IsEnabled == right.IsEnabled
            && left.IsDone == right.IsDone
            && left.NotifyInGame == right.NotifyInGame
            && left.NotifyDiscord == right.NotifyDiscord
            && left.ReminderHour == right.ReminderHour
            && left.ReminderMinute == right.ReminderMinute
            && left.LastResetKey == right.LastResetKey
            && left.LastReminderKey == right.LastReminderKey;
    }
}
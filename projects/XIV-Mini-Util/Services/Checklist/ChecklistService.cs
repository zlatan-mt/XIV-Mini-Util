using Dalamud.Plugin.Services;
using XivMiniUtil.Models.Checklist;
using XivMiniUtil.Services.Notification;

namespace XivMiniUtil.Services.Checklist;

public sealed class ChecklistService : IDisposable
{
    private readonly IFramework _framework;
    private readonly Configuration _configuration;
    private readonly IChatGui _chatGui;
    private readonly DiscordService _discordService;
    private readonly IPluginLog _pluginLog;
    private readonly object _syncRoot = new();

    public ChecklistService(
        IFramework framework,
        Configuration configuration,
        IChatGui chatGui,
        DiscordService discordService,
        IPluginLog pluginLog)
    {
        _framework = framework;
        _configuration = configuration;
        _chatGui = chatGui;
        _discordService = discordService;
        _pluginLog = pluginLog;

        EnsureDefaultItems();
        _framework.Update += OnFrameworkUpdate;
    }

    public IReadOnlyList<ChecklistItem> GetItems()
    {
        lock (_syncRoot)
        {
            ApplyResetRules(DateTime.Now);
            return _configuration.ChecklistItems
                .OrderBy(item => item.Frequency)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Select(CloneItem)
                .ToList();
        }
    }

    public void AddItem(string title, ChecklistFrequency frequency)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return;
        }

        lock (_syncRoot)
        {
            var newItem = new ChecklistItem
            {
                Title = trimmedTitle,
                Frequency = frequency,
                ReminderHour = 21,
                ReminderMinute = 0,
                NotifyInGame = true,
                NotifyDiscord = false,
            };

            _configuration.ChecklistItems.Add(newItem);
            _configuration.Save();
        }
    }

    public void SetDone(Guid itemId, bool isDone)
    {
        lock (_syncRoot)
        {
            var item = FindItem(itemId);
            if (item == null)
            {
                return;
            }

            item.IsDone = isDone;
            _configuration.Save();
        }
    }

    public void SetEnabled(Guid itemId, bool isEnabled)
    {
        lock (_syncRoot)
        {
            var item = FindItem(itemId);
            if (item == null)
            {
                return;
            }

            item.IsEnabled = isEnabled;
            _configuration.Save();
        }
    }

    public void SetReminder(Guid itemId, int hour, int minute)
    {
        lock (_syncRoot)
        {
            var item = FindItem(itemId);
            if (item == null)
            {
                return;
            }

            item.ReminderHour = Math.Clamp(hour, 0, 23);
            item.ReminderMinute = Math.Clamp(minute, 0, 59);
            _configuration.Save();
        }
    }

    public void SetNotificationChannels(Guid itemId, bool inGame, bool discord)
    {
        lock (_syncRoot)
        {
            var item = FindItem(itemId);
            if (item == null)
            {
                return;
            }

            item.NotifyInGame = inGame;
            item.NotifyDiscord = discord;
            _configuration.Save();
        }
    }

    public void DeleteItem(Guid itemId)
    {
        lock (_syncRoot)
        {
            var removed = _configuration.ChecklistItems.RemoveAll(item => item.Id == itemId);
            if (removed > 0)
            {
                _configuration.Save();
            }
        }
    }

    public void ResetItems(ChecklistFrequency? frequency = null)
    {
        lock (_syncRoot)
        {
            foreach (var item in _configuration.ChecklistItems)
            {
                if (frequency.HasValue && item.Frequency != frequency.Value)
                {
                    continue;
                }

                item.IsDone = false;
                item.LastReminderKey = 0;
            }

            _configuration.Save();
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        lock (_syncRoot)
        {
            if (!_configuration.ChecklistFeatureEnabled)
            {
                return;
            }

            var now = DateTime.Now;
            var changed = ApplyResetRules(now);
            changed |= TriggerReminders(now);

            if (changed)
            {
                _configuration.Save();
            }
        }
    }

    private bool ApplyResetRules(DateTime now)
    {
        var changed = false;
        var dailyKey = BuildDailyKey(now);
        var weeklyKey = BuildWeeklyKey(now, _configuration.ChecklistWeeklyResetDay);

        foreach (var item in _configuration.ChecklistItems)
        {
            var expectedKey = item.Frequency == ChecklistFrequency.Daily ? dailyKey : weeklyKey;
            if (item.LastResetKey == expectedKey)
            {
                continue;
            }

            item.LastResetKey = expectedKey;
            item.LastReminderKey = 0;
            item.IsDone = false;
            changed = true;
        }

        return changed;
    }

    private bool TriggerReminders(DateTime now)
    {
        var changed = false;
        var dailyKey = BuildDailyKey(now);
        var weeklyKey = BuildWeeklyKey(now, _configuration.ChecklistWeeklyResetDay);

        foreach (var item in _configuration.ChecklistItems.Where(item => item.IsEnabled && !item.IsDone))
        {
            var cycleKey = item.Frequency == ChecklistFrequency.Daily ? dailyKey : weeklyKey;
            if (item.LastReminderKey == cycleKey)
            {
                continue;
            }

            var reminderTime = new TimeOnly(Math.Clamp(item.ReminderHour, 0, 23), Math.Clamp(item.ReminderMinute, 0, 59));
            if (TimeOnly.FromDateTime(now) < reminderTime)
            {
                continue;
            }

            item.LastReminderKey = cycleKey;
            _ = SendReminderAsync(item);
            changed = true;
        }

        return changed;
    }

    private async Task SendReminderAsync(ChecklistItem item)
    {
        try
        {
            var label = item.Frequency == ChecklistFrequency.Daily ? "Daily" : "Weekly";
            if (item.NotifyInGame)
            {
                _chatGui.Print($"[XIV Mini Util] [Checklist/{label}] 未完了: {item.Title}");
            }

            if (item.NotifyDiscord && _configuration.ChecklistDiscordNotificationEnabled)
            {
                await _discordService.SendChecklistReminderAsync(item.Title, label);
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "チェックリスト通知の送信に失敗しました。");
        }
    }

    private ChecklistItem? FindItem(Guid itemId)
    {
        return _configuration.ChecklistItems.FirstOrDefault(item => item.Id == itemId);
    }

    private void EnsureDefaultItems()
    {
        lock (_syncRoot)
        {
            _configuration.ChecklistItems ??= new List<ChecklistItem>();
            if (_configuration.ChecklistItems.Count > 0)
            {
                return;
            }

            _configuration.ChecklistItems.AddRange(new[]
            {
                new ChecklistItem { Title = "デイリールーレット", Frequency = ChecklistFrequency.Daily, ReminderHour = 21 },
                new ChecklistItem { Title = "リテイナーベンチャー回収", Frequency = ChecklistFrequency.Daily, ReminderHour = 21 },
                new ChecklistItem { Title = "週制限コンテンツ確認", Frequency = ChecklistFrequency.Weekly, ReminderHour = 20 },
            });

            _configuration.Save();
        }
    }

    private static long BuildDailyKey(DateTime now)
    {
        return now.Year * 10000L + now.Month * 100L + now.Day;
    }

    private static long BuildWeeklyKey(DateTime now, DayOfWeek resetDay)
    {
        var date = now.Date;
        var diff = (7 + (date.DayOfWeek - resetDay)) % 7;
        var weekStart = date.AddDays(-diff);
        return weekStart.Year * 10000L + weekStart.Month * 100L + weekStart.Day;
    }

    private static ChecklistItem CloneItem(ChecklistItem source)
    {
        return new ChecklistItem
        {
            Id = source.Id,
            Title = source.Title,
            Frequency = source.Frequency,
            IsEnabled = source.IsEnabled,
            IsDone = source.IsDone,
            NotifyInGame = source.NotifyInGame,
            NotifyDiscord = source.NotifyDiscord,
            ReminderHour = source.ReminderHour,
            ReminderMinute = source.ReminderMinute,
            LastResetKey = source.LastResetKey,
            LastReminderKey = source.LastReminderKey,
        };
    }
}

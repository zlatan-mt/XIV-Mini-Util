namespace XivMiniUtil.Models.Checklist;

public sealed class ChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public ChecklistFrequency Frequency { get; set; } = ChecklistFrequency.Daily;
    public bool IsEnabled { get; set; } = true;
    public bool IsDone { get; set; }
    public bool NotifyInGame { get; set; } = true;
    public bool NotifyDiscord { get; set; }
    public int ReminderHour { get; set; } = 21;
    public int ReminderMinute { get; set; }

    // 日次/週次のリセット単位を表すキー（ローカル時間基準）
    public long LastResetKey { get; set; }

    // 同一サイクル内での重複通知を防止するキー
    public long LastReminderKey { get; set; }
}

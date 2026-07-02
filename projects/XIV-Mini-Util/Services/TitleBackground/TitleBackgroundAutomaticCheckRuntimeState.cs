// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRuntimeState.cs
// Description: 1クリック自動確認の実行・レポート・復元状態を保持する
// Reason: 巨大サービスから同一ライフサイクルの可変状態を責務単位で分離するため

namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundAutomaticCheckRuntimeState
{
    public TitleBackgroundAutomaticCheckState State { get; set; } =
        TitleBackgroundAutomaticCheckState.Idle;

    public bool Requested { get; set; }

    public DateTimeOffset? CompletionDueAt { get; set; }

    public DateTimeOffset? LoginObservedAt { get; set; }

    public string Status { get; set; } = "自動確認は未開始です。";

    public string LastReport { get; set; } = string.Empty;

    public string PendingClipboardText { get; set; } = string.Empty;

    public bool ReportAvailabilityInitialized { get; set; }

    public bool ReportAvailable { get; set; }

    public TitleBackgroundAutomaticCheckSettingsSnapshot? SettingsSnapshot { get; set; }

    public string RunId { get; set; } = string.Empty;

    public bool SettingsRestored { get; set; } = true;
}

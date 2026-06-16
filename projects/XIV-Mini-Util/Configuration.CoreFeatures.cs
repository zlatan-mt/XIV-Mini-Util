// Path: projects/XIV-Mini-Util/Configuration.CoreFeatures.cs
// Description: Materia / Desynth / Shop / Submarine / Notification 関連の保存設定を保持する
// Reason: Configuration の巨大化を抑え、JSON プロパティ互換を維持したまま機能別に分割するため

namespace XivMiniUtil;

public sealed partial class Configuration
{
    // マテリア精製設定
    public bool MateriaExtractEnabled { get; set; } = false;
    public bool MateriaFeatureEnabled { get; set; } = true;

    // アイテム分解設定
    public int DesynthMinLevel { get; set; } = 1;
    public int DesynthMaxLevel { get; set; } = 999;
    public JobCondition DesynthJobCondition { get; set; } = JobCondition.Any;
    public bool DesynthWarningEnabled { get; set; } = true;
    public int DesynthWarningThreshold { get; set; } = 100;
    public DesynthTargetMode DesynthTargetMode { get; set; } = DesynthTargetMode.All;
    public int DesynthTargetCount { get; set; } = 1;
    public bool DesynthFeatureEnabled { get; set; } = true;

    // 販売場所検索設定
    public bool ShopSearchEchoEnabled { get; set; } = true;
    public bool ShopSearchWindowEnabled { get; set; } = true;
    public bool ShopSearchAutoTeleportEnabled { get; set; } = false;
    public List<uint> ShopSearchAreaPriority { get; set; } = new();
    public bool ShopDataVerboseLogging { get; set; } = false;
    public bool UniversalisShowTopThreeListings { get; set; } = false;
    public bool UniversalisSearchRegionWide { get; set; } = false;

    // 潜水艦探索管理設定
    public bool SubmarineTrackerEnabled { get; set; } = true;
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public bool SubmarineNotificationEnabled { get; set; } = false;
    public int NotificationRateLimitRetryMax { get; set; } = 3;

    // シャキ通知設定
    public bool DutyReadySoundNotificationEnabled { get; set; } = false;
    public int DutyReadySoundDurationSeconds { get; set; } = 10;

    public static IReadOnlyList<uint> DefaultShopSearchAreaPriority => new List<uint>
    {
        // デフォルト: 三大都市優先
        128,  // リムサ・ロミンサ：下甲板層
        129,  // リムサ・ロミンサ：上甲板層
        130,  // ウルダハ：ナル回廊
        131,  // ウルダハ：ザル回廊
        132,  // グリダニア：新市街
        133,  // グリダニア：旧市街
    };

    public void ResetShopSearchAreaPriority()
    {
        ShopSearchAreaPriority = DefaultShopSearchAreaPriority.ToList();
        Save();
    }

    private void ApplyCoreFeatureSettingsFrom(Configuration source)
    {
        MateriaExtractEnabled = source.MateriaExtractEnabled;
        MateriaFeatureEnabled = source.MateriaFeatureEnabled;
        DesynthMinLevel = Math.Clamp(source.DesynthMinLevel, 1, 999);
        DesynthMaxLevel = Math.Clamp(source.DesynthMaxLevel, 1, 999);
        DesynthJobCondition = source.DesynthJobCondition;
        DesynthWarningEnabled = source.DesynthWarningEnabled;
        DesynthWarningThreshold = Math.Clamp(source.DesynthWarningThreshold, 1, 999);
        DesynthTargetMode = source.DesynthTargetMode;
        DesynthTargetCount = Math.Clamp(source.DesynthTargetCount, 1, 999);
        DesynthFeatureEnabled = source.DesynthFeatureEnabled;
        ShopSearchEchoEnabled = source.ShopSearchEchoEnabled;
        ShopSearchWindowEnabled = source.ShopSearchWindowEnabled;
        ShopSearchAutoTeleportEnabled = source.ShopSearchAutoTeleportEnabled;
        ShopSearchAreaPriority = source.ShopSearchAreaPriority?.ToList() ?? DefaultShopSearchAreaPriority.ToList();
        ShopDataVerboseLogging = source.ShopDataVerboseLogging;
        UniversalisShowTopThreeListings = source.UniversalisShowTopThreeListings;
        UniversalisSearchRegionWide = source.UniversalisSearchRegionWide;
        SubmarineTrackerEnabled = source.SubmarineTrackerEnabled;
        DiscordWebhookUrl = source.DiscordWebhookUrl;
        SubmarineNotificationEnabled = source.SubmarineNotificationEnabled;
        NotificationRateLimitRetryMax = Math.Clamp(source.NotificationRateLimitRetryMax, 0, 10);
    }

    private bool NormalizeCoreFeatureSettings()
    {
        var changed = false;

        DesynthMinLevel = Clamp(DesynthMinLevel, 1, 999, out var minLevelChanged);
        DesynthMaxLevel = Clamp(DesynthMaxLevel, 1, 999, out var maxLevelChanged);
        DesynthWarningThreshold = Clamp(DesynthWarningThreshold, 1, 999, out var warningThresholdChanged);
        DesynthTargetCount = Clamp(DesynthTargetCount, 1, 999, out var targetCountChanged);
        NotificationRateLimitRetryMax = Clamp(NotificationRateLimitRetryMax, 0, 10, out var retryCountChanged);
        DutyReadySoundDurationSeconds = Clamp(DutyReadySoundDurationSeconds, 3, 30, out var dutyReadyDurationChanged);
        changed |= minLevelChanged;
        changed |= maxLevelChanged;
        changed |= warningThresholdChanged;
        changed |= targetCountChanged;
        changed |= retryCountChanged;
        changed |= dutyReadyDurationChanged;

        if (DesynthMinLevel > DesynthMaxLevel)
        {
            (DesynthMinLevel, DesynthMaxLevel) = (DesynthMaxLevel, DesynthMinLevel);
            changed = true;
        }

        if (ShopSearchAreaPriority == null || ShopSearchAreaPriority.Count == 0)
        {
            ShopSearchAreaPriority = DefaultShopSearchAreaPriority.ToList();
            changed = true;
        }

        return changed;
    }
}
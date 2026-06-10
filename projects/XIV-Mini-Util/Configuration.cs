// Path: projects/XIV-Mini-Util/Configuration.cs
// Description: プラグイン設定の保存と読み込みを管理する
// Reason: 再起動後もユーザー設定を維持するため
// RELEVANT FILES: projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Text;
using System.Text.Json;
using XivMiniUtil.Models.Checklist;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil;

[Serializable]
public sealed partial class Configuration : IPluginConfiguration
{
    public const int ExportVersion = 1;
    public const int CurrentVersion = 7;

    public int Version { get; set; } = CurrentVersion;

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

    // 日課チェックリスト設定
    public bool ChecklistFeatureEnabled { get; set; } = true;
    public bool ChecklistDiscordNotificationEnabled { get; set; } = false;
    public DayOfWeek ChecklistWeeklyResetDay { get; set; } = DayOfWeek.Tuesday;
    public List<ChecklistItem> ChecklistItems { get; set; } = new();
    public List<Guid> ChecklistDisabledItemIds { get; set; } = new();

    // シャキ通知設定
    public bool DutyReadySoundNotificationEnabled { get; set; } = false;
    public int DutyReadySoundDurationSeconds { get; set; } = 10;

    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        // JSONデシリアライズ後、リストが空の場合のみデフォルト値を設定
        // (Newtonsoft.Jsonは既存リストに追加するため、初期化子での設定は避ける)
        if (NormalizeAndMigrate())
        {
            Save();
        }
    }

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

    public void Save()
    {
        // 設定変更時は即時保存する
        _pluginInterface?.SavePluginConfig(this);
    }

    public void ResetShopSearchAreaPriority()
    {
        ShopSearchAreaPriority = DefaultShopSearchAreaPriority.ToList();
        Save();
    }

    public string ExportToBase64()
    {
        var envelope = new ConfigurationEnvelope(ExportVersion, BuildExportSnapshot());
        var json = JsonSerializer.Serialize(envelope);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool TryParseImport(string base64, out Configuration imported, out string errorMessage)
    {
        imported = new Configuration();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(base64))
        {
            errorMessage = "インポート文字列が空です。";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
            var envelope = JsonSerializer.Deserialize<ConfigurationEnvelope>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (envelope == null || envelope.Config == null)
            {
                errorMessage = "設定データを読み取れませんでした。";
                return false;
            }

            if (envelope.Version != ExportVersion)
            {
                errorMessage = $"バージョンが一致しません。(期待: {ExportVersion} / 取得: {envelope.Version})";
                return false;
            }

            imported = envelope.Config;
            imported.NormalizeAndMigrate();
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "Base64形式が正しくありません。";
            return false;
        }
        catch (JsonException)
        {
            errorMessage = "JSON形式が正しくありません。";
            return false;
        }
        catch (Exception)
        {
            errorMessage = "読み込み中にエラーが発生しました。";
            return false;
        }
    }

    public void ApplyFrom(Configuration source)
    {
        // 外部入力の設定値はここで安全な範囲に収める
        Version = CurrentVersion;
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
        DutyReadySoundNotificationEnabled = source.DutyReadySoundNotificationEnabled;
        DutyReadySoundDurationSeconds = Math.Clamp(source.DutyReadySoundDurationSeconds, 3, 30);
        ApplyCharaSelectFrom(source);
        ApplyTitleBackgroundFrom(source);
        NormalizeAndMigrate();
    }

    private bool NormalizeAndMigrate()
    {
        var changed = false;

        if (Version != CurrentVersion)
        {
            Version = CurrentVersion;
            changed = true;
        }

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

        changed |= NormalizeCharaSelectSettings();
        changed |= NormalizeTitleBackgroundSettings();

        return changed;
    }

    private static int Clamp(int value, int min, int max, out bool changed)
    {
        var clamped = Math.Clamp(value, min, max);
        changed = clamped != value;
        return clamped;
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

    private static float SanitizeCoordinate(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, -100000f, 100000f) : 0f;
    }

    private static float SanitizeFovY(float value)
    {
        return TitleBackgroundPreset.ClampFovY(value);
    }

    private static CharaSelectScenePlacementMode NormalizeCharaSelectScenePlacementMode(CharaSelectScenePlacementMode mode)
    {
        return Enum.IsDefined(typeof(CharaSelectScenePlacementMode), mode)
            ? mode
            : CharaSelectScenePlacementMode.ObserveOnly;
    }

    private static CharaSelectStageStrategy NormalizeCharaSelectStageStrategy(CharaSelectStageStrategy strategy)
    {
        return Enum.IsDefined(typeof(CharaSelectStageStrategy), strategy)
            ? strategy
            : CharaSelectStageStrategy.ObserveOnly;
    }

    private static CharaSelectBrightnessRating NormalizeCharaSelectBrightnessRating(CharaSelectBrightnessRating brightness)
    {
        return Enum.IsDefined(typeof(CharaSelectBrightnessRating), brightness)
            ? brightness
            : CharaSelectBrightnessRating.Unknown;
    }

    private static CharaSelectSceneBinaryResult NormalizeCharaSelectSceneBinaryResult(CharaSelectSceneBinaryResult result)
    {
        return Enum.IsDefined(typeof(CharaSelectSceneBinaryResult), result)
            ? result
            : CharaSelectSceneBinaryResult.Unknown;
    }

    private static CharaSelectSceneBrightnessResult NormalizeCharaSelectSceneBrightnessResult(CharaSelectSceneBrightnessResult result)
    {
        return Enum.IsDefined(typeof(CharaSelectSceneBrightnessResult), result)
            ? result
            : CharaSelectSceneBrightnessResult.Unknown;
    }

    private static string NormalizeShortDiagnostic(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "none";
        }

        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string NormalizeTitleBackgroundTerritoryPath(string? path)
    {
        return TitleBackgroundPathHelper.NormalizeTerritoryPathInput(path);
    }

    private static TitleBackgroundRuntimeMode NormalizeTitleBackgroundRuntimeMode(TitleBackgroundRuntimeMode mode)
    {
        if (!Enum.IsDefined(typeof(TitleBackgroundRuntimeMode), mode))
        {
            return TitleBackgroundRuntimeMode.ResolveOnly;
        }

        return TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(mode)
            ? mode
            : TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    private static TitleBackgroundCharacterSelectBackgroundMode NormalizeTitleBackgroundCharacterSelectBackgroundMode(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharacterSelectBackgroundMode), mode)
            ? mode
            : TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly;
    }

    private static TitleBackgroundCharacterSelectLightingMode NormalizeTitleBackgroundCharacterSelectLightingMode(TitleBackgroundCharacterSelectLightingMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharacterSelectLightingMode), mode)
            ? mode
            : TitleBackgroundCharacterSelectLightingMode.Default;
    }

    private static TitleBackgroundSettingsDisplayMode NormalizeTitleBackgroundSettingsDisplayMode(TitleBackgroundSettingsDisplayMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundSettingsDisplayMode), mode)
            ? mode
            : TitleBackgroundSettingsDisplayMode.Simple;
    }

    private static TitleBackgroundQuickCheckLevel NormalizeTitleBackgroundQuickCheckLevel(TitleBackgroundQuickCheckLevel level)
    {
        return Enum.IsDefined(typeof(TitleBackgroundQuickCheckLevel), level)
            ? level
            : TitleBackgroundQuickCheckLevel.NotRun;
    }

    private static TitleBackgroundCharacterSelectExpectedBrightness NormalizeTitleBackgroundCharacterSelectExpectedBrightness(TitleBackgroundCharacterSelectExpectedBrightness brightness)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharacterSelectExpectedBrightness), brightness)
            ? brightness
            : TitleBackgroundCharacterSelectExpectedBrightness.Unknown;
    }

    private static TitleBackgroundResolverMode NormalizeTitleBackgroundResolverMode(TitleBackgroundResolverMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundResolverMode), mode)
            ? mode
            : TitleBackgroundResolverMode.AutoDiagnosticOnly;
    }

    private static TitleBackgroundCharaSelectCameraFramingMode NormalizeTitleBackgroundCameraFramingMode(TitleBackgroundCharaSelectCameraFramingMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharaSelectCameraFramingMode), mode)
            ? mode
            : TitleBackgroundCharaSelectCameraFramingMode.Default;
    }

    private static TitleBackgroundCharacterVisualStatus NormalizeTitleBackgroundCharacterVisualStatus(TitleBackgroundCharacterVisualStatus status)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharacterVisualStatus), status)
            ? status
            : TitleBackgroundCharacterVisualStatus.Unknown;
    }

    private static TitleBackgroundPhase2MExperimentalApplyMode NormalizeTitleBackgroundPhase2MExperimentalApplyMode(TitleBackgroundPhase2MExperimentalApplyMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundPhase2MExperimentalApplyMode), mode)
            ? mode
            : TitleBackgroundPhase2MExperimentalApplyMode.None;
    }

    private static string NormalizeAssetPath(string? path)
    {
        return (path ?? string.Empty).Trim().Replace('\\', '/');
    }

    private static string NormalizeTitleBackgroundCharacterSelectOverrideCandidateId(string? id)
    {
        return TitleBackgroundCharacterSelectOverrideCandidateRegistry.NormalizeId(id);
    }

    private static string NormalizeTitleBackgroundManualCandidateDisplayName(string? displayName)
    {
        return (displayName ?? string.Empty).Trim();
    }

    private static string NormalizeSignature(string? signature)
    {
        return (signature ?? string.Empty).Trim();
    }

    private static bool NormalizeSignatureProperty(string signature, Action<string> setValue)
    {
        var normalized = NormalizeSignature(signature);
        if (signature == normalized)
        {
            return false;
        }

        setValue(normalized);
        return true;
    }

    private static bool DictionaryListEquals(Dictionary<ulong, List<uint>> left, Dictionary<ulong, List<uint>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var values)
                || pair.Value == null
                || !pair.Value.SequenceEqual(values))
            {
                return false;
            }
        }

        return true;
    }

    private Configuration BuildExportSnapshot()
    {
        var snapshot = new Configuration();
        snapshot.ApplyFrom(this);
        return snapshot;
    }

    private sealed record ConfigurationEnvelope(int Version, Configuration Config);
}

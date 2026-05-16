// Path: projects/XIV-Mini-Util/Configuration.cs
// Description: プラグイン設定の保存と読み込みを管理する
// Reason: 再起動後もユーザー設定を維持するため
// RELEVANT FILES: projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Text;
using System.Text.Json;
using XivMiniUtil.Models.Checklist;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil;

[Serializable]
public sealed class Configuration : IPluginConfiguration
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

    // ログイン/キャラ選択画面設定
    public bool CharaSelectEmoteEnabled { get; set; } = false;
    public bool CharaSelectPreloadTerritoryEnabled { get; set; } = false;
    public Dictionary<ulong, uint> CharaSelectSelectedEmotes { get; set; } = new();
    public Dictionary<ulong, List<uint>> CharaSelectEmotePresets { get; set; } = new();
    public Dictionary<ulong, int> CharaSelectActiveEmotePresetIndexes { get; set; } = new();
    public Dictionary<ulong, uint> CharaSelectLastRecordedEmotes { get; set; } = new();
    public Dictionary<ulong, ushort> CharaSelectVoiceIds { get; set; } = new();
    public bool CharaSelectOverrideTerritoryEnabled { get; set; } = false;
    public ushort CharaSelectOverrideTerritoryTypeId { get; set; } = 0;
    public bool CharaSelectOverridePositionEnabled { get; set; } = false;
    public float CharaSelectOverridePositionX { get; set; } = 0f;
    public float CharaSelectOverridePositionY { get; set; } = 0f;
    public float CharaSelectOverridePositionZ { get; set; } = 0f;
    public bool CharaSelectShowLastDataCenterNameEnabled { get; set; } = false;
    public string CharaSelectLastDataCenterName { get; set; } = string.Empty;

    // タイトル背景設定
    public bool TitleBackgroundOverrideEnabled { get; set; } = false;
    public bool TitleBackgroundCameraOverrideEnabled { get; set; } = false;
    public string TitleBackgroundSelectedPresetId { get; set; } = string.Empty;
    public TitleBackgroundRuntimeMode TitleBackgroundRuntimeMode { get; set; } = TitleBackgroundRuntimeMode.ResolveOnly;
    public TitleBackgroundResolverMode TitleBackgroundCreateSceneResolverMode { get; set; } = TitleBackgroundResolverMode.AutoDiagnosticOnly;
    public TitleBackgroundResolverMode TitleBackgroundLobbyUpdateResolverMode { get; set; } = TitleBackgroundResolverMode.AutoDiagnosticOnly;
    public string TitleBackgroundTerritoryPath { get; set; } = string.Empty;
    public uint TitleBackgroundTerritoryTypeId { get; set; } = 0;
    public uint TitleBackgroundLayoutTerritoryTypeId { get; set; } = 0;
    public uint TitleBackgroundLayoutLayerFilterKey { get; set; } = 0;
    public float TitleBackgroundCharacterPositionX { get; set; } = 0f;
    public float TitleBackgroundCharacterPositionY { get; set; } = 0f;
    public float TitleBackgroundCharacterPositionZ { get; set; } = 0f;
    public float TitleBackgroundCharacterRotation { get; set; } = 0f;
    public float TitleBackgroundCameraX { get; set; } = 0f;
    public float TitleBackgroundCameraY { get; set; } = 0f;
    public float TitleBackgroundCameraZ { get; set; } = 0f;
    public float TitleBackgroundFocusX { get; set; } = 0f;
    public float TitleBackgroundFocusY { get; set; } = 0f;
    public float TitleBackgroundFocusZ { get; set; } = 0f;
    public float TitleBackgroundFovY { get; set; } = TitleBackgroundPreset.DefaultFovY;
    public byte TitleBackgroundWeatherId { get; set; } = 0;
    public ushort TitleBackgroundTimeOffset { get; set; } = 0;
    public string TitleBackgroundBgmPath { get; set; } = string.Empty;
    public string TitleBackgroundCreateSceneSignature { get; set; } = "E8 ?? ?? ?? ?? 66 89 3D ?? ?? ?? ?? E9";
    public string TitleBackgroundFixOnSignature { get; set; } = "C6 81 ?? ?? ?? ?? ?? 0F 28 CB 8B 02";
    public string TitleBackgroundLobbyUpdateSignature { get; set; } = "E8 ?? ?? ?? ?? 80 BF ?? ?? ?? ?? ?? 48 8D 35";
    public string TitleBackgroundLoadLobbySceneSignature { get; set; } = "48 89 5C 24 ?? 57 48 83 EC ?? 8B D9 E8";
    public string TitleBackgroundLobbyCurrentMapSignature { get; set; } = "66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 48 8B 4B";
    public string TitleBackgroundCalculateLobbyCameraLookAtYSignature { get; set; } = "48 83 EC ?? F3 41 0F 10 01 0F 28 D1";

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
        CharaSelectEmoteEnabled = source.CharaSelectEmoteEnabled;
        CharaSelectPreloadTerritoryEnabled = source.CharaSelectPreloadTerritoryEnabled;
        CharaSelectSelectedEmotes = source.CharaSelectSelectedEmotes?
            .Where(pair => pair.Key != 0 && pair.Value != 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<ulong, uint>();
        CharaSelectEmotePresets = source.CharaSelectEmotePresets?
            .Where(pair => pair.Key != 0 && pair.Value != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToList())
            ?? new Dictionary<ulong, List<uint>>();
        CharaSelectActiveEmotePresetIndexes = source.CharaSelectActiveEmotePresetIndexes?
            .Where(pair => pair.Key != 0 && pair.Value >= 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<ulong, int>();
        CharaSelectLastRecordedEmotes = source.CharaSelectLastRecordedEmotes?
            .Where(pair => pair.Key != 0 && pair.Value != 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<ulong, uint>();
        CharaSelectVoiceIds = source.CharaSelectVoiceIds?
            .Where(pair => pair.Key != 0 && pair.Value != 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<ulong, ushort>();
        CharaSelectOverrideTerritoryEnabled = source.CharaSelectOverrideTerritoryEnabled;
        CharaSelectOverrideTerritoryTypeId = source.CharaSelectOverrideTerritoryTypeId;
        CharaSelectOverridePositionEnabled = source.CharaSelectOverridePositionEnabled;
        CharaSelectOverridePositionX = SanitizeCoordinate(source.CharaSelectOverridePositionX);
        CharaSelectOverridePositionY = SanitizeCoordinate(source.CharaSelectOverridePositionY);
        CharaSelectOverridePositionZ = SanitizeCoordinate(source.CharaSelectOverridePositionZ);
        CharaSelectShowLastDataCenterNameEnabled = source.CharaSelectShowLastDataCenterNameEnabled;
        CharaSelectLastDataCenterName = source.CharaSelectLastDataCenterName ?? string.Empty;
        TitleBackgroundOverrideEnabled = source.TitleBackgroundOverrideEnabled;
        TitleBackgroundCameraOverrideEnabled = source.TitleBackgroundCameraOverrideEnabled;
        TitleBackgroundSelectedPresetId = TitleBackgroundBuiltInPresetCatalog.NormalizeId(source.TitleBackgroundSelectedPresetId);
        TitleBackgroundRuntimeMode = NormalizeTitleBackgroundRuntimeMode(source.TitleBackgroundRuntimeMode);
        TitleBackgroundCreateSceneResolverMode = NormalizeTitleBackgroundResolverMode(source.TitleBackgroundCreateSceneResolverMode);
        TitleBackgroundLobbyUpdateResolverMode = NormalizeTitleBackgroundResolverMode(source.TitleBackgroundLobbyUpdateResolverMode);
        TitleBackgroundTerritoryPath = NormalizeTitleBackgroundTerritoryPath(source.TitleBackgroundTerritoryPath);
        TitleBackgroundTerritoryTypeId = source.TitleBackgroundTerritoryTypeId;
        TitleBackgroundLayoutTerritoryTypeId = source.TitleBackgroundLayoutTerritoryTypeId;
        TitleBackgroundLayoutLayerFilterKey = source.TitleBackgroundLayoutLayerFilterKey;
        TitleBackgroundCharacterPositionX = SanitizeCoordinate(source.TitleBackgroundCharacterPositionX);
        TitleBackgroundCharacterPositionY = SanitizeCoordinate(source.TitleBackgroundCharacterPositionY);
        TitleBackgroundCharacterPositionZ = SanitizeCoordinate(source.TitleBackgroundCharacterPositionZ);
        TitleBackgroundCharacterRotation = SanitizeCoordinate(source.TitleBackgroundCharacterRotation);
        TitleBackgroundCameraX = SanitizeCoordinate(source.TitleBackgroundCameraX);
        TitleBackgroundCameraY = SanitizeCoordinate(source.TitleBackgroundCameraY);
        TitleBackgroundCameraZ = SanitizeCoordinate(source.TitleBackgroundCameraZ);
        TitleBackgroundFocusX = SanitizeCoordinate(source.TitleBackgroundFocusX);
        TitleBackgroundFocusY = SanitizeCoordinate(source.TitleBackgroundFocusY);
        TitleBackgroundFocusZ = SanitizeCoordinate(source.TitleBackgroundFocusZ);
        TitleBackgroundFovY = SanitizeFovY(source.TitleBackgroundFovY);
        TitleBackgroundWeatherId = source.TitleBackgroundWeatherId;
        TitleBackgroundTimeOffset = source.TitleBackgroundTimeOffset;
        TitleBackgroundBgmPath = NormalizeAssetPath(source.TitleBackgroundBgmPath);
        TitleBackgroundCreateSceneSignature = NormalizeSignature(source.TitleBackgroundCreateSceneSignature);
        TitleBackgroundFixOnSignature = NormalizeSignature(source.TitleBackgroundFixOnSignature);
        TitleBackgroundLobbyUpdateSignature = NormalizeSignature(source.TitleBackgroundLobbyUpdateSignature);
        TitleBackgroundLoadLobbySceneSignature = NormalizeSignature(source.TitleBackgroundLoadLobbySceneSignature);
        TitleBackgroundLobbyCurrentMapSignature = NormalizeSignature(source.TitleBackgroundLobbyCurrentMapSignature);
        TitleBackgroundCalculateLobbyCameraLookAtYSignature = NormalizeSignature(source.TitleBackgroundCalculateLobbyCameraLookAtYSignature);
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

        if (CharaSelectSelectedEmotes == null)
        {
            CharaSelectSelectedEmotes = new Dictionary<ulong, uint>();
            changed = true;
        }
        else
        {
            var normalizedEmotes = CharaSelectSelectedEmotes
                .Where(pair => pair.Key != 0 && pair.Value != 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            if (normalizedEmotes.Count != CharaSelectSelectedEmotes.Count)
            {
                CharaSelectSelectedEmotes = normalizedEmotes;
                changed = true;
            }
        }

        if (CharaSelectEmotePresets == null)
        {
            CharaSelectEmotePresets = new Dictionary<ulong, List<uint>>();
            changed = true;
        }

        if (CharaSelectActiveEmotePresetIndexes == null)
        {
            CharaSelectActiveEmotePresetIndexes = new Dictionary<ulong, int>();
            changed = true;
        }

        var normalizedPresets = CharaSelectEmotePresets
            .Where(pair => pair.Key != 0)
            .Select(pair =>
            {
                var emotes = (pair.Value ?? new List<uint>())
                    .Where(emoteId => emoteId != 0)
                    .Distinct()
                    .ToList();
                return new KeyValuePair<ulong, List<uint>>(pair.Key, emotes);
            })
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var pair in CharaSelectSelectedEmotes)
        {
            if (!normalizedPresets.ContainsKey(pair.Key) && pair.Value != 0)
            {
                normalizedPresets[pair.Key] = new List<uint> { pair.Value };
            }
        }

        if (!DictionaryListEquals(CharaSelectEmotePresets, normalizedPresets))
        {
            CharaSelectEmotePresets = normalizedPresets;
            changed = true;
        }

        var normalizedActiveIndexes = CharaSelectActiveEmotePresetIndexes
            .Where(pair => pair.Key != 0 && CharaSelectEmotePresets.TryGetValue(pair.Key, out var emotes) && emotes.Count > 0)
            .ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var emotes = CharaSelectEmotePresets[pair.Key];
                    return Math.Clamp(pair.Value, 0, emotes.Count - 1);
                });

        foreach (var key in CharaSelectEmotePresets.Keys)
        {
            normalizedActiveIndexes.TryAdd(key, 0);
        }

        if (!CharaSelectActiveEmotePresetIndexes.OrderBy(pair => pair.Key).SequenceEqual(normalizedActiveIndexes.OrderBy(pair => pair.Key)))
        {
            CharaSelectActiveEmotePresetIndexes = normalizedActiveIndexes;
            changed = true;
        }

        if (CharaSelectLastRecordedEmotes == null)
        {
            CharaSelectLastRecordedEmotes = new Dictionary<ulong, uint>();
            changed = true;
        }
        else
        {
            var normalizedLastRecorded = CharaSelectLastRecordedEmotes
                .Where(pair => pair.Key != 0 && pair.Value != 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            if (!CharaSelectLastRecordedEmotes.OrderBy(pair => pair.Key).SequenceEqual(normalizedLastRecorded.OrderBy(pair => pair.Key)))
            {
                CharaSelectLastRecordedEmotes = normalizedLastRecorded;
                changed = true;
            }
        }

        if (CharaSelectVoiceIds == null)
        {
            CharaSelectVoiceIds = new Dictionary<ulong, ushort>();
            changed = true;
        }
        else
        {
            var normalizedVoiceIds = CharaSelectVoiceIds
                .Where(pair => pair.Key != 0 && pair.Value != 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            if (normalizedVoiceIds.Count != CharaSelectVoiceIds.Count)
            {
                CharaSelectVoiceIds = normalizedVoiceIds;
                changed = true;
            }
        }

        var normalizedLastDataCenterName = (CharaSelectLastDataCenterName ?? string.Empty).Trim();
        if (CharaSelectLastDataCenterName != normalizedLastDataCenterName)
        {
            CharaSelectLastDataCenterName = normalizedLastDataCenterName;
            changed = true;
        }

        var normalizedX = SanitizeCoordinate(CharaSelectOverridePositionX);
        var normalizedY = SanitizeCoordinate(CharaSelectOverridePositionY);
        var normalizedZ = SanitizeCoordinate(CharaSelectOverridePositionZ);
        if (CharaSelectOverridePositionX != normalizedX
            || CharaSelectOverridePositionY != normalizedY
            || CharaSelectOverridePositionZ != normalizedZ)
        {
            CharaSelectOverridePositionX = normalizedX;
            CharaSelectOverridePositionY = normalizedY;
            CharaSelectOverridePositionZ = normalizedZ;
            changed = true;
        }

        var normalizedTitleTerritoryPath = NormalizeTitleBackgroundTerritoryPath(TitleBackgroundTerritoryPath);
        if (TitleBackgroundTerritoryPath != normalizedTitleTerritoryPath)
        {
            TitleBackgroundTerritoryPath = normalizedTitleTerritoryPath;
            changed = true;
        }

        var normalizedSelectedPresetId = TitleBackgroundBuiltInPresetCatalog.NormalizeId(TitleBackgroundSelectedPresetId);
        if (TitleBackgroundSelectedPresetId != normalizedSelectedPresetId)
        {
            TitleBackgroundSelectedPresetId = normalizedSelectedPresetId;
            changed = true;
        }

        var normalizedTitleRuntimeMode = NormalizeTitleBackgroundRuntimeMode(TitleBackgroundRuntimeMode);
        if (TitleBackgroundRuntimeMode != normalizedTitleRuntimeMode)
        {
            TitleBackgroundRuntimeMode = normalizedTitleRuntimeMode;
            changed = true;
        }

        var normalizedCreateSceneResolverMode = NormalizeTitleBackgroundResolverMode(TitleBackgroundCreateSceneResolverMode);
        var normalizedLobbyUpdateResolverMode = NormalizeTitleBackgroundResolverMode(TitleBackgroundLobbyUpdateResolverMode);
        if (TitleBackgroundCreateSceneResolverMode != normalizedCreateSceneResolverMode
            || TitleBackgroundLobbyUpdateResolverMode != normalizedLobbyUpdateResolverMode)
        {
            TitleBackgroundCreateSceneResolverMode = normalizedCreateSceneResolverMode;
            TitleBackgroundLobbyUpdateResolverMode = normalizedLobbyUpdateResolverMode;
            changed = true;
        }

        var normalizedTitleCharacterPositionX = SanitizeCoordinate(TitleBackgroundCharacterPositionX);
        var normalizedTitleCharacterPositionY = SanitizeCoordinate(TitleBackgroundCharacterPositionY);
        var normalizedTitleCharacterPositionZ = SanitizeCoordinate(TitleBackgroundCharacterPositionZ);
        var normalizedTitleCharacterRotation = SanitizeCoordinate(TitleBackgroundCharacterRotation);
        if (TitleBackgroundCharacterPositionX != normalizedTitleCharacterPositionX
            || TitleBackgroundCharacterPositionY != normalizedTitleCharacterPositionY
            || TitleBackgroundCharacterPositionZ != normalizedTitleCharacterPositionZ
            || TitleBackgroundCharacterRotation != normalizedTitleCharacterRotation)
        {
            TitleBackgroundCharacterPositionX = normalizedTitleCharacterPositionX;
            TitleBackgroundCharacterPositionY = normalizedTitleCharacterPositionY;
            TitleBackgroundCharacterPositionZ = normalizedTitleCharacterPositionZ;
            TitleBackgroundCharacterRotation = normalizedTitleCharacterRotation;
            changed = true;
        }

        var normalizedTitleCameraX = SanitizeCoordinate(TitleBackgroundCameraX);
        var normalizedTitleCameraY = SanitizeCoordinate(TitleBackgroundCameraY);
        var normalizedTitleCameraZ = SanitizeCoordinate(TitleBackgroundCameraZ);
        var normalizedTitleFocusX = SanitizeCoordinate(TitleBackgroundFocusX);
        var normalizedTitleFocusY = SanitizeCoordinate(TitleBackgroundFocusY);
        var normalizedTitleFocusZ = SanitizeCoordinate(TitleBackgroundFocusZ);
        if (TitleBackgroundCameraX != normalizedTitleCameraX
            || TitleBackgroundCameraY != normalizedTitleCameraY
            || TitleBackgroundCameraZ != normalizedTitleCameraZ
            || TitleBackgroundFocusX != normalizedTitleFocusX
            || TitleBackgroundFocusY != normalizedTitleFocusY
            || TitleBackgroundFocusZ != normalizedTitleFocusZ)
        {
            TitleBackgroundCameraX = normalizedTitleCameraX;
            TitleBackgroundCameraY = normalizedTitleCameraY;
            TitleBackgroundCameraZ = normalizedTitleCameraZ;
            TitleBackgroundFocusX = normalizedTitleFocusX;
            TitleBackgroundFocusY = normalizedTitleFocusY;
            TitleBackgroundFocusZ = normalizedTitleFocusZ;
            changed = true;
        }

        var normalizedTitleFovY = SanitizeFovY(TitleBackgroundFovY);
        if (TitleBackgroundFovY != normalizedTitleFovY)
        {
            TitleBackgroundFovY = normalizedTitleFovY;
            changed = true;
        }

        var normalizedTitleBgmPath = NormalizeAssetPath(TitleBackgroundBgmPath);
        if (TitleBackgroundBgmPath != normalizedTitleBgmPath)
        {
            TitleBackgroundBgmPath = normalizedTitleBgmPath;
            changed = true;
        }

        changed |= NormalizeSignatureProperty(TitleBackgroundCreateSceneSignature, value => TitleBackgroundCreateSceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundFixOnSignature, value => TitleBackgroundFixOnSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyUpdateSignature, value => TitleBackgroundLobbyUpdateSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLoadLobbySceneSignature, value => TitleBackgroundLoadLobbySceneSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundLobbyCurrentMapSignature, value => TitleBackgroundLobbyCurrentMapSignature = value);
        changed |= NormalizeSignatureProperty(TitleBackgroundCalculateLobbyCameraLookAtYSignature, value => TitleBackgroundCalculateLobbyCameraLookAtYSignature = value);
        changed |= TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(this);

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

    private static TitleBackgroundResolverMode NormalizeTitleBackgroundResolverMode(TitleBackgroundResolverMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundResolverMode), mode)
            ? mode
            : TitleBackgroundResolverMode.AutoDiagnosticOnly;
    }

    private static string NormalizeAssetPath(string? path)
    {
        return (path ?? string.Empty).Trim().Replace('\\', '/');
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

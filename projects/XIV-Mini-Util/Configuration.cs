// Path: projects/XIV-Mini-Util/Configuration.cs
// Description: プラグイン設定の保存と読み込みを管理する
// Reason: 再起動後もユーザー設定を維持するため
// RELEVANT FILES: projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Configuration;
using Dalamud.Plugin;
using System.Text;
using System.Text.Json;

namespace XivMiniUtil;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const int ExportVersion = 1;

    public int Version { get; set; } = 1;

    // マテリア精製設定
    public bool MateriaExtractEnabled { get; set; } = false;

    // アイテム分解設定
    public int DesynthMinLevel { get; set; } = 1;
    public int DesynthMaxLevel { get; set; } = 999;
    public JobCondition DesynthJobCondition { get; set; } = JobCondition.Any;
    public bool DesynthWarningEnabled { get; set; } = true;
    public int DesynthWarningThreshold { get; set; } = 100;
    public DesynthTargetMode DesynthTargetMode { get; set; } = DesynthTargetMode.All;
    public int DesynthTargetCount { get; set; } = 1;

    // 販売場所検索設定
    public bool ShopSearchEchoEnabled { get; set; } = true;
    public bool ShopSearchWindowEnabled { get; set; } = true;
    public bool ShopSearchAutoTeleportEnabled { get; set; } = false;
    public List<uint> ShopSearchAreaPriority { get; set; } = new();
    public bool ShopDataVerboseLogging { get; set; } = false;

    // 潜水艦探索管理設定
    public bool SubmarineTrackerEnabled { get; set; } = true;
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public bool SubmarineNotificationEnabled { get; set; } = false;
    public int NotificationRateLimitRetryMax { get; set; } = 3;

    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;

        // JSONデシリアライズ後、リストが空の場合のみデフォルト値を設定
        // (Newtonsoft.Jsonは既存リストに追加するため、初期化子での設定は避ける)
        if (ShopSearchAreaPriority.Count == 0)
        {
            ShopSearchAreaPriority = DefaultShopSearchAreaPriority.ToList();
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
            imported.ShopSearchAreaPriority ??= DefaultShopSearchAreaPriority.ToList();
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
        Version = source.Version;
        MateriaExtractEnabled = source.MateriaExtractEnabled;
        DesynthMinLevel = Math.Clamp(source.DesynthMinLevel, 1, 999);
        DesynthMaxLevel = Math.Clamp(source.DesynthMaxLevel, 1, 999);
        DesynthJobCondition = source.DesynthJobCondition;
        DesynthWarningEnabled = source.DesynthWarningEnabled;
        DesynthWarningThreshold = Math.Clamp(source.DesynthWarningThreshold, 1, 999);
        DesynthTargetMode = source.DesynthTargetMode;
        DesynthTargetCount = Math.Clamp(source.DesynthTargetCount, 1, 999);
        ShopSearchEchoEnabled = source.ShopSearchEchoEnabled;
        ShopSearchWindowEnabled = source.ShopSearchWindowEnabled;
        ShopSearchAutoTeleportEnabled = source.ShopSearchAutoTeleportEnabled;
        ShopSearchAreaPriority = source.ShopSearchAreaPriority?.ToList() ?? DefaultShopSearchAreaPriority.ToList();
        ShopDataVerboseLogging = source.ShopDataVerboseLogging;
        SubmarineTrackerEnabled = source.SubmarineTrackerEnabled;
        DiscordWebhookUrl = source.DiscordWebhookUrl;
        SubmarineNotificationEnabled = source.SubmarineNotificationEnabled;
        NotificationRateLimitRetryMax = Math.Clamp(source.NotificationRateLimitRetryMax, 0, 10);
    }

    private Configuration BuildExportSnapshot()
    {
        var snapshot = new Configuration();
        snapshot.ApplyFrom(this);
        return snapshot;
    }

    private sealed record ConfigurationEnvelope(int Version, Configuration Config);
}

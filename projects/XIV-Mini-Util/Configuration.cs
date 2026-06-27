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

    public void Save()
    {
        // 設定変更時は即時保存する
        _pluginInterface?.SavePluginConfig(this);
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
        ApplyCoreFeatureSettingsFrom(source);
        ApplyChecklistFrom(source);
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

        changed |= NormalizeCoreFeatureSettings();
        changed |= NormalizeChecklistSettings();
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
        return mode == TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics
            ? TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics
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

    private static TitleBackgroundCharacterPlacementExperimentalApplyMode NormalizeTitleBackgroundCharacterPlacementExperimentalApplyMode(TitleBackgroundCharacterPlacementExperimentalApplyMode mode)
    {
        return Enum.IsDefined(typeof(TitleBackgroundCharacterPlacementExperimentalApplyMode), mode)
            ? mode
            : TitleBackgroundCharacterPlacementExperimentalApplyMode.None;
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

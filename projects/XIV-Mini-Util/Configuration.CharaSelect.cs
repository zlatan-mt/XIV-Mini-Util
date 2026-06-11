// Path: projects/XIV-Mini-Util/Configuration.CharaSelect.cs
// Description: CharaSelect 関連の保存設定を保持する
// Reason: Configuration の巨大化を抑え、JSON プロパティ互換を維持したまま機能別に分割するため
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil;

public sealed partial class Configuration
{
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
    public bool CharaSelectSceneCompositionEnabled { get; set; } = false;
    public string CharaSelectSceneProfileId { get; set; } = CharaSelectSceneProfileRegistry.DefaultProfileId;
    public bool CharaSelectSceneUseProfileTerritory { get; set; } = true;
    public bool CharaSelectSceneUseProfilePosition { get; set; } = false;
    public bool CharaSelectSceneUseSavedEmote { get; set; } = true;
    public CharaSelectScenePlacementMode CharaSelectScenePlacementMode { get; set; } = CharaSelectScenePlacementMode.ObserveOnly;
    public CharaSelectStageStrategy CharaSelectSceneStageStrategy { get; set; } = CharaSelectStageStrategy.ObserveOnly;
    public string CharaSelectSceneStageStrategyLastResult { get; set; } = "none";
    public string CharaSelectSceneStageStrategyLastReason { get; set; } = "none";
    public CharaSelectBrightnessRating CharaSelectSceneExpectedBrightness { get; set; } = CharaSelectBrightnessRating.Unknown;
    public CharaSelectSceneBinaryResult LastSceneProfileCharacterVisibleResult { get; set; } = CharaSelectSceneBinaryResult.Unknown;
    public CharaSelectSceneBinaryResult LastSceneProfileLocationChangedResult { get; set; } = CharaSelectSceneBinaryResult.Unknown;
    public CharaSelectSceneBinaryResult LastSceneProfileEmotePlayedResult { get; set; } = CharaSelectSceneBinaryResult.Unknown;
    public CharaSelectSceneBrightnessResult LastSceneProfileBrightnessResult { get; set; } = CharaSelectSceneBrightnessResult.Unknown;
    public bool CharaSelectShowLastDataCenterNameEnabled { get; set; } = false;
    public string CharaSelectLastDataCenterName { get; set; } = string.Empty;

    private void ApplyCharaSelectFrom(Configuration source)
    {
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
        CharaSelectSceneCompositionEnabled = source.CharaSelectSceneCompositionEnabled;
        CharaSelectSceneProfileId = CharaSelectSceneProfileRegistry.NormalizeId(source.CharaSelectSceneProfileId);
        CharaSelectSceneUseProfileTerritory = source.CharaSelectSceneUseProfileTerritory;
        CharaSelectSceneUseProfilePosition = source.CharaSelectSceneUseProfilePosition;
        CharaSelectSceneUseSavedEmote = source.CharaSelectSceneUseSavedEmote;
        CharaSelectScenePlacementMode = NormalizeCharaSelectScenePlacementMode(source.CharaSelectScenePlacementMode);
        CharaSelectSceneStageStrategy = NormalizeCharaSelectStageStrategy(source.CharaSelectSceneStageStrategy);
        CharaSelectSceneStageStrategyLastResult = NormalizeShortDiagnostic(source.CharaSelectSceneStageStrategyLastResult);
        CharaSelectSceneStageStrategyLastReason = NormalizeShortDiagnostic(source.CharaSelectSceneStageStrategyLastReason);
        CharaSelectSceneExpectedBrightness = NormalizeCharaSelectBrightnessRating(source.CharaSelectSceneExpectedBrightness);
        LastSceneProfileCharacterVisibleResult = NormalizeCharaSelectSceneBinaryResult(source.LastSceneProfileCharacterVisibleResult);
        LastSceneProfileLocationChangedResult = NormalizeCharaSelectSceneBinaryResult(source.LastSceneProfileLocationChangedResult);
        LastSceneProfileEmotePlayedResult = NormalizeCharaSelectSceneBinaryResult(source.LastSceneProfileEmotePlayedResult);
        LastSceneProfileBrightnessResult = NormalizeCharaSelectSceneBrightnessResult(source.LastSceneProfileBrightnessResult);
        CharaSelectShowLastDataCenterNameEnabled = source.CharaSelectShowLastDataCenterNameEnabled;
        CharaSelectLastDataCenterName = source.CharaSelectLastDataCenterName ?? string.Empty;
    }

    private bool NormalizeCharaSelectSettings()
    {
        var changed = false;

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

        var normalizedSceneProfileId = CharaSelectSceneProfileRegistry.NormalizeId(CharaSelectSceneProfileId);
        if (CharaSelectSceneProfileId != normalizedSceneProfileId)
        {
            CharaSelectSceneProfileId = normalizedSceneProfileId;
            changed = true;
        }

        var normalizedScenePlacementMode = NormalizeCharaSelectScenePlacementMode(CharaSelectScenePlacementMode);
        if (CharaSelectScenePlacementMode != normalizedScenePlacementMode)
        {
            CharaSelectScenePlacementMode = normalizedScenePlacementMode;
            changed = true;
        }

        var normalizedStageStrategy = NormalizeCharaSelectStageStrategy(CharaSelectSceneStageStrategy);
        if (CharaSelectSceneStageStrategy != normalizedStageStrategy)
        {
            CharaSelectSceneStageStrategy = normalizedStageStrategy;
            changed = true;
        }

        var normalizedStageStrategyLastResult = NormalizeShortDiagnostic(CharaSelectSceneStageStrategyLastResult);
        var normalizedStageStrategyLastReason = NormalizeShortDiagnostic(CharaSelectSceneStageStrategyLastReason);
        if (CharaSelectSceneStageStrategyLastResult != normalizedStageStrategyLastResult
            || CharaSelectSceneStageStrategyLastReason != normalizedStageStrategyLastReason)
        {
            CharaSelectSceneStageStrategyLastResult = normalizedStageStrategyLastResult;
            CharaSelectSceneStageStrategyLastReason = normalizedStageStrategyLastReason;
            changed = true;
        }

        var normalizedSceneBrightness = NormalizeCharaSelectBrightnessRating(CharaSelectSceneExpectedBrightness);
        if (CharaSelectSceneExpectedBrightness != normalizedSceneBrightness)
        {
            CharaSelectSceneExpectedBrightness = normalizedSceneBrightness;
            changed = true;
        }

        var normalizedCharacterVisibleResult = NormalizeCharaSelectSceneBinaryResult(LastSceneProfileCharacterVisibleResult);
        var normalizedLocationChangedResult = NormalizeCharaSelectSceneBinaryResult(LastSceneProfileLocationChangedResult);
        var normalizedEmotePlayedResult = NormalizeCharaSelectSceneBinaryResult(LastSceneProfileEmotePlayedResult);
        var normalizedBrightnessResult = NormalizeCharaSelectSceneBrightnessResult(LastSceneProfileBrightnessResult);
        if (LastSceneProfileCharacterVisibleResult != normalizedCharacterVisibleResult
            || LastSceneProfileLocationChangedResult != normalizedLocationChangedResult
            || LastSceneProfileEmotePlayedResult != normalizedEmotePlayedResult
            || LastSceneProfileBrightnessResult != normalizedBrightnessResult)
        {
            LastSceneProfileCharacterVisibleResult = normalizedCharacterVisibleResult;
            LastSceneProfileLocationChangedResult = normalizedLocationChangedResult;
            LastSceneProfileEmotePlayedResult = normalizedEmotePlayedResult;
            LastSceneProfileBrightnessResult = normalizedBrightnessResult;
            changed = true;
        }

        return changed;
    }
}

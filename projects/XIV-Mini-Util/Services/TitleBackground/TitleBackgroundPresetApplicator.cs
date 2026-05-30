// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPresetApplicator.cs
// Description: タイトル背景 preset の検証と Configuration 展開を行う純粋ロジック
// Reason: preset 適用を all-or-nothing にし、UI/native hook から独立して検証するため
namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundPresetApplicator
{
    public static bool TryApplyPreset(
        Configuration configuration,
        TitleBackgroundPreset preset,
        string selectedPresetId,
        Func<string, bool> fileExists,
        out string errorMessage)
    {
        var normalized = preset.Normalize();
        if (!normalized.Validate(out errorMessage))
        {
            return false;
        }

        var lvbPath = TitleBackgroundPathHelper.BuildLvbPath(normalized.TerritoryPath);
        try
        {
            if (!fileExists(lvbPath))
            {
                errorMessage = $"LVB が見つかりません: {lvbPath}";
                return false;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"LVB validation failed: {ex.Message}";
            return false;
        }

        ApplyPresetFields(configuration, normalized, TitleBackgroundBuiltInPresetCatalog.NormalizeId(selectedPresetId));
        errorMessage = string.Empty;
        return true;
    }

    public static bool ClearInvalidSelectedPreset(Configuration configuration)
    {
        var selectedPresetId = TitleBackgroundBuiltInPresetCatalog.NormalizeId(configuration.TitleBackgroundSelectedPresetId);
        if (configuration.TitleBackgroundSelectedPresetId != selectedPresetId)
        {
            configuration.TitleBackgroundSelectedPresetId = selectedPresetId;
        }

        if (string.IsNullOrEmpty(selectedPresetId))
        {
            return false;
        }

        if (!TitleBackgroundBuiltInPresetCatalog.TryGetById(selectedPresetId, out var entry)
            || !IsConfigurationSynchronizedWithPreset(configuration, entry.Preset))
        {
            configuration.TitleBackgroundSelectedPresetId = string.Empty;
            return true;
        }

        return false;
    }

    public static bool IsConfigurationSynchronizedWithPreset(Configuration configuration, TitleBackgroundPreset preset)
    {
        var normalized = preset.Normalize();
        return configuration.TitleBackgroundTerritoryPath == normalized.TerritoryPath
            && configuration.TitleBackgroundTerritoryTypeId == normalized.TerritoryTypeId
            && configuration.TitleBackgroundLayoutTerritoryTypeId == normalized.LayoutTerritoryTypeId
            && configuration.TitleBackgroundLayoutLayerFilterKey == normalized.LayoutLayerFilterKey
            && configuration.TitleBackgroundCharacterPositionX == normalized.CharacterPosition.X
            && configuration.TitleBackgroundCharacterPositionY == normalized.CharacterPosition.Y
            && configuration.TitleBackgroundCharacterPositionZ == normalized.CharacterPosition.Z
            && configuration.TitleBackgroundCharacterRotation == normalized.CharacterRotation
            && configuration.TitleBackgroundCameraX == normalized.CameraX
            && configuration.TitleBackgroundCameraY == normalized.CameraY
            && configuration.TitleBackgroundCameraZ == normalized.CameraZ
            && configuration.TitleBackgroundFocusX == normalized.FocusX
            && configuration.TitleBackgroundFocusY == normalized.FocusY
            && configuration.TitleBackgroundFocusZ == normalized.FocusZ
            && configuration.TitleBackgroundFovY == normalized.FovY;
    }

    public static void ApplyDebugPreset(Configuration configuration, TitleBackgroundPreset preset)
    {
        ApplyPresetFields(configuration, preset.Normalize(), string.Empty);
    }

    private static void ApplyPresetFields(Configuration configuration, TitleBackgroundPreset normalized, string selectedPresetId)
    {
        configuration.TitleBackgroundSelectedPresetId = selectedPresetId;
        configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
        configuration.TitleBackgroundTerritoryPath = normalized.TerritoryPath;
        configuration.TitleBackgroundTerritoryTypeId = normalized.TerritoryTypeId;
        configuration.TitleBackgroundLayoutTerritoryTypeId = normalized.LayoutTerritoryTypeId;
        configuration.TitleBackgroundLayoutLayerFilterKey = normalized.LayoutLayerFilterKey;
        configuration.TitleBackgroundCharacterPositionX = normalized.CharacterPosition.X;
        configuration.TitleBackgroundCharacterPositionY = normalized.CharacterPosition.Y;
        configuration.TitleBackgroundCharacterPositionZ = normalized.CharacterPosition.Z;
        configuration.TitleBackgroundCharacterRotation = normalized.CharacterRotation;
        configuration.TitleBackgroundCameraX = normalized.CameraX;
        configuration.TitleBackgroundCameraY = normalized.CameraY;
        configuration.TitleBackgroundCameraZ = normalized.CameraZ;
        configuration.TitleBackgroundFocusX = normalized.FocusX;
        configuration.TitleBackgroundFocusY = normalized.FocusY;
        configuration.TitleBackgroundFocusZ = normalized.FocusZ;
        configuration.TitleBackgroundFovY = normalized.FovY;
    }
}

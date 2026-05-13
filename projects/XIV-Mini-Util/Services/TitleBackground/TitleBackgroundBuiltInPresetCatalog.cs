// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundBuiltInPresetCatalog.cs
// Description: タイトル背景の built-in preset catalog を管理する
// Reason: 表示名ではなく安定 ID で preset を選択・保存するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundBuiltInPresetEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required TitleBackgroundPreset Preset { get; init; }
}

internal static class TitleBackgroundBuiltInPresetCatalog
{
    public static IReadOnlyList<TitleBackgroundBuiltInPresetEntry> Presets { get; } =
    [
        // Verified preset values are intentionally added one by one after live visual checks.
        // TODO(feasibility): Add the first verified preset here after Mona confirms the final
        // CharaSelect composition at least three times with the same values.
        //
        // new TitleBackgroundBuiltInPresetEntry
        // {
        //     Id = "verified-001",
        //     DisplayName = "Verified 001",
        //     Preset = new TitleBackgroundPreset
        //     {
        //         Name = "Verified 001",
        //         TerritoryPath = "ffxiv/.../level/...",
        //         TerritoryTypeId = 0,
        //         LayoutTerritoryTypeId = 0,
        //         LayoutLayerFilterKey = 0,
        //         CameraX = 0f,
        //         CameraY = 0f,
        //         CameraZ = 0f,
        //         FocusX = 0f,
        //         FocusY = 0f,
        //         FocusZ = 0f,
        //         FovY = TitleBackgroundPreset.DefaultFovY,
        //     }.Normalize(),
        // },
    ];

    public static bool TryGetById(string? id, out TitleBackgroundBuiltInPresetEntry entry)
    {
        var normalizedId = NormalizeId(id);
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Id, normalizedId, StringComparison.Ordinal))
            {
                entry = preset;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    public static string NormalizeId(string? id)
    {
        return (id ?? string.Empty).Trim();
    }
}

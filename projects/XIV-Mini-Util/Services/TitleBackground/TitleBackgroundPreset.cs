// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPreset.cs
// Description: タイトル背景 preset DTO と純粋な正規化/検証ロジック
// Reason: UI/設定/native hook から独立して camera/focus/FOV の安全化を検証するため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundPreset
{
    public const float DefaultFovY = 1f;
    public const float MinFovY = 0.01f;
    public const float MaxFovY = 180f;
    private const float MinCoordinate = -100000f;
    private const float MaxCoordinate = 100000f;

    public string Name { get; set; } = string.Empty;
    public string TerritoryPath { get; set; } = string.Empty;
    public uint TerritoryTypeId { get; set; }
    public uint LayoutTerritoryTypeId { get; set; }
    public uint LayoutLayerFilterKey { get; set; }
    public Vector3 CharacterPosition { get; set; }
    public float CharacterRotation { get; set; }

    public float CameraX { get; set; }
    public float CameraY { get; set; }
    public float CameraZ { get; set; }

    public float FocusX { get; set; }
    public float FocusY { get; set; }
    public float FocusZ { get; set; }

    public float FovY { get; set; } = DefaultFovY;

    public byte WeatherId { get; set; }
    public ushort TimeOffset { get; set; }
    public string BgmPath { get; set; } = string.Empty;

    public TitleBackgroundPreset Normalize()
    {
        return new TitleBackgroundPreset
        {
            Name = Name?.Trim() ?? string.Empty,
            TerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(TerritoryPath),
            TerritoryTypeId = TerritoryTypeId,
            LayoutTerritoryTypeId = LayoutTerritoryTypeId,
            LayoutLayerFilterKey = LayoutLayerFilterKey,
            CharacterPosition = SanitizeVector(CharacterPosition),
            CharacterRotation = SanitizeCoordinate(CharacterRotation),
            CameraX = SanitizeCoordinate(CameraX),
            CameraY = SanitizeCoordinate(CameraY),
            CameraZ = SanitizeCoordinate(CameraZ),
            FocusX = SanitizeCoordinate(FocusX),
            FocusY = SanitizeCoordinate(FocusY),
            FocusZ = SanitizeCoordinate(FocusZ),
            FovY = ClampFovY(FovY),
            WeatherId = WeatherId,
            TimeOffset = TimeOffset,
            BgmPath = BgmPath?.Trim() ?? string.Empty,
        };
    }

    public bool Validate(out string errorMessage)
    {
        var normalized = Normalize();
        if (!TitleBackgroundPathHelper.TryNormalizeAndValidateTerritoryPath(
            normalized.TerritoryPath,
            out _,
            out errorMessage))
        {
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static float ClampFovY(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, MinFovY, MaxFovY) : DefaultFovY;
    }

    public static float SanitizeCoordinate(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, MinCoordinate, MaxCoordinate) : 0f;
    }

    private static Vector3 SanitizeVector(Vector3 value)
    {
        return new Vector3(
            SanitizeCoordinate(value.X),
            SanitizeCoordinate(value.Y),
            SanitizeCoordinate(value.Z));
    }
}

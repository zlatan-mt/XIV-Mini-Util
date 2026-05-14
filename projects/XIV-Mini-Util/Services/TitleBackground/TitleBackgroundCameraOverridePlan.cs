// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCameraOverridePlan.cs
// Description: タイトル背景 camera override の適用値と純粋な適用条件を定義する
// Reason: FixOn detour から Focus/CharacterPosition の意味混同を切り離してテスト可能にするため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCameraOverridePlan(
    Vector3 Camera,
    Vector3 Focus,
    float FovY)
{
    public static TitleBackgroundCameraOverridePlan FromConfiguration(Configuration configuration)
    {
        return Create(
            new Vector3(
                configuration.TitleBackgroundCameraX,
                configuration.TitleBackgroundCameraY,
                configuration.TitleBackgroundCameraZ),
            new Vector3(
                configuration.TitleBackgroundFocusX,
                configuration.TitleBackgroundFocusY,
                configuration.TitleBackgroundFocusZ),
            configuration.TitleBackgroundFovY);
    }

    public static TitleBackgroundCameraOverridePlan Create(Vector3 camera, Vector3 focus, float fovY)
    {
        return new TitleBackgroundCameraOverridePlan(
            SanitizeVector(camera),
            SanitizeVector(focus),
            TitleBackgroundPreset.ClampFovY(fovY));
    }

    public static bool ShouldApply(
        bool cameraOverrideEnabled,
        bool isHookProbeMode,
        bool cameraApplyPending,
        bool stateReady,
        bool currentMapAvailable,
        GameLobbyType currentMap)
    {
        return cameraOverrideEnabled
            && !isHookProbeMode
            && cameraApplyPending
            && stateReady
            && currentMapAvailable
            && currentMap == GameLobbyType.CharaSelect;
    }

    public static string GetFixOnInvocationMode(bool overrideApplied)
    {
        return overrideApplied ? "override-applied" : "passthrough";
    }

    private static Vector3 SanitizeVector(Vector3 value)
    {
        return new Vector3(
            TitleBackgroundPreset.SanitizeCoordinate(value.X),
            TitleBackgroundPreset.SanitizeCoordinate(value.Y),
            TitleBackgroundPreset.SanitizeCoordinate(value.Z));
    }
}

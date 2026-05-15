// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraRuntimeState.cs
// Description: Character select lobby camera adapter の非永続 runtime state を表す
// Reason: scene load 後に復元する yaw / pitch / distance を preset schema から切り離すため
namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectCameraRuntimeState(
    float? Yaw,
    float? Pitch,
    float? Distance,
    float? LookAtY,
    int SceneGeneration)
{
    public bool HasCameraPose =>
        Yaw.HasValue
        && Pitch.HasValue
        && Distance.HasValue;

    public static TitleBackgroundCharaSelectCameraRuntimeState Empty { get; } = new(null, null, null, null, 0);

    public static TitleBackgroundCharaSelectCameraRuntimeState Create(
        float? yaw,
        float? pitch,
        float? distance,
        float? lookAtY,
        int sceneGeneration)
    {
        return new TitleBackgroundCharaSelectCameraRuntimeState(
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(yaw),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalAngle(pitch),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(distance),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalCoordinate(lookAtY),
            Math.Max(0, sceneGeneration));
    }

    public TitleBackgroundCharaSelectCameraRuntimeState WithoutCameraPose()
    {
        return new TitleBackgroundCharaSelectCameraRuntimeState(null, null, null, null, SceneGeneration);
    }
}

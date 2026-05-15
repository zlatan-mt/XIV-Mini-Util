// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraRuntimeState.cs
// Description: Character select lobby camera adapter の非永続 runtime state を表す
// Reason: scene load 後に復元する yaw / pitch / distance を preset schema から切り離すため
namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectCameraRuntimeState(
    float? Yaw,
    float? YawOffset,
    float? Pitch,
    float? Distance,
    float? LookAtY,
    float? CharacterRotationAtRecord,
    int SceneGeneration)
{
    public bool HasCameraPose =>
        Yaw.HasValue
        && Pitch.HasValue
        && Distance.HasValue;

    public static TitleBackgroundCharaSelectCameraRuntimeState Empty { get; } = new(null, null, null, null, null, null, 0);

    public static TitleBackgroundCharaSelectCameraRuntimeState Create(
        float? yaw,
        float? yawOffset,
        float? pitch,
        float? distance,
        float? lookAtY,
        float? characterRotationAtRecord,
        int sceneGeneration)
    {
        return new TitleBackgroundCharaSelectCameraRuntimeState(
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(yaw),
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(yawOffset),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalAngle(pitch),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(distance),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalCoordinate(lookAtY),
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(characterRotationAtRecord),
            Math.Max(0, sceneGeneration));
    }

    public static TitleBackgroundCharaSelectCameraRuntimeState FromObservedPose(
        float yaw,
        float pitch,
        float distance,
        float lookAtY,
        float characterRotation,
        int sceneGeneration)
    {
        var normalizedRotation = TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(characterRotation);
        return Create(
            yaw,
            TitleBackgroundCharaSelectCameraLogic.CalculateYawOffset(yaw, normalizedRotation),
            pitch,
            distance,
            lookAtY,
            normalizedRotation,
            sceneGeneration);
    }

    public float? GetRestoredYaw(float characterRotation)
    {
        return YawOffset.HasValue
            ? TitleBackgroundCharaSelectCameraLogic.CalculateRestoredYaw(YawOffset.Value, characterRotation)
            : Yaw;
    }

    public TitleBackgroundCharaSelectCameraRuntimeState WithoutCameraPose()
    {
        return new TitleBackgroundCharaSelectCameraRuntimeState(null, null, null, null, null, null, SceneGeneration);
    }
}

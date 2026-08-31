// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraRuntimeState.cs
// Description: Character select lobby camera adapter の非永続 runtime state を表す
// Reason: scene load 後に復元する yaw / pitch / distance を preset schema から切り離すため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal readonly record struct TitleBackgroundCharaSelectCameraRuntimeState(
    float? Yaw,
    float? YawOffset,
    float? Pitch,
    float? Distance,
    float? LookAtY,
    Vector3? LookAt,
    TitleBackgroundCharaSelectCameraCurve? CurveAtRecord,
    float? CharacterRotationAtRecord,
    bool ShouldSetLookAtY,
    int SceneGeneration)
{
    public bool HasCameraPose =>
        Yaw.HasValue
        && Pitch.HasValue
        && Distance.HasValue;

    public bool HasLookAt =>
        LookAt.HasValue
        && TitleBackgroundCameraMath.IsFiniteVector(LookAt.Value);

    public static TitleBackgroundCharaSelectCameraRuntimeState Empty { get; } = new(null, null, null, null, null, null, null, null, false, 0);

    public static TitleBackgroundCharaSelectCameraRuntimeState Create(
        float? yaw,
        float? yawOffset,
        float? pitch,
        float? distance,
        float? lookAtY,
        Vector3? lookAt,
        TitleBackgroundCharaSelectCameraCurve? curveAtRecord,
        float? characterRotationAtRecord,
        bool shouldSetLookAtY,
        int sceneGeneration)
    {
        return new TitleBackgroundCharaSelectCameraRuntimeState(
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(yaw),
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(yawOffset),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalAngle(pitch),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalDistance(distance),
            TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalCoordinate(lookAtY),
            SanitizeOptionalLookAt(lookAt),
            SanitizeOptionalCurve(curveAtRecord),
            TitleBackgroundCharaSelectCameraLogic.NormalizeOptionalRadians(characterRotationAtRecord),
            shouldSetLookAtY && TitleBackgroundCharaSelectCameraLogic.SanitizeOptionalCoordinate(lookAtY).HasValue,
            Math.Max(0, sceneGeneration));
    }

    public static TitleBackgroundCharaSelectCameraRuntimeState FromObservedPose(
        float yaw,
        float pitch,
        float distance,
        float lookAtY,
        Vector3? lookAt,
        TitleBackgroundCharaSelectCameraCurve curveAtRecord,
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
            lookAt,
            curveAtRecord,
            normalizedRotation,
            false,
            sceneGeneration);
    }

    public static TitleBackgroundCharaSelectCameraRuntimeState FromPresetPose(
        float yaw,
        float pitch,
        float distance,
        float lookAtY,
        Vector3? lookAt,
        TitleBackgroundCharaSelectCameraCurve curveAtRecord,
        float characterRotation,
        int sceneGeneration)
    {
        return Create(
            yaw,
            null,
            pitch,
            distance,
            lookAtY,
            lookAt,
            curveAtRecord,
            TitleBackgroundCharaSelectCameraLogic.NormalizeRadians(characterRotation),
            true,
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
        return new TitleBackgroundCharaSelectCameraRuntimeState(null, null, null, null, null, null, null, null, false, SceneGeneration);
    }

    public TitleBackgroundCharaSelectCameraRuntimeState WithShouldSetLookAtY(bool shouldSetLookAtY)
    {
        return Create(
            Yaw,
            YawOffset,
            Pitch,
            Distance,
            LookAtY,
            LookAt,
            CurveAtRecord,
            CharacterRotationAtRecord,
            shouldSetLookAtY,
            SceneGeneration);
    }

    private static Vector3? SanitizeOptionalLookAt(Vector3? value)
    {
        return value.HasValue && TitleBackgroundCameraMath.IsFiniteVector(value.Value)
            ? TitleBackgroundCharaSelectCameraLogic.SanitizeVector(value.Value)
            : null;
    }

    private static TitleBackgroundCharaSelectCameraCurve? SanitizeOptionalCurve(TitleBackgroundCharaSelectCameraCurve? value)
    {
        return value.HasValue
            ? new TitleBackgroundCharaSelectCameraCurve(
                TitleBackgroundPreset.SanitizeCoordinate(value.Value.Low),
                TitleBackgroundPreset.SanitizeCoordinate(value.Value.Mid),
                TitleBackgroundPreset.SanitizeCoordinate(value.Value.High))
            : null;
    }
}

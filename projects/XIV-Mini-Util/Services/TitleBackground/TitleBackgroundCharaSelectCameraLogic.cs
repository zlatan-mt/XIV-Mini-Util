// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraLogic.cs
// Description: Character select lobby camera adapter の純粋ロジックを提供する
// Reason: native hook なしで state machine / runtime state の退行を検出できるようにするため
using System.Numerics;

namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCharaSelectCameraAdapterEvent
{
    ConfigureDisabled = 0,
    ConfigureEnabled = 1,
    SceneLoadStarted = 2,
    SceneOverrideApplied = 3,
    SceneLoaded = 4,
    LobbyBecameActive = 5,
    StopRequested = 6,
    Reset = 7,
}

internal static class TitleBackgroundCharaSelectCameraLogic
{
    public const float MagicLow = 1.4350828f;
    public const float MagicMid = 0.85870504f;
    public const float MagicHigh = 0.6742642f;
    public const float MinDistance = 0.01f;
    public const float MaxDistance = 100000f;

    public static TitleBackgroundCharaSelectCameraAdapterState Transition(
        TitleBackgroundCharaSelectCameraAdapterState current,
        TitleBackgroundCharaSelectCameraAdapterEvent adapterEvent)
    {
        return adapterEvent switch
        {
            TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureDisabled => TitleBackgroundCharaSelectCameraAdapterState.Inactive,
            TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureEnabled => current == TitleBackgroundCharaSelectCameraAdapterState.Inactive
                ? TitleBackgroundCharaSelectCameraAdapterState.Armed
                : current,
            TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted => current is TitleBackgroundCharaSelectCameraAdapterState.Armed
                    or TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
                    or TitleBackgroundCharaSelectCameraAdapterState.Active
                    or TitleBackgroundCharaSelectCameraAdapterState.Stopping
                ? TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
                : current,
            TitleBackgroundCharaSelectCameraAdapterEvent.SceneOverrideApplied => current is TitleBackgroundCharaSelectCameraAdapterState.Armed
                    or TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
                    or TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
                    or TitleBackgroundCharaSelectCameraAdapterState.Active
                    or TitleBackgroundCharaSelectCameraAdapterState.Stopping
                ? TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
                : current,
            TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoaded => current == TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
                ? TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
                : current,
            TitleBackgroundCharaSelectCameraAdapterEvent.LobbyBecameActive => current == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
                ? TitleBackgroundCharaSelectCameraAdapterState.Active
                : current,
            TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested => current == TitleBackgroundCharaSelectCameraAdapterState.Inactive
                ? TitleBackgroundCharaSelectCameraAdapterState.Inactive
                : TitleBackgroundCharaSelectCameraAdapterState.Stopping,
            TitleBackgroundCharaSelectCameraAdapterEvent.Reset => current == TitleBackgroundCharaSelectCameraAdapterState.Inactive
                ? TitleBackgroundCharaSelectCameraAdapterState.Inactive
                : TitleBackgroundCharaSelectCameraAdapterState.Armed,
            _ => current,
        };
    }

    public static bool ShouldArmAdapter(
        bool overrideEnabled,
        bool cameraAdaptationEnabled,
        TitleBackgroundRuntimeMode runtimeMode)
    {
        return overrideEnabled
            && cameraAdaptationEnabled
            && runtimeMode == TitleBackgroundRuntimeMode.CharaSelectOnly;
    }

    public static bool IsCharaSelectMap(GameLobbyType map)
    {
        return map == GameLobbyType.CharaSelect;
    }

    public static bool ShouldRestoreRuntimeCameraState(
        TitleBackgroundCharaSelectCameraAdapterState state,
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState)
    {
        return state == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
            && runtimeState.HasCameraPose;
    }

    public static bool ShouldHandleSceneReadySignal(
        bool serviceReady,
        bool hookProbeMode,
        bool adapterArmed,
        TitleBackgroundCharaSelectCameraAdapterState adapterState,
        GameLobbyType map)
    {
        return serviceReady
            && !hookProbeMode
            && adapterArmed
            && adapterState is TitleBackgroundCharaSelectCameraAdapterState.Armed
                or TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
            && IsCharaSelectMap(map);
    }

    public static TitleBackgroundCharaSelectCameraCurve BuildCurve(float characterPositionY)
    {
        var y = TitleBackgroundPreset.SanitizeCoordinate(characterPositionY);
        return new TitleBackgroundCharaSelectCameraCurve(
            TitleBackgroundPreset.SanitizeCoordinate(MagicLow + y),
            TitleBackgroundPreset.SanitizeCoordinate(MagicMid + y),
            TitleBackgroundPreset.SanitizeCoordinate(MagicHigh + y));
    }

    public static float CalculateYawOffset(float yaw, float characterRotation)
    {
        return NormalizeRadians(yaw - characterRotation);
    }

    public static float CalculateRestoredYaw(float yawOffset, float characterRotation)
    {
        return NormalizeRadians(yawOffset + characterRotation);
    }

    public static Vector3 SanitizeVector(Vector3 value)
    {
        return new Vector3(
            TitleBackgroundPreset.SanitizeCoordinate(value.X),
            TitleBackgroundPreset.SanitizeCoordinate(value.Y),
            TitleBackgroundPreset.SanitizeCoordinate(value.Z));
    }

    public static float NormalizeRadians(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0f;
        }

        var normalized = value % (MathF.PI * 2f);
        if (normalized <= -MathF.PI)
        {
            normalized += MathF.PI * 2f;
        }
        else if (normalized > MathF.PI)
        {
            normalized -= MathF.PI * 2f;
        }

        return normalized;
    }

    public static float? NormalizeOptionalRadians(float? value)
    {
        return value.HasValue ? NormalizeRadians(value.Value) : null;
    }

    public static float? SanitizeOptionalAngle(float? value)
    {
        return value.HasValue && float.IsFinite(value.Value)
            ? Math.Clamp(value.Value, -MathF.PI / 2f, MathF.PI / 2f)
            : null;
    }

    public static float? SanitizeOptionalDistance(float? value)
    {
        return value.HasValue && float.IsFinite(value.Value)
            ? Math.Clamp(value.Value, MinDistance, MaxDistance)
            : null;
    }

    public static float? SanitizeOptionalCoordinate(float? value)
    {
        return value.HasValue && float.IsFinite(value.Value)
            ? TitleBackgroundPreset.SanitizeCoordinate(value.Value)
            : null;
    }
}

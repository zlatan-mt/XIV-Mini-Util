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

    public static bool IsCharaSelectOrTitleBackgroundMap(GameLobbyType map)
    {
        return map is GameLobbyType.CharaSelect or GameLobbyType.Title;
    }

    public static bool ShouldEndCharaSelectTitleBackgroundSession(bool isLoggedIn, GameLobbyType currentMap)
    {
        return isLoggedIn
            || (currentMap != GameLobbyType.None && !IsCharaSelectOrTitleBackgroundMap(currentMap));
    }

    public static bool ShouldRestoreRuntimeCameraState(
        TitleBackgroundCharaSelectCameraAdapterState state,
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState)
    {
        return state == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
            && runtimeState.HasCameraPose;
    }

    public static bool ShouldApplyCurve(
        TitleBackgroundCharaSelectCameraAdapterState state,
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState,
        int lastAppliedSceneGeneration)
    {
        return state == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
            && runtimeState.SceneGeneration > 0
            && runtimeState.SceneGeneration != lastAppliedSceneGeneration;
    }

    public static bool ShouldApplyGeneratedCurveOverride(
        bool serviceReady,
        bool hookProbeMode,
        bool sceneOverrideEnabled,
        bool adapterArmed,
        bool isLoggedIn,
        bool activeCharaSelectSession,
        bool sceneGenerationMatchesActiveSession,
        TitleBackgroundCharaSelectCameraAdapterState state,
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState,
        GameLobbyType currentLobbyMap,
        GameLobbyType resolvedLobbyMap)
    {
        return serviceReady
            && !hookProbeMode
            && sceneOverrideEnabled
            && adapterArmed
            && !isLoggedIn
            && activeCharaSelectSession
            && sceneGenerationMatchesActiveSession
            && state is TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
                or TitleBackgroundCharaSelectCameraAdapterState.Active
            && runtimeState.SceneGeneration > 0
            && runtimeState.HasCameraPose
            && runtimeState.CurveAtRecord.HasValue
            && (IsCharaSelectOrTitleBackgroundMap(currentLobbyMap) || IsCharaSelectOrTitleBackgroundMap(resolvedLobbyMap));
    }

    public static bool ShouldApplyLookAtY(
        TitleBackgroundCharaSelectCameraAdapterState state,
        TitleBackgroundCharaSelectCameraRuntimeState runtimeState,
        int lastAppliedSceneGeneration)
    {
        return state == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
            && runtimeState.SceneGeneration > 0
            && runtimeState.SceneGeneration != lastAppliedSceneGeneration
            && runtimeState.ShouldSetLookAtY
            && runtimeState.LookAtY.HasValue;
    }

    public static bool ShouldStopOnLobbyUpdate(
        TitleBackgroundCharaSelectCameraAdapterState state,
        GameLobbyType map)
    {
        return !IsCharaSelectMap(map)
            && state is not TitleBackgroundCharaSelectCameraAdapterState.Armed
                and not TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
                and not TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded;
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

    public static bool TryBuildPoseFromCameraFocus(
        Vector3 camera,
        Vector3 focus,
        out float yaw,
        out float pitch,
        out float distance,
        out float lookAtY,
        out string errorMessage)
    {
        yaw = 0f;
        pitch = 0f;
        distance = 0f;
        lookAtY = 0f;
        errorMessage = string.Empty;
        if (!TitleBackgroundCameraMath.IsFiniteVector(camera)
            || !TitleBackgroundCameraMath.IsFiniteVector(focus))
        {
            errorMessage = "preset camera/focus contains non-finite values";
            return false;
        }

        var direction = focus - camera;
        distance = direction.Length();
        if (!float.IsFinite(distance) || distance < MinDistance)
        {
            errorMessage = "preset camera/focus distance is too small";
            return false;
        }

        var horizontalDistance = MathF.Sqrt((direction.X * direction.X) + (direction.Z * direction.Z));
        yaw = NormalizeRadians(MathF.Atan2(direction.X, direction.Z));
        pitch = Math.Clamp(MathF.Atan2(direction.Y, horizontalDistance), -MathF.PI / 2f, MathF.PI / 2f);
        distance = Math.Clamp(distance, MinDistance, MaxDistance);
        lookAtY = TitleBackgroundPreset.SanitizeCoordinate(focus.Y);
        return true;
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

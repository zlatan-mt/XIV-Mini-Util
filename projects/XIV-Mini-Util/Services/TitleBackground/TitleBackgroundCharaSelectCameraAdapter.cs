// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraAdapter.cs
// Description: Character select lobby camera system 適応の Phase 1 adapter 境界
// Reason: curve / LookAt / scene loaded 復元の native hook 実装を次フェーズへ閉じ込めるため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed class TitleBackgroundCharaSelectCameraAdapter
{
    public TitleBackgroundCharaSelectCameraAdapterState State { get; private set; } = TitleBackgroundCharaSelectCameraAdapterState.Inactive;
    public TitleBackgroundCharaSelectCameraInput Input { get; private set; }
    public TitleBackgroundCharaSelectCameraRuntimeState RuntimeState { get; private set; } = TitleBackgroundCharaSelectCameraRuntimeState.Empty;
    public TitleBackgroundCharaSelectCameraCurve Curve { get; private set; } = TitleBackgroundCharaSelectCameraCurve.Default;
    public string LastEvent { get; private set; } = "not-run";

    public bool IsArmed => State != TitleBackgroundCharaSelectCameraAdapterState.Inactive;

    public void Configure(bool enabled, TitleBackgroundCharaSelectCameraInput input)
    {
        Input = input;
        Curve = TitleBackgroundCharaSelectCameraLogic.BuildCurve(input.CharacterPosition.Y);
        ApplyTransition(enabled
            ? TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureEnabled
            : TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureDisabled);
        if (!enabled)
        {
            RuntimeState = TitleBackgroundCharaSelectCameraRuntimeState.Empty;
        }
    }

    public void NotifySceneLoadStarted(GameLobbyType map)
    {
        if (!IsArmed || !TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(map))
        {
            return;
        }

        RuntimeState = TitleBackgroundCharaSelectCameraRuntimeState.Create(
            RuntimeState.Yaw,
            RuntimeState.YawOffset,
            RuntimeState.Pitch,
            RuntimeState.Distance,
            RuntimeState.LookAtY,
            RuntimeState.LookAt,
            RuntimeState.CurveAtRecord,
            RuntimeState.CharacterRotationAtRecord,
            RuntimeState.SceneGeneration + 1);
        ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted);
    }

    public void NotifySceneOverrideApplied(GameLobbyType lobbyType)
    {
        if (!IsArmed || !TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(lobbyType))
        {
            return;
        }

        ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.SceneOverrideApplied);
    }

    public void NotifySceneLoaded(GameLobbyType map)
    {
        if (!IsArmed || !TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(map))
        {
            return;
        }

        ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoaded);
    }

    public void NotifyLobbyUpdate(GameLobbyType map)
    {
        if (!IsArmed)
        {
            return;
        }

        if (TitleBackgroundCharaSelectCameraLogic.IsCharaSelectMap(map))
        {
            ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.LobbyBecameActive);
            return;
        }

        if (State != TitleBackgroundCharaSelectCameraAdapterState.Armed)
        {
            ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested);
        }
    }

    public void SaveRuntimeCameraState(float yaw, float pitch, float distance, float lookAtY)
    {
        SaveRuntimeCameraState(yaw, pitch, distance, lookAtY, null);
    }

    public void SaveRuntimeCameraState(float yaw, float pitch, float distance, float lookAtY, System.Numerics.Vector3? lookAt)
    {
        RuntimeState = TitleBackgroundCharaSelectCameraRuntimeState.FromObservedPose(
            yaw,
            pitch,
            distance,
            lookAtY,
            lookAt,
            Curve,
            Input.CharacterRotation,
            RuntimeState.SceneGeneration);
    }

    public float? GetRestoredYaw()
    {
        return RuntimeState.GetRestoredYaw(Input.CharacterRotation);
    }

    public void ResetRuntimeCameraState()
    {
        RuntimeState = RuntimeState.WithoutCameraPose();
    }

    public void Reset()
    {
        RuntimeState = TitleBackgroundCharaSelectCameraRuntimeState.Empty;
        ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent.Reset);
    }

    public bool ShouldRestoreRuntimeCameraState()
    {
        return TitleBackgroundCharaSelectCameraLogic.ShouldRestoreRuntimeCameraState(State, RuntimeState);
    }

    private void ApplyTransition(TitleBackgroundCharaSelectCameraAdapterEvent adapterEvent)
    {
        State = TitleBackgroundCharaSelectCameraLogic.Transition(State, adapterEvent);
        LastEvent = adapterEvent.ToString();
    }
}

// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundProbeSettingsSnapshot.cs
// Description: probe が変更する前の TitleBackground 設定を保持する
// Reason: 診断用の設定 snapshot を TitleScreenBackgroundService の本体ロジックから分離するため
namespace XivMiniUtil.Services.TitleBackground;

internal sealed record TitleBackgroundProbeSettingsSnapshot(
    bool OverrideEnabled,
    bool CameraOverrideEnabled,
    TitleBackgroundRuntimeMode RuntimeMode,
    TitleBackgroundResolverMode CreateSceneResolverMode,
    TitleBackgroundResolverMode LobbyUpdateResolverMode)
{
    public static TitleBackgroundProbeSettingsSnapshot Capture(Configuration configuration)
    {
        return new TitleBackgroundProbeSettingsSnapshot(
            configuration.TitleBackgroundOverrideEnabled,
            configuration.TitleBackgroundCameraOverrideEnabled,
            configuration.TitleBackgroundRuntimeMode,
            configuration.TitleBackgroundCreateSceneResolverMode,
            configuration.TitleBackgroundLobbyUpdateResolverMode);
    }

    public void ApplyTo(Configuration configuration)
    {
        configuration.TitleBackgroundOverrideEnabled = OverrideEnabled;
        configuration.TitleBackgroundCameraOverrideEnabled = CameraOverrideEnabled;
        configuration.TitleBackgroundRuntimeMode = RuntimeMode;
        configuration.TitleBackgroundCreateSceneResolverMode = CreateSceneResolverMode;
        configuration.TitleBackgroundLobbyUpdateResolverMode = LobbyUpdateResolverMode;
    }
}

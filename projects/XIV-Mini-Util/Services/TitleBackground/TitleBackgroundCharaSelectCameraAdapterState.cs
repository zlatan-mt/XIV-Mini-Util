// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectCameraAdapterState.cs
// Description: Character select lobby camera adapter の段階状態を定義する
// Reason: native camera hook 実装前に MiniUtil 側の状態遷移境界を固定するため
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundCharaSelectCameraAdapterState
{
    Inactive = 0,
    Armed = 1,
    SceneLoading = 2,
    SceneLoaded = 3,
    Active = 4,
    Stopping = 5,
}

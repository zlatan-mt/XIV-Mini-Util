// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundServiceState.cs
// Description: タイトル背景差し替えサービスの状態を表す
// Reason: hook 初期化や validation 失敗を機能内に閉じて UI/docs に出せるようにするため
namespace XivMiniUtil.Services.TitleBackground;

internal enum TitleBackgroundServiceState
{
    Disabled,
    Ready,
    InvalidConfiguration,
    AddressResolveFailed,
    HookCreateFailed,
    HookEnableFailed,
    RuntimeError,
}

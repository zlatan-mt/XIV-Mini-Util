// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectPrefetchOwner.cs
// Description: キャラ選択preload layoutの所有元を表す
// Reason: override表示とログイン待機preloadのunload範囲を分離するため
namespace XivMiniUtil.Services.CharaSelect;

internal enum CharaSelectPrefetchOwner
{
    None,
    OverrideDisplay,
    LoginWait,
}

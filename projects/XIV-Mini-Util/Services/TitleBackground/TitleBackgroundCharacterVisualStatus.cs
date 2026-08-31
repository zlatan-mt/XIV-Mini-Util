// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharacterVisualStatus.cs
// Description: Character Select 背景でのキャラ視覚ステータスをユーザーが手動で記録するための enum
// Reason: 診断側で actor source が取れなくても、ユーザーの目視確認をコンフィグに保存してQuickCheckに転写するため
namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundCharacterVisualStatus
{
    Unknown = 0,
    Visible = 1,
    VisibleButTooSmall = 2,
    VisibleTopDown = 3,
    NotVisible = 4,
    Offscreen = 5,
}

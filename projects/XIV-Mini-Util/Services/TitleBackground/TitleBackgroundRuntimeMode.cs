// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundRuntimeMode.cs
// Description: タイトル背景差し替えnative連携の段階的な有効化モード
// Reason: signature確認後もhook/overrideを段階投入できるようにするため
namespace XivMiniUtil.Services.TitleBackground;

public enum TitleBackgroundRuntimeMode
{
    Disabled = 0,
    ResolveOnly = 1,
    CharaSelectOnly = 2,
    TitleAndCharaSelect = 3,
}

// Path: projects/XIV-Mini-Util/Services/TitleBackground/GameLobbyType.cs
// Description: タイトル/キャラ選択ロビー種別の純粋な判定ロジック
// Reason: native hook 本体から遷移判定を切り離してゲーム不要テストで検証するため
namespace XivMiniUtil.Services.TitleBackground;

internal enum GameLobbyType : short
{
    None = -1,
    Title = 0,
    CharaSelect = 1,
    Aetherial = 2,
    LaNoscea = 3,
    BlackShroud = 4,
    Thanalan = 5,
    Residence = 6,
}

internal static class GameLobbyTypeHelper
{
    public static bool IsTitleCharaSelectTransition(GameLobbyType current, GameLobbyType next)
    {
        return (current == GameLobbyType.Title && next == GameLobbyType.CharaSelect)
            || (current == GameLobbyType.CharaSelect && next == GameLobbyType.Title);
    }

    public static GameLobbyType GetCurrentMapForTransition(GameLobbyType current, GameLobbyType next, bool overrideEnabled)
    {
        return overrideEnabled && IsTitleCharaSelectTransition(current, next)
            ? GameLobbyType.None
            : current;
    }
}

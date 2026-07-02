// Path: projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundKnownSignatures.cs
// Description: API15で確認済みのTitle Background native署名を一元管理する
// Reason: 1クリック確認で空の保存設定を変更せず、一時的なresolver fallbackとして利用するため

namespace XivMiniUtil.Services.TitleBackground;

internal static class TitleBackgroundKnownSignatures
{
    public const string CreateScene = "E8 ?? ?? ?? ?? 66 89 3D ?? ?? ?? ?? E9";
    public const string FixOn = "C6 81 ?? ?? ?? ?? ?? 0F 28 CB 8B 02";
    public const string LobbyUpdate = "E8 ?? ?? ?? ?? 80 BF ?? ?? ?? ?? ?? 48 8D 35";
    public const string LoadLobbyScene = "48 89 5C 24 ?? 57 48 83 EC ?? 8B D9 E8";
    public const string LobbyCurrentMap = "66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 66 89 05 ?? ?? ?? ?? 48 8B 4B";
    public const string CalculateLobbyCameraLookAtY = "48 83 EC ?? F3 41 0F 10 01 0F 28 D1";
    public const string SetCameraCurveMidPoint = "0F 57 C0 0F 2F C1 73 ?? F3 0F 11 89";
    public const string CalculateCameraCurveLowAndHighPoint = "F3 0F 10 81 ?? ?? ?? ?? F3 0F 11 89";

    public static string ResolveMissing(
        string configured,
        string known,
        bool useKnownWhenMissing)
    {
        return useKnownWhenMissing && string.IsNullOrWhiteSpace(configured)
            ? known
            : configured;
    }
}

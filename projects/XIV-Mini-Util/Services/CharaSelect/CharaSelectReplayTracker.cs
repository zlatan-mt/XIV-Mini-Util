// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectReplayTracker.cs
// Description: キャラ選択エモートの再生済み状態を管理する
// Reason: 初期選択キャラとキャラ切替時の再生判定をゲーム非依存で検証できるようにするため
namespace XivMiniUtil.Services.CharaSelect;

internal sealed class CharaSelectReplayTracker
{
    private ulong _contentId;
    private uint _emoteId;
    private nint _characterAddress;

    public bool ShouldReplay(ulong contentId, uint emoteId, nint characterAddress, bool force)
    {
        if (contentId == 0 || emoteId == 0 || characterAddress == nint.Zero)
        {
            return false;
        }

        return force
            || _contentId != contentId
            || _emoteId != emoteId
            || _characterAddress != characterAddress;
    }

    public void MarkReplayed(ulong contentId, uint emoteId, nint characterAddress)
    {
        _contentId = contentId;
        _emoteId = emoteId;
        _characterAddress = characterAddress;
    }

    public void Clear()
    {
        _contentId = 0;
        _emoteId = 0;
        _characterAddress = nint.Zero;
    }
}

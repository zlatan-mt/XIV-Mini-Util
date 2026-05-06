// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectCharacterState.cs
// Description: キャラ選択画面で選択中のキャラクター状態を保持する
// Reason: ContentId単位で保存エモートとログイン先情報を扱うため
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace XivMiniUtil.Services.CharaSelect;

internal unsafe sealed class CharaSelectCharacterState
{
    public CharaSelectCharacterState(Character* character, ulong contentId, ushort territoryTypeId, byte classJobId, ushort voiceId)
    {
        Character = character;
        ContentId = contentId;
        TerritoryTypeId = territoryTypeId;
        ClassJobId = classJobId;
        VoiceId = voiceId;
    }

    public Character* Character { get; }
    public ulong ContentId { get; }
    public ushort TerritoryTypeId { get; }
    public byte ClassJobId { get; }
    public ushort VoiceId { get; }
}

// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectCharacterApplier.cs
// Description: キャラ選択画面の表示キャラクターへロビー情報を反映する
// Reason: 声ID反映をゲーム非依存テストで検証できる単位に分けるため
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace XivMiniUtil.Services.CharaSelect;

internal static unsafe class CharaSelectCharacterApplier
{
    public static void ApplyVoice(Character* character, ushort voiceId, bool loadSound = true)
    {
        if (character == null)
        {
            return;
        }

        character->Vfx.VoiceId = voiceId;
        if (loadSound)
        {
            character->Vfx.LoadCharacterSound(voiceId, 0, (nint)character, 0, 0, 0, 0);
        }
    }
}

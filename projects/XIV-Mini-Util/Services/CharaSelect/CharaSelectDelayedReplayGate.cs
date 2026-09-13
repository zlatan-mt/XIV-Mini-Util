// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectDelayedReplayGate.cs
// Description: ScheduleDelayedReplay で予約した voice 適用 / emote 再生を、書込み直前に
//              再解決した native actor identity と照合してよいかどうかの純粋判定。
// Reason: _currentEntry は 10 フレーム毎の poll でしか更新されないため、schedule 時点の
//         値と _currentEntry を比較するだけでは両方とも同じ古い参照のまま一致してしまいうる。
//         書込み直前に canonical resolver（TryResolveCurrentCharaSelectActor）で解決し直した
//         結果を判定材料にする側の純粋ロジックだけを切り出し、native 構造体なしでテストする。
namespace XivMiniUtil.Services.CharaSelect;

internal static class CharaSelectDelayedReplayGate
{
    // resolved* は書込み直前に canonical resolver で再解決した現在の native identity。
    // scheduled* は ScheduleDelayedReplay 時点で保存した予約対象。
    // currentEntry* は _currentEntry（poll 更新、遅れうる）由来の値。
    public static bool ShouldApplyDelayedVoice(
        bool resolvedValid,
        ulong resolvedContentId,
        nint resolvedCharacterAddress,
        ulong scheduledContentId,
        nint scheduledCharacterAddress,
        bool currentEntryAvailable,
        ulong currentEntryContentId,
        ushort currentEntryVoiceId)
    {
        return resolvedValid
            && resolvedContentId == scheduledContentId
            && resolvedCharacterAddress == scheduledCharacterAddress
            && currentEntryAvailable
            && currentEntryContentId == resolvedContentId
            && currentEntryVoiceId > 0;
    }

    public static bool ShouldPlayDelayedEmote(
        bool resolvedValid,
        ulong resolvedContentId,
        nint resolvedCharacterAddress,
        ulong scheduledContentId,
        nint scheduledCharacterAddress,
        bool currentEntryAvailable,
        ulong currentEntryContentId,
        bool hasCurrentSelectedEmoteId,
        uint currentSelectedEmoteId,
        uint scheduledEmoteId)
    {
        return resolvedValid
            && resolvedContentId == scheduledContentId
            && resolvedCharacterAddress == scheduledCharacterAddress
            && currentEntryAvailable
            && currentEntryContentId == resolvedContentId
            && hasCurrentSelectedEmoteId
            && currentSelectedEmoteId == scheduledEmoteId;
    }
}

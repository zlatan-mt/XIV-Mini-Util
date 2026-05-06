// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectEmotePlaybackPlanner.cs
// Description: エモート再生時のmode/timeline指定を決定する
// Reason: Excel rowから得た値の変換をゲーム非依存で検証できるようにするため
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectEmotePlaybackPlan(
    CharacterModes Mode,
    byte ModeParam,
    ushort IntroTimelineId,
    ushort LoopTimelineId)
{
    public bool HasTimeline => IntroTimelineId != 0 || LoopTimelineId != 0;
}

internal static class CharaSelectEmotePlaybackPlanner
{
    public static CharaSelectEmotePlaybackPlan Create(uint emoteModeRowId, byte conditionMode, ushort introTimelineId, ushort loopTimelineId)
    {
        if (emoteModeRowId == 0)
        {
            return new CharaSelectEmotePlaybackPlan(CharacterModes.Normal, 0, introTimelineId, loopTimelineId);
        }

        return new CharaSelectEmotePlaybackPlan(
            (CharacterModes)conditionMode,
            emoteModeRowId > byte.MaxValue ? byte.MaxValue : (byte)emoteModeRowId,
            introTimelineId,
            loopTimelineId);
    }
}

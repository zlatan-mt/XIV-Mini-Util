// Path: tools/CharaSelectLogicTests/Program.cs
// Description: キャラ選択エモートのゲーム非依存ロジックを検証する
// Reason: 実機なしで再生判定とEmoteMode変換の退行を検出するため
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using XivMiniUtil.Services.CharaSelect;

var failures = new List<string>();

Test("first valid character replays", () =>
{
    var tracker = new CharaSelectReplayTracker();
    return tracker.ShouldReplay(1, 100, 0x1000, force: false);
});

Test("same character and emote does not replay repeatedly", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return !tracker.ShouldReplay(1, 100, 0x1000, force: false);
});

Test("same content id with new character pointer replays", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 100, 0x2000, force: false);
});

Test("force replay bypasses previous state", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 100, 0x1000, force: true);
});

Test("invalid replay inputs are ignored", () =>
{
    var tracker = new CharaSelectReplayTracker();
    return !tracker.ShouldReplay(0, 100, 0x1000, force: true)
        && !tracker.ShouldReplay(1, 0, 0x1000, force: true)
        && !tracker.ShouldReplay(1, 100, nint.Zero, force: true);
});

Test("emote mode uses condition mode and row id parameter", () =>
{
    var plan = CharaSelectEmotePlaybackPlanner.Create(
        emoteModeRowId: 7,
        conditionMode: (byte)CharacterModes.EmoteLoop,
        introTimelineId: 10,
        loopTimelineId: 11);
    return plan.Mode == CharacterModes.EmoteLoop
        && plan.ModeParam == 7
        && plan.IntroTimelineId == 10
        && plan.LoopTimelineId == 11
        && plan.HasTimeline;
});

Test("empty emote mode resets to normal", () =>
{
    var plan = CharaSelectEmotePlaybackPlanner.Create(0, 0, 0, 3);
    return plan.Mode == CharacterModes.Normal
        && plan.ModeParam == 0
        && plan.LoopTimelineId == 3
        && plan.HasTimeline;
});

Test("voice id is applied from selected lobby entry", () =>
{
    unsafe
    {
        var character = new Character();

        CharaSelectCharacterApplier.ApplyVoice(&character, 42, loadSound: false);
        return character.Vfx.VoiceId == 42;
    }
});

Test("voice id resolves through zero-based voice table", () =>
{
    byte[] voices = [20, 21, 22];
    return CharaSelectVoiceIdResolver.Resolve(1, voices.Length, index => voices[index]) == 21;
});

Test("voice id falls back to one-based voice table when zero-based entry is empty", () =>
{
    byte[] voices = [20, 0, 22];
    return CharaSelectVoiceIdResolver.Resolve(1, voices.Length, index => voices[index]) == 20;
});

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("CharaSelect logic tests passed.");
return 0;

void Test(string name, Func<bool> assertion)
{
    try
    {
        if (!assertion())
        {
            failures.Add($"FAILED: {name}");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"ERROR: {name}: {ex.GetType().Name}: {ex.Message}");
    }
}

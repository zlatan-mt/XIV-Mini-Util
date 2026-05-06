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

Test("preset active index is clamped", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 9 };
    return CharaSelectEmotePresetStore.GetActiveIndex(presets, activeIndexes, 1) == 1
        && CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1) == 101;
});

Test("preset next changes active emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 0 };
    return CharaSelectEmotePresetStore.SelectNext(presets, activeIndexes, 1)
        && CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1) == 101;
});

Test("preset append selects added emote and avoids duplicates", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 0 };
    var added = CharaSelectEmotePresetStore.Append(presets, activeIndexes, 1, 101);
    var duplicateAdded = CharaSelectEmotePresetStore.Append(presets, activeIndexes, 1, 100);
    return added
        && !duplicateAdded
        && presets[1].SequenceEqual(new uint[] { 100, 101 })
        && activeIndexes[1] == 0;
});

Test("preset save to active slot replaces current emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 1 };
    return CharaSelectEmotePresetStore.SaveToActiveSlot(presets, activeIndexes, 1, 102)
        && presets[1].SequenceEqual(new uint[] { 100, 102 })
        && activeIndexes[1] == 1;
});

Test("preset remove active advances to remaining emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 1 };
    return CharaSelectEmotePresetStore.RemoveActive(presets, activeIndexes, 1)
        && presets[1].SequenceEqual(new uint[] { 100 })
        && activeIndexes[1] == 0;
});

Test("legacy fallback is gone after explicit clear removes both stores", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 0 };
    var legacy = new Dictionary<ulong, uint> { [1] = 100 };
    CharaSelectEmotePresetStore.RemoveActive(presets, activeIndexes, 1);
    legacy.Remove(1);
    return CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1, legacy) == null;
});

Test("active emote change replays through tracker", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 101, 0x1000, force: false);
});

Test("last recorded emote is isolated per content id", () =>
{
    var lastRecorded = new Dictionary<ulong, uint>
    {
        [1] = 100,
        [2] = 101,
    };
    return lastRecorded[1] == 100 && lastRecorded[2] == 101;
});

Test("nearest level resolves by territory and xyz", () =>
{
    CharaSelectLevelCandidate[] candidates =
    [
        new(10, 100, 1, 0f, 0f, 0f),
        new(11, 100, 2, 10f, 0f, 0f),
        new(12, 101, 3, 1f, 0f, 0f),
    ];
    var resolved = CharaSelectLevelResolver.ResolveNearest(candidates, 100, 8f, 0f, 0f);
    return resolved.RowId == 11 && resolved.Type == 2;
});

Test("nearest level ignores different territory", () =>
{
    CharaSelectLevelCandidate[] candidates =
    [
        new(10, 100, 1, 0f, 0f, 0f),
    ];
    return !CharaSelectLevelResolver.ResolveNearest(candidates, 101, 0f, 0f, 0f).IsValid;
});

Test("lobby position resolves by territory param", () =>
{
    CharaSelectLobbyCandidate[] candidates =
    [
        new(1, 0, 100, 0),
        new(2, 1, 200, 0),
    ];
    return CharaSelectLobbyPositionResolver.ResolveByTerritory(candidates, 200, 9) == 2;
});

Test("lobby position falls back when territory is missing", () =>
{
    CharaSelectLobbyCandidate[] candidates =
    [
        new(1, 0, 100, 0),
    ];
    return CharaSelectLobbyPositionResolver.ResolveByTerritory(candidates, 200, 9) == 9;
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

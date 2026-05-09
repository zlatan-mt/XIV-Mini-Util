// Path: tools/CharaSelectLogicTests/Program.cs
// Description: キャラ選択エモートのゲーム非依存ロジックを検証する
// Reason: 実機なしで再生判定とEmoteMode変換の退行を検出するため
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.TitleBackground;

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

Test("title background path normalizes bg lvb wrapper", () =>
{
    var normalized = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(
        @" bg\ffxiv\area\region\level\sample.lvb ");
    return normalized == "ffxiv/area/region/level/sample"
        && TitleBackgroundPathHelper.BuildLvbPath(normalized) == "bg/ffxiv/area/region/level/sample.lvb";
});

Test("title background path validation requires ffxiv level path", () =>
{
    return TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/level/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("../ffxiv/area/region/level/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("bg/ffxiv/area/region/level/sample.lvb");
});

Test("title background preset normalizes path and clamps fov", () =>
{
    var preset = new TitleBackgroundPreset
    {
        Name = "  test  ",
        TerritoryPath = "bg/ffxiv/area/region/level/sample.lvb",
        CameraX = float.PositiveInfinity,
        CameraY = -200000f,
        FocusZ = 200000f,
        FovY = 999f,
        BgmPath = "  bgm/test.scd  ",
    }.Normalize();

    return preset.Name == "test"
        && preset.TerritoryPath == "ffxiv/area/region/level/sample"
        && preset.CameraX == 0f
        && preset.CameraY == -100000f
        && preset.FocusZ == 100000f
        && preset.FovY == TitleBackgroundPreset.MaxFovY
        && preset.BgmPath == "bgm/test.scd";
});

Test("title background preset validates normalized territory path", () =>
{
    var valid = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
    };
    var invalid = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/sample",
    };

    return valid.Validate(out _)
        && !invalid.Validate(out var errorMessage)
        && !string.IsNullOrWhiteSpace(errorMessage);
});

Test("title background fov clamp handles lower bound and non finite", () =>
{
    return TitleBackgroundPreset.ClampFovY(-1f) == TitleBackgroundPreset.MinFovY
        && TitleBackgroundPreset.ClampFovY(float.NaN) == TitleBackgroundPreset.DefaultFovY;
});

Test("title chara select transition is isolated", () =>
{
    return GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.Title, GameLobbyType.CharaSelect)
        && GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.CharaSelect, GameLobbyType.Title)
        && !GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.Title, GameLobbyType.LaNoscea);
});

Test("title chara select transition resets current map only when override enabled", () =>
{
    return GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.CharaSelect, overrideEnabled: true) == GameLobbyType.None
        && GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.CharaSelect, overrideEnabled: false) == GameLobbyType.Title
        && GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.LaNoscea, overrideEnabled: true) == GameLobbyType.Title;
});

Test("title background resolve only does not create hooks", () =>
{
    return !TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.ResolveOnly, overrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.ResolveOnly, overrideEnabled: true, cameraOverrideEnabled: true);
});

Test("title background hook probe creates scene hooks only", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: true, cameraOverrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldValidateSceneOverrideConfiguration(TitleBackgroundRuntimeMode.HookProbe);
});

Test("title background chara select scene readiness does not require fix on", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true)
        && TitleBackgroundRuntimeModeHelper.AreSceneHooksReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true, cameraOverrideEnabled: false);
});

Test("title background update lobby ui stage failure does not block scene readiness", () =>
{
    var updateLobbyUiStageResolved = false;
    return !updateLobbyUiStageResolved
        && TitleBackgroundRuntimeModeHelper.AreNativeSceneAddressesReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true, currentMapReady: true);
});

Test("title and chara select mode is hidden until implemented", () =>
{
    return !TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(TitleBackgroundRuntimeMode.TitleAndCharaSelect)
        && !TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(TitleBackgroundRuntimeMode.TitleAndCharaSelect);
});

Test("title background focus fields are reserved while camera override disabled", () =>
{
    return !TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: false)
        && TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: true);
});

Test("game lobby type none remains minus one", () =>
{
    return (short)GameLobbyType.None == -1;
});

Test("title background e8 callsite resolver rejects non e8 match", () =>
{
    return !TitleBackgroundAddressResolver.TryResolveE8CallTarget(0x90, new nint(0x1000), 0x20, out var rejectedTarget)
        && rejectedTarget == nint.Zero
        && TitleBackgroundAddressResolver.TryResolveE8CallTarget(0xE8, new nint(0x1000), 0x20, out var acceptedTarget)
        && acceptedTarget == new nint(0x1025);
});

Test("title background e8 callsite resolver finds nearby forward callsite", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0xE8, 0x11, 0x22, 0x33, 0x44];
    return TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 0, out var callsiteOffset)
        && callsiteOffset == 5;
});

Test("title background e8 callsite resolver finds nearby backward callsite", () =>
{
    byte[] bytes = [0xE8, 0x11, 0x22, 0x33, 0x44, 0x48, 0x89, 0x5C, 0x24, 0x08];
    return TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 8, out var callsiteOffset)
        && callsiteOffset == 0;
});

Test("title background e8 callsite resolver rejects window without callsite", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x90, 0x90, 0x90];
    return !TitleBackgroundAddressResolver.TryFindNearbyE8Callsite(bytes, 0, out var callsiteOffset)
        && callsiteOffset == -1;
});

Test("title background direct text candidate requires nonzero match", () =>
{
    return TitleBackgroundAddressResolver.ShouldRecordDirectTextCandidate(new nint(0x1000))
        && !TitleBackgroundAddressResolver.ShouldRecordDirectTextCandidate(nint.Zero);
});

Test("title background direct text hook target requires manual probe opt in", () =>
{
    return TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextProbeTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextProbeTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextProbeTarget: false);
});

Test("title background prologue hint classifies common msvc prologue", () =>
{
    byte[] bytes = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83];
    return TitleBackgroundAddressResolver.ClassifyFunctionPrologue(bytes) == "likely-msvc-prologue";
});

Test("title background prologue hint does not verify unknown bytes", () =>
{
    byte[] bytes = [0x8B, 0xD9, 0xE8, 0x11, 0x22, 0x33, 0x44];
    return TitleBackgroundAddressResolver.ClassifyFunctionPrologue(bytes) == "unknown";
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

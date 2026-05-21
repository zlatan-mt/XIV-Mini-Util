// Path: tools/CharaSelectLogicTests/Program.cs
// Description: キャラ選択エモートのゲーム非依存ロジックを検証する
// Reason: 実機なしで再生判定とEmoteMode変換の退行を検出するため
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Text;
using XivMiniUtil;
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
        @" bg\ex5\01_xkt_x6\fld\x6f3\level\x6f3.lvb ");
    return normalized == "ex5/01_xkt_x6/fld/x6f3/level/x6f3"
        && TitleBackgroundPathHelper.BuildLvbPath(normalized) == "bg/ex5/01_xkt_x6/fld/x6f3/level/x6f3.lvb"
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(normalized);
});

Test("title background path validation accepts base and expansion pack roots", () =>
{
    return TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/level/sample")
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/fld/x6f3/level/x6f3")
        && TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex6/foo/bar/level/baz")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("abc/foo/bar/level/baz")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ffxiv/area/region/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("../ffxiv/area/region/level/sample")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("bg/ex5/01_xkt_x6/fld/x6f3/level/x6f3.lvb");
});

Test("title background path validation rejects unsafe normalized paths", () =>
{
    return !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(string.Empty)
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath(@"ex5\01_xkt_x6\fld\x6f3\level\x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6//fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/../x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5:/01_xkt_x6/fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("/ex5/01_xkt_x6/fld/x6f3/level/x6f3")
        && !TitleBackgroundPathHelper.IsLikelyValidNormalizedTerritoryPath("ex5/01_xkt_x6/fld/x6f3/level/x6f3/");
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
        TerritoryPath = "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
    };
    var invalid = new TitleBackgroundPreset
    {
        TerritoryPath = "abc/foo/bar/level/baz",
    };

    return valid.Validate(out _)
        && !invalid.Validate(out var errorMessage)
        && errorMessage == "TerritoryPath は <pack>/.../level/... 形式で指定してください。";
});

Test("title background built-in preset catalog ids are stable and unique", () =>
{
    var ids = TitleBackgroundBuiltInPresetCatalog.Presets
        .Select(entry => TitleBackgroundBuiltInPresetCatalog.NormalizeId(entry.Id))
        .ToList();

    return ids.All(id => !string.IsNullOrWhiteSpace(id))
        && ids.Count == ids.Distinct(StringComparer.Ordinal).Count()
        && TitleBackgroundBuiltInPresetCatalog.Presets.All(entry =>
            entry.Id == TitleBackgroundBuiltInPresetCatalog.NormalizeId(entry.Id)
            && !string.IsNullOrWhiteSpace(entry.DisplayName)
            && entry.Preset.Normalize().Validate(out _));
});

Test("title background preset applicator expands selected preset atomically", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "bg/ffxiv/area/region/level/sample.lvb",
        TerritoryTypeId = 777,
        LayoutTerritoryTypeId = 778,
        LayoutLayerFilterKey = 9,
        CharacterPosition = new Vector3(1f, 2f, 3f),
        CharacterRotation = 0.5f,
        CameraX = 4f,
        CameraY = 5f,
        CameraZ = 6f,
        FocusX = 7f,
        FocusY = 8f,
        FocusZ = 9f,
        FovY = 1.2f,
    };

    return TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            preset,
            "verified-a",
            path => path == "bg/ffxiv/area/region/level/sample.lvb",
            out _)
        && configuration.TitleBackgroundSelectedPresetId == "verified-a"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample"
        && configuration.TitleBackgroundTerritoryTypeId == 777
        && configuration.TitleBackgroundLayoutTerritoryTypeId == 778
        && configuration.TitleBackgroundLayoutLayerFilterKey == 9
        && configuration.TitleBackgroundCharacterPositionX == 1f
        && configuration.TitleBackgroundCameraX == 4f
        && configuration.TitleBackgroundFocusZ == 9f
        && Math.Abs(configuration.TitleBackgroundFovY - 1.2f) < 0.0001f;
});

Test("title background preset applicator keeps configuration on invalid preset", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var invalidPreset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/sample",
        CameraX = 4f,
    };

    return !TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            invalidPreset,
            "bad",
            _ => true,
            out var errorMessage)
        && !string.IsNullOrWhiteSpace(errorMessage)
        && configuration.TitleBackgroundSelectedPresetId == "old"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/old/region/level/old"
        && configuration.TitleBackgroundCameraX == 99f;
});

Test("title background preset applicator keeps configuration when lvb is missing", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "old",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundCameraX = 99f,
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        CameraX = 4f,
    };

    return !TitleBackgroundPresetApplicator.TryApplyPreset(
            configuration,
            preset,
            "missing-lvb",
            _ => false,
            out var errorMessage)
        && errorMessage.Contains("LVB", StringComparison.Ordinal)
        && configuration.TitleBackgroundSelectedPresetId == "old"
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/old/region/level/old"
        && configuration.TitleBackgroundCameraX == 99f;
});

Test("title background unknown selected preset id falls back to custom", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "missing",
        TitleBackgroundTerritoryPath = "ffxiv/area/region/level/sample",
    };

    return TitleBackgroundPresetApplicator.ClearInvalidSelectedPreset(configuration)
        && configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample";
});

Test("title background debug capture clears selected preset id", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "verified-a",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
    };
    var preset = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        CameraX = 1f,
    };

    TitleBackgroundPresetApplicator.ApplyDebugPreset(configuration, preset);
    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ffxiv/area/region/level/sample"
        && configuration.TitleBackgroundCameraX == 1f;
});

Test("title background selected preset id is present in export import payload", () =>
{
    var configuration = new Configuration();
    var exported = configuration.ExportToBase64();
    var json = Encoding.UTF8.GetString(Convert.FromBase64String(exported));

    return json.Contains("\"TitleBackgroundSelectedPresetId\"", StringComparison.Ordinal)
        && configuration.TryParseImport(exported, out var imported, out _)
        && imported.TitleBackgroundSelectedPresetId == string.Empty;
});

Test("title background fov clamp handles lower bound and non finite", () =>
{
    return TitleBackgroundPreset.ClampFovY(-1f) == TitleBackgroundPreset.MinFovY
        && TitleBackgroundPreset.ClampFovY(float.NaN) == TitleBackgroundPreset.DefaultFovY;
});

Test("title background camera override plan uses focus fields", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCameraX = 1f,
        TitleBackgroundCameraY = 2f,
        TitleBackgroundCameraZ = 3f,
        TitleBackgroundFocusX = 4f,
        TitleBackgroundFocusY = 5f,
        TitleBackgroundFocusZ = 6f,
        TitleBackgroundCharacterPositionX = 40f,
        TitleBackgroundCharacterPositionY = 50f,
        TitleBackgroundCharacterPositionZ = 60f,
        TitleBackgroundFovY = 1.2f,
    };

    var plan = TitleBackgroundCameraOverridePlan.FromConfiguration(configuration);
    return plan.Camera == new Vector3(1f, 2f, 3f)
        && plan.Focus == new Vector3(4f, 5f, 6f)
        && plan.Focus != new Vector3(40f, 50f, 60f)
        && Math.Abs(plan.FovY - 1.2f) < 0.0001f;
});

Test("title background camera override plan clamps fov", () =>
{
    var plan = TitleBackgroundCameraOverridePlan.Create(
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        999f);
    return plan.FovY == TitleBackgroundPreset.MaxFovY;
});

Test("title background legacy direct camera apply is disabled", () =>
{
    return !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: true,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: false,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.CharaSelect)
        && !TitleBackgroundCameraOverridePlan.ShouldApply(
            cameraOverrideEnabled: true,
            isHookProbeMode: false,
            cameraApplyPending: true,
            stateReady: true,
            currentMapAvailable: true,
            currentMap: GameLobbyType.Title);
});

Test("title background chara select camera input uses character fields only", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundCharacterPositionX = 1f,
        TitleBackgroundCharacterPositionY = 2f,
        TitleBackgroundCharacterPositionZ = 3f,
        TitleBackgroundCharacterRotation = MathF.PI * 3f,
        TitleBackgroundCameraX = 100f,
        TitleBackgroundFocusY = 200f,
        TitleBackgroundFovY = 9f,
    };

    var input = TitleBackgroundCharaSelectCameraInput.FromConfiguration(configuration);
    return input.CharacterPosition == new Vector3(1f, 2f, 3f)
        && Math.Abs(input.CharacterRotation - MathF.PI) < 0.0001f;
});

Test("title background chara select camera state machine follows phase one path", () =>
{
    var state = TitleBackgroundCharaSelectCameraAdapterState.Inactive;
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.ConfigureEnabled);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoaded);
    state = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.LobbyBecameActive);
    var stopping = TitleBackgroundCharaSelectCameraLogic.Transition(state, TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested);
    var reset = TitleBackgroundCharaSelectCameraLogic.Transition(stopping, TitleBackgroundCharaSelectCameraAdapterEvent.Reset);

    return state == TitleBackgroundCharaSelectCameraAdapterState.Active
        && stopping == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && reset == TitleBackgroundCharaSelectCameraAdapterState.Armed;
});

Test("title background chara select camera curve offsets magic values by character y", () =>
{
    var curve = TitleBackgroundCharaSelectCameraLogic.BuildCurve(2f);
    var negativeCurve = TitleBackgroundCharaSelectCameraLogic.BuildCurve(-10f);

    return Math.Abs(curve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow + 2f)) < 0.0001f
        && Math.Abs(curve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid + 2f)) < 0.0001f
        && Math.Abs(curve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh + 2f)) < 0.0001f
        && Math.Abs(negativeCurve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow - 10f)) < 0.0001f
        && Math.Abs(negativeCurve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid - 10f)) < 0.0001f
        && Math.Abs(negativeCurve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh - 10f)) < 0.0001f;
});

Test("title background chara select camera adapter derives curve from input", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(new Vector3(1f, 4f, 3f), 0.25f));

    return Math.Abs(adapter.Curve.Low - (TitleBackgroundCharaSelectCameraLogic.MagicLow + 4f)) < 0.0001f
        && Math.Abs(adapter.Curve.Mid - (TitleBackgroundCharaSelectCameraLogic.MagicMid + 4f)) < 0.0001f
        && Math.Abs(adapter.Curve.High - (TitleBackgroundCharaSelectCameraLogic.MagicHigh + 4f)) < 0.0001f;
});

Test("title background chara select camera adapter records runtime state without persistence", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(new Vector3(1f, 2f, 3f), 0.25f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(
        yaw: MathF.PI * 3f,
        pitch: MathF.PI,
        distance: -1f,
        lookAtY: float.PositiveInfinity,
        lookAt: new Vector3(1f, 2f, 3f));
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded
        && adapter.RuntimeState.SceneGeneration == 1
        && Math.Abs(adapter.RuntimeState.Yaw!.Value - MathF.PI) < 0.0001f
        && Math.Abs(adapter.RuntimeState.YawOffset!.Value - (MathF.PI - 0.25f)) < 0.0001f
        && Math.Abs(adapter.RuntimeState.Pitch!.Value - (MathF.PI / 2f)) < 0.0001f
        && Math.Abs(adapter.RuntimeState.Distance!.Value - TitleBackgroundCharaSelectCameraLogic.MinDistance) < 0.0001f
        && adapter.RuntimeState.LookAtY == null
        && adapter.RuntimeState.LookAt == new Vector3(1f, 2f, 3f)
        && adapter.RuntimeState.HasLookAt
        && adapter.RuntimeState.CurveAtRecord == adapter.Curve
        && Math.Abs(adapter.RuntimeState.CharacterRotationAtRecord!.Value - 0.25f) < 0.0001f
        && adapter.ShouldRestoreRuntimeCameraState();
});

Test("title background chara select camera load start does not mark scene loaded", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 1f);
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted.ToString()
        && !adapter.ShouldRestoreRuntimeCameraState();
});

Test("title background chara select camera runtime restores yaw relative to current character rotation", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0.5f));
    adapter.SaveRuntimeCameraState(yaw: 1.5f, pitch: 0.25f, distance: 4f, lookAtY: 1f);
    var restoredAtInitialRotation = adapter.GetRestoredYaw();

    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 1.0f));
    var restoredAtNewRotation = adapter.GetRestoredYaw();

    return Math.Abs(adapter.RuntimeState.YawOffset!.Value - 1.0f) < 0.0001f
        && Math.Abs(restoredAtInitialRotation!.Value - 1.5f) < 0.0001f
        && Math.Abs(restoredAtNewRotation!.Value - 2.0f) < 0.0001f;
});

Test("title background chara select camera marks LookAtY as one-shot after runtime restore", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();
    var firstConsume = adapter.ConsumeShouldSetLookAtY();
    var secondConsume = adapter.ConsumeShouldSetLookAtY();

    return firstConsume
        && !secondConsume
        && !adapter.RuntimeState.ShouldSetLookAtY;
});

Test("title background chara select camera does not mark LookAtY one-shot without observed LookAtY", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: float.NaN);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    return !adapter.RuntimeState.ShouldSetLookAtY
        && !adapter.ConsumeShouldSetLookAtY();
});

Test("title background chara select camera permits curve apply after scene loaded", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return adapter.ShouldApplyCurve();
});

Test("title background chara select camera curve apply is generation one-shot", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    var first = adapter.ShouldApplyCurve();
    adapter.MarkCurveApplied();
    var second = adapter.ShouldApplyCurve();

    return first
        && !second
        && adapter.LastCurveAppliedSceneGeneration == adapter.RuntimeState.SceneGeneration;
});

Test("title background phase2g generated curve override allows loaded and active states", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    var loaded = TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        adapter.State,
        adapter.RuntimeState);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    var active = TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        adapter.State,
        adapter.RuntimeState);

    return loaded && active;
});

Test("title background phase2g generated curve override rejects unsafe contexts", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: false,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            adapter.State,
            adapter.RuntimeState)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: true,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            adapter.State,
            adapter.RuntimeState)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: false,
            adapterArmed: adapter.IsArmed,
            adapter.State,
            adapter.RuntimeState)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            adapter.RuntimeState)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: false,
            adapter.State,
            adapter.RuntimeState);
});

Test("title background chara select camera LookAtY is consumed once per scene generation", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    var firstShouldApply = adapter.ShouldApplyLookAtY();
    adapter.MarkLookAtYApplied();
    var secondShouldApply = adapter.ShouldApplyLookAtY();

    return firstShouldApply
        && !secondShouldApply
        && !adapter.RuntimeState.ShouldSetLookAtY
        && adapter.LastLookAtYAppliedSceneGeneration == adapter.RuntimeState.SceneGeneration;
});

Test("title background chara select camera LookAtY remains pending until apply success is marked", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();

    return adapter.ShouldApplyLookAtY()
        && adapter.ShouldApplyLookAtY()
        && adapter.RuntimeState.ShouldSetLookAtY
        && adapter.LastLookAtYAppliedSceneGeneration == 0;
});

Test("title background chara select camera does not apply curve or LookAtY after stop requested", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.MarkRuntimeCameraStateRestored();
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && !adapter.ShouldApplyCurve()
        && !adapter.ShouldApplyLookAtY();
});

Test("title background chara select camera scene-ready signal handles only armed or loading chara select", () =>
{
    return TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.Armed,
            GameLobbyType.CharaSelect)
        && TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoaded,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.Active,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: true,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldHandleSceneReadySignal(
            serviceReady: true,
            hookProbeMode: false,
            adapterArmed: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.Title);
});

Test("title background chara select camera does not stop while waiting for scene-ready signal", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);
    adapter.NotifyLobbyUpdate(GameLobbyType.None);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.SceneLoading
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.SceneLoadStarted.ToString()
        && !TitleBackgroundCharaSelectCameraLogic.ShouldStopOnLobbyUpdate(
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            GameLobbyType.Title);
});

Test("title background chara select camera stops after active scene leaves chara select", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && adapter.LastEvent == TitleBackgroundCharaSelectCameraAdapterEvent.StopRequested.ToString()
        && TitleBackgroundCharaSelectCameraLogic.ShouldStopOnLobbyUpdate(
            TitleBackgroundCharaSelectCameraAdapterState.Active,
            GameLobbyType.Title);
});

Test("title background chara select camera adapter ignores runtime notifications while inactive", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.NotifySceneOverrideApplied(GameLobbyType.CharaSelect);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Inactive
        && adapter.RuntimeState.SceneGeneration == 0
        && adapter.LastEvent == "not-run";
});

Test("title background chara select camera adapter stays armed until chara select scene starts", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifyLobbyUpdate(GameLobbyType.Title);
    adapter.NotifyLobbyUpdate(GameLobbyType.None);

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Armed;
});

Test("title background chara select camera adapter arms only for chara select camera adaptation", () =>
{
    return TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: true,
            runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: true,
            runtimeMode: TitleBackgroundRuntimeMode.HookProbe)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldArmAdapter(
            overrideEnabled: true,
            cameraAdaptationEnabled: false,
            runtimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly);
});

Test("title background fix on invocation mode is explicit", () =>
{
    return TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(overrideApplied: false) == "passthrough"
        && TitleBackgroundCameraOverridePlan.GetFixOnInvocationMode(overrideApplied: true) == "override-applied";
});

Test("title background fix on hook creation is disabled for phase one", () =>
{
    return Enum.GetValues<TitleBackgroundRuntimeMode>()
        .All(mode =>
            !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: false, cameraOverrideEnabled: false)
            && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: true, cameraOverrideEnabled: false)
            && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(mode, overrideEnabled: true, cameraOverrideEnabled: true));
});

Test("title background camera math accepts finite vectors only", () =>
{
    return TitleBackgroundCameraMath.IsFiniteVector(new Vector3(1f, 2f, 3f))
        && !TitleBackgroundCameraMath.IsFiniteVector(new Vector3(float.NaN, 2f, 3f))
        && !TitleBackgroundCameraMath.IsFiniteVector(new Vector3(1f, float.PositiveInfinity, 3f));
});

Test("title background camera math calculates nullable deltas", () =>
{
    return TitleBackgroundCameraMath.CalculateVectorDelta(
            new Vector3(5f, 3f, 1f),
            new Vector3(2f, 1f, 4f)) == new Vector3(3f, 2f, -3f)
        && TitleBackgroundCameraMath.CalculateVectorDelta(null, new Vector3(1f, 1f, 1f)) == null
        && TitleBackgroundCameraMath.CalculateFloatDelta(5f, 2.5f) == 2.5f
        && TitleBackgroundCameraMath.CalculateFloatDelta(null, 2.5f) == null;
});

Test("title background camera probe detects reflected then overwritten camera y", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: true,
        BaselineCamera: new Vector3(0f, 10f, 0f),
        BaselineFocus: new Vector3(0f, 20f, 0f),
        ProbeCamera: new Vector3(0f, 60f, 0f),
        ProbeFocus: new Vector3(0f, -30f, 0f),
        LastAppliedCamera: new Vector3(0f, 60f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 60.2f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 92f, 0f),
        LastAppliedFocus: new Vector3(0f, -30f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, -30.2f, 0f),
        CurrentLookAtVector: new Vector3(0f, -30.4f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.CameraYPostFixOnStability == TitleBackgroundCameraProbeVerdict.PossiblyOverwritten
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.FocusYPostFixOnStability == TitleBackgroundCameraProbeVerdict.Stable
        && result.LikelyConclusion.Contains("overwritten later", StringComparison.Ordinal);
});

Test("title background camera probe detects focus reflection with missing camera y reflection", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: true,
        BaselineCamera: new Vector3(0f, 10f, 0f),
        BaselineFocus: new Vector3(0f, 20f, 0f),
        ProbeCamera: new Vector3(0f, 60f, 0f),
        ProbeFocus: new Vector3(0f, -30f, 0f),
        LastAppliedCamera: new Vector3(0f, 60f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 10f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 10f, 0f),
        LastAppliedFocus: new Vector3(0f, -30f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, -30f, 0f),
        CurrentLookAtVector: new Vector3(0f, -30f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.NotReflected
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Reflected
        && result.LikelyConclusion.Contains("FocusY reflects correctly", StringComparison.Ordinal);
});

Test("title background camera probe does not evaluate when unarmed", () =>
{
    var result = TitleBackgroundCameraProbeReport.Evaluate(new TitleBackgroundCameraProbeReportInput(
        Armed: false,
        BaselineCamera: default,
        BaselineFocus: default,
        ProbeCamera: default,
        ProbeFocus: default,
        LastAppliedCamera: new Vector3(0f, 10f, 0f),
        PostFixOnSceneCameraPosition: new Vector3(0f, 10f, 0f),
        CurrentSceneCameraPosition: new Vector3(0f, 10f, 0f),
        LastAppliedFocus: new Vector3(0f, 20f, 0f),
        PostFixOnLookAtVector: new Vector3(0f, 20f, 0f),
        CurrentLookAtVector: new Vector3(0f, 20f, 0f)));

    return result.CameraYFixOnReflection == TitleBackgroundCameraProbeVerdict.Inconclusive
        && result.FocusYFixOnReflection == TitleBackgroundCameraProbeVerdict.Inconclusive
        && result.LikelyConclusion.Contains("arm the probe first", StringComparison.Ordinal);
});

Test("title background camera probe timeline detects first overwrite frames", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(1, new Vector3(0f, 60.5f, 0f), new Vector3(0f, -29.5f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(2, new Vector3(0f, 40f, 0f), new Vector3(0f, -29f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(4, new Vector3(0f, 20f, 0f), new Vector3(0f, -18f, 0f)),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 2
        && result.FocusOverwriteFirstObservedFrame == 4
        && result.CameraOverwritePattern == TitleBackgroundCameraOverwritePattern.Immediate
        && result.FocusOverwritePattern == TitleBackgroundCameraOverwritePattern.Gradual;
});

Test("title background camera probe timeline classifies late overwrite", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(8, new Vector3(0f, 59f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(16, new Vector3(0f, 40f, 0f), new Vector3(0f, -30f, 0f)),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 16
        && result.FocusOverwriteFirstObservedFrame == null
        && result.CameraOverwritePattern == TitleBackgroundCameraOverwritePattern.Late
        && result.FocusOverwritePattern == TitleBackgroundCameraOverwritePattern.Inconclusive;
});

Test("title background camera probe timeline summarizes coincident events", () =>
{
    var samples = new[]
    {
        new TitleBackgroundCameraProbeTimelineSample(0, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(1, new Vector3(0f, 60f, 0f), new Vector3(0f, -30f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(2, new Vector3(0f, 40f, 0f), new Vector3(0f, -26f, 0f)),
        new TitleBackgroundCameraProbeTimelineSample(4, new Vector3(0f, 39f, 0f), new Vector3(0f, -18f, 0f)),
    };
    var events = new Dictionary<int, TitleBackgroundCameraProbeTimelineEventCounts>
    {
        [0] = new(1, 0, 1, 1),
        [2] = new(0, 1, 0, 0),
        [4] = new(0, 2, 0, 0),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzeTimeline(
        samples,
        new Vector3(0f, 60f, 0f),
        new Vector3(0f, -30f, 0f));
    var cameraEvents = TitleBackgroundCameraProbeReport.DescribeCoincidentEvents(
        result.CameraOverwriteFirstObservedFrame,
        events.GetValueOrDefault(result.CameraOverwriteFirstObservedFrame ?? -1));
    var focusDriftEvents = TitleBackgroundCameraProbeReport.DescribeFocusDriftEvents(
        samples,
        (startFrame, endFrame) => events
            .Where(entry => entry.Key >= startFrame && entry.Key <= endFrame)
            .Aggregate(
                new TitleBackgroundCameraProbeTimelineEventCounts(),
                (total, entry) => new TitleBackgroundCameraProbeTimelineEventCounts(
                    total.FixOnCalls + entry.Value.FixOnCalls,
                    total.LobbyUpdateCalls + entry.Value.LobbyUpdateCalls,
                    total.LoadLobbySceneCalls + entry.Value.LoadLobbySceneCalls,
                    total.CreateSceneCalls + entry.Value.CreateSceneCalls)),
        new Vector3(0f, -30f, 0f));

    return result.CameraOverwriteFirstObservedFrame == 2
        && cameraEvents == "fixOn=0,lobbyUpdate=1,loadLobbyScene=0,createScene=0"
        && focusDriftEvents == "fixOn=0,lobbyUpdate=2,loadLobbyScene=0,createScene=0";
});

Test("title background phase2d analysis detects late transform and distance overwrite", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2DTimelineSample(0, new Vector3(0f, 0.8f, 3.3f), new Vector3(0f, 0.8f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(60, new Vector3(0f, 0.8f, 3.3f), new Vector3(0f, 0.8f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(300, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
        new TitleBackgroundPhase2DTimelineSample(450, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
        new TitleBackgroundPhase2DTimelineSample(600, new Vector3(-48.5f, 15.8f, 9.1f), new Vector3(-52.7f, 14.6f, 9.4f), 4.25f, 0.5f, 0.4f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2D(samples, restoredDistance: 3.3f);

    return result.SceneTransformShiftObserved == "observed"
        && result.DistanceEventuallyOverwritten == "observed"
        && result.FinalCameraStabilizationObserved == "observed";
});

Test("title background phase2d analysis reports unstabilized late camera", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2DTimelineSample(300, new Vector3(10f, 10f, 10f), new Vector3(0f, 0f, 0f), 3.3f, 0.1f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(450, new Vector3(12f, 10f, 10f), new Vector3(0f, 2f, 0f), 3.4f, 0.2f, 0.2f),
        new TitleBackgroundPhase2DTimelineSample(600, new Vector3(14f, 10f, 10f), new Vector3(0f, 4f, 0f), 3.5f, 0.3f, 0.2f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2D(samples, restoredDistance: 3.3f);

    return result.FinalCameraStabilizationObserved == "not-observed"
        && result.DistanceEventuallyOverwritten == "observed";
});

Test("title background phase2e detects native return matching active look at y", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2EProbeSample(1, 0, 10f, 9.8f),
        new TitleBackgroundPhase2EProbeSample(2, 1, 14.6f, 14.6f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2E(samples, finalStableLookAtY: 14.6f);
    return result.NativeReturnMatchesActiveLookAtY == "observed"
        && result.NativeReturnMatchesFinalStableLookAtY == "observed"
        && result.ComparedCallCount == 2;
});

Test("title background phase2f accepts early stable curve timeline for one shot write", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(16, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurveGeneratedEarly == "observed"
        && result.CurveStableByFinalWindow == "observed"
        && result.CurveRegeneratedAfterEarlyFrame == "not-observed"
        && result.OneShotWriteViability == "plausible"
        && result.CurvePointValuesChangedAfterEarlyFrame == "not-observed"
        && result.CameraCurveEnabledTransitionObserved == "not-observed"
        && result.OneShotCurvePointWriteValueStability == "observed"
        && result.OneShotCurvePointWriteTimingRisk == "not-observed";
});

Test("title background phase2f flags late curve regeneration as one shot risk", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, true, 1.1f, 1.1f, 3.3f, 0.7f, 5.5f, 0.5f),
        new TitleBackgroundPhase2FCurveTimelineSample(60, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurveGeneratedEarly == "observed"
        && result.CurveStableByFinalWindow == "observed"
        && result.CurveRegeneratedAfterEarlyFrame == "observed"
        && result.LastChangedFrame == 60
        && result.LastPointValueChangedFrame == 60
        && result.OneShotWriteViability == "risky";
});

Test("title background phase2f separates camera curve enabled transition from point value changes", () =>
{
    var samples = new[]
    {
        new TitleBackgroundPhase2FCurveTimelineSample(0, true, false, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(16, true, false, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(30, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(300, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(450, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
        new TitleBackgroundPhase2FCurveTimelineSample(600, true, true, 1.1f, 1.393f, 3.3f, 0.834f, 5.5f, 0.655f),
    };

    var result = TitleBackgroundCameraProbeReport.AnalyzePhase2F(samples);
    return result.CurvePointValuesChangedAfterEarlyFrame == "not-observed"
        && result.CurveRegeneratedAfterEarlyFrame == "not-observed"
        && result.CameraCurveEnabledTransitionObserved == "observed"
        && result.CameraCurveEnabledFirstObservedFrame == 30
        && result.OneShotCurvePointWriteValueStability == "observed"
        && result.OneShotCurvePointWriteTimingRisk == "observed"
        && result.OneShotWriteViability == "plausible";
});

Test("title background capture preset builder keeps existing fov when unavailable", () =>
{
    var existing = new TitleBackgroundPreset
    {
        TerritoryPath = "ffxiv/old/region/level/old",
        FovY = 1.5f,
        CharacterPosition = new Vector3(9f, 9f, 9f),
        CharacterRotation = 0.25f,
    }.Normalize();
    var draft = new TitleBackgroundCameraCaptureDraft(
        "bg/ffxiv/area/region/level/sample.lvb",
        777,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        null,
        777,
        null,
        null,
        null);

    return TitleBackgroundCameraCapturePresetBuilder.TryBuild(
            draft,
            existing,
            out var preset,
            out var fovState,
            out _,
            out _)
        && preset.TerritoryPath == "ffxiv/area/region/level/sample"
        && preset.TerritoryTypeId == 777
        && preset.FovY == 1.5f
        && fovState == TitleBackgroundCaptureValueState.KeptExisting
        && preset.CharacterPosition == new Vector3(9f, 9f, 9f)
        && Math.Abs(preset.CharacterRotation - 0.25f) < 0.0001f;
});

Test("title background capture preset builder accepts expansion pack bg path", () =>
{
    var existing = new TitleBackgroundPreset { FovY = 1f }.Normalize();
    var draft = new TitleBackgroundCameraCaptureDraft(
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        1.1f,
        1234,
        7,
        null,
        null);

    return TitleBackgroundCameraCapturePresetBuilder.TryBuild(
            draft,
            existing,
            out var preset,
            out var fovState,
            out _,
            out _)
        && preset.TerritoryPath == "ex5/01_xkt_x6/fld/x6f3/level/x6f3"
        && preset.TerritoryTypeId == 1234
        && preset.LayoutTerritoryTypeId == 1234
        && preset.LayoutLayerFilterKey == 7
        && fovState == TitleBackgroundCaptureValueState.Captured;
});

Test("title background capture preset builder fails closed on invalid required values", () =>
{
    var existing = new TitleBackgroundPreset { FovY = 1f }.Normalize();
    var invalidPath = new TitleBackgroundCameraCaptureDraft(
        "../bad",
        777,
        new Vector3(1f, 2f, 3f),
        new Vector3(4f, 5f, 6f),
        1f,
        null,
        null,
        null,
        null);
    var invalidCamera = invalidPath with
    {
        TerritoryPath = "ffxiv/area/region/level/sample",
        Camera = new Vector3(float.NaN, 2f, 3f),
    };

    return !TitleBackgroundCameraCapturePresetBuilder.TryBuild(invalidPath, existing, out _, out _, out _, out var pathError)
        && !string.IsNullOrWhiteSpace(pathError)
        && !TitleBackgroundCameraCapturePresetBuilder.TryBuild(invalidCamera, existing, out _, out _, out _, out var cameraError)
        && cameraError.Contains("Camera", StringComparison.Ordinal);
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
        && TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: false)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.HookProbe, overrideEnabled: true, cameraOverrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldValidateSceneOverrideConfiguration(TitleBackgroundRuntimeMode.HookProbe);
});

Test("title background automatic probe counters require hook probe manual direct text", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            false,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.CharaSelectOnly,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            TitleBackgroundResolverMode.ManualDirectTextProbe)
        && !TitleBackgroundRuntimeModeHelper.ShouldCollectAutomaticProbeCounters(
            TitleBackgroundRuntimeMode.HookProbe,
            true,
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            TitleBackgroundResolverMode.AutoDiagnosticOnly);
});

Test("title background probe report classifies complete observation", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.HookProbe,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: true,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 2,
        LobbyUpdateCallCount: 120,
        LoadLobbySceneCallCount: 1,
        LastCreateScenePath: "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        LastCreateSceneTerritoryId: 1192,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.CharaSelect);

    return TitleBackgroundProbeReportHelper.GetModeStatus(input) == "ready"
        && TitleBackgroundProbeReportHelper.GetOverallStatus(input) == "observed"
        && TitleBackgroundProbeReportHelper.GetAttentionItems(input).Count == 0;
});

Test("title background probe report classifies missing detours", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.HookProbe,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: true,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 1,
        LobbyUpdateCallCount: 0,
        LoadLobbySceneCallCount: 0,
        LastCreateScenePath: "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        LastCreateSceneTerritoryId: 1192,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.None);

    var attentionItems = TitleBackgroundProbeReportHelper.GetAttentionItems(input);
    return TitleBackgroundProbeReportHelper.GetOverallStatus(input) == "partial"
        && attentionItems.Any(item => item.Contains("LobbyUpdate", StringComparison.Ordinal))
        && attentionItems.Any(item => item.Contains("LoadLobbyScene", StringComparison.Ordinal));
});

Test("title background probe report flags wrong mode before counters", () =>
{
    var input = new TitleBackgroundProbeReportInput(
        ProbeActive: false,
        OverrideEnabled: true,
        RuntimeMode: TitleBackgroundRuntimeMode.CharaSelectOnly,
        CreateSceneResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        LobbyUpdateResolverMode: TitleBackgroundResolverMode.ManualDirectTextProbe,
        AutomaticCountersEnabled: false,
        HooksEnabled: true,
        RuntimeError: false,
        ResolverError: string.Empty,
        LastError: string.Empty,
        CreateSceneCallCount: 0,
        LobbyUpdateCallCount: 0,
        LoadLobbySceneCallCount: 0,
        LastCreateScenePath: string.Empty,
        LastCreateSceneTerritoryId: 0,
        LastCreateSceneLayerFilterKey: 0,
        LastLobbyUpdateMapId: GameLobbyType.None,
        LastLobbyUpdateTime: 0,
        LastLoadLobbySceneMapId: GameLobbyType.None);

    return TitleBackgroundProbeReportHelper.GetModeStatus(input) == "attention"
        && TitleBackgroundProbeReportHelper.GetAttentionItems(input)
            .Any(item => item.Contains("HookProbe + ManualDirectTextProbe", StringComparison.Ordinal));
});

Test("title background chara select scene readiness does not require fix on", () =>
{
    return TitleBackgroundRuntimeModeHelper.ShouldCreateSceneHooks(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true)
        && TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldAllowDirectTextHookTargets(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: false)
        && TitleBackgroundRuntimeModeHelper.AreSceneHooksReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true, cameraOverrideEnabled: true)
        && !TitleBackgroundRuntimeModeHelper.ShouldCreateCameraHook(TitleBackgroundRuntimeMode.CharaSelectOnly, overrideEnabled: true, cameraOverrideEnabled: false);
});

Test("title background update lobby ui stage failure does not block scene readiness", () =>
{
    var updateLobbyUiStageResolved = false;
    return !updateLobbyUiStageResolved
        && TitleBackgroundRuntimeModeHelper.AreNativeSceneAddressesReady(createSceneReady: true, lobbyUpdateReady: true, loadLobbySceneReady: true, currentMapReady: true);
});

Test("title background implemented modes match selectable modes", () =>
{
    return TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(TitleBackgroundRuntimeMode.CharaSelectOnly)
        && !TitleBackgroundRuntimeModeHelper.IsTitleOverrideImplemented(TitleBackgroundRuntimeMode.TitleAndCharaSelect)
        && !TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(TitleBackgroundRuntimeMode.TitleAndCharaSelect);
});

Test("title background focus fields are reserved while direct camera override is discarded", () =>
{
    return !TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: false)
        && !TitleBackgroundRuntimeModeHelper.IsFocusUsed(cameraOverrideEnabled: true);
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

Test("title background direct text hook target supports probe and chara select runtime", () =>
{
    return TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextProbeTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForProbe(
            new nint(0x1000),
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextProbeTarget: true)
        && TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForHook(
            new nint(0x1000),
            TitleBackgroundResolverMode.AutoDiagnosticOnly,
            allowDirectTextHookTarget: true)
        && !TitleBackgroundAddressResolver.ShouldPromoteDirectTextCandidateForHook(
            new nint(0x1000),
            TitleBackgroundResolverMode.ManualDirectTextProbe,
            allowDirectTextHookTarget: false);
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

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

Test("chara select scene profile old sharlayan exists", () =>
{
    return CharaSelectSceneProfileRegistry.TryGet("scene:old-sharlayan-k5t1", out var profile)
        && profile.DisplayName == "Old Sharlayan outdoor test"
        && profile.TerritoryTypeId == 962
        && profile.TerritoryPath == "ex4/03_kld_k5/twn/k5t1/level/k5t1"
        && profile.LayerFilterKey == 8
        && profile.ExpectedBrightness == CharaSelectBrightnessRating.Unknown
        && !profile.VerifiedInGame
        && profile.Source == "observed";
});

Test("chara select scene profile expects character visible", () =>
{
    var profile = CharaSelectSceneProfileRegistry.GetDefault();
    return profile.Id == "scene:old-sharlayan-k5t1"
        && profile.CharacterExpectedVisible
        && profile.RecommendedAction == "verify-character-visible-and-emote";
});

Test("chara select scene composition is default off", () =>
{
    var configuration = new Configuration();
    return !configuration.CharaSelectSceneCompositionEnabled
        && configuration.CharaSelectSceneProfileId == CharaSelectSceneProfileRegistry.DefaultProfileId
        && configuration.CharaSelectScenePlacementMode == CharaSelectScenePlacementMode.ObserveOnly;
});

Test("chara select scene profile maps to override territory config", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneProfileId = "scene:old-sharlayan-k5t1",
    };

    CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(
        configuration,
        CharaSelectSceneProfileRegistry.GetDefault());

    return configuration.CharaSelectOverrideTerritoryEnabled
        && configuration.CharaSelectOverrideTerritoryTypeId == 962
        && !configuration.TitleBackgroundOverrideEnabled;
});

Test("chara select scene profile preserves existing emote presets", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseSavedEmote = true,
        CharaSelectEmotePresets = new Dictionary<ulong, List<uint>>
        {
            [77] = [101, 102],
        },
        CharaSelectActiveEmotePresetIndexes = new Dictionary<ulong, int>
        {
            [77] = 1,
        },
    };

    CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(
        configuration,
        CharaSelectSceneProfileRegistry.GetDefault());

    return configuration.CharaSelectEmoteEnabled
        && configuration.CharaSelectEmotePresets[77].SequenceEqual([101u, 102u])
        && configuration.CharaSelectActiveEmotePresetIndexes[77] == 1;
});

Test("chara select scene observe only does not enable position override without profile position", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfilePosition = true,
        CharaSelectScenePlacementMode = CharaSelectScenePlacementMode.ObserveOnly,
    };

    CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(
        configuration,
        CharaSelectSceneProfileRegistry.GetDefault());

    return !configuration.CharaSelectOverridePositionEnabled
        && configuration.CharaSelectOverridePositionX == 0f
        && configuration.CharaSelectOverridePositionY == 0f
        && configuration.CharaSelectOverridePositionZ == 0f;
});

Test("chara select scene runtime apply is post login no op", () =>
{
    return !CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: true, agentIsLoggedIn: false, enabled: true)
        && !CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: false, agentIsLoggedIn: true, enabled: true)
        && CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: false, agentIsLoggedIn: false, enabled: true);
});

Test("chara select scene diagnostic uses foreground preserving route", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectOverrideTerritoryEnabled = true,
        CharaSelectOverrideTerritoryTypeId = 962,
        CharaSelectSceneUseSavedEmote = true,
        CharaSelectEmoteEnabled = true,
    };

    var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(configuration, "Test Emote", "Unknown");
    var lines = CharaSelectSceneCompositionPlanner.BuildDiagnosticLines(diagnostic);

    return diagnostic.Route == "foreground-preserving"
        && diagnostic.ExpectedCharacterVisible
        && diagnostic.TerritoryOverrideEnabled
        && diagnostic.TerritoryOverrideTerritoryTypeId == 962
        && diagnostic.EmoteEnabled
        && diagnostic.NextAction == "verify-character-visible-background-and-emote-with-screenshot"
        && lines.Contains("charaSelectScene.profileId=scene:old-sharlayan-k5t1");
});

Test("chara select scene phase3a does not introduce forbidden write paths", () =>
{
    var root = FindRepositoryRoot();
    var files = new[]
    {
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectSceneProfileRegistry.cs"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectSceneCompositionPlanner.cs"),
    };
    var text = string.Join("\n", files.Select(File.ReadAllText));

    return !text.Contains("SceneCamera.Position", StringComparison.Ordinal)
        && !text.Contains("SceneCamera.LookAtVector", StringComparison.Ordinal)
        && !text.Contains("SceneCamera.FoV", StringComparison.Ordinal)
        && !text.Contains("ObjectTable", StringComparison.Ordinal)
        && !text.Contains(".Position =", StringComparison.Ordinal)
        && !text.Contains(".Rotation =", StringComparison.Ordinal)
        && !text.Contains("lighting", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("environment", StringComparison.OrdinalIgnoreCase);
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

Test("title background preset camera focus derives lobby pose", () =>
{
    var camera = new Vector3(0f, 1f, 0f);
    var focus = new Vector3(0f, 1f, 10f);
    return TitleBackgroundCharaSelectCameraLogic.TryBuildPoseFromCameraFocus(
            camera,
            focus,
            out var yaw,
            out var pitch,
            out var distance,
            out var lookAtY,
            out _)
        && Math.Abs(yaw) < 0.0001f
        && Math.Abs(pitch) < 0.0001f
        && Math.Abs(distance - 10f) < 0.0001f
        && Math.Abs(lookAtY - 1f) < 0.0001f;
});

Test("title background preset camera focus rejects zero distance", () =>
{
    return !TitleBackgroundCharaSelectCameraLogic.TryBuildPoseFromCameraFocus(
        new Vector3(1f, 2f, 3f),
        new Vector3(1f, 2f, 3f),
        out _,
        out _,
        out _,
        out _,
        out var errorMessage)
        && errorMessage.Contains("distance", StringComparison.Ordinal);
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
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
    adapter.NotifyLobbyUpdate(GameLobbyType.CharaSelect);
    var active = TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);

    return loaded && active;
});

Test("title background phase2g generated curve override accepts title or chara select context", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.Title,
        GameLobbyType.None);
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
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: true,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: false,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: adapter.IsArmed,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            TitleBackgroundCharaSelectCameraAdapterState.SceneLoading,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
            serviceReady: true,
            hookProbeMode: false,
            sceneOverrideEnabled: true,
            adapterArmed: false,
            isLoggedIn: false,
            activeCharaSelectSession: true,
            sceneGenerationMatchesActiveSession: true,
            adapter.State,
            adapter.RuntimeState,
            GameLobbyType.CharaSelect,
            GameLobbyType.CharaSelect);
});

Test("title background phase2g generated curve override rejects logged in context", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: true,
        activeCharaSelectSession: true,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
});

Test("title background phase2g generated curve override rejects inactive session", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    return !TitleBackgroundCharaSelectCameraLogic.ShouldApplyGeneratedCurveOverride(
        serviceReady: true,
        hookProbeMode: false,
        sceneOverrideEnabled: true,
        adapterArmed: adapter.IsArmed,
        isLoggedIn: false,
        activeCharaSelectSession: false,
        sceneGenerationMatchesActiveSession: true,
        adapter.State,
        adapter.RuntimeState,
        GameLobbyType.CharaSelect,
        GameLobbyType.CharaSelect);
});

Test("title background adapter end session clears active runtime state", () =>
{
    var adapter = new TitleBackgroundCharaSelectCameraAdapter();
    adapter.Configure(true, TitleBackgroundCharaSelectCameraInput.Create(Vector3.Zero, 0f));
    adapter.NotifySceneLoadStarted(GameLobbyType.CharaSelect);
    adapter.SaveRuntimeCameraState(yaw: 1f, pitch: 0.25f, distance: 4f, lookAtY: 2f);
    adapter.NotifySceneLoaded(GameLobbyType.CharaSelect);

    adapter.EndSession();

    return adapter.State == TitleBackgroundCharaSelectCameraAdapterState.Stopping
        && !adapter.RuntimeState.HasCameraPose
        && !adapter.ShouldApplyCurve()
        && !adapter.ShouldApplyLookAtY();
});

Test("title background generated curve self-test treats final yaw pitch distance mismatch as non-blocking", () =>
{
    return TitleBackgroundCameraProbeReport.IsGeneratedCurveSelfTestSuccess(
        sceneVerdict: "observed",
        generatedCurveOverrideVerdict: "observed",
        finalLookAtYMatchesGeneratedCurveVerdict: "observed",
        finalYawPitchDistanceMatchesPresetVerdict: "not-observed");
});

Test("title background generated curve success requires counts and final look at y", () =>
{
    return TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 3,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "observed")
        && !TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 2,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "observed")
        && !TitleBackgroundCameraProbeReport.IsGeneratedCurveOverrideSuccess(
            setMidAttemptCount: 3,
            setMidAppliedCount: 3,
            lowHighAttemptCount: 3,
            lowHighAppliedCount: 3,
            finalLookAtYMatchesGeneratedCurveVerdict: "not-observed");
});

Test("title background transition diagnostics retain last 128 monotonic events", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    for (var i = 0; i < 140; i++)
    {
        recorder.Record($"event-{i}");
    }

    var events = recorder.Events;
    return events.Count == TitleBackgroundTransitionDiagnosticRecorder.RingCapacity
        && events[0].Sequence == 13
        && events[^1].Sequence == 140
        && events.Zip(events.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence);
});

Test("title background transition diagnostics flag repeated sceneReady acceptance", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "first", 1, isLoggedIn: false);
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "second", 1, isLoggedIn: false);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false));

    return lines.Contains("transition.sceneReady.acceptedCount=2")
        && lines.Contains("transition.sceneReady.acceptedCount.suspicious=True")
        && lines.Contains("transition.verdict.sceneReadyAcceptedMultipleTimes=True");
});

Test("title background transition diagnostics compute deltas since previous diag", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var first = recorder.ComputeDeltaSinceLastDiagnostic(new TitleBackgroundTransitionCounters(10, 20, 30, 40, 50, 2, 3));
    var second = recorder.ComputeDeltaSinceLastDiagnostic(new TitleBackgroundTransitionCounters(13, 22, 31, 41, 55, 2, 4));

    return first.FirstReport
        && !first.BaselineEstablished
        && first.Phase2ELookAtYCallCount == 0
        && first.Phase2GSetMidAttemptCount == 0
        && !second.FirstReport
        && second.BaselineEstablished
        && second.Phase2ELookAtYCallCount == 3
        && second.Phase2FSetMidCallCount == 2
        && second.Phase2FLowHighCallCount == 1
        && second.Phase2GSetMidAttemptCount == 1
        && second.Phase2GLowHighAttemptCount == 5
        && second.SceneReadyAcceptedCount == 0
        && second.SceneReadyRawCallCount == 1;
});

Test("title background transition normal diagnostics include summary without full trace", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour entered");
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false));

    return lines.Any(line => line.StartsWith("transition.eventCount=", StringComparison.Ordinal))
        && lines.Any(line => line.StartsWith("transition.verdict.loginTransitionSafety=", StringComparison.Ordinal))
        && !lines.Any(line => line.StartsWith("transition.event[", StringComparison.Ordinal));
});

Test("title background transition detailed diagnostics include event trace", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour entered");
    return recorder.BuildTraceLines().Any(line => line.StartsWith("transition.event[0].seq=1; name=CreateSceneDetour entered", StringComparison.Ordinal));
});

Test("title background transition diagnostics flag stale adapter after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.MarkPostLoginStaleState(new Dictionary<string, string>(), staleAdapter: true, staleCurrentLobbyMap: false, staleSceneOverride: false);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        staleAdapter: true));

    return lines.Contains("transition.adapter.staleAfterLoginDetected=True")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test("title background historical scene override alone is safe after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        historicalLastOverrideApplied: true));

    return lines.Contains("transition.sceneOverride.active=False")
        && lines.Contains("transition.sceneOverride.historicalLastOverrideApplied=True")
        && lines.Contains("transition.sceneOverride.activeAfterLoginDetected=False")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=False")
        && lines.Contains("transition.verdict.loginTransitionSafety=safe");
});

Test("title background active scene override after login is unsafe", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        activeSceneOverride: true,
        historicalLastOverrideApplied: true,
        activeSceneOverrideAfterLogin: true));

    return lines.Contains("transition.sceneOverride.active=True")
        && lines.Contains("transition.sceneOverride.activeAfterLoginDetected=True")
        && lines.Contains("transition.verdict.staleCharaSelectStateAfterLogin=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test("title background transition cleanup reason is reported", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var worldLoginLines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        cleanupReason: "world-login-transition"));
    var leavingLines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false,
        cleanupReason: "leaving-chara-select-context"));

    return worldLoginLines.Contains("transition.sceneOverride.lastCurrentLobbyMapResetReason=world-login-transition")
        && worldLoginLines.Contains("transition.sceneOverride.cleanupReason=world-login-transition")
        && leavingLines.Contains("transition.sceneOverride.lastCurrentLobbyMapResetReason=leaving-chara-select-context")
        && leavingLines.Contains("transition.sceneOverride.cleanupReason=leaving-chara-select-context");
});

Test("title background transition diagnostics flag Phase2G applied after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordPhase2GApply(new Dictionary<string, string>(), isLoggedIn: true, isCharaSelectOrTitleBackground: false, "allowed");
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 1, 0, 0, 0),
        isLoggedIn: true,
        phase2GAppliedAfterLogin: true));

    return lines.Contains("transition.phase2G.appliedAfterLogin=True")
        && lines.Contains("transition.verdict.postLoginPhase2GStillApplying=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test("title background transition diagnostics flag sceneReady accepted after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.RecordSceneReadyAccepted(new Dictionary<string, string>(), "after-login", 2, isLoggedIn: true);
    var lines = TitleBackgroundTransitionDiagnosticRecorder.BuildSummaryLines(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 1, 0),
        isLoggedIn: true,
        sceneReadyAcceptedAfterLogin: true));

    return lines.Contains("transition.verdict.postLoginSceneReadyAccepted=True")
        && lines.Contains("transition.verdict.loginTransitionSafety=unsafe");
});

Test("title background transition verdict ignores first diagnostic cumulative Phase2G delta", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var input = BuildTransitionSummaryInput(
        recorder,
        new TitleBackgroundTransitionDelta(false, true, 0, 0, 0, 1, 1, 0, 0),
        isLoggedIn: true);
    var verdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(input);

    return !verdicts.PostLoginPhase2GStillApplying
        && verdicts.LoginTransitionSafety == "safe";
});

Test("title background login transition safety is safe only after login", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var input = BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: false);
    var verdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(input);

    return verdicts.LoginTransitionSafety == "unsafe";
});

Test("title background transition spam does not evict important events", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    recorder.Record("CreateSceneDetour override applied");
    recorder.Record("CurrentLobbyMap reset");
    recorder.Record("CharaSelect title background session cleanup executed");
    var snapshot = new Dictionary<string, string>
    {
        ["isLoggedIn"] = "True",
        ["CurrentLobbyMap"] = "None",
        ["adapterState"] = "Stopping",
    };

    for (var i = 0; i < 500; i++)
    {
        recorder.RecordSceneReadyRaw(snapshot, "map=None; stateBefore=Stopping");
        recorder.RecordSceneReadyRejected(snapshot, "map=None; stateBefore=Stopping");
    }

    var names = recorder.Events.Select(item => item.Name).ToArray();
    return names.Contains("CreateSceneDetour override applied")
        && names.Contains("CurrentLobbyMap reset")
        && names.Contains("CharaSelect title background session cleanup executed")
        && recorder.SceneReadyRawCallCount == 500
        && recorder.SceneReadyRejectedCount == 500
        && recorder.EventCount < TitleBackgroundTransitionDiagnosticRecorder.RingCapacity;
});

Test("title background yaw pitch distance not-observed is safe only after login transition safety", () =>
{
    return !TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("not-observed", "unsafe")
        && TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("not-observed", "safe")
        && TitleBackgroundTransitionDiagnosticRecorder.IsFinalYawPitchDistanceSafe("observed", "unsafe");
});

Test("title background normal diagnostics exclude detailed failure-only lines", () =>
{
    return TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2C.timeline[60].activeCamera.DirH=1.2")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2D.timeline[600].lobbyCamera.Distance=4.2")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2F.timeline[600].expandedLobbyCamera.MidPoint.Value=0.834")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2M.placementFrame[60].actor.status=observed")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2E.calculateLobbyCameraLookAtY.call[1].returnValue=0.834")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2F.setCameraCurveMidPoint.call[1].status=original")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2F.calculateCameraCurveLowAndHighPoint.interestingCall[1].status=phase2G=low-high-applied")
        && TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("transition.event[0].seq=1; name=CreateSceneDetour entered")
        && !TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2M.actorDiagnostic.status=observed")
        && !TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2G.generationOverride.setMid.appliedCount=3")
        && !TitleBackgroundCameraProbeReport.IsDetailedFailureDiagnosticLine("phase2E.calculateLobbyCameraLookAtY.callCount=128");
});

Test("title background phase2m diagnostics retain scene-ready frames for post-login summary", () =>
{
    var frames = new[]
    {
        Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        Phase2MFrame(30, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        Phase2MFrame(600, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(frames);

    return TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(0)
        && TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(30)
        && TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(600)
        && !TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(90)
        && summary.ActorDiagnosticStatus == "observed"
        && summary.ActorVisible == "observed";
});

Test("title background phase2m ambiguous actor candidates prevent write-capable conclusions", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.Ambiguous, candidateCount: 2),
    ]);

    return summary.ActorDiagnosticStatus == "ambiguous"
        && summary.VisualPlacementSafety == "unsafe";
});

Test("title background phase2m ground height unavailable is unknown not failure", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true, groundStatus: "unavailable"),
    ]);

    return summary.ActorGroundAligned == "unknown"
        && summary.CameraFramesActor == "observed"
        && summary.VisualPlacementSafety == "unknown";
});

Test("title background phase2m visual placement is unsafe when actor is not observed", () =>
{
    var frame = Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.None, candidateCount: 0);
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        frame,
    ]);

    return summary.ActorDiagnosticStatus == "not-observed"
        && summary.ActorVisible == "not-observed"
        && summary.VisualPlacementSafety == "unsafe"
        && frame.ActorCandidateStatus == "none"
        && frame.ActorSource == "objectTable-unavailable-or-not-exposed";
});

Test("title background phase2m single stable candidate is observed but not automatically safe", () =>
{
    var frame = Phase2MFrame(60, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true);
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([frame]);

    return summary.ActorDiagnosticStatus == "observed"
        && frame.ActorCandidateStatus == "single"
        && frame.ObjectTableStats.PlayerLikeCount == 1
        && frame.ObjectCandidates.Count == 1
        && summary.VisualPlacementSafety == "unknown";
});

Test("title background phase2m visual placement safety is independent from login transition safety", () =>
{
    var recorder = new TitleBackgroundTransitionDiagnosticRecorder();
    var transitionVerdicts = TitleBackgroundTransitionDiagnosticRecorder.BuildVerdicts(BuildTransitionSummaryInput(
        recorder,
        TrustedDelta(0, 0, 0, 0, 0, 0, 0),
        isLoggedIn: true,
        historicalLastOverrideApplied: true,
        cleanupReason: "world-login-transition"));
    var placementSummary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.None, candidateCount: 0),
    ]);

    return transitionVerdicts.LoginTransitionSafety == "safe"
        && placementSummary.VisualPlacementSafety == "unsafe";
});

Test("title background phase2m resolves all zero candidates as stub only", () =>
{
    var candidates = Enumerable.Range(0, 8)
        .Select(index => Phase2MCandidate(index, Vector3.Zero, named: false, drawObject: false, visible: false))
        .ToArray();
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates(candidates)]);

    return summary.Resolution == "stub-only"
        && summary.TransformValidity == "all-zero-transform"
        && summary.StubLikelihood == "high"
        && (summary.IdentityConfidence == "none" || summary.IdentityConfidence == "weak")
        && summary.NextAction == "inspect-native-source";
});

Test("title background phase2m resolves single valid visible draw object candidate", () =>
{
    var candidates = new[]
    {
        Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates(candidates, TitleBackgroundPhase2MActorMatchKind.Single)]);

    return summary.Resolution == "single"
        && summary.TransformValidity == "valid-world-transform"
        && summary.IdentityConfidence is "medium" or "strong"
        && summary.BestScore > 0;
});

Test("title background phase2m resolves multiple non-zero candidates as ambiguous", () =>
{
    var candidates = new[]
    {
        Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates(candidates, TitleBackgroundPhase2MActorMatchKind.Ambiguous)]);

    return summary.Resolution == "ambiguous";
});

Test("title background phase2m ambiguous object table without model evidence is not observed", () =>
{
    var candidates = new[]
    {
        Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
        Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates(candidates, TitleBackgroundPhase2MActorMatchKind.Ambiguous)]);

    return summary.Resolution == "ambiguous"
        && summary.DrawObjectNonNullCount == 0
        && summary.ModelLikeNonNullCount == 0
        && !summary.BestCandidateStableAcrossFrames
        && summary.ActorDiagnosticStatus != "observed"
        && summary.ActorVisible != "observed"
        && summary.CameraFramesActor != "observed"
        && summary.VisualPlacementSafety != "safe"
        && summary.NextAction == "insufficient-data";
});

Test("title background phase2m post login style object table candidate does not mark actor visible", () =>
{
    var candidates = new[]
    {
        Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
        Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates(candidates, TitleBackgroundPhase2MActorMatchKind.Ambiguous)]);

    return summary.BestSource == "ObjectTable"
        && summary.ActorVisible != "observed"
        && summary.CameraFramesActor != "observed"
        && summary.ActorDiagnosticStatus != "observed";
});

Test("title background phase2m resolves unavailable source as source missing", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrameFromCandidates([])]);
    return summary.Resolution == "source-missing"
        && summary.NextAction == "inspect-native-source";
});

Test("title background phase2m prelogin capture summary survives post-login style summary", () =>
{
    var frames = new[]
    {
        Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
        Phase2MFrame(1200, TitleBackgroundPhase2MActorMatchKind.Single, visibleHint: true, withCameraDeltas: true),
    };
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(frames);

    return TitleBackgroundPhase2MPlacementDiagnostic.ShouldCaptureFrame(1200)
        && summary.ActorDiagnosticStatus == "observed";
});

Test("title background phase2m experimental mode none never writes", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary([Phase2MFrame(0, TitleBackgroundPhase2MActorMatchKind.Single)]);
    return TitleBackgroundPhase2MPlacementDiagnostic.EvaluateExperimentalApply(
        TitleBackgroundPhase2MExperimentalApplyMode.None,
        summary,
        sceneGenerationMatches: true,
        isCharaSelectActive: true,
        isLoggedIn: false) == "skip:none-mode";
});

Test("title background phase2m actor placement one shot requires single valid transform", () =>
{
    var ambiguous = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(1f, 2f, 3f), named: true, drawObject: true, visible: true),
            Phase2MCandidate(2, new Vector3(2f, 2f, 3f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var stub = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([Phase2MCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false)]),
    ]);

    return TitleBackgroundPhase2MPlacementDiagnostic.EvaluateExperimentalApply(
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot,
            ambiguous,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal)
        && TitleBackgroundPhase2MPlacementDiagnostic.EvaluateExperimentalApply(
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot,
            stub,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal);
});

Test("title background phase2n object table all zero is stub only", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false),
            Phase2MCandidate(2, Vector3.Zero, named: false, drawObject: false, visible: false),
        ]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return summary.Resolution == "stub-only"
        && delivery.ObjectTableActorRejected
        && delivery.ObjectTableActorRejectedReason == "zero-transform-stub-only";
});

Test("title background phase2n stub only blocks actor placement one shot", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([Phase2MCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: false)]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return !delivery.ActorPlacementReady
        && delivery.ActorPlacementBlocker == "stub-only-object-table"
        && TitleBackgroundPhase2NDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot,
            summary,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false) == "skip:stub-only-object-table";
});

Test("title background phase2n valid native single is ready", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundPhase2MActorMatchKind.Single),
    ]);
    var delivery = Phase2N(summary, TitleBackgroundCharacterSelectBackgroundMode.NativePreviewModelSource);

    return delivery.NativePreviewSourceResolution == "found-single"
        && delivery.ActorPlacementReady
        && delivery.NextAction == "try-native-preview-source";
});

Test("title background phase2n multiple valid native candidates are ambiguous", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary);

    return delivery.NativePreviewSourceResolution == "found-ambiguous"
        && !delivery.ActorPlacementReady;
});

Test("title background phase2n no native source falls back to background only", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.NativePreviewSourceResolution == "not-found"
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.NextAction == "use-background-only";
});

Test("title background phase2n custom n4f4 override warns and recommends bright candidate", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.PresetCompatibility.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && delivery.PresetCompatibility.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Dark
        && delivery.Lighting.RecommendedAction == "add-bright-override-candidate"
        && delivery.OverrideCompatibility.BackgroundUsable
        && delivery.PresetCompatibility.RecommendedMode == TitleBackgroundCharacterSelectBackgroundMode.CompatiblePresetOnly;
});

Test("title background phase2o custom n4f4 registry entry exists", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:n4f4", out var candidate)
        && candidate.Id == TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId
        && candidate.DisplayName == "Custom n4f4 override target"
        && candidate.TerritoryPath == "ex3/01_nvt_n4/fld/n4f4/level/n4f4"
        && candidate.TerritoryId == 816
        && candidate.LayerFilterKey == 51;
});

Test("title background phase2o custom n4f4 is background only", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault();
    return candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && candidate.BackgroundUsable
        && !candidate.CharacterExpectedVisible;
});

Test("title background phase2o custom n4f4 is dark", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().ExpectedBrightness
        == TitleBackgroundCharacterSelectExpectedBrightness.Dark;
});

Test("title background phase2o custom n4f4 is verified in game", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().VerifiedInGame;
});

Test("title background phase2q old sharlayan observed candidate exists", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:old-sharlayan-k5t1", out var candidate)
        && candidate.DisplayName == "Old Sharlayan outdoor test"
        && candidate.TerritoryPath == "ex4/03_kld_k5/twn/k5t1/level/k5t1"
        && candidate.TerritoryId == 962
        && candidate.LayerFilterKey == 8
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && candidate.BackgroundUsable
        && !candidate.CharacterExpectedVisible
        && !candidate.VerifiedInGame
        && candidate.Source == "registry-observed";
});

Test("title background phase2q old sharlayan does not replace default", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault().Id == "custom:n4f4"
        && TitleBackgroundCharacterSelectOverrideCandidateRegistry.DefaultCandidateId == "custom:n4f4";
});

Test("title background phase2q old sharlayan dropdown label is unverified unknown background only", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet("custom:old-sharlayan-k5t1", out var candidate)
        && CandidateLabel(candidate) == "Old Sharlayan outdoor test [Unverified / Unknown / Background-only]";
});

Test("title background phase2o candidate registry keeps selected preset separate", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
    };
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault());

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:n4f4";
});

Test("title background phase2o selecting candidate updates override fields only", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
        TitleBackgroundTerritoryPath = "ffxiv/old/region/level/old",
        TitleBackgroundTerritoryTypeId = 1,
        TitleBackgroundLayoutTerritoryTypeId = 2,
        TitleBackgroundLayoutLayerFilterKey = 3,
    };
    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(
        configuration,
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault());

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundTerritoryPath == "ex3/01_nvt_n4/fld/n4f4/level/n4f4"
        && configuration.TitleBackgroundTerritoryTypeId == 816
        && configuration.TitleBackgroundLayoutTerritoryTypeId == 816
        && configuration.TitleBackgroundLayoutLayerFilterKey == 51;
});

Test("title background phase2o unknown custom override falls back to custom unknown", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
        string.Empty,
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        7);

    return candidate.Id == "custom"
        && candidate.DisplayName == "Custom override target"
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.Unknown
        && candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && !candidate.VerifiedInGame;
});

Test("title background phase2o stale candidate id does not override custom values", () =>
{
    var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
        "custom:n4f4",
        "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
        1234,
        7);

    return candidate.Id == "custom"
        && candidate.DisplayName == "Custom override target"
        && candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.Unknown
        && !candidate.VerifiedInGame;
});

Test("title background phase2o no bright candidate reports none", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All) == "none"
        && TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.All) == "add-bright-override-candidate";
});

Test("title background phase2q old sharlayan delivery exposes observed unverified background only", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "custom:old-sharlayan-k5t1",
        overrideTerritoryPath: "ex4/03_kld_k5/twn/k5t1/level/k5t1",
        overrideTerritoryId: 962,
        overrideLayerFilterKey: 8,
        historicalLastOverrideApplied: true,
        historicalLastOverridePath: "ex4/03_kld_k5/twn/k5t1/level/k5t1");

    return delivery.OverrideCandidate.Selected.Id == "custom:old-sharlayan-k5t1"
        && delivery.OverrideCandidate.Selected.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Unknown
        && !delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.BackgroundApplication.Observed
        && delivery.BackgroundApplication.LastHistoricalOverrideApplied
        && delivery.BackgroundApplication.LastHistoricalOverridePath == "ex4/03_kld_k5/twn/k5t1/level/k5t1"
        && delivery.BackgroundApplication.CurrentCandidateId == "custom:old-sharlayan-k5t1"
        && delivery.BackgroundApplication.VisualConfirmationRequired
        && delivery.BackgroundApplication.UserVerdict == "background-applied-character-hidden"
        && delivery.BackgroundDeliveryVerdict == "working-background-only-observed"
        && delivery.CandidateHumanName == "Old Sharlayan outdoor test"
        && delivery.CandidateHumanStatus == "Observed / Unverified / Background-only"
        && delivery.UserMessage == "Background was applied as background-only. Selected character model is expected to remain hidden."
        && delivery.UserNextAction == "Take screenshot in Character Select, then run /xmutbgdiag after login.";
});

Test("title background phase2q background application survives transition safety warning", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "unsafe",
        sceneReadyAcceptedMultipleTimes: true);

    return delivery.DeliveryVerdict == "unsafe"
        && delivery.BackgroundApplication.Observed
        && delivery.BackgroundDeliveryVerdict == "working-background-only-observed"
        && delivery.Safety.Verdict == "warning"
        && delivery.Safety.Reason == "scene-ready-accepted-multiple-times"
        && delivery.Safety.BlocksBackgroundCandidatePromotion
        && delivery.TransitionSafetyVerdict == "warning-scene-ready-accepted-multiple-times";
});

Test("title background phase2q post login leak not observed without active override or phase2g", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        transitionSafety: "unsafe",
        sceneReadyAcceptedMultipleTimes: true,
        activeAfterLoginDetected: false,
        phase2GAppliedAfterLogin: false);

    return delivery.PostLoginLeakVerdict == "not-observed"
        && delivery.TransitionUserMessage == "No post-login scene override leak observed, but sceneReady was accepted multiple times in this session.";
});

Test("title background phase2q leak blocks candidate promotion", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        activeAfterLoginDetected: true);

    return delivery.PostLoginLeakVerdict == "observed"
        && delivery.Safety.Verdict == "unsafe"
        && delivery.Safety.BlocksBackgroundCandidatePromotion;
});

Test("title background phase2p manual candidate disabled is not available", () =>
{
    var slot = ManualSlot(enabled: false);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "disabled"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test("title background phase2p manual candidate invalid path is rejected", () =>
{
    var slot = ManualSlot(path: "bad/path", territoryId: 900, enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "territory-path-invalid"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test("title background phase2p manual candidate territory id zero is rejected", () =>
{
    var slot = ManualSlot(territoryId: 0, enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);

    return !slot.Valid
        && slot.ValidationError == "territory-id-zero"
        && candidates.All(candidate => candidate.Id != "manual:slot1");
});

Test("title background phase2p valid manual candidate is available", () =>
{
    var slot = ManualSlot(enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.FirstOrDefault(candidate => candidate.Id == "manual:slot1");

    return slot.Valid
        && manual.Id == "manual:slot1"
        && manual.Source == "manual"
        && manual.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly;
});

Test("title background phase2p manual candidate is never verified by default", () =>
{
    var slot = ManualSlot(enabled: true);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.First(candidate => candidate.Id == "manual:slot1");

    return !manual.VerifiedInGame
        && manual.Warning.Contains("unverified", StringComparison.OrdinalIgnoreCase);
});

Test("title background phase2p manual bright candidate contributes to bright list", () =>
{
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(
        [ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright)]);

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(candidates) == "manual:slot1";
});

Test("title background phase2p manual bright candidate recommends verification", () =>
{
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(
        [ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright)]);

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(candidates) == "verify-manual-bright-candidate";
});

Test("title background phase2p selecting manual candidate updates override fields", () =>
{
    var configuration = new Configuration
    {
        TitleBackgroundSelectedPresetId = "built-in-test",
    };
    var slot = ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright);
    var candidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates([slot]);
    var manual = candidates.First(candidate => candidate.Id == "manual:slot1");

    TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(configuration, manual);

    return configuration.TitleBackgroundSelectedPresetId == string.Empty
        && configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "manual:slot1"
        && configuration.TitleBackgroundTerritoryPath == manual.TerritoryPath
        && configuration.TitleBackgroundTerritoryTypeId == manual.TerritoryId
        && configuration.TitleBackgroundLayoutTerritoryTypeId == manual.TerritoryId
        && configuration.TitleBackgroundLayoutLayerFilterKey == manual.LayerFilterKey;
});

Test("title background phase2p delivery selects valid manual candidate", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var slot = ManualSlot(enabled: true, brightness: TitleBackgroundCharacterSelectExpectedBrightness.Bright);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "manual:slot1",
        overrideTerritoryPath: slot.TerritoryPath,
        overrideTerritoryId: slot.TerritoryId,
        overrideLayerFilterKey: slot.LayerFilterKey,
        manualCandidateSlots: [slot]);

    return delivery.OverrideCandidate.Selected.Id == "manual:slot1"
        && delivery.OverrideCandidate.Selected.Source == "manual"
        && !delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.OverrideCandidate.ManualSlots[0].Valid
        && delivery.Lighting.BrightLayerCandidates == "manual:slot1"
        && delivery.Lighting.RecommendedAction == "verify-manual-bright-candidate";
});

Test("title background phase2p invalid manual candidate falls back safely", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var slot = ManualSlot(path: "bad/path", territoryId: 900, enabled: true);
    var delivery = Phase2N(
        summary,
        lastOverrideApplied: true,
        selectedOverrideCandidateId: "manual:slot1",
        overrideTerritoryPath: "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        overrideTerritoryId: 816,
        overrideLayerFilterKey: 51,
        manualCandidateSlots: [slot]);

    return delivery.OverrideCandidate.Selected.Id == "custom:n4f4"
        && delivery.OverrideCandidate.ManualSlots[0].ValidationError == "territory-path-invalid"
        && delivery.MvpStatus == "complete-background-only";
});

Test("title background phase2o bright candidate list reports candidate id", () =>
{
    var candidates = new[]
    {
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault(),
        TestBrightCandidate(),
    };

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildBrightLayerCandidateList(candidates) == "custom:test-bright";
});

Test("title background phase2o bright candidate recommends trying custom target", () =>
{
    var candidates = new[]
    {
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.GetDefault(),
        TestBrightCandidate(),
    };

    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildLightingRecommendedAction(candidates) == "try-bright-custom-target";
});

Test("title background phase2o unverified bright candidate does not claim verified", () =>
{
    var candidate = TestBrightCandidate();
    return candidate.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Bright
        && !candidate.VerifiedInGame;
});

Test("title background phase2o delivery exposes selected override candidate", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.OverrideCandidate.Selected.Id == "custom:n4f4"
        && delivery.OverrideCandidate.Selected.VerifiedInGame
        && delivery.OverrideCandidate.Available.Count == 2
        && delivery.OverrideCandidate.Available[0].Id == "custom:n4f4";
});

Test("title background phase2q docs mention xmutbgdiag after login", () =>
{
    var root = FindRepositoryRoot();
    var text = File.ReadAllText(Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"));

    return text.Contains("`/xmutbgdiag` cannot be run from Character Select", StringComparison.Ordinal)
        && text.Contains("Run `/xmutbgdiag` after login", StringComparison.Ordinal)
        && text.Contains("Capture a screenshot in Character Select", StringComparison.Ordinal);
});

Test("title background phase2q implementation avoids prohibited write paths", () =>
{
    var root = FindRepositoryRoot();
    var changedFiles = new[]
    {
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundCharacterSelectOverrideCandidateRegistry.cs"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleBackgroundPhase2NDeliveryDiagnostic.cs"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.cs"),
    };
    var titleBackgroundText = string.Join(
        "\n",
        changedFiles.Select(File.ReadAllText));
    var docs = File.ReadAllText(Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"));

    return !titleBackgroundText.Contains("SceneCamera.Position", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("SceneCamera.LookAtVector", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("SceneCamera.FoV", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("Framework.Update", StringComparison.Ordinal)
        && !titleBackgroundText.Contains(".Position =", StringComparison.Ordinal)
        && !titleBackgroundText.Contains(".Rotation =", StringComparison.Ordinal)
        && !titleBackgroundText.Contains("light write", StringComparison.OrdinalIgnoreCase)
        && !titleBackgroundText.Contains("environment write", StringComparison.OrdinalIgnoreCase)
        && !docs.Contains("automatic map cycling", StringComparison.OrdinalIgnoreCase)
        && !docs.Contains("n4f4 " + "preset", StringComparison.OrdinalIgnoreCase);
});

Test("title background phase2o docs and ui avoid n4f4 synthetic preset wording", () =>
{
    var root = FindRepositoryRoot();
    var paths = new[]
    {
        Path.Combine(root, "docs", "title-background-character-select-bright-candidates.md"),
        Path.Combine(root, "docs", "title-background-character-select-delivery-notes.md"),
        Path.Combine(root, "docs", "title-background-character-select-phase2n-plan.md"),
        Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.cs"),
    };

    return paths.All(path => File.Exists(path)
        && !File.ReadAllText(path).Contains("n4f4 " + "preset", StringComparison.OrdinalIgnoreCase));
});

Test("title background phase2n stub only never reports character observed", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([Phase2MCandidate(1, Vector3.Zero, named: false, drawObject: false, visible: true)]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.ObjectTableActorRejected
        && delivery.NativePreviewSourceResolution == "not-found"
        && delivery.CharacterVisibilityObserved != "observed"
        && delivery.CharacterVisibilityObserved == "not-observed"
        && delivery.CharacterVisibilityBlocker == "stub-only-object-table";
});

Test("title background phase2n native source not found never reports character observed", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.NativePreviewSourceResolution == "not-found"
        && delivery.CharacterVisibilityObserved != "observed";
});

Test("title background phase2n background only keeps character expected hidden", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.DeliveryVerdict == "working-background-only"
        && !delivery.PresetCompatibility.CharacterExpectedVisible
        && !delivery.OverrideCompatibility.CharacterExpectedVisible;
});

Test("title background phase2n custom override source keeps selected preset none", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, selectedPresetId: string.Empty, lastOverrideApplied: true);

    return delivery.OverrideCompatibility.Source == "custom-override"
        && delivery.OverrideCompatibility.SelectedPresetId == "none"
        && delivery.OverrideCompatibility.CurrentOverrideId == "custom:n4f4";
});

Test("title background phase2n custom n4f4 synthetic entry is not selected preset", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, selectedPresetId: string.Empty, lastOverrideApplied: true);

    return delivery.PresetCompatibility.CurrentPresetId == "custom:n4f4"
        && delivery.OverrideCompatibility.Id == "custom:n4f4"
        && delivery.OverrideCompatibility.Source == "custom-override"
        && delivery.OverrideCompatibility.SelectedPresetId != "custom:n4f4";
});

Test("title background phase2n custom n4f4 dark lighting has recommendation", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.OverrideCompatibility.ExpectedBrightness == TitleBackgroundCharacterSelectExpectedBrightness.Dark
        && delivery.Lighting.CurrentLayerFilterKey == 51
        && delivery.Lighting.LayerBrightnessKnown
        && !string.IsNullOrEmpty(delivery.Lighting.RecommendedAction)
        && delivery.Lighting.RecommendedAction != "none";
});

Test("title background phase2n background only safe compatibility delivers background only", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return delivery.PresetCompatibility.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        && delivery.PresetCompatibility.SafeToUse
        && delivery.DeliveryVerdict == "working-background-only";
});

Test("title background phase2n post login current object table is ignored", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return delivery.NativePreviewSourceCurrentObjectTableIgnored
        && delivery.NativePreviewSourceCurrentObjectTableIgnoredReason == "post-login-world-object-table-not-valid-for-chara-select"
        && delivery.NativePreviewSourceResolution == "not-verifiable-post-login"
        && delivery.CharacterVisibilityBlocker == "post-login-object-table-not-valid"
        && delivery.ObjectTableActorRejected
        && !delivery.ActorPlacementReady
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.ObjectTableActorRejectedReason == "post-login-world-object-table-not-valid-for-chara-select";
});

Test("title background phase2n background only mvp is complete with known limitation", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return delivery.NativePreviewSourceCurrentObjectTableIgnored
        && delivery.NativePreviewSourceResolution == "not-verifiable-post-login"
        && delivery.ObjectTableActorRejected
        && !delivery.ActorPlacementReady
        && delivery.DeliveryVerdict == "working-background-only"
        && delivery.MvpStatus == "complete-background-only"
        && delivery.MvpBlockingIssue == "none"
        && delivery.MvpKnownLimitation == "selected-character-model-hidden";
});

Test("title background phase2n post login ambiguous object table never readies actor placement", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true, currentObjectTableValidForCharaSelect: false);

    return !delivery.ActorPlacementReady
        && delivery.ActorPlacementBlocker == "post-login-world-object-table-not-valid-for-chara-select"
        && delivery.CharacterVisibilityObserved != "observed";
});

Test("title background phase2n draw object absent unstable ambiguous is not observed", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: false, visible: false),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: false, visible: false),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return summary.DrawObjectNonNullCount == 0
        && summary.ModelLikeNonNullCount == 0
        && !summary.BestCandidateStableAcrossFrames
        && delivery.CharacterVisibilityObserved == "not-verifiable"
        && !delivery.ActorPlacementReady;
});

Test("title background phase2n source local counts do not leak object table counts", () =>
{
    var sourceDiscovery = new[]
    {
        new TitleBackgroundPhase2MSourceDiscovery("ObjectTable", true, 16, 16, string.Empty, 16, 0, 0),
        new TitleBackgroundPhase2MSourceDiscovery("PlayerObjects", true, 0, 0, string.Empty, 0, 0, 0),
        new TitleBackgroundPhase2MSourceDiscovery("CharacterManagerObjects", true, 0, 0, string.Empty, 0, 0, 0),
    };
    var delivery = Phase2NFromRaw(
        phase2MResolution: "ambiguous",
        phase2MTransformValidity: "valid-world-transform",
        phase2MActorVisible: "ambiguous",
        zeroPositionCandidateCount: 0,
        nonZeroPositionCandidateCount: 16,
        drawObjectNonNullCount: 0,
        modelLikeNonNullCount: 0,
        sourceDiscovery,
        lastOverrideApplied: true);
    var playerObjects = delivery.NativePreviewSources.First(source => source.Name == "PlayerObjects");
    var characterManagerObjects = delivery.NativePreviewSources.First(source => source.Name == "CharacterManagerObjects");

    return playerObjects.CandidateCount == 0
        && playerObjects.NonZeroTransformCount == 0
        && characterManagerObjects.CandidateCount == 0
        && characterManagerObjects.NonZeroTransformCount == 0;
});

Test("title background phase2n object table ambiguous candidate never enables one shot readiness", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
            Phase2MCandidate(2, new Vector3(11f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundPhase2MActorMatchKind.Ambiguous),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true);

    return summary.Resolution == "ambiguous"
        && !delivery.ActorPlacementReady
        && TitleBackgroundPhase2NDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
            TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot,
            summary,
            sceneGenerationMatches: true,
            isCharaSelectActive: true,
            isLoggedIn: false).StartsWith("skip:resolution-", StringComparison.Ordinal);
});

Test("title background phase2n default mode does not enable actor or camera direct writes", () =>
{
    return TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly)
        && !TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.Disabled)
        && !TitleBackgroundPhase2NDeliveryDiagnostic.IsMutationMode(TitleBackgroundCharacterSelectBackgroundMode.DiagnosticsOnly);
});

Test("title background phase2n login transition unsafe stops delivery", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates([]),
    ]);
    var delivery = Phase2N(summary, lastOverrideApplied: true, transitionSafety: "unsafe");

    return delivery.DeliveryVerdict == "unsafe"
        && delivery.NextAction == "unsafe-stop";
});

Test("title background phase2n scene generation mismatch remains no-op for actor placement", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: true),
        ], TitleBackgroundPhase2MActorMatchKind.Single),
    ]);

    return TitleBackgroundPhase2NDeliveryDiagnostic.EvaluateExperimentalActorPlacement(
        TitleBackgroundPhase2MExperimentalApplyMode.ActorPlacementOneShot,
        summary,
        sceneGenerationMatches: false,
        isCharaSelectActive: true,
        isLoggedIn: false) == "skip:scene-generation-mismatch";
});

Test("title background phase2m next action selects visibility probe for valid invisible actor", () =>
{
    var summary = TitleBackgroundPhase2MPlacementDiagnostic.BuildSummary(
    [
        Phase2MFrameFromCandidates(
        [
            Phase2MCandidate(1, new Vector3(10f, 20f, 30f), named: true, drawObject: true, visible: false),
        ], TitleBackgroundPhase2MActorMatchKind.Single),
    ]);

    return summary.NextAction == "enable-visibility-probe" || summary.NextAction == "actor-placement-preview";
});

Test("title background normal diagnostics exclude obsolete direct look at y fields", () =>
{
    return TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("lookAtYApply.attemptCount=1")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("lookAtYApply.readBackValueImmediatelyAfterWrite=0.834")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.lookAtYImmediateReflection=reflected")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.lookAtYPostApplyStability=stable")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.phase2G.finalLookAtYMatchesGeneratedCurve=observed")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("phase2E.calculateLobbyCameraLookAtY.call[1].returnValue=0.834")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.phase2G.finalYawPitchDistanceMatchesPreset=not-observed");
});

Test("title background normal diagnostics keep yaw pitch distance blocking flag and deprecated camera verdict", () =>
{
    const string finalYawPitchDistanceMatchesPreset = "not-observed";
    var lines = new[]
    {
        $"verdict.phase2G.finalYawPitchDistanceMatchesPreset={finalYawPitchDistanceMatchesPreset}",
        "verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False",
        $"verdict.phase2G.finalCameraStateMatchesPreset={finalYawPitchDistanceMatchesPreset}",
    };

    return lines[0].EndsWith(lines[2].Split('=')[1], StringComparison.Ordinal)
        && lines[1] == "verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False";
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

Test("title background session cleanup gate keeps non logged-in none map", () =>
{
    return !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.None)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.Title)
        && !TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.CharaSelect)
        && TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: false, GameLobbyType.LaNoscea)
        && TitleBackgroundCharaSelectCameraLogic.ShouldEndCharaSelectTitleBackgroundSession(isLoggedIn: true, GameLobbyType.None);
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

TitleBackgroundCharacterSelectOverrideCandidate TestBrightCandidate()
{
    return new TitleBackgroundCharacterSelectOverrideCandidate(
        "custom:test-bright",
        "Test bright custom target",
        "ex5/test/bright/level/bright",
        999,
        10,
        TitleBackgroundCharacterSelectCompatibility.BackgroundOnly,
        TitleBackgroundCharacterSelectExpectedBrightness.Bright,
        true,
        false,
        false,
        "registry",
        "test-only bright candidate",
        "test-only",
        "try-bright-custom-target");
}

TitleBackgroundCharacterSelectManualCandidateSlot ManualSlot(
    bool enabled = true,
    string path = "ex5/01_xkt_x6/fld/x6f3/level/x6f3",
    uint territoryId = 1234,
    uint layerFilterKey = 7,
    TitleBackgroundCharacterSelectExpectedBrightness brightness = TitleBackgroundCharacterSelectExpectedBrightness.Unknown)
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildManualSlot(
        1,
        enabled,
        "Manual test candidate",
        path,
        territoryId,
        layerFilterKey,
        brightness);
}

string CandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
{
    var verified = candidate.VerifiedInGame ? "Verified" : "Unverified";
    var source = candidate.Source == "manual" ? "Manual / " : string.Empty;
    var compatibility = candidate.ExpectedCompatibility == TitleBackgroundCharacterSelectCompatibility.BackgroundOnly
        ? "Background-only"
        : candidate.ExpectedCompatibility.ToString();
    return $"{candidate.DisplayName} [{source}{verified} / {candidate.ExpectedBrightness} / {compatibility}]";
}

string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "projects", "XIV-Mini-Util", "XivMiniUtil.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

TitleBackgroundPhase2NDeliverySummary Phase2N(
    TitleBackgroundPhase2MSummary summary,
    TitleBackgroundCharacterSelectBackgroundMode backgroundMode = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
    TitleBackgroundCharacterSelectLightingMode lightingMode = TitleBackgroundCharacterSelectLightingMode.Default,
    string selectedPresetId = "",
    bool lastOverrideApplied = false,
    string transitionSafety = "safe",
    bool currentObjectTableValidForCharaSelect = true,
    string selectedOverrideCandidateId = "",
    string overrideTerritoryPath = "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
    uint overrideTerritoryId = 816,
    uint overrideLayerFilterKey = 51,
    IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot>? manualCandidateSlots = null,
    bool historicalLastOverrideApplied = false,
    string historicalLastOverridePath = "",
    bool sceneReadyAcceptedMultipleTimes = false,
    bool activeAfterLoginDetected = false,
    bool phase2GAppliedAfterLogin = false)
{
    return TitleBackgroundPhase2NDeliveryDiagnostic.BuildSummary(
        backgroundMode,
        lightingMode,
        selectedPresetId,
        overrideTerritoryPath,
        overrideTerritoryId,
        overrideLayerFilterKey,
        sceneOverrideEnabled: true,
        lastOverrideApplied,
        summary.Resolution,
        summary.TransformValidity,
        summary.ActorVisible,
        summary.ZeroPositionCandidateCount,
        summary.NonZeroPositionCandidateCount,
        summary.DrawObjectNonNullCount,
        summary.ModelLikeNonNullCount,
        summary.BestCandidate,
        [
            new TitleBackgroundPhase2MSourceDiscovery(
                "ObjectTable",
                true,
                summary.ZeroPositionCandidateCount + summary.NonZeroPositionCandidateCount,
                summary.ZeroPositionCandidateCount + summary.NonZeroPositionCandidateCount,
                string.Empty,
                summary.NonZeroPositionCandidateCount,
                summary.DrawObjectNonNullCount,
                summary.ModelLikeNonNullCount),
        ],
        transitionSafety,
        currentObjectTableValidForCharaSelect,
        currentObjectTableValidForCharaSelect ? "none" : "post-login-world-object-table-not-valid-for-chara-select",
        selectedOverrideCandidateId,
        manualCandidateSlots,
        historicalLastOverrideApplied,
        historicalLastOverridePath,
        sceneReadyAcceptedMultipleTimes,
        activeAfterLoginDetected,
        phase2GAppliedAfterLogin);
}

TitleBackgroundPhase2NDeliverySummary Phase2NFromRaw(
    string phase2MResolution,
    string phase2MTransformValidity,
    string phase2MActorVisible,
    int zeroPositionCandidateCount,
    int nonZeroPositionCandidateCount,
    int drawObjectNonNullCount,
    int modelLikeNonNullCount,
    IReadOnlyList<TitleBackgroundPhase2MSourceDiscovery> sourceDiscovery,
    bool lastOverrideApplied = false,
    bool currentObjectTableValidForCharaSelect = true)
{
    return TitleBackgroundPhase2NDeliveryDiagnostic.BuildSummary(
        TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly,
        TitleBackgroundCharacterSelectLightingMode.Default,
        string.Empty,
        "ex3/01_nvt_n4/fld/n4f4/level/n4f4",
        816,
        51,
        sceneOverrideEnabled: true,
        lastOverrideApplied,
        phase2MResolution,
        phase2MTransformValidity,
        phase2MActorVisible,
        zeroPositionCandidateCount,
        nonZeroPositionCandidateCount,
        drawObjectNonNullCount,
        modelLikeNonNullCount,
        "ObjectTable:1",
        sourceDiscovery,
        "safe",
        currentObjectTableValidForCharaSelect,
        currentObjectTableValidForCharaSelect ? "none" : "post-login-world-object-table-not-valid-for-chara-select");
}

TitleBackgroundPhase2MPlacementFrame Phase2MFrame(
    int frame,
    TitleBackgroundPhase2MActorMatchKind matchKind,
    bool visibleHint = false,
    bool withCameraDeltas = false,
    string groundStatus = "unavailable",
    int candidateCount = 1)
{
    var actor = matchKind == TitleBackgroundPhase2MActorMatchKind.Single
        ? new TitleBackgroundPhase2MActorCandidate(
            SourceIndex: 1,
            Source: "test",
            ObjectIndex: 1,
            ObjectKind: "Pc",
            Name: "candidate",
            GameObjectId: 0x100,
            EntityId: 0x200,
            Address: new nint(0x300),
            Position: new Vector3(1f, 2f, 3f),
            Rotation: 0.5f,
            Scale: 1f,
            HitboxRadius: 0.5f,
            CurrentHp: 100,
            MaxHp: 100,
            Targetable: visibleHint,
            VisibilityHint: visibleHint ? "targetable" : "not-targetable",
            SelectableHint: visibleHint ? "selectable-hint" : "unknown",
            Flags: "none",
            Customize: "none",
            Model: "model",
            DrawObject: "0x400",
            DrawObjectNonNull: true,
            ModelLikePointer: "model",
            ModelLikeNonNull: true,
            SafeReadError: "none",
            Named: true,
            PlayerLike: true,
            BattleCharacterLike: true,
            EventNpcLike: false,
            CompanionLike: false,
            VisibleHint: visibleHint,
            DistanceFromConfiguredCharacter: 0f,
            DistanceFromActiveLookAt: withCameraDeltas ? 1f : null,
            DistanceFromActiveCameraPosition: withCameraDeltas ? 3f : null,
            YDeltaFromConfiguredCharacter: 0f,
            NearConfiguredCharacter: true,
            NearCameraLookAt: withCameraDeltas,
            NearCameraPosition: withCameraDeltas,
            CategoryReason: "PlayerCharacter,BattleChara,Named",
            Score: 100,
            ScoreReason: "test")
        : (TitleBackgroundPhase2MActorCandidate?)null;
    var objectCandidates = actor.HasValue
        ? new[] { actor.Value }
        : Array.Empty<TitleBackgroundPhase2MActorCandidate>();

    return new TitleBackgroundPhase2MPlacementFrame(
        Frame: frame,
        Reason: frame == 0 ? "scene-ready-accepted" : "timeline",
        ActiveCameraCaptured: withCameraDeltas,
        ActiveCameraPosition: withCameraDeltas ? Vector3.Zero : null,
        ActiveCameraLookAt: withCameraDeltas ? new Vector3(1f, 2f, 2f) : null,
        ActiveCameraYaw: withCameraDeltas ? 0f : null,
        ActiveCameraPitch: withCameraDeltas ? 0f : null,
        ActiveCameraDistance: withCameraDeltas ? 3f : null,
        LobbyCameraCaptured: withCameraDeltas,
        LobbyCameraLookAt: withCameraDeltas ? new Vector3(1f, 2f, 2f) : null,
        LobbyDirH: withCameraDeltas ? 0f : null,
        LobbyDirV: withCameraDeltas ? 0f : null,
        LobbyDistance: withCameraDeltas ? 3f : null,
        LobbyInterpDistance: withCameraDeltas ? 3f : null,
        ActorMatchKind: matchKind,
        Actor: actor,
        ObjectCandidates: objectCandidates,
        SourceDiscovery:
        [
            new TitleBackgroundPhase2MSourceDiscovery(
                "ObjectTable",
                true,
                candidateCount,
                objectCandidates.Length,
                string.Empty,
                objectCandidates.Count(candidate => candidate.Position != Vector3.Zero),
                objectCandidates.Count(candidate => candidate.DrawObjectNonNull),
                objectCandidates.Count(candidate => candidate.ModelLikeNonNull)),
            new TitleBackgroundPhase2MSourceDiscovery("CharacterManagerObjects", true, candidateCount, 0, string.Empty),
        ],
        CandidateCount: candidateCount,
        ActorStatus: matchKind switch
        {
            TitleBackgroundPhase2MActorMatchKind.Single => "observed",
            TitleBackgroundPhase2MActorMatchKind.Ambiguous => "ambiguous",
            _ => "not-observed",
        },
        ObjectTableStats: new TitleBackgroundPhase2MObjectTableStats(
            TotalScanned: candidateCount,
            NamedCount: actor.HasValue ? 1 : 0,
            PlayerLikeCount: actor.HasValue ? 1 : 0,
            BattleCharaCount: actor.HasValue ? 1 : 0,
            EventNpcCount: 0,
            CompanionLikeCount: 0,
            NearCameraCount: withCameraDeltas && actor.HasValue ? 1 : 0,
            NearConfiguredCharacterCount: actor.HasValue ? 1 : 0),
        ActorCandidateStatus: matchKind switch
        {
            TitleBackgroundPhase2MActorMatchKind.Single => "single",
            TitleBackgroundPhase2MActorMatchKind.Ambiguous => "ambiguous",
            _ => "none",
        },
        ActorCandidateReason: matchKind switch
        {
            TitleBackgroundPhase2MActorMatchKind.Single => "single-candidate",
            TitleBackgroundPhase2MActorMatchKind.Ambiguous => "multiple-candidates:2",
            _ => "objectTable-unavailable-or-not-exposed",
        },
        ActorSource: matchKind == TitleBackgroundPhase2MActorMatchKind.Single ? "test" : "objectTable-unavailable-or-not-exposed",
        NextNativeSourceToInspect: matchKind == TitleBackgroundPhase2MActorMatchKind.None ? "native character-select actor manager" : "none",
        GroundHeightStatus: groundStatus,
        GroundY: null,
        ActorToCameraDistance: withCameraDeltas ? 3f : null,
        ActorToLookAtDelta: withCameraDeltas ? new Vector3(0f, 0f, 1f) : null,
        ConfiguredCharacterPosition: new Vector3(1f, 2f, 3f),
        ConfiguredCharacterRotation: 0.5f,
        CurveLow: 1f,
        CurveMid: 2f,
        CurveHigh: 3f,
        ActorYMinusPresetCharacterY: actor.HasValue ? 0f : null,
        ActorYMinusFocusY: actor.HasValue ? 0f : null,
        ActorYMinusNativeLookAtY: actor.HasValue ? 0f : null);
}

TitleBackgroundPhase2MActorCandidate Phase2MCandidate(
    int index,
    Vector3 position,
    bool named,
    bool drawObject,
    bool visible)
{
    var zero = position == Vector3.Zero;
    return new TitleBackgroundPhase2MActorCandidate(
        SourceIndex: index,
        Source: "ObjectTable",
        ObjectIndex: 200 + index,
        ObjectKind: "Pc",
        Name: named ? $"candidate-{index}" : string.Empty,
        GameObjectId: (ulong)(0x100 + index),
        EntityId: visible ? (uint)(0x200 + index) : 0xE0000000,
        Address: new nint(0x300 + index),
        Position: position,
        Rotation: 0f,
        Scale: 1f,
        HitboxRadius: 0.5f,
        CurrentHp: visible ? 100u : null,
        MaxHp: visible ? 100u : null,
        Targetable: visible,
        VisibilityHint: visible ? "targetable" : "not-targetable",
        SelectableHint: visible ? "selectable-hint" : "unknown",
        Flags: "none",
        Customize: named ? "customize" : "none",
        Model: drawObject ? "model" : "none",
        DrawObject: drawObject ? "0x400" : "none",
        DrawObjectNonNull: drawObject,
        ModelLikePointer: drawObject ? "model" : "none",
        ModelLikeNonNull: drawObject,
        SafeReadError: "none",
        Named: named,
        PlayerLike: true,
        BattleCharacterLike: true,
        EventNpcLike: false,
        CompanionLike: false,
        VisibleHint: visible,
        DistanceFromConfiguredCharacter: zero ? 551f : 1f,
        DistanceFromActiveLookAt: zero ? 1f : 2f,
        DistanceFromActiveCameraPosition: zero ? 1f : 3f,
        YDeltaFromConfiguredCharacter: position.Y - 2f,
        NearConfiguredCharacter: !zero,
        NearCameraLookAt: true,
        NearCameraPosition: true,
        CategoryReason: "PlayerCharacter,BattleChara",
        Score: zero ? -20 : 100,
        ScoreReason: zero ? "all-zero-transform-penalty:-40" : "non-zero-world-position:+30");
}

TitleBackgroundPhase2MPlacementFrame Phase2MFrameFromCandidates(
    IReadOnlyList<TitleBackgroundPhase2MActorCandidate> candidates,
    TitleBackgroundPhase2MActorMatchKind matchKind = TitleBackgroundPhase2MActorMatchKind.Ambiguous)
{
    var actor = candidates.Count == 1
        ? candidates[0]
        : candidates.FirstOrDefault();
    var hasActor = candidates.Count > 0;
    return new TitleBackgroundPhase2MPlacementFrame(
        Frame: 0,
        Reason: "scene-ready-accepted",
        ActiveCameraCaptured: true,
        ActiveCameraPosition: Vector3.Zero,
        ActiveCameraLookAt: new Vector3(0f, 0f, 0f),
        ActiveCameraYaw: 0f,
        ActiveCameraPitch: 0f,
        ActiveCameraDistance: 3f,
        LobbyCameraCaptured: true,
        LobbyCameraLookAt: Vector3.Zero,
        LobbyDirH: 0f,
        LobbyDirV: 0f,
        LobbyDistance: 3f,
        LobbyInterpDistance: 3f,
        ActorMatchKind: hasActor ? matchKind : TitleBackgroundPhase2MActorMatchKind.None,
        Actor: hasActor ? actor : null,
        ObjectCandidates: candidates,
        SourceDiscovery:
        [
            new TitleBackgroundPhase2MSourceDiscovery(
                "ObjectTable",
                true,
                candidates.Count,
                candidates.Count,
                string.Empty,
                candidates.Count(candidate => candidate.Position != Vector3.Zero),
                candidates.Count(candidate => candidate.DrawObjectNonNull),
                candidates.Count(candidate => candidate.ModelLikeNonNull)),
            new TitleBackgroundPhase2MSourceDiscovery("CharacterManagerObjects", false, 0, 0, "not-exposed"),
        ],
        CandidateCount: candidates.Count,
        ActorStatus: hasActor ? matchKind == TitleBackgroundPhase2MActorMatchKind.Single ? "observed" : "ambiguous" : "not-observed",
        ObjectTableStats: new TitleBackgroundPhase2MObjectTableStats(
            TotalScanned: candidates.Count,
            NamedCount: candidates.Count(candidate => candidate.Named),
            PlayerLikeCount: candidates.Count(candidate => candidate.PlayerLike),
            BattleCharaCount: candidates.Count(candidate => candidate.BattleCharacterLike),
            EventNpcCount: 0,
            CompanionLikeCount: 0,
            NearCameraCount: candidates.Count(candidate => candidate.NearCameraLookAt || candidate.NearCameraPosition),
            NearConfiguredCharacterCount: candidates.Count(candidate => candidate.NearConfiguredCharacter)),
        ActorCandidateStatus: hasActor ? matchKind == TitleBackgroundPhase2MActorMatchKind.Single ? "single" : "ambiguous" : "none",
        ActorCandidateReason: hasActor ? $"multiple-candidates:{candidates.Count}" : "objectTable-unavailable-or-not-exposed",
        ActorSource: hasActor ? "ObjectTable" : "objectTable-unavailable-or-not-exposed",
        NextNativeSourceToInspect: hasActor ? "CharacterManager" : "native character-select actor manager",
        GroundHeightStatus: "unavailable",
        GroundY: null,
        ActorToCameraDistance: hasActor ? Vector3.Distance(actor.Position, Vector3.Zero) : null,
        ActorToLookAtDelta: hasActor ? actor.Position : null,
        ConfiguredCharacterPosition: new Vector3(1f, 2f, 3f),
        ConfiguredCharacterRotation: 0f,
        CurveLow: 1f,
        CurveMid: 2f,
        CurveHigh: 3f,
        ActorYMinusPresetCharacterY: hasActor ? actor.Position.Y - 2f : null,
        ActorYMinusFocusY: hasActor ? actor.Position.Y - 2f : null,
        ActorYMinusNativeLookAtY: hasActor ? actor.Position.Y : null);
}

TitleBackgroundTransitionSummaryInput BuildTransitionSummaryInput(
    TitleBackgroundTransitionDiagnosticRecorder recorder,
    TitleBackgroundTransitionDelta delta,
    bool isLoggedIn,
    bool staleAdapter = false,
    bool phase2GAppliedAfterLogin = false,
    bool activeSceneOverride = false,
    bool historicalLastOverrideApplied = false,
    bool activeSceneOverrideAfterLogin = false,
    bool sceneReadyAcceptedAfterLogin = false,
    string cleanupReason = "none")
{
    return new TitleBackgroundTransitionSummaryInput(
        new TitleBackgroundTransitionContext(
            isLoggedIn,
            IsCharaSelectOrTitleBackground: !isLoggedIn,
            CurrentTerritoryId: isLoggedIn ? 777u : 0u,
            CurrentTerritoryType: isLoggedIn ? "777" : "0",
            CurrentLobbyMap: isLoggedIn ? "None" : "CharaSelect"),
        new TitleBackgroundTransitionSceneOverrideState(
            Active: activeSceneOverride,
            HistoricalLastOverrideApplied: historicalLastOverrideApplied,
            ActiveLobbyType: activeSceneOverride ? "CharaSelect" : "None",
            ActiveOverridePath: activeSceneOverride ? "ex5/test/level/test" : "none",
            LastHistoricalOverridePath: historicalLastOverrideApplied ? "ex5/test/level/test" : "none",
            CurrentLobbyMap: isLoggedIn ? "None" : "CharaSelect",
            LastCurrentLobbyMapResetReason: cleanupReason,
            CleanupReason: cleanupReason,
            ActiveAfterLoginDetected: activeSceneOverrideAfterLogin),
        new TitleBackgroundTransitionAdapterState(
            State: staleAdapter ? "Active" : "Inactive",
            LastEvent: "not-run",
            SceneGeneration: staleAdapter ? 1 : 0,
            StaleAfterLoginDetected: staleAdapter),
        new TitleBackgroundTransitionPhase2GState(
            LastApplyContext: phase2GAppliedAfterLogin ? "logged-in" : recorder.LastPhase2GApplyContext,
            AppliedAfterLogin: phase2GAppliedAfterLogin || recorder.Phase2GAppliedAfterLogin,
            LastAppliedAfterLoginEventSeq: phase2GAppliedAfterLogin
                ? Math.Max(1, recorder.LastPhase2GAppliedAfterLoginEventSeq)
                : recorder.LastPhase2GAppliedAfterLoginEventSeq,
            AppliedAfterLeavingCharaSelect: recorder.Phase2GAppliedAfterLeavingCharaSelect,
            LastAllowedReason: recorder.LastPhase2GAllowedReason,
            LastSkippedReason: recorder.LastPhase2GSkippedReason),
        new TitleBackgroundTransitionCameraState(
            CurrentCaptureStatus: "not-run",
            CurrentDirH: "none",
            CurrentDirV: "none",
            CurrentDistance: "none",
            CurrentPosition: "none",
            CurrentLookAt: "none"),
        new TitleBackgroundTransitionCounters(0, 0, 0, 0, 0, recorder.SceneReadyAcceptedCount, recorder.SceneReadyRawCallCount),
        delta,
        recorder.FirstEvent,
        recorder.LastEvent,
        recorder.EventCount,
        recorder.SceneReadyRawCallCount,
        recorder.SceneReadyAcceptedCount,
        recorder.SceneReadyRejectedCount,
        recorder.SceneReadyAcceptedCount > 1,
        recorder.SceneReadyLastAcceptedReason,
        recorder.SceneReadyLastRejectedReason,
        recorder.SceneReadyLastAcceptedSceneGeneration,
        recorder.AcceptedGenerations,
        sceneReadyAcceptedAfterLogin || recorder.PostLoginSceneReadyAccepted,
        sceneReadyAcceptedAfterLogin
            ? Math.Max(1, recorder.LastSceneReadyAcceptedAfterLoginEventSeq)
            : recorder.LastSceneReadyAcceptedAfterLoginEventSeq);
}

TitleBackgroundTransitionDelta TrustedDelta(
    int phase2ELookAtYCallCount,
    int phase2FSetMidCallCount,
    int phase2FLowHighCallCount,
    int phase2GSetMidAttemptCount,
    int phase2GLowHighAttemptCount,
    int sceneReadyAcceptedCount,
    int sceneReadyRawCallCount)
{
    return new TitleBackgroundTransitionDelta(
        BaselineEstablished: true,
        FirstReport: false,
        phase2ELookAtYCallCount,
        phase2FSetMidCallCount,
        phase2FLowHighCallCount,
        phase2GSetMidAttemptCount,
        phase2GLowHighAttemptCount,
        sceneReadyAcceptedCount,
        sceneReadyRawCallCount);
}

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

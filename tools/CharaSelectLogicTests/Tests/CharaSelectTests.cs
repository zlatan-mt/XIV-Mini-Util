// Path: tools/CharaSelectLogicTests/Tests/CharaSelectTests.cs
// Description: Registers regression tests for the CharaSelect responsibility
// Reason: Keeps the former monolithic runner maintainable
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Text;
using System.Text.Json;
using XivMiniUtil;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Market;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

internal static partial class TestRunner
{
    private static void AddCharaSelectTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

Test(0, "first valid character replays", () =>
{
    var tracker = new CharaSelectReplayTracker();
    return tracker.ShouldReplay(1, 100, 0x1000, force: false);
});

Test(1, "same character and emote does not replay repeatedly", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return !tracker.ShouldReplay(1, 100, 0x1000, force: false);
});

Test(2, "same content id with new character pointer replays", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 100, 0x2000, force: false);
});

Test(11, "force replay bypasses previous state", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 100, 0x1000, force: true);
});

Test(12, "invalid replay inputs are ignored", () =>
{
    var tracker = new CharaSelectReplayTracker();
    return !tracker.ShouldReplay(0, 100, 0x1000, force: true)
        && !tracker.ShouldReplay(1, 0, 0x1000, force: true)
        && !tracker.ShouldReplay(1, 100, nint.Zero, force: true);
});

Test(13, "chara select scene profile old sharlayan exists", () =>
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

Test(14, "chara select scene profile expects character visible", () =>
{
    var profile = CharaSelectSceneProfileRegistry.GetDefault();
    return profile.Id == "scene:old-sharlayan-k5t1"
        && profile.CharacterExpectedVisible
        && profile.RecommendedAction == "verify-character-visible-and-emote";
});

Test(15, "chara select scene composition is default off", () =>
{
    var configuration = new Configuration();
    return !configuration.CharaSelectSceneCompositionEnabled
        && configuration.CharaSelectSceneProfileId == CharaSelectSceneProfileRegistry.DefaultProfileId
        && configuration.CharaSelectScenePlacementMode == CharaSelectScenePlacementMode.ObserveOnly
        && configuration.CharaSelectSceneStageStrategy == CharaSelectStageStrategy.ObserveOnly;
});

Test(19, "chara select scene profile preserves existing emote presets", () =>
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

Test(20, "chara select scene observe only does not enable position override without profile position", () =>
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

Test(21, "chara select scene runtime apply is post login no op", () =>
{
    return !CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: true, agentIsLoggedIn: false, enabled: true)
        && !CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: false, agentIsLoggedIn: true, enabled: true)
        && CharaSelectSceneCompositionPlanner.ShouldApplyRuntime(isLoggedIn: false, agentIsLoggedIn: false, enabled: true);
});

Test(23, "chara select scene last observation survives as value diagnostics", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ClientSelectDataTerritoryPatch,
        CharaSelectOverrideTerritoryEnabled = true,
        CharaSelectOverrideTerritoryTypeId = 962,
    };
    var observation = new CharaSelectSceneLastObservation(
        true,
        true,
        123456789,
        2,
        "scene:old-sharlayan-k5t1",
        "Old Sharlayan outdoor test",
        true,
        true,
        false,
        false,
        "2026-05-31T00:00:00.0000000+00:00",
        new CharaSelectStageProbeSnapshot(
            true,
            "read-only-observation",
            "2026-05-31T00:00:00.0000000+00:00",
            2,
            2,
            0,
            123456789,
            true,
            1,
            2,
            962,
            0,
            true,
            true,
            true,
            "patch-applied",
            0,
            0,
            false,
            0,
            "not-changed",
            true,
            1,
            10,
            1,
            962,
            0,
            "lobby-row-candidate-found",
            true,
            "ex4/03_kld_k5/twn/k5t1/level/k5t1",
            0,
            0,
            "OverrideDisplay",
            "loaded",
            false,
            false,
            "disabled-for-final-composition",
            "source-not-resolved",
            "verify-with-screenshot-and-set-manual-results"));

    var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(configuration, "none", "True", observation);
    var lines = CharaSelectSceneCompositionPlanner.BuildDiagnosticLines(diagnostic);

    return diagnostic.LastObservationAvailable
        && diagnostic.LastObservationCharacterPointerResolved
        && diagnostic.LastObservationContentId == 123456789
        && diagnostic.LastObservationSelectedIndex == 2
        && diagnostic.LastObservationProfileName == "Old Sharlayan outdoor test"
        && diagnostic.LastObservationTerritoryOverrideApplied
        && diagnostic.LastObservationEmoteReplayAttempted
        && !diagnostic.LastObservationEmoteReplayApplied
        && !diagnostic.LastObservationTitleBackgroundConflictDetected
        && diagnostic.StageProbe.ClientSelectDataPatchApplied
        && lines.Contains("charaSelectScene.lastObservation.characterPointerResolved=True")
        && lines.Contains("charaSelectStageProbe.clientSelectData.patchApplied=True");
});

Test(24, "chara select scene next action follows manual result", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        LastSceneProfileCharacterVisibleResult = CharaSelectSceneBinaryResult.No,
    };
    var hiddenNext = CharaSelectSceneCompositionPlanner.BuildNextAction(configuration);

    configuration.LastSceneProfileCharacterVisibleResult = CharaSelectSceneBinaryResult.Yes;
    configuration.LastSceneProfileLocationChangedResult = CharaSelectSceneBinaryResult.No;
    var noLocationNext = CharaSelectSceneCompositionPlanner.BuildNextAction(configuration);

    configuration.LastSceneProfileLocationChangedResult = CharaSelectSceneBinaryResult.Yes;
    configuration.LastSceneProfileEmotePlayedResult = CharaSelectSceneBinaryResult.No;
    var noEmoteNext = CharaSelectSceneCompositionPlanner.BuildNextAction(configuration);

    configuration.LastSceneProfileEmotePlayedResult = CharaSelectSceneBinaryResult.Yes;
    var readyNext = CharaSelectSceneCompositionPlanner.BuildNextAction(configuration);

    return hiddenNext == "inspect-character-visibility-route"
        && noLocationNext == "discover-visible-stage-source"
        && noEmoteNext == "fix-emote-replay-route"
        && readyNext == "implement-one-shot-placement";
});

Test(26, "chara select stage strategy observe only does not perform write", () =>
{
    var configuration = new Configuration
    {
        CharaSelectSceneCompositionEnabled = true,
        CharaSelectSceneUseProfileTerritory = true,
        CharaSelectSceneStageStrategy = CharaSelectStageStrategy.ObserveOnly,
    };

    CharaSelectSceneCompositionPlanner.ApplyProfileToConfiguration(
        configuration,
        CharaSelectSceneProfileRegistry.GetDefault());
    var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(configuration, "none");

    return !configuration.CharaSelectOverrideTerritoryEnabled
        && diagnostic.StageStrategyDiagnostic.Selected == CharaSelectStageStrategy.ObserveOnly
        && diagnostic.StageStrategyDiagnostic.Available
        && !diagnostic.StageStrategyDiagnostic.Applied;
});

Test(28, "chara select scene composition controls are removed from settings", () =>
{
    var root = FindRepositoryRoot();
    var settings = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    return !settings.Contains("場所が default のままなら", StringComparison.Ordinal)
        && !settings.Contains("場所=変わらない", StringComparison.Ordinal)
        && !settings.Contains("DrawCharaSelectSceneCompositionSettings", StringComparison.Ordinal);
});

Test(102, "normal diagnostic sceneReady verdict preserves cumulative count", () =>
{
    // 通常診断（automaticInvocation=false）や run-scoped 無効時は累積値を維持して長期傾向を残す。
    var normal = TitleBackgroundAutomaticCheckLogic.ResolveVerdictSceneReadyAcceptedCount(
        automaticInvocation: false,
        runScopedActive: true,
        cumulativeAcceptedCount: 3,
        runStartAcceptedCount: 2);
    var notRunScoped = TitleBackgroundAutomaticCheckLogic.ResolveVerdictSceneReadyAcceptedCount(
        automaticInvocation: true,
        runScopedActive: false,
        cumulativeAcceptedCount: 3,
        runStartAcceptedCount: 2);

    return normal == 3 && notRunScoped == 3;
});

Test(105, "placement result is run-scoped: previous run success does not leak into current run", () =>
{
    // 前回 run で1回配置成功（累積1）、今回 run の開始 baseline も1 → 今回の配置は0回。
    var currentRunCount = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
        runScoped: true,
        cumulativeCount: 1,
        runStartCount: 1);
    // run-scoped でないときは累積を維持する。
    var cumulative = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
        runScoped: false,
        cumulativeCount: 1,
        runStartCount: 1);
    // 今回 run でも配置されたケース。
    var placedThisRun = TitleBackgroundAutomaticCheckLogic.ResolveRunScopedPlacementCount(
        runScoped: true,
        cumulativeCount: 2,
        runStartCount: 1);

    // 今回0回なら composited 扱いにならず、過去 run の source/frame を流用しない。
    var result = TitleBackgroundQuickCheckEvaluator.Evaluate(QuickCheckInput(
        characterExpectedVisible: false,
        characterCompositedApplied: currentRunCount > 0,
        characterGroundPlacementVerified: false));

    return currentRunCount == 0
        && cumulative == 1
        && placedThisRun == 1
        && !result.Warnings.Any(warning => warning.Contains("ground position is not verified", StringComparison.Ordinal))
        && result.CharacterStatus == "not detected by diagnostics / visual confirmation required";
});

Test(124, "emote mode uses condition mode and row id parameter", () =>
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

Test(125, "empty emote mode resets to normal", () =>
{
    var plan = CharaSelectEmotePlaybackPlanner.Create(0, 0, 0, 3);
    return plan.Mode == CharacterModes.Normal
        && plan.ModeParam == 0
        && plan.LoopTimelineId == 3
        && plan.HasTimeline;
});

Test(126, "voice id is applied from selected lobby entry", () =>
{
    unsafe
    {
        var character = new Character();

        CharaSelectCharacterApplier.ApplyVoice(&character, 42, loadSound: false);
        return character.Vfx.VoiceId == 42;
    }
});

Test(127, "voice id resolves through zero-based voice table", () =>
{
    byte[] voices = [20, 21, 22];
    return CharaSelectVoiceIdResolver.Resolve(1, voices.Length, index => voices[index]) == 21;
});

Test(128, "voice id falls back to one-based voice table when zero-based entry is empty", () =>
{
    byte[] voices = [20, 0, 22];
    return CharaSelectVoiceIdResolver.Resolve(1, voices.Length, index => voices[index]) == 20;
});

Test(129, "preset active index is clamped", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 9 };
    return CharaSelectEmotePresetStore.GetActiveIndex(presets, activeIndexes, 1) == 1
        && CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1) == 101;
});

Test(130, "preset next changes active emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 0 };
    return CharaSelectEmotePresetStore.SelectNext(presets, activeIndexes, 1)
        && CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1) == 101;
});

Test(131, "preset append selects added emote and avoids duplicates", () =>
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

Test(132, "preset save to active slot replaces current emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 1 };
    return CharaSelectEmotePresetStore.SaveToActiveSlot(presets, activeIndexes, 1, 102)
        && presets[1].SequenceEqual(new uint[] { 100, 102 })
        && activeIndexes[1] == 1;
});

Test(133, "preset remove active advances to remaining emote", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100, 101] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 1 };
    return CharaSelectEmotePresetStore.RemoveActive(presets, activeIndexes, 1)
        && presets[1].SequenceEqual(new uint[] { 100 })
        && activeIndexes[1] == 0;
});

Test(134, "legacy fallback is gone after explicit clear removes both stores", () =>
{
    var presets = new Dictionary<ulong, List<uint>> { [1] = [100] };
    var activeIndexes = new Dictionary<ulong, int> { [1] = 0 };
    var legacy = new Dictionary<ulong, uint> { [1] = 100 };
    CharaSelectEmotePresetStore.RemoveActive(presets, activeIndexes, 1);
    legacy.Remove(1);
    return CharaSelectEmotePresetStore.GetActiveEmoteId(presets, activeIndexes, 1, legacy) == null;
});

Test(135, "active emote change replays through tracker", () =>
{
    var tracker = new CharaSelectReplayTracker();
    tracker.MarkReplayed(1, 100, 0x1000);
    return tracker.ShouldReplay(1, 101, 0x1000, force: false);
});

Test(136, "last recorded emote is isolated per content id", () =>
{
    var lastRecorded = new Dictionary<ulong, uint>
    {
        [1] = 100,
        [2] = 101,
    };
    return lastRecorded[1] == 100 && lastRecorded[2] == 101;
});

Test(297, "title chara select transition is isolated", () =>
{
    return GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.Title, GameLobbyType.CharaSelect)
        && GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.CharaSelect, GameLobbyType.Title)
        && !GameLobbyTypeHelper.IsTitleCharaSelectTransition(GameLobbyType.Title, GameLobbyType.LaNoscea);
});

Test(298, "title chara select transition resets current map only when override enabled", () =>
{
    return GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.CharaSelect, overrideEnabled: true) == GameLobbyType.None
        && GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.CharaSelect, overrideEnabled: false) == GameLobbyType.Title
        && GameLobbyTypeHelper.GetCurrentMapForTransition(GameLobbyType.Title, GameLobbyType.LaNoscea, overrideEnabled: true) == GameLobbyType.Title;
});

Test(310, "game lobby type none remains minus one", () =>
{
    return (short)GameLobbyType.None == -1;
});

Test(332, "chara select service voice diagnostics extracted to partial", () =>
{
    var root = FindRepositoryRoot();
    var diagFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectService.Diagnostics.cs");
    var mainFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect", "CharaSelectService.cs");
    var diagText = File.ReadAllText(diagFile);
    var mainText = File.ReadAllText(mainFile);
    return diagText.Contains("GetVoiceDiagnosticLines", StringComparison.Ordinal)
        && diagText.Contains("GetSceneCompositionDiagnosticLines", StringComparison.Ordinal)
        && diagText.Contains("AppendVoiceTableDiagnostics", StringComparison.Ordinal)
        && mainText.Contains("partial class CharaSelectService", StringComparison.Ordinal)
        && !mainText.Contains("GetVoiceDiagnosticLines", StringComparison.Ordinal);
});

Test(334, "chara select service emote and prefetch logic extracted to partials", () =>
{
    var root = FindRepositoryRoot();
    var serviceDir = Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "CharaSelect");
    var mainText = File.ReadAllText(Path.Combine(serviceDir, "CharaSelectService.cs"));
    var emoteText = File.ReadAllText(Path.Combine(serviceDir, "CharaSelectService.Emotes.cs"));
    var prefetchText = File.ReadAllText(Path.Combine(serviceDir, "CharaSelectService.Prefetch.cs"));
    return emoteText.Contains("private void SaveExecutedEmote(", StringComparison.Ordinal)
        && emoteText.Contains("private bool PlayEmote(", StringComparison.Ordinal)
        && prefetchText.Contains("private void PreloadLoginTerritory()", StringComparison.Ordinal)
        && prefetchText.Contains("private void TryLoadPrefetchLayout(", StringComparison.Ordinal)
        && !mainText.Contains("private bool PlayEmote(", StringComparison.Ordinal)
        && !mainText.Contains("private void TryLoadPrefetchLayout(", StringComparison.Ordinal);
});

Test(335, "plugin commands are registered through a command table", () =>
{
    var root = FindRepositoryRoot();
    var pluginText = string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(
            Path.Combine(root, "projects", "XIV-Mini-Util"),
            "Plugin*.cs").Select(File.ReadAllText));
    return pluginText.Contains("private readonly record struct CommandRegistration", StringComparison.Ordinal)
        && pluginText.Contains("private IReadOnlyList<CommandRegistration> GetCommandRegistrations()", StringComparison.Ordinal)
        && pluginText.Contains("private void RegisterCommands()", StringComparison.Ordinal)
        && pluginText.Contains("private void UnregisterCommands()", StringComparison.Ordinal)
        && pluginText.Contains("_commandManager.AddHandler(registration.Name", StringComparison.Ordinal)
        && pluginText.Contains("_commandManager.RemoveHandler(registration.Name", StringComparison.Ordinal)
        && CountOccurrences(pluginText, "_commandManager.AddHandler(") == 1
        && CountOccurrences(pluginText, "_commandManager.RemoveHandler(") == 1;
});

Test(401, "new automatic run clears prior in-memory report state", () =>
{
    var reset = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "private void ResetAutomaticCheckReportForNewRun()");
    var oneClick = ReadServiceMethodBody(
        "TitleScreenBackgroundService.OneClickVerification.cs",
        "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()");
    var legacy = ReadServiceMethodBody(
        "TitleScreenBackgroundService.QuickCheck.cs",
        "internal IReadOnlyList<string> StartAutomaticQuickCheck()");
    return reset.Contains("_automaticCheck.LastReport = string.Empty", StringComparison.Ordinal)
        && reset.Contains("_automaticCheck.PendingClipboardText = string.Empty", StringComparison.Ordinal)
        && reset.Contains("_automaticCheck.ReportAvailable = false", StringComparison.Ordinal)
        && oneClick.Contains("ResetAutomaticCheckReportForNewRun()", StringComparison.Ordinal)
        && legacy.Contains("ResetAutomaticCheckReportForNewRun()", StringComparison.Ordinal);
});

Test(405, "windows release script safely cleans staging and verifies package artifacts", () =>
{
    var root = FindRepositoryRoot();
    var script = File.ReadAllText(Path.Combine(root, "scripts", "release-build.ps1"));
    return script.Contains("StartsWith(", StringComparison.Ordinal)
        && script.Contains("Remove-Item -LiteralPath $stagingDirectory -Force -Recurse", StringComparison.Ordinal)
        && script.Contains("'XivMiniUtil.json'", StringComparison.Ordinal)
        && script.Contains("'latest.zip'", StringComparison.Ordinal);
});

Test(406, "simplification keeps safety boundaries intact", () =>
{
    // 2026-07-03: PersistentApplyEnabled は実機3点検証(残差0.002)を経て解禁(true)。
    // Evaluate gate 自体・ground provenance 判定・placement 判定の安全境界は不変であることをロックする。
    return TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled
        && !TitleBackgroundCharaSelectAnchorFrame.IsPlacementSupported(TitleBackgroundCharaSelectAnchorFrame.World)
        && !TitleBackgroundCharaSelectAnchorFrame.HasGroundProvenance("world")
        && !TitleBackgroundAutomaticCheckLogic.ResolveGroundPlacementVerified(true, "world-experimental", "world");
});

Test(424, "run bulk diagnostic uses summary entrypoint", () =>
{
    var root = FindRepositoryRoot();
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var body = ExtractMethodBody(serviceText, "public IReadOnlyList<string> RunBulkDiagnostic()");

    // 一括診断は要約分岐（false）を使い、詳細(true)で 17,000 行超に膨張させない。
    return body.Contains("GetDiagnosticLines(includeDetailedPhase2Diagnostics: false)", StringComparison.Ordinal)
        && !body.Contains("includeDetailedPhase2Diagnostics: true", StringComparison.Ordinal);
});

    }
}

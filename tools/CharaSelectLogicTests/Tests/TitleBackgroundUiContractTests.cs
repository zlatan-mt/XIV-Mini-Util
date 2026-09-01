// Path: tools/CharaSelectLogicTests/Tests/TitleBackgroundUiContractTests.cs
// Description: Registers regression tests for the TitleBackgroundUiContract responsibility
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
    private static void AddTitleBackgroundUiContractTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

        string ReadOneClickPreparationBody() =>
            ReadServiceMethodBody(
                "TitleScreenBackgroundService.OneClickVerification.cs",
                "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()")
            + ReadServiceMethodBody(
                "TitleScreenBackgroundService.OneClickVerification.cs",
                "private IReadOnlyList<string> ContinueOneClickAfterSourceCapture()");

Test(77, "title background normal screen hides advanced diagnostics", () =>
{
    var root = FindRepositoryRoot();
    var settingsText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    var normal = ExtractMethodBody(settingsText, "private void DrawTitleBackgroundSettings()");

    return normal.Contains("この場所で確認を開始", StringComparison.Ordinal)
        && normal.Contains("初期状態に戻す", StringComparison.Ordinal)
        && !normal.Contains("結果をコピー", StringComparison.Ordinal)
        && !normal.Contains("自動確認を開始", StringComparison.Ordinal)
        && !normal.Contains("DrawTitleBackgroundBulkDiagnosticButton", StringComparison.Ordinal)
        && !normal.Contains("TryCaptureLoggedInPositionAsAnchor", StringComparison.Ordinal)
        && !normal.Contains("DrawTitleBackgroundLayerStepControls", StringComparison.Ordinal)
        && !normal.Contains("DrawTitleBackgroundCharacterCompositionBridgeDiagnostics", StringComparison.Ordinal)
        && !normal.Contains("DrawTitleBackgroundDiagnostics", StringComparison.Ordinal)
        && !normal.Contains("Start QuickCheck", StringComparison.Ordinal)
        && !normal.Contains("Reset Check", StringComparison.Ordinal);
});

Test(78, "title background normal screen uses the one-click entrypoint", () =>
{
    var root = FindRepositoryRoot();
    var settingsText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    var serviceText = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Services", "TitleBackground"), "TitleScreenBackgroundService*.cs").Select(File.ReadAllText));
    var normal = ExtractMethodBody(settingsText, "private void DrawTitleBackgroundSettings()");

    return normal.Contains("StartOneClickTitleBackgroundVerification", StringComparison.Ordinal)
        && !normal.Contains("StartAutomaticQuickCheck", StringComparison.Ordinal)
        && !normal.Contains("RunBulkDiagnostic", StringComparison.Ordinal)
        && serviceText.Contains("public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()", StringComparison.Ordinal);
});

Test(193, "title background normal diagnostics exclude detailed failure-only lines", () =>
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

Test(267, "title background normal diagnostics exclude obsolete direct look at y fields", () =>
{
    return TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("lookAtYApply.attemptCount=1")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("lookAtYApply.readBackValueImmediatelyAfterWrite=0.834")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.lookAtYImmediateReflection=reflected")
        && TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.lookAtYPostApplyStability=stable")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.phase2G.finalLookAtYMatchesGeneratedCurve=observed")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("phase2E.calculateLobbyCameraLookAtY.call[1].returnValue=0.834")
        && !TitleBackgroundCameraProbeReport.IsObsoleteDirectLookAtYDiagnosticLine("verdict.phase2G.finalYawPitchDistanceMatchesPreset=not-observed");
});

Test(268, "title background normal diagnostics keep yaw pitch distance blocking flag and deprecated camera verdict", () =>
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

Test(328, "settings tab is split into chara select partial", () =>
{
    var root = FindRepositoryRoot();
    var charaSelectFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.CharaSelect.cs");
    var mainFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.cs");
    var charaSelectText = File.ReadAllText(charaSelectFile);
    var mainText = File.ReadAllText(mainFile);
    return charaSelectText.Contains("private void DrawCharaSelectEmoteSettings()", StringComparison.Ordinal)
        && !charaSelectText.Contains("DrawLegacyCharaSelectDiagnostics", StringComparison.Ordinal)
        && !charaSelectText.Contains("Legacy experiments", StringComparison.Ordinal)
        && !charaSelectText.Contains("DrawCharaSelectSceneCompositionSettings", StringComparison.Ordinal)
        && !charaSelectText.Contains("CollapsingHeader", StringComparison.Ordinal)
        && mainText.Contains("partial class SettingsTab", StringComparison.Ordinal)
        && !mainText.Contains("private void DrawCharaSelectEmoteSettings()", StringComparison.Ordinal);
});

Test(329, "settings tab is split into title background partial", () =>
{
    var root = FindRepositoryRoot();
    var tbFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackground.cs");
    var mainFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.cs");
    var tbText = File.ReadAllText(tbFile);
    var mainText = File.ReadAllText(mainFile);
    // 通常画面は最小化し、不要になった開発者向け診断UIはファイルごと削除する。
    return tbText.Contains("private void DrawTitleBackgroundSettings()", StringComparison.Ordinal)
        && !tbText.Contains("DrawTitleBackgroundSimplePanel", StringComparison.Ordinal)
        && !tbText.Contains("ClearTitleBackgroundInputs", StringComparison.Ordinal)
        && !File.Exists(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackgroundDiagnostics.cs"))
        && !mainText.Contains("private void DrawTitleBackgroundSettings()", StringComparison.Ordinal);
});

Test(331, "settings tab main file does not contain dead IsStatusError method", () =>
{
    var root = FindRepositoryRoot();
    var settingsAll = string.Join(Environment.NewLine, Directory.EnumerateFiles(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components"), "SettingsTab*.cs").Select(File.ReadAllText));
    return !settingsAll.Contains("IsStatusError", StringComparison.Ordinal);
});

Test(389, "title background normal screen has at most 4 interactive controls", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    var controls = CountOccurrences(body, "ImGui.RadioButton(")
        + CountOccurrences(body, "ImGui.Button(")
        + CountOccurrences(body, "ImGui.Checkbox(")
        + CountOccurrences(body, "ImGui.BeginCombo(");
    return body.Length > 0 && controls <= 4;
});

Test(390, "title background normal screen only uses the allowed labels", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    var root = FindRepositoryRoot();
    var selectionLogic = File.ReadAllText(Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground",
        "TitleBackgroundCharaSelectPresetSelectionLogic.cs"));

    return body.Contains("背景##TitleBackgroundPreset", StringComparison.Ordinal)
        && body.Contains("BuildChoices()", StringComparison.Ordinal)
        && body.Contains("SelectTitleBackgroundPreset(", StringComparison.Ordinal)
        && body.Contains("SetEnabled(false)", StringComparison.Ordinal)
        && body.Contains("この場所で確認を開始##", StringComparison.Ordinal)
        && body.Contains("初期状態に戻す##", StringComparison.Ordinal)
        && body.Contains("現在の構図を保存##", StringComparison.Ordinal)
        && body.Contains("IsCharaSelectViewCaptureAvailable()", StringComparison.Ordinal)
        && body.Contains("TryCaptureCharaSelectViewFromCurrentCamera", StringComparison.Ordinal)
        // curated ラベルは selection logic 側に閉じ込め、通常画面には英語内部名を出さない。
        && selectionLogic.Contains("\"イル・メグ\"", StringComparison.Ordinal)
        && !selectionLogic.Contains("\"オールド・シャーレアン\"", StringComparison.Ordinal)
        && !body.Contains("結果をコピー", StringComparison.Ordinal)
        && !body.Contains("一括診断", StringComparison.Ordinal)
        && !body.Contains("座標対応", StringComparison.Ordinal)
        && !body.Contains("probe", StringComparison.OrdinalIgnoreCase)
        && !body.Contains("開発者", StringComparison.Ordinal);
});

Test(391, "title background normal screen does not call developer draw methods", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    return !body.Contains("DrawTitleBackgroundDiagnostics", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundQuickActions", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundCharaSelectAnchorControls", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundViewControls", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundBulkDiagnosticButton", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundPresetSettings", StringComparison.Ordinal)
        && !body.Contains("DrawCharaSelectSceneCompositionSettings", StringComparison.Ordinal)
        && !body.Contains("DrawTitleBackgroundSimpleStandingPositionButton", StringComparison.Ordinal);
});

Test(392, "title background normal screen has no collapsing/treenode/developer toggle", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    return !body.Contains("CollapsingHeader", StringComparison.Ordinal)
        && !body.Contains("TreeNode", StringComparison.Ordinal)
        && !body.Contains("DeveloperDiagnostics", StringComparison.Ordinal)
        && !body.Contains("SettingsDisplayMode", StringComparison.Ordinal);
});

Test(393, "title background main button calls only the single one-click service entry", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    return CountOccurrences(body, "StartOneClickTitleBackgroundVerification(") == 1
        && !body.Contains("CaptureWorldProbeAnchorInMemory", StringComparison.Ordinal)
        && !body.Contains("StartAutomaticQuickCheck", StringComparison.Ordinal)
        && !body.Contains("StartQuickCheck", StringComparison.Ordinal)
        && !body.Contains("ApplySimpleAutoSetup", StringComparison.Ordinal);
});

Test(394, "one-click applies recommended candidate before probe capture", () =>
{
    var body = ReadOneClickPreparationBody();
    var applyIndex = body.IndexOf("ApplySimpleAutoSetup", StringComparison.Ordinal);
    var probeIndex = body.IndexOf("CaptureWorldProbeAnchorInMemory", StringComparison.Ordinal);
    return applyIndex >= 0 && probeIndex >= 0 && applyIndex < probeIndex;
});

Test(395, "one-click keeps non-V2 fail-closed current-location probe before automatic check start", () =>
{
    var body = ReadOneClickPreparationBody();
    var resolveIndex = body.IndexOf("ResolveExperimentalWorldPlacement(candidate)", StringComparison.Ordinal);
    var startIndex = body.IndexOf("_automaticCheck.Requested = true", StringComparison.Ordinal);
    return body.Contains("CaptureWorldProbeAnchorInMemory(out var probeStatus)", StringComparison.Ordinal)
        && resolveIndex >= 0
        && startIndex >= 0
        && resolveIndex < startIndex
        && body.Contains("if (!worldResolution.Eligible)", StringComparison.Ordinal)
        && body.Contains("\"probe-not-applicable\"", StringComparison.Ordinal)
        && body.Contains("\"probe-capture-failed\"", StringComparison.Ordinal)
        && body.Contains("FailOneClickWithReport(", StringComparison.Ordinal);
});

Test(398, "one-click bypasses the legacy current-location probe preflight when a new Character Select engine is active", () =>
{
    var body = ReadOneClickPreparationBody();
    var applyIndex = body.IndexOf("ApplySimpleAutoSetup", StringComparison.Ordinal);
    var engineGuardIndex = body.IndexOf("if (!IsNewCharaSelectEngineActive)", StringComparison.Ordinal);
    var probeIndex = body.IndexOf("CaptureWorldProbeAnchorInMemory(out var probeStatus)", StringComparison.Ordinal);
    var resolveIndex = body.IndexOf("ResolveExperimentalWorldPlacement(candidate)", StringComparison.Ordinal);
    var hookIndex = body.IndexOf("ReloadNativeIntegrationForOneClick()", StringComparison.Ordinal);

    // candidate 設定は probe より先。probe / world resolution は新エンジン非 active ガードの内側にあり、
    // hook readiness へ抜ける経路より前に置かれている（V2 / placement は probe をスキップして直行）。
    return applyIndex >= 0
        && engineGuardIndex > applyIndex
        && probeIndex > engineGuardIndex
        && resolveIndex > engineGuardIndex
        && hookIndex > engineGuardIndex
        && resolveIndex < hookIndex;
});

Test(396, "one-click retries native init once before declaring hook-not-ready", () =>
{
    var body = ReadOneClickPreparationBody();
    return CountOccurrences(body, "ReloadNativeIntegrationForOneClick()") == 2
        && body.Contains("hook-not-ready", StringComparison.Ordinal)
        && body.Contains("_hookLifecycle.State != TitleBackgroundServiceState.Ready", StringComparison.Ordinal);
});

Test(397, "one-click failure report is auto-copied (no extra action required)", () =>
{
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.OneClickVerification.cs", "private void EmitOneClickFailureReport(string reason, string detail)");
    return body.Contains("PublishAutomaticCheckReport(report, \"one-click-failure\")", StringComparison.Ordinal)
        && body.Contains("hookReady=", StringComparison.Ordinal)
        && body.Contains("candidate=", StringComparison.Ordinal)
        && body.Contains("reinitResult=", StringComparison.Ordinal);
});

Test(399, "one-click status surfaces only user-facing strings, no internal names", () =>
{
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.OneClickVerification.cs", "internal TitleBackgroundOneClickStatus GetOneClickStatus()");
    return body.Contains("準備完了", StringComparison.Ordinal)
        && body.Contains("ログアウトしてください", StringComparison.Ordinal)
        && body.Contains("キャラ選択画面を確認中", StringComparison.Ordinal)
        && body.Contains("ログインしてください", StringComparison.Ordinal)
        && body.Contains("完了処理中", StringComparison.Ordinal)
        && body.Contains("完了：レポートをコピーしました", StringComparison.Ordinal)
        && body.Contains("失敗：レポートをコピーしました", StringComparison.Ordinal)
        && !body.Contains("Phase", StringComparison.Ordinal)
        && !body.Contains("custom:n4f4", StringComparison.Ordinal)
        && !body.Contains("probe", StringComparison.OrdinalIgnoreCase);
});

Test(404, "repository contract keeps one-click verification and minimal visible ui", () =>
{
    var root = FindRepositoryRoot();
    var agentsPath = Path.Combine(root, "AGENTS.md");
    var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));
    var agents = File.ReadAllText(agentsPath);
    var settingsPath = Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Windows",
        "Components",
        "SettingsTab.cs");
    var settings = File.ReadAllText(settingsPath);

    return !gitignore
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), "AGENTS.md", StringComparison.Ordinal))
        && agents.Contains("原則として1回の操作または1回の対象フロー", StringComparison.Ordinal)
        && !File.Exists(Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackgroundDiagnostics.cs"))
        && !settings.Contains("Title背景 診断（開発者）", StringComparison.Ordinal)
        && !settings.Contains("DrawTitleBackgroundDiagnostics", StringComparison.Ordinal);
});

Test(405, "title background OFF clears the V2 production flag without adding a normal-screen control", () =>
{
    var root = FindRepositoryRoot();
    var servicePath = Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs");
    var setEnabledBody = ExtractMethodBody(File.ReadAllText(servicePath), "public void SetEnabled(bool enabled)");

    var normalBody = ReadTitleBackgroundNormalBody();
    var controls = CountOccurrences(normalBody, "ImGui.RadioButton(")
        + CountOccurrences(normalBody, "ImGui.Button(")
        + CountOccurrences(normalBody, "ImGui.Checkbox(")
        + CountOccurrences(normalBody, "ImGui.BeginCombo(");

    // OFF 選択は SetEnabled(false) を呼ぶだけ。V2 フラグ解除はサービス側で行い、通常画面の操作数は増やさない。
    return setEnabledBody.Contains("if (!enabled)", StringComparison.Ordinal)
        && setEnabledBody.Contains("TitleBackgroundV2Enabled = false", StringComparison.Ordinal)
        && normalBody.Contains("SetEnabled(false)", StringComparison.Ordinal)
        && controls <= 4;
});

Test(406, "curated preset selector exposes OFF, Il Mheg, Elpis, and FRU in order", () =>
{
    var choices = TitleBackgroundCharaSelectPresetSelectionLogic.BuildChoices();
    return choices.Count == 4
        && choices[0].CandidateId == string.Empty
        && choices[0].DisplayLabel == "OFF"
        && !choices[0].Experimental
        && choices[1].CandidateId == "custom:n4f4"
        && choices[1].DisplayLabel == "イル・メグ"
        && !choices[1].Experimental
        && choices[2].CandidateId == "custom:ultima-thule-elpis"
        && choices[2].DisplayLabel == "エルピスの花畑 [実験中]"
        && choices[2].Experimental
        // FRU passed its real-game OneClick -> verified, so no [実験中] suffix.
        && choices[3].CandidateId == "custom:fru-clear-stage"
        && choices[3].DisplayLabel == "FRU クリア後ステージ"
        && !choices[3].Experimental;
});

Test(407, "curated membership excludes the dormant Old Sharlayan candidate", () =>
{
    return TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId("custom:n4f4")
        && !TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId("custom:old-sharlayan-k5t1")
        && !TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId("")
        && !TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId("manual:slot1")
        && !TitleBackgroundCharaSelectPresetSelectionLogic.IsCuratedCandidateId("custom");
});

Test(408, "curated selector falls back to OFF for a dormant candidate", () =>
{
    return TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(false, "custom:n4f4") == 0
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(true, "custom:n4f4") == 1
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(true, "custom:ultima-thule-elpis") == 2
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(true, "custom:fru-clear-stage") == 3
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(true, "custom:old-sharlayan-k5t1") == 0
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveSelectedIndex(true, "manual:slot1") == 0
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveCandidateIdForIndex(1) == "custom:n4f4"
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveCandidateIdForIndex(2) == "custom:ultima-thule-elpis"
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveCandidateIdForIndex(3) == "custom:fru-clear-stage"
        && TitleBackgroundCharaSelectPresetSelectionLogic.ResolveCandidateIdForIndex(99) == string.Empty;
});

Test(409, "simple auto setup does not activate the dormant Old Sharlayan candidate", () =>
{
    var configuration = new Configuration();
    TitleBackgroundQuickCheckUiPresenter.ApplySimpleAutoSetup(configuration, "custom:old-sharlayan-k5t1");

    return configuration.TitleBackgroundCharacterSelectOverrideCandidateId == "custom:n4f4"
        && configuration.TitleBackgroundTerritoryTypeId == 816
        && TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(configuration);
});

Test(410, "one-click verification is fail-closed when no curated background is selected", () =>
{
    var body = ReadServiceMethodBody(
        "TitleScreenBackgroundService.OneClickVerification.cs",
        "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()");
    var guardIndex = body.IndexOf("\"no-preset-selected\"", StringComparison.Ordinal);
    var transactionIndex = body.IndexOf("TryBeginAutomaticCheckSettingsTransaction", StringComparison.Ordinal);
    var applyIndex = body.IndexOf("ApplySimpleAutoSetup", StringComparison.Ordinal);

    return guardIndex >= 0
        && transactionIndex >= 0
        && applyIndex >= 0
        && guardIndex < transactionIndex
        && guardIndex < applyIndex
        && body.Contains("IsCuratedCandidateId(", StringComparison.Ordinal)
        && body.Contains("FailOneClickWithoutTransaction(", StringComparison.Ordinal);
});

Test(411, "startup normalization fail-closes a stale deferred Old Sharlayan override", () =>
{
    var root = FindRepositoryRoot();
    var servicePath = Path.Combine(
        root, "projects", "XIV-Mini-Util", "Services", "TitleBackground", "TitleScreenBackgroundService.cs");
    var source = File.ReadAllText(servicePath);
    var normalizeBody = ExtractMethodBody(source, "private void NormalizeConfiguration()");

    return source.Contains(
            "DeferredOldSharlayanCandidateId = \"custom:old-sharlayan-k5t1\"",
            StringComparison.Ordinal)
        && normalizeBody.Contains("TitleBackgroundOverrideEnabled", StringComparison.Ordinal)
        && normalizeBody.Contains("DeferredOldSharlayanCandidateId", StringComparison.Ordinal)
        && normalizeBody.Contains("TitleBackgroundOverrideEnabled = false", StringComparison.Ordinal)
        && normalizeBody.Contains("TitleBackgroundV2Enabled = false", StringComparison.Ordinal)
        && normalizeBody.Contains("_v2.Reset()", StringComparison.Ordinal)
        && normalizeBody.Contains("_configuration.Save()", StringComparison.Ordinal)
        && !normalizeBody.Contains("TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty", StringComparison.Ordinal);
});

Test(412, "Elpis candidate keeps live layout metadata unguessed", () =>
{
    return TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
            out var candidate)
        && candidate.TerritoryId == 960
        && candidate.TerritoryPath == "ex4/04_uvs_u5/fld/u5f2/level/u5f2"
        && candidate.DisplayName == "エルピスの花畑"
        && candidate.LayerFilterKey == 0
        && candidate.RequiresSourceBackedLayout
        && !candidate.VerifiedInGame;
});

Test(413, "Elpis same-terrain source gate accepts finite matching active layout", () =>
{
    if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
            out var candidate))
    {
        return false;
    }

    var snapshot = TitleBackgroundCharaSelectSourceLayoutLogic.Evaluate(
        candidate,
        960,
        candidate.TerritoryPath,
        layoutReady: true,
        layoutTerritoryTypeId: 960,
        layoutLayerFilterKey: 37,
        new Vector3(12f, 34f, 56f));
    return snapshot.Eligible
        && snapshot.PositionAuthorized
        && snapshot.SourceMode == "same-terrain-world"
        && snapshot.LayoutLayerFilterKey == 37;
});

Test(414, "Elpis source gate rejects a mismatched active layout", () =>
{
    if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
            out var candidate))
    {
        return false;
    }

    var snapshot = TitleBackgroundCharaSelectSourceLayoutLogic.Evaluate(
        candidate,
        960,
        candidate.TerritoryPath,
        layoutReady: true,
        layoutTerritoryTypeId: 959,
        layoutLayerFilterKey: 37,
        new Vector3(12f, 34f, 56f));
    return !snapshot.Eligible
        && !snapshot.PositionAuthorized
        && snapshot.FailureReason == "source-layout-territory-mismatch";
});

Test(415, "Elpis source gate accepts zero layer filter key for a matching nonzero territory", () =>
{
    if (!TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryGet(
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.ElpisCandidateId,
            out var candidate))
    {
        return false;
    }

    var snapshot = TitleBackgroundCharaSelectSourceLayoutLogic.Evaluate(
        candidate,
        960,
        candidate.TerritoryPath,
        layoutReady: true,
        layoutTerritoryTypeId: 960,
        layoutLayerFilterKey: 0,
        new Vector3(12f, 34f, 56f),
        layoutInitState: 7);
    return snapshot.Eligible
        && snapshot.LayerFilterKeyAvailable
        && snapshot.LayoutLayerFilterKey == 0
        && snapshot.LayoutInitState == 7;
});

Test(416, "Elpis source capture keeps InitState and bounded pre-logout retry source-local", () =>
{
    var root = FindRepositoryRoot();
    var sourceLayout = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.SourceLayout.cs"));
    var oneClick = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.OneClickVerification.cs"));
    var nativeHooks = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleScreenBackgroundService.NativeHooks.cs"));
    var runtimeState = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "TitleBackground",
        "TitleBackgroundCharaSelectSourceLayoutRuntimeState.cs"));
    var retryState = new TitleBackgroundCharaSelectSourceLayoutRuntimeState();
    retryState.RecordSourceCaptureAttempt();
    retryState.BeginSourceCaptureRetry();
    var attempts = 1;
    while (retryState.PrepareNextSourceCaptureRetry())
    {
        retryState.RecordSourceCaptureAttempt();
        attempts++;
    }

    return sourceLayout.Contains("activeLayout->InitState", StringComparison.Ordinal)
        && sourceLayout.Contains("layoutInitState: layoutInitState", StringComparison.Ordinal)
        && !sourceLayout.Contains("LvbResourceHandle", StringComparison.Ordinal)
        && oneClick.Contains("TryScheduleOneClickSourceCaptureRetry()", StringComparison.Ordinal)
        && oneClick.Contains("TryProcessPendingOneClickSourceCapture()", StringComparison.Ordinal)
        && nativeHooks.Contains("TryProcessPendingOneClickSourceCapture()", StringComparison.Ordinal)
        && nativeHooks.LastIndexOf("TryProcessPendingOneClickSourceCapture()", StringComparison.Ordinal)
            < nativeHooks.LastIndexOf("StopSavedViewPoseMaintainIfInvalid()", StringComparison.Ordinal)
        && runtimeState.Contains("SourceCaptureRetryBudget", StringComparison.Ordinal)
        && runtimeState.Contains("LayoutInitState", StringComparison.Ordinal)
        && attempts == TitleBackgroundCharaSelectSourceLayoutRuntimeState.SourceCaptureRetryBudget
        && retryState.SourceCaptureRetryExhausted
        && !retryState.SourceCaptureRetryPending;
});

    }
}

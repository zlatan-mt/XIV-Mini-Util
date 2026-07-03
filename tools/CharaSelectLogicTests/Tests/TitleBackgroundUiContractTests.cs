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
    var diagFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.TitleBackgroundDiagnostics.cs");
    var tbText = File.ReadAllText(tbFile);
    var diagText = File.ReadAllText(diagFile);
    var mainText = File.ReadAllText(mainFile);
    // 通常画面は最小、診断は別ファイルへ物理分割。通常ファイルに診断系メソッドを残さない。
    return tbText.Contains("private void DrawTitleBackgroundSettings()", StringComparison.Ordinal)
        && !tbText.Contains("DrawTitleBackgroundSimplePanel", StringComparison.Ordinal)
        && !tbText.Contains("ClearTitleBackgroundInputs", StringComparison.Ordinal)
        && diagText.Contains("private void DrawTitleBackgroundDiagnostics()", StringComparison.Ordinal)
        && !diagText.Contains("ClearTitleBackgroundInputs", StringComparison.Ordinal)
        && !diagText.Contains("CollapsingHeader", StringComparison.Ordinal)
        && File.ReadAllLines(diagFile).Length < 100
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
        + CountOccurrences(body, "ImGui.Checkbox(");
    return body.Length > 0 && controls <= 4;
});

Test(390, "title background normal screen only uses the allowed labels", () =>
{
    var body = ReadTitleBackgroundNormalBody();
    return body.Contains("OFF##", StringComparison.Ordinal)
        && body.Contains("イル・メグ##", StringComparison.Ordinal)
        && body.Contains("この場所で確認を開始##", StringComparison.Ordinal)
        && body.Contains("初期状態に戻す##", StringComparison.Ordinal)
        && body.Contains("現在の構図を保存##", StringComparison.Ordinal)
        && body.Contains("IsCharaSelectViewCaptureAvailable()", StringComparison.Ordinal)
        && body.Contains("TryCaptureCharaSelectViewFromCurrentCamera", StringComparison.Ordinal)
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
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.OneClickVerification.cs", "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()");
    var applyIndex = body.IndexOf("ApplySimpleAutoSetup", StringComparison.Ordinal);
    var probeIndex = body.IndexOf("CaptureWorldProbeAnchorInMemory", StringComparison.Ordinal);
    return applyIndex >= 0 && probeIndex >= 0 && applyIndex < probeIndex;
});

Test(395, "one-click fails closed before logout when probe is not applicable", () =>
{
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.OneClickVerification.cs", "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()");
    var resolveIndex = body.IndexOf("ResolveExperimentalWorldPlacement(candidate)", StringComparison.Ordinal);
    var startIndex = body.IndexOf("_automaticCheck.Requested = true", StringComparison.Ordinal);
    return body.Contains("CaptureWorldProbeAnchorInMemory(out var probeStatus)", StringComparison.Ordinal)
        && resolveIndex >= 0
        && startIndex >= 0
        && resolveIndex < startIndex
        && body.Contains("if (!worldResolution.Eligible)", StringComparison.Ordinal)
        && body.Contains("\"probe-not-applicable\"", StringComparison.Ordinal)
        && body.Contains("FailOneClickWithReport(", StringComparison.Ordinal);
});

Test(396, "one-click retries native init once before declaring hook-not-ready", () =>
{
    var body = ReadServiceMethodBody("TitleScreenBackgroundService.OneClickVerification.cs", "public IReadOnlyList<string> StartOneClickTitleBackgroundVerification()");
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
    var diagnosticsPath = Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Windows",
        "Components",
        "SettingsTab.TitleBackgroundDiagnostics.cs");
    var diagnostics = File.ReadAllText(diagnosticsPath);

    return !gitignore
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => string.Equals(line.Trim(), "AGENTS.md", StringComparison.Ordinal))
        && agents.Contains("原則として1回の操作または1回の対象フロー", StringComparison.Ordinal)
        && File.ReadAllLines(diagnosticsPath).Length < 100
        && !diagnostics.Contains("CollapsingHeader", StringComparison.Ordinal)
        && !diagnostics.Contains("TreeNode", StringComparison.Ordinal);
});

    }
}

// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackgroundDiagnostics.cs
// Description: Title背景の開発者向け診断を通常画面と生設定UIから分離して表示する
// Reason: 自動レポートで代替できる旧診断・実験操作を削除し、診断ページ自体も小さく保つため
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private void DrawTitleBackgroundDiagnostics()
    {
        ImGui.Text("Title背景 診断");
        ImGui.TextDisabled("通常の確認は「ログイン背景」の1クリック確認だけで完結します。");
        ImGui.TextDisabled(
            $"座標対応サンプル: {_titleScreenBackgroundService.WorldCoordinateSampleCount}件");

        // 背景セッション中（pre-login）のみ環境時刻を正午へ固定する。ログイン中には適用されない
        // （IsLoggedIn ゲートで遮断）。通常画面には出さない開発者向けトグル。既定 ON。
        var noonEnabled = _configuration.TitleBackgroundEnvironmentNoonEnabled;
        if (ImGui.Checkbox("ログイン画面の時刻を正午に固定##TitleBackgroundEnvironmentNoon", ref noonEnabled))
        {
            _configuration.TitleBackgroundEnvironmentNoonEnabled = noonEnabled;
            _configuration.Save();
        }

        // 背景セッション中（pre-login）のみ環境天候を晴れへ固定する。ログイン中には適用されない
        // （IsLoggedIn ゲートで遮断）。通常画面には出さない開発者向けトグル。既定 ON。
        var clearSkyEnabled = _configuration.TitleBackgroundEnvironmentClearSkyEnabled;
        if (ImGui.Checkbox("ログイン画面の天候を晴れに固定##TitleBackgroundEnvironmentClearSky", ref clearSkyEnabled))
        {
            _configuration.TitleBackgroundEnvironmentClearSkyEnabled = clearSkyEnabled;
            _configuration.Save();
        }

        if (ImGui.Button("現在の診断レポートをコピー##TitleBackgroundDiagnosticCopy"))
        {
            var lines = _titleScreenBackgroundService.RunBulkDiagnostic();
            var coordinateReport =
                _titleScreenBackgroundService.BuildWorldCoordinateCorrespondenceReportText();
            var text = string.Join(Environment.NewLine, lines);
            if (!string.IsNullOrWhiteSpace(coordinateReport))
            {
                text = $"{text}{Environment.NewLine}{coordinateReport}";
            }

            ImGui.SetClipboardText(text);
            _titleBackgroundAnchorMessage = "診断レポートをコピーしました";
            _titleBackgroundAnchorMessageColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);
        }

        if (!string.IsNullOrWhiteSpace(_titleBackgroundAnchorMessage))
        {
            ImGui.TextColored(
                _titleBackgroundAnchorMessageColor,
                _titleBackgroundAnchorMessage);
        }
    }
}

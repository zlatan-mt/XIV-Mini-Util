// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs
// Description: Title背景（キャラ選択背景）の通常画面のみ。操作は OFF / イル・メグ / この場所で確認を開始 /
//              初期状態に戻す の最大4個、状態行は最大6行に限定する（AGENTS.md 恒久契約）。
// Reason: 実機確認を「1クリック→ログアウト→ログイン→レポート貼付」だけで完結させ、診断・生設定・
//         probe 操作は通常画面に出さない。開発機能は SettingsTab.TitleBackgroundDiagnostics.cs に分離。
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private string _titleBackgroundViewMessage = string.Empty;
    private Vector4 _titleBackgroundViewMessageColor = new(0.7f, 0.7f, 0.7f, 1f);

    // 通常画面: 操作部品は最大4個、状態行は最大6行。Developer 描画メソッドは一切呼ばない。
    private void DrawTitleBackgroundSettings()
    {
        // 操作1・2: OFF / イル・メグ
        var offSelected = !_configuration.TitleBackgroundOverrideEnabled;
        if (ImGui.RadioButton("OFF##TitleBackgroundOff", offSelected))
        {
            _titleBackgroundViewMessage = string.Empty;
            _titleScreenBackgroundService.SetEnabled(false);
        }

        ImGui.SameLine();
        var recommendedSelected = TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(_configuration);
        if (ImGui.RadioButton("イル・メグ##TitleBackgroundN4F4", recommendedSelected))
        {
            _titleBackgroundViewMessage = string.Empty;
            _titleScreenBackgroundService.RunSimpleAutoSetup();
        }

        var status = _titleScreenBackgroundService.GetOneClickStatus();

        // 操作3: 主ボタン「この場所で確認を開始」（単一サービスメソッドだけを呼ぶ）。実行中は無効。
        ImGui.Spacing();
        if (status.Busy)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("この場所で確認を開始##TitleBackgroundOneClick"))
        {
            _titleBackgroundViewMessage = string.Empty;
            _titleScreenBackgroundService.StartOneClickTitleBackgroundVerification();
        }

        if (status.Busy)
        {
            ImGui.EndDisabled();
        }

        // 操作4: キャラ選択中は現在の構図を保存し、それ以外では初期状態に戻す。
        // 1つの操作枠と同じ ImGui ID を共有し、通常画面の最大4操作を維持する。
        var viewCaptureAvailable = _titleScreenBackgroundService.IsCharaSelectViewCaptureAvailable();
        var secondaryActionLabel = viewCaptureAvailable
            ? "現在の構図を保存##TitleBackgroundReset"
            : "初期状態に戻す##TitleBackgroundReset";
        ImGui.SameLine();
        if (status.Busy && viewCaptureAvailable)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(secondaryActionLabel))
        {
            if (viewCaptureAvailable)
            {
                var captured = _titleScreenBackgroundService.TryCaptureCharaSelectViewFromCurrentCamera(out var captureStatus);
                _titleBackgroundViewMessage = captured
                    ? "構図を保存しました。次回から自動で再現します。"
                    : DescribeTitleBackgroundViewCaptureFailure(captureStatus);
                _titleBackgroundViewMessageColor = captured
                    ? new Vector4(0.3f, 0.8f, 0.45f, 1f)
                    : new Vector4(1f, 0.45f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundViewMessage = string.Empty;
                _titleScreenBackgroundService.ResetSimpleTitleBackgroundSettings();
            }
        }

        if (status.Busy && viewCaptureAvailable)
        {
            ImGui.EndDisabled();
        }

        // 状態行（最大3行）: 現在状態 / 次動作 / 完了・失敗。
        ImGui.Spacing();
        ImGui.Text($"状態: {status.StateLine}");
        if (!string.IsNullOrEmpty(status.NextActionLine))
        {
            ImGui.Text($"次: {status.NextActionLine}");
        }

        if (!string.IsNullOrEmpty(status.ResultLine))
        {
            var resultColor = status.ResultLine.StartsWith("完了", StringComparison.Ordinal)
                ? new Vector4(0.3f, 0.8f, 0.45f, 1f)
                : new Vector4(1f, 0.45f, 0.45f, 1f);
            ImGui.TextColored(resultColor, status.ResultLine);
        }

        if (!string.IsNullOrEmpty(_titleBackgroundViewMessage))
        {
            ImGui.TextColored(_titleBackgroundViewMessageColor, _titleBackgroundViewMessage);
        }
    }

    private static string DescribeTitleBackgroundViewCaptureFailure(string status)
    {
        return status switch
        {
            "skipped-empty-candidate" => "先に「イル・メグ」を選んでください。",
            "skipped-post-login" or "skipped-not-chara-select" => "キャラ選択画面で保存してください。",
            _ => "カメラ情報を取得できませんでした。少し待って再度保存してください。",
        };
    }
}

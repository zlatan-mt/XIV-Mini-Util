// Path: projects/XIV-Mini-Util/Plugin.Commands.cs
// Description: Dalamudコマンドの定義・登録・解除を管理する
// Reason: 同じ登録表から登録と解除を行い、コマンド互換性を保つため

using Dalamud.Game.Command;

namespace XivMiniUtil;

public sealed partial class Plugin
{
    private const string CommandName = "/xivminiutil";
    private const string CommandAlias = "/xmu";
    private const string VersionCommandName = "/xmuversion";
    private const string VersionCommandAlias = "/xmuv";
    private const string CharaSelectDiagnosticCommandName = "/xmucdiag";
    private const string CharaSelectDiagnosticCommandAlias = "/xmuc";
    private const string TitleBackgroundDiagnosticCommandName = "/xmutbgdiag";
    private const string TitleBackgroundDiagnosticCommandAlias = "/xmutbg";
    private const string TitleBackgroundProbeCommandName = "/xmutbgprobe";
    private const string TitleBackgroundCameraProbeCommandName = "/xmutbgcamprobe";
    private const string TitleBackgroundSelfTestCommandName = "/xmutbgtest";
    private const string TitleBackgroundReloadCommandName = "/xmutbgreload";
    private const string TitleBackgroundQuickCheckCommandName = "/xmutbgcheck";

    private readonly record struct CommandRegistration(
        string Name,
        IReadOnlyCommandInfo.HandlerDelegate Handler,
        string HelpMessage);

    private IReadOnlyList<CommandRegistration> GetCommandRegistrations()
    {
        return
        [
            new(CommandName, OnCommand, "メインウィンドウを開きます。サブコマンド: config / diag / version / help"),
            new(CommandAlias, OnCommand, "メインウィンドウを開きます。サブコマンド: config / diag / version / help"),
            new(VersionCommandName, OnVersionCommand, "読み込み中のXIV Mini Util DLLとビルド時刻を表示します。"),
            new(VersionCommandAlias, OnVersionCommand, "読み込み中のXIV Mini Util DLLとビルド時刻を表示します。"),
            new(CharaSelectDiagnosticCommandName, OnCharaSelectDiagnosticCommand, "キャラ選択画面のエモート/声診断情報を表示します。"),
            new(CharaSelectDiagnosticCommandAlias, OnCharaSelectDiagnosticCommand, "キャラ選択画面のエモート/声診断情報を表示します。"),
            new(TitleBackgroundDiagnosticCommandName, OnTitleBackgroundDiagnosticCommand, "タイトル背景差し替えの診断情報を表示します。サブコマンド: copy"),
            new(TitleBackgroundDiagnosticCommandAlias, OnTitleBackgroundDiagnosticCommand, "タイトル背景差し替えの診断情報を表示します。サブコマンド: copy"),
            new(TitleBackgroundProbeCommandName, OnTitleBackgroundProbeCommand, "タイトル背景hook probeを開始/停止/表示します。サブコマンド: on / report / off"),
            new(TitleBackgroundCameraProbeCommandName, OnTitleBackgroundCameraProbeCommand, "タイトル背景camera Y probeを準備/表示/復元します。サブコマンド: arm-y / report / restore"),
            new(TitleBackgroundSelfTestCommandName, OnTitleBackgroundSelfTestCommand, "debug-only: タイトル背景差し替えのself-testを実行します。通常確認では使用しません。"),
            new(TitleBackgroundReloadCommandName, OnTitleBackgroundReloadCommand, "debug-only: キャラ選択ロビー中にタイトル背景とカメラを再適用します。通常確認では使用しません。"),
            new(TitleBackgroundQuickCheckCommandName, OnTitleBackgroundQuickCheckCommand, "Character Select 背景 QuickCheck を開始/評価/表示/リセットします。サブコマンド: start / status / reset"),
        ];
    }

    private void RegisterCommands()
    {
        foreach (var registration in GetCommandRegistrations())
        {
            _commandManager.AddHandler(registration.Name, new CommandInfo(registration.Handler)
            {
                HelpMessage = registration.HelpMessage,
            });
        }
    }

    private void UnregisterCommands()
    {
        foreach (var registration in GetCommandRegistrations())
        {
            _commandManager.RemoveHandler(registration.Name);
        }
    }

    private static string GetSubCommand(string args)
    {
        var trimmed = args?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var separatorIndex = trimmed.IndexOfAny([' ', '\t', '　']);
        return (separatorIndex < 0 ? trimmed : trimmed[..separatorIndex]).ToLowerInvariant();
    }

    private static bool ShouldCopyCommandOutput(string args)
    {
        return GetSubCommand(args) is "copy" or "clip" or "clipboard";
    }
}

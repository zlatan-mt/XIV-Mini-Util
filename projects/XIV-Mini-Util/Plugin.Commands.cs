// Path: projects/XIV-Mini-Util/Plugin.Commands.cs
// Description: Dalamudコマンドの定義・登録・解除を管理する
// Reason: 通常利用向けコマンドの登録と解除を同じ定義から行うため

using Dalamud.Game.Command;

namespace XivMiniUtil;

public sealed partial class Plugin
{
    private const string CommandName = "/xivminiutil";
    private const string CommandAlias = "/xmu";

    private readonly record struct CommandRegistration(
        string Name,
        IReadOnlyCommandInfo.HandlerDelegate Handler,
        string HelpMessage);

    private IReadOnlyList<CommandRegistration> GetCommandRegistrations()
    {
        return
        [
            new(CommandName, OnCommand, "メインウィンドウを開きます。サブコマンド: config / version / help"),
            new(CommandAlias, OnCommand, "メインウィンドウを開きます。サブコマンド: config / version / help"),
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

}

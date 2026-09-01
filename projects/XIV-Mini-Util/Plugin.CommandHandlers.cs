// Path: projects/XIV-Mini-Util/Plugin.CommandHandlers.cs
// Description: コマンド入力を既存サービス操作へ引き渡す
// Reason: コマンド定義と処理本体の責務を分けるため

using System.Reflection;

namespace XivMiniUtil;

public sealed partial class Plugin
{
    private void OnCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        if (string.IsNullOrEmpty(subCommand))
        {
            _mainWindow.Toggle();
            return;
        }

        switch (subCommand)
        {
            case "config":
                OpenSettingsWindow();
                break;
            case "version":
                PrintVersionInfo();
                break;
            case "help":
                PrintHelp();
                break;
            default:
                PrintHelp();
                break;
        }
    }

    private void PrintVersionInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();
        var assemblyFile = _pluginInterface.AssemblyLocation;
        var location = assemblyFile.FullName;
        var displayTimeZone = GetDisplayTimeZone();
        var writeTime = File.Exists(location)
            ? TimeZoneInfo.ConvertTimeFromUtc(File.GetLastWriteTimeUtc(location), displayTimeZone)
            : (DateTime?)null;
        var loadTime = TimeZoneInfo.ConvertTimeFromUtc(_pluginInterface.LoadTimeUTC, displayTimeZone);

        _chatGui.Print($"[XIV Mini Util] Assembly: {assemblyName.Name}");
        _chatGui.Print($"[XIV Mini Util] Version: {assemblyName.Version?.ToString(3) ?? "unknown"} / IsDev: {_pluginInterface.IsDev}");
        _chatGui.Print($"[XIV Mini Util] Loaded: {loadTime:yyyy-MM-dd HH:mm:ss} JST");
        _chatGui.Print($"[XIV Mini Util] DLL: {location}");

        if (writeTime.HasValue)
        {
            _chatGui.Print($"[XIV Mini Util] DLL updated: {writeTime.Value:yyyy-MM-dd HH:mm:ss} JST");
        }
    }

    private static TimeZoneInfo GetDisplayTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private void PrintHelp()
    {
        _chatGui.Print("/xivminiutil : メインウィンドウを開きます。");
        _chatGui.Print("/xivminiutil config : 設定タブを開きます。");
        _chatGui.Print("/xivminiutil version : バージョン情報を表示します。");
        _chatGui.Print("/xivminiutil help : このヘルプを表示します。");
        _chatGui.Print("/xmu : /xivminiutil のエイリアス");
    }
}

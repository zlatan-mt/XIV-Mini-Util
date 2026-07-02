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
            case "diag":
                GenerateDiagnosticsReport();
                break;
            case "version":
            case "ver":
            case "-v":
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

    private void GenerateDiagnosticsReport()
    {
        var configDir = _pluginInterface.ConfigDirectory.FullName;
        var outputPath = Path.Combine(configDir, $"shop-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        var result = _shopDataCache.GenerateDiagnosticsReport(outputPath);
        _chatGui.Print($"[XIV Mini Util] {result}");
        _chatGui.Print($"[XIV Mini Util] 位置情報なしNPC: {_shopDataCache.GetExcludedNpcCount()}件, NPCマッチなしショップ: {_shopDataCache.GetUnmatchedShopCount()}件");
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

    private void OnVersionCommand(string command, string args)
    {
        PrintVersionInfo();
    }

    private void OnCharaSelectDiagnosticCommand(string command, string args)
    {
        foreach (var line in _charaSelectService.GetVoiceDiagnosticLines())
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
        }
    }

    private void OnTitleBackgroundDiagnosticCommand(string command, string args)
    {
        var lines = _titleScreenBackgroundService.GetDiagnosticLines();
        if (ShouldCopyCommandOutput(args))
        {
            CopyTitleBackgroundDiagnosticLines(lines);
            return;
        }

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground diag: {Line}", line);
        }
    }

    private void OnTitleBackgroundProbeCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "report" => _titleScreenBackgroundService.GetProbeReportLines(),
            "on" or "start" => _titleScreenBackgroundService.StartProbe(),
            "off" or "stop" => _titleScreenBackgroundService.StopProbe(),
            _ =>
            [
                "[Probe] usage: /xmutbgprobe on | report | off",
            ],
        };

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground probe: {Line}", line);
        }
    }

    private void OnTitleBackgroundCameraProbeCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "report" => _titleScreenBackgroundService.GetCameraProbeReportLines(),
            "arm-y" => _titleScreenBackgroundService.ArmCameraYProbe(),
            "restore" => _titleScreenBackgroundService.RestoreCameraProbe(),
            _ =>
            [
                "[CameraProbe] usage: /xmutbgcamprobe arm-y | report | restore",
            ],
        };

        if (subCommand is "" or "report")
        {
            CopyTitleBackgroundCameraProbeLines(lines);
        }

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground camera probe: {Line}", line);
        }
    }

    private void OnTitleBackgroundSelfTestCommand(string command, string args)
    {
        var startMessage = _titleScreenBackgroundService.StartSelfTest();
        if (!string.IsNullOrWhiteSpace(startMessage))
        {
            _chatGui.Print($"[XIV Mini Util] {startMessage}");
            _pluginLog.Information("TitleBackground self-test: {Line}", startMessage);
        }
    }

    private void OnTitleBackgroundReloadCommand(string command, string args)
    {
        var message = _titleScreenBackgroundService.RequestCharaSelectReload();
        _chatGui.Print($"[XIV Mini Util] {message}");
        _pluginLog.Information("TitleBackground reload: {Line}", message);
    }

    private void OnTitleBackgroundQuickCheckCommand(string command, string args)
    {
        var subCommand = GetSubCommand(args);
        IReadOnlyList<string> lines = subCommand switch
        {
            "" or "run" => _titleScreenBackgroundService.RunQuickCheck(),
            "start" => _titleScreenBackgroundService.StartQuickCheck(),
            "status" => _titleScreenBackgroundService.GetQuickCheckStatusLines(),
            "reset" => _titleScreenBackgroundService.ResetQuickCheck(),
            _ =>
            [
                "[XMU QuickCheck] usage: /xmutbgcheck [start|status|reset]",
            ],
        };

        foreach (var line in lines)
        {
            _chatGui.Print($"[XIV Mini Util] {line}");
            _pluginLog.Information("TitleBackground quickcheck: {Line}", line);
        }
    }

    private void OnTitleBackgroundSelfTestCompleted(string message)
    {
        _chatGui.Print($"[XIV Mini Util] {message}");
        _pluginLog.Information("TitleBackground self-test: {Line}", message);
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
        _chatGui.Print("/xivminiutil diag : ショップデータ診断レポートを出力します。");
        _chatGui.Print("/xivminiutil version : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuv : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuversion : 読み込み中のDLLとビルド時刻を表示します。");
        _chatGui.Print("/xmuc : キャラ選択画面のエモート/声診断情報を表示します。");
        _chatGui.Print("/xmutbg : タイトル背景差し替えの診断情報を表示します。");
        _chatGui.Print("/xmutbg copy : タイトル背景差し替えの診断情報をクリップボードへコピーします。");
        _chatGui.Print("/xmutbgcheck : Character Select 背景 QuickCheck を表示します。");
        _chatGui.Print("/xmutbgcheck start : QuickCheck のrun-scoped確認を開始します。");
        _chatGui.Print("/xmutbgcamprobe arm-y : CameraY / FocusY one-shot probeを準備します。");
        _chatGui.Print("/xmu : /xivminiutil のエイリアス");
    }
}

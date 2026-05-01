// Path: projects/XIV-Mini-Util/Services/Notification/DutyReadyNotificationService.cs
// Description: コンテンツ突入確認画面の表示を検知してWindows通知音を鳴らす
// Reason: FFXIV側の音量設定に依存しにくいシャキ通知を提供するため
using Dalamud.Plugin.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.Notification;

public sealed class DutyReadyNotificationService : IDisposable
{
    private const uint MbIconExclamation = 0x00000030;
    private const uint SndAsync = 0x0001;
    private const uint SndNodefault = 0x0002;
    private const uint SndFilename = 0x00020000;
    private static readonly string AlarmSoundPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "Media",
        "Alarm05.wav");
    private static readonly TimeSpan PlayInterval = TimeSpan.FromSeconds(1);

    private readonly IFramework _framework;
    private readonly Configuration _configuration;
    private readonly AddonStateTracker _addonStateTracker;
    private readonly IPluginLog _pluginLog;

    private bool _wasVisible;
    private bool _triggeredForCurrentVisible;
    private bool _mutedForCurrentVisible;
    private bool _isRinging;
    private DateTime _ringingStartedAt;
    private DateTime _lastPlayedAt;
    private CancellationTokenSource? _testPlaybackDelayCts;

    public DutyReadyNotificationService(
        IFramework framework,
        Configuration configuration,
        AddonStateTracker addonStateTracker,
        IPluginLog pluginLog)
    {
        _framework = framework;
        _configuration = configuration;
        _addonStateTracker = addonStateTracker;
        _pluginLog = pluginLog;

        _framework.Update += OnFrameworkUpdate;
    }

    public void PlayTest()
    {
        PlayOnceSafe();
    }

    public void StopNotification()
    {
        CancelPendingTestPlayback();
        StopRinging();
        StopCurrentSound();
        _mutedForCurrentVisible = IsAnyDutyReadyConfirmVisible(DateTime.UtcNow);
    }

    public async Task PlayTestAfterDelayAsync(TimeSpan delay)
    {
        var previousCts = _testPlaybackDelayCts;
        var currentCts = new CancellationTokenSource();
        _testPlaybackDelayCts = currentCts;

        previousCts?.Cancel();
        previousCts?.Dispose();

        try
        {
            await Task.Delay(delay, currentCts.Token).ConfigureAwait(false);
            if (IsGameWindowInactive())
            {
                PlayOnceSafe();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_testPlaybackDelayCts, currentCts))
            {
                _testPlaybackDelayCts = null;
            }

            currentCts.Dispose();
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        CancelPendingTestPlayback();
        StopRinging();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        var visible = IsAnyDutyReadyConfirmVisible(now);
        var gameWindowInactive = IsGameWindowInactive();

        if (visible && !_wasVisible)
        {
            _triggeredForCurrentVisible = false;
            _mutedForCurrentVisible = false;
        }

        if (visible
            && !_triggeredForCurrentVisible
            && !_mutedForCurrentVisible
            && _configuration.DutyReadySoundNotificationEnabled
            && gameWindowInactive)
        {
            StartRinging(now);
            _triggeredForCurrentVisible = true;
        }

        if (_isRinging)
        {
            var duration = TimeSpan.FromSeconds(Math.Clamp(_configuration.DutyReadySoundDurationSeconds, 3, 30));
            var timedOut = now - _ringingStartedAt >= duration;
            if (!visible || timedOut || !_configuration.DutyReadySoundNotificationEnabled || !gameWindowInactive)
            {
                StopRinging();
                if (visible && !gameWindowInactive && !timedOut)
                {
                    _triggeredForCurrentVisible = false;
                }
            }
            else if (now - _lastPlayedAt >= PlayInterval)
            {
                PlayOnceSafe();
                _lastPlayedAt = now;
            }
        }

        if (!visible)
        {
            _triggeredForCurrentVisible = false;
            _mutedForCurrentVisible = false;
        }

        _wasVisible = visible;
    }

    private bool IsAnyDutyReadyConfirmVisible(DateTime now)
    {
        foreach (var addonName in GameUiConstants.DutyReadyConfirmAddonNames)
        {
            if (_addonStateTracker.GetSnapshot(addonName, now).Visible)
            {
                return true;
            }
        }

        return false;
    }

    private void StartRinging(DateTime now)
    {
        _isRinging = true;
        _ringingStartedAt = now;
        _lastPlayedAt = now;
        PlayOnceSafe();
    }

    private static bool IsGameWindowInactive()
    {
        using var currentProcess = Process.GetCurrentProcess();
        currentProcess.Refresh();

        var gameWindow = currentProcess.MainWindowHandle;
        if (gameWindow == IntPtr.Zero)
        {
            return false;
        }

        return IsIconic(gameWindow) || GetForegroundWindow() != gameWindow;
    }

    private void StopRinging()
    {
        if (_isRinging)
        {
            StopCurrentSound();
        }

        _isRinging = false;
    }

    private void CancelPendingTestPlayback()
    {
        _testPlaybackDelayCts?.Cancel();
        _testPlaybackDelayCts?.Dispose();
        _testPlaybackDelayCts = null;
    }

    private void PlayOnceSafe()
    {
        try
        {
            if (File.Exists(AlarmSoundPath))
            {
                if (!PlaySound(AlarmSoundPath, IntPtr.Zero, SndFilename | SndAsync | SndNodefault))
                {
                    _pluginLog.Warning("Duty ready notification sound playback failed.");
                }

                return;
            }

            if (!MessageBeep(MbIconExclamation))
            {
                _pluginLog.Warning("Duty ready notification sound playback failed.");
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "Duty ready notification sound playback failed.");
        }
    }

    private void StopCurrentSound()
    {
        try
        {
            PlaySound(null, IntPtr.Zero, 0);
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "Duty ready notification sound stop failed.");
        }
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);
}

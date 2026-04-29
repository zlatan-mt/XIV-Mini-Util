// Path: projects/XIV-Mini-Util/Services/Notification/DutyReadyNotificationService.cs
// Description: コンテンツ突入確認画面の表示を検知してWindows通知音を鳴らす
// Reason: FFXIV側の音量設定に依存しにくいシャキ通知を提供するため
using Dalamud.Plugin.Services;
using System.Runtime.InteropServices;
using XivMiniUtil.Services.Common;

namespace XivMiniUtil.Services.Notification;

public sealed class DutyReadyNotificationService : IDisposable
{
    private const uint MbIconExclamation = 0x00000030;
    private static readonly TimeSpan PlayInterval = TimeSpan.FromSeconds(1);

    private readonly IFramework _framework;
    private readonly Configuration _configuration;
    private readonly AddonStateTracker _addonStateTracker;
    private readonly IPluginLog _pluginLog;

    private bool _wasVisible;
    private bool _triggeredForCurrentVisible;
    private bool _isRinging;
    private DateTime _ringingStartedAt;
    private DateTime _lastPlayedAt;

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

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        StopRinging();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        var visible = IsAnyDutyReadyConfirmVisible(now);

        if (visible && !_wasVisible)
        {
            _triggeredForCurrentVisible = false;
        }

        if (visible && !_triggeredForCurrentVisible && _configuration.DutyReadySoundNotificationEnabled)
        {
            StartRinging(now);
            _triggeredForCurrentVisible = true;
        }

        if (_isRinging)
        {
            var duration = TimeSpan.FromSeconds(Math.Clamp(_configuration.DutyReadySoundDurationSeconds, 3, 30));
            var timedOut = now - _ringingStartedAt >= duration;
            if (!visible || timedOut || !_configuration.DutyReadySoundNotificationEnabled)
            {
                StopRinging();
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

    private void StopRinging()
    {
        _isRinging = false;
    }

    private void PlayOnceSafe()
    {
        try
        {
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

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);
}

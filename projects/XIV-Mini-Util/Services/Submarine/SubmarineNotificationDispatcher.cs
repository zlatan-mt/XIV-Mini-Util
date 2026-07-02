// Path: projects/XIV-Mini-Util/Services/Submarine/SubmarineNotificationDispatcher.cs
// Description: 潜水艦通知の非同期送信と例外観測を担当する
// Reason: unsafeなメモリ読取サービスの外でawaitを安全に完了するため

using Dalamud.Plugin.Services;
using XivMiniUtil.Models.Submarine;
using XivMiniUtil.Services.Notification;

namespace XivMiniUtil.Services.Submarine;

internal static class SubmarineNotificationDispatcher
{
    public static async Task SendSafeAsync(
        DiscordService discordService,
        IPluginLog pluginLog,
        string characterName,
        string world,
        List<SubmarineData> submarines)
    {
        try
        {
            await discordService.SendDispatchNotificationAsync(
                characterName,
                world,
                submarines);
        }
        catch (Exception ex)
        {
            pluginLog.Error(ex, "Failed to send submarine dispatch notification.");
        }
    }
}

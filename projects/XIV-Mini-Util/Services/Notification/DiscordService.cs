// Path: projects/XIV-Mini-Util/Services/DiscordService.cs
using Dalamud.Plugin.Services;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using XivMiniUtil.Models.Submarine;
using XivMiniUtil.Windows;

namespace XivMiniUtil.Services.Notification;

public class DiscordService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Configuration _configuration;
    private readonly IPluginLog _pluginLog;
    private readonly IChatGui _chatGui;

    public DiscordService(Configuration configuration, IPluginLog pluginLog, IChatGui chatGui)
    {
        _configuration = configuration;
        _pluginLog = pluginLog;
        _chatGui = chatGui;
        _httpClient = new HttpClient();
    }

    public async Task SendTestNotificationAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration.DiscordWebhookUrl))
        {
            _chatGui.Print("[XIV Mini Util] Discord Webhook URLが設定されていません。");
            return;
        }

        var japaneseCulture = new CultureInfo("ja-JP");
        var now = DateTime.Now;

        // ダミーの潜水艦データを生成
        var dummySubmarines = new List<(string Name, DateTime ReturnTime, double DurationHours)>
        {
            ("シャーク級1号", now.AddHours(12), 18.5),
            ("シャーク級2号", now.AddHours(14), 20.0),
            ("シャーク級3号", now.AddHours(16), 22.5),
            ("シャーク級4号", now.AddHours(18), 24.0),
        };

        var maxReturnTime = dummySubmarines.Max(s => s.ReturnTime);

        // 各潜水艦の情報をフォーマット
        var submarineLines = new List<string>();
        foreach (var sub in dummySubmarines.OrderBy(s => s.ReturnTime))
        {
            var returnTimeText = sub.ReturnTime.ToString("M/d(ddd) HH:mm", japaneseCulture);
            submarineLines.Add($"{sub.Name}  {returnTimeText} ({sub.DurationHours:F1}h)");
        }

        var relativeTimeText = GetRelativeTimeText(maxReturnTime);

        var payload = new DiscordNotificationPayload
        {
            Username = "XIV Mini Util",
            Embeds = new List<DiscordEmbed>
            {
                new DiscordEmbed
                {
                    Author = new DiscordAuthor
                    {
                        Name = "🧪 テスト通知 - TestChar@TestWorld - 4隻出航"
                    },
                    Description = $"🟠 帰還時間: {maxReturnTime.ToString("M/d(ddd) HH:mm", japaneseCulture)}\n```\n" +
                                  string.Join("\n", submarineLines) + "\n```",
                    Color = 0x808080, // Gray (テスト用)
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Footer = new DiscordFooter { Text = $"🧪 TEST | {relativeTimeText}" }
                }
            }
        };

        try
        {
            await SendPayloadAsync(payload);
            _chatGui.Print("[XIV Mini Util] テスト通知を送信しました。");
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to send test notification");
            _chatGui.Print($"[XIV Mini Util] テスト通知の送信に失敗しました: {ex.Message}");
        }
    }

    // [Deprecated] 帰還通知 - 出航通知に置き換え
    // public async Task SendVoyageCompletionAsync(string characterName, List<string> submarineNames, DateTime returnTime)
    // {
    //     if (!_configuration.SubmarineNotificationEnabled || string.IsNullOrWhiteSpace(_configuration.DiscordWebhookUrl))
    //     {
    //         return;
    //     }
    //
    //     var payload = new DiscordNotificationPayload
    //     {
    //         Username = "XIV Mini Util",
    //         Embeds = new List<DiscordEmbed>
    //         {
    //             new DiscordEmbed
    //             {
    //                 Title = "潜水艦帰還通知 (Submarine Returned)",
    //                 Color = 0x0099FF,
    //                 Fields = new List<DiscordField>
    //                 {
    //                     new DiscordField { Name = "Character", Value = characterName, Inline = true },
    //                     new DiscordField { Name = "Submarines", Value = string.Join(", ", submarineNames), Inline = false },
    //                     new DiscordField { Name = "Time", Value = returnTime.ToString("yyyy/MM/dd HH:mm"), Inline = true }
    //                 },
    //                 Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    //                 Footer = new DiscordFooter { Text = "XIV Mini Util" }
    //             }
    //         }
    //     };
    //
    //     await SendPayloadAsync(payload);
    // }

    /// <summary>
    /// 全艦出航時の通知を送信する
    /// </summary>
    public async Task SendDispatchNotificationAsync(string characterName, string world, List<SubmarineData> submarines)
    {
        if (!_configuration.SubmarineNotificationEnabled || string.IsNullOrWhiteSpace(_configuration.DiscordWebhookUrl))
        {
            return;
        }

        var japaneseCulture = new CultureInfo("ja-JP");

        // 最大帰還時刻を取得
        var maxReturnTime = submarines.Max(s => s.ReturnTime).ToLocalTime();

        // 各潜水艦の情報をフォーマット (コードブロック用に整列)
        var submarineLines = new List<string>();
        var nowUtc = DateTime.UtcNow;

        // 名前の最大幅を計算 (日本語文字は2幅としてカウント)
        int GetDisplayWidth(string s) => s.Sum(c => c > 127 ? 2 : 1);
        var maxNameWidth = submarines.Max(s => GetDisplayWidth(s.Name));

        foreach (var sub in submarines.OrderBy(s => s.ReturnTime))
        {
            // ReturnTime は UTC として扱う (Kind が Unspecified でも対応)
            var returnTimeUtc = sub.ReturnTime.Kind == DateTimeKind.Local
                ? sub.ReturnTime.ToUniversalTime()
                : DateTime.SpecifyKind(sub.ReturnTime, DateTimeKind.Utc);
            var returnTimeLocal = returnTimeUtc.ToLocalTime();
            var remaining = returnTimeUtc - nowUtc;

            // 残り時間: 0時間未満（過去）または異常に長い場合は "--h"
            string remainingText;
            if (remaining.TotalHours < 0 || remaining.TotalDays > 7)
            {
                remainingText = "-- h";
            }
            else
            {
                remainingText = $"{remaining.TotalHours,3:F0} h";
            }

            // 名前をパディング
            var nameWidth = GetDisplayWidth(sub.Name);
            var paddedName = sub.Name + new string(' ', maxNameWidth - nameWidth);

            var returnTimeText = returnTimeLocal.ToString("M/d(ddd) HH:mm", japaneseCulture);
            submarineLines.Add($"{paddedName} {returnTimeText} ({remainingText})");
        }

        // 相対時間テキストを生成
        var relativeTimeText = GetRelativeTimeText(maxReturnTime);

        var payload = new DiscordNotificationPayload
        {
            Username = "XIV Mini Util",
            Embeds = new List<DiscordEmbed>
            {
                new DiscordEmbed
                {
                    Author = new DiscordAuthor
                    {
                        Name = $"{characterName}@{world} - {submarines.Count}隻出航"
                    },
                    Description = $"🟠 帰還時間: {maxReturnTime.ToString("M/d(ddd) HH:mm", japaneseCulture)}\n```\n" +
                                  string.Join("\n", submarineLines) + "\n```",
                    Color = 0xFFA500, // Orange
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Footer = new DiscordFooter { Text = $"{relativeTimeText}•{MainWindow.BuildInfo}" }
                }
            }
        };

        await SendPayloadAsync(payload);
    }

    /// <summary>
    /// 相対時間テキストを生成（今日/明日/日付）
    /// </summary>
    private static string GetRelativeTimeText(DateTime targetTime)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var targetDate = targetTime.Date;

        if (targetDate == today)
        {
            return $"今日 {targetTime:HH:mm}";
        }
        else if (targetDate == today.AddDays(1))
        {
            return $"明日 {targetTime:HH:mm}";
        }
        else
        {
            return targetTime.ToString("M/d HH:mm", new CultureInfo("ja-JP"));
        }
    }

    private async Task SendPayloadAsync(DiscordNotificationPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = _configuration.DiscordWebhookUrl;

        int retryCount = 0;
        while (true)
        {
            try
            {
                var response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if ((int)response.StatusCode == 429) // Too Many Requests
                {
                    // シンプルなExponential Backoff
                    var delay = Math.Pow(2, retryCount) * 1000;
                    await Task.Delay((int)delay);
                }
                else if ((int)response.StatusCode >= 500)
                {
                     // Server Error
                    var delay = Math.Pow(2, retryCount) * 1000;
                    await Task.Delay((int)delay);
                }
                else
                {
                    // 4xx系などはリトライしない
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Discord API returned {response.StatusCode}: {errorMsg}");
                }
            }
            catch (HttpRequestException)
            {
                 // ネットワークエラー等
                 if (retryCount >= _configuration.NotificationRateLimitRetryMax) throw;
                 var delay = Math.Pow(2, retryCount) * 1000;
                 await Task.Delay((int)delay);
            }

            retryCount++;
            if (retryCount > _configuration.NotificationRateLimitRetryMax)
            {
                throw new Exception("Max retry attempts reached");
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

// Path: projects/XIV-Mini-Util/Services/DiscordService.cs
using Dalamud.Plugin.Services;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using XivMiniUtil.Models.Submarine;

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

        var payload = new DiscordNotificationPayload
        {
            Username = "XIV Mini Util",
            Embeds = new List<DiscordEmbed>
            {
                new DiscordEmbed
                {
                    Title = "Test Notification / テスト通知",
                    Description = "これはテスト通知です。\nThis is a test notification.",
                    Color = 0x0099FF,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Footer = new DiscordFooter { Text = "XIV Mini Util" }
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

    public async Task SendVoyageCompletionAsync(string characterName, List<string> submarineNames, DateTime returnTime)
    {
        if (!_configuration.SubmarineNotificationEnabled || string.IsNullOrWhiteSpace(_configuration.DiscordWebhookUrl))
        {
            return;
        }

        var payload = new DiscordNotificationPayload
        {
            Username = "XIV Mini Util",
            Embeds = new List<DiscordEmbed>
            {
                new DiscordEmbed
                {
                    Title = "潜水艦帰還通知 (Submarine Returned)",
                    Color = 0x0099FF,
                    Fields = new List<DiscordField>
                    {
                        new DiscordField { Name = "Character", Value = characterName, Inline = true },
                        new DiscordField { Name = "Submarines", Value = string.Join(", ", submarineNames), Inline = false },
                        new DiscordField { Name = "Time", Value = returnTime.ToString("yyyy/MM/dd HH:mm"), Inline = true }
                    },
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Footer = new DiscordFooter { Text = "XIV Mini Util" }
                }
            }
        };

        await SendPayloadAsync(payload);
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

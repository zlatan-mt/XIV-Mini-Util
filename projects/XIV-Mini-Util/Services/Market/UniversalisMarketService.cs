// Path: projects/XIV-Mini-Util/Services/Market/UniversalisMarketService.cs
// Description: Universalis APIから現在DC内の最安マーケット価格を取得する
// Reason: 右クリック起点で外部マーケット価格を安全に確認するため
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.Market;

public sealed class UniversalisMarketService : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IDataManager _dataManager;
    private readonly IObjectTable _objectTable;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _pluginLog;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<string, byte> _runningRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedResult> _resultCache = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    private IReadOnlyList<UniversalisDataCenter>? _dataCenters;

    public UniversalisMarketService(
        IDataManager dataManager,
        IObjectTable objectTable,
        IChatGui chatGui,
        IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _objectTable = objectTable;
        _chatGui = chatGui;
        _pluginLog = pluginLog;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://universalis.app/api/v2/"),
            Timeout = RequestTimeout,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XivMiniUtil", GetPluginVersion()));
    }

    public void CheckLowestPrice(uint itemId, UniversalisItemQuality quality)
    {
        _ = RunLowestPriceCheckAsync(itemId, quality);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _httpClient.Dispose();
        _runningRequests.Clear();
        lock (_cacheLock)
        {
            _resultCache.Clear();
        }
    }

    private async Task RunLowestPriceCheckAsync(uint itemId, UniversalisItemQuality quality)
    {
        string? requestKey = null;
        try
        {
            if (itemId == 0)
            {
                PostError("アイテムIDが取得できませんでした。");
                return;
            }

            var itemName = GetItemName(itemId);
            var dcName = await ResolveCurrentDataCenterNameAsync(_disposeCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(dcName))
            {
                PostError("現在DCを判定できませんでした。");
                return;
            }

            requestKey = $"{dcName}:{itemId}:{quality}";
            if (TryGetCachedResult(requestKey, out var cachedResult))
            {
                PostResult(itemName, cachedResult, fromCache: true);
                return;
            }

            if (!_runningRequests.TryAdd(requestKey, 0))
            {
                PostError($"{itemName} は検索中です。");
                return;
            }

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            requestCts.CancelAfter(RequestTimeout);
            var marketData = await FetchMarketDataAsync(dcName, itemId, requestCts.Token).ConfigureAwait(false);
            var result = BuildLowestPriceResult(marketData, quality);
            if (result == null)
            {
                PostError($"{itemName} ({FormatQuality(quality)}) の出品が見つかりませんでした。");
                return;
            }

            SetCachedResult(requestKey, result);
            PostResult(itemName, result, fromCache: false);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            _pluginLog.Debug("Universalis price check was canceled during dispose.");
        }
        catch (OperationCanceledException ex)
        {
            _pluginLog.Warning(ex, "Universalis price check timed out.");
            PostError("Universalis の価格取得がタイムアウトしました。");
        }
        catch (HttpRequestException ex)
        {
            _pluginLog.Warning(ex, "Universalis price check failed.");
            PostError("Universalis の価格取得に失敗しました。");
        }
        catch (JsonException ex)
        {
            _pluginLog.Warning(ex, "Universalis response parsing failed.");
            PostError("Universalis の応答を読み取れませんでした。");
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "Unexpected Universalis price check error.");
            PostError("Universalis の価格確認中にエラーが発生しました。");
        }
        finally
        {
            if (requestKey != null)
            {
                _runningRequests.TryRemove(requestKey, out _);
            }
        }
    }

    private async Task<string?> ResolveCurrentDataCenterNameAsync(CancellationToken cancellationToken)
    {
        var currentWorldId = _objectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (currentWorldId != 0)
        {
            var currentDcName = await FindDataCenterNameAsync(currentWorldId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(currentDcName))
            {
                _pluginLog.Debug($"Universalis DC resolved from CurrentWorld: {currentWorldId} -> {currentDcName}");
                return currentDcName;
            }
        }

        var homeWorldId = _objectTable.LocalPlayer?.HomeWorld.RowId ?? 0;
        if (homeWorldId != 0)
        {
            var homeDcName = await FindDataCenterNameAsync(homeWorldId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(homeDcName))
            {
                _pluginLog.Warning($"Universalis DC resolved from HomeWorld fallback: {homeWorldId} -> {homeDcName}");
                return homeDcName;
            }
        }

        _pluginLog.Warning("Universalis DC resolution failed.");
        return null;
    }

    private async Task<string?> FindDataCenterNameAsync(uint worldId, CancellationToken cancellationToken)
    {
        var dataCenters = await GetDataCentersAsync(cancellationToken).ConfigureAwait(false);
        return dataCenters.FirstOrDefault(dc => dc.Worlds.Contains(worldId))?.Name;
    }

    private async Task<IReadOnlyList<UniversalisDataCenter>> GetDataCentersAsync(CancellationToken cancellationToken)
    {
        if (_dataCenters != null)
        {
            return _dataCenters;
        }

        await using var stream = await _httpClient.GetStreamAsync("data-centers", cancellationToken).ConfigureAwait(false);
        var dataCenters = await JsonSerializer.DeserializeAsync<List<UniversalisDataCenter>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Universalis data-centers response was empty.");
        _dataCenters = dataCenters;
        return dataCenters;
    }

    private async Task<UniversalisMarketData> FetchMarketDataAsync(string dcName, uint itemId, CancellationToken cancellationToken)
    {
        var path = $"{Uri.EscapeDataString(dcName)}/{itemId}?listings=40&entries=20";
        await using var stream = await _httpClient.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<UniversalisMarketData>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException($"Universalis market response was empty for item {itemId}.");
    }

    private LowestPriceResult? BuildLowestPriceResult(UniversalisMarketData marketData, UniversalisItemQuality quality)
    {
        var shouldUseHq = quality == UniversalisItemQuality.HighQuality;
        var latestSale = FindLatestSale(marketData, quality);
        var listing = marketData.Listings
            .Where(entry => entry.PricePerUnit > 0 && entry.Hq == shouldUseHq)
            .OrderBy(entry => entry.PricePerUnit)
            .ThenBy(entry => entry.Total)
            .ThenBy(entry => entry.Quantity)
            .FirstOrDefault();

        if (listing == null)
        {
            return latestSale == null
                ? null
                : new LowestPriceResult(null, null, null, marketData.WorldName ?? marketData.DcName ?? "不明", shouldUseHq, null, latestSale);
        }

        DateTimeOffset? reviewedAt = null;
        if (listing.LastReviewTime > 0)
        {
            reviewedAt = DateTimeOffset.FromUnixTimeSeconds(listing.LastReviewTime);
        }
        else if (marketData.LastUploadTime > 0)
        {
            reviewedAt = DateTimeOffset.FromUnixTimeMilliseconds(marketData.LastUploadTime);
        }

        return new LowestPriceResult(
            listing.PricePerUnit,
            listing.Total,
            listing.Quantity,
            listing.WorldName ?? marketData.WorldName ?? marketData.DcName ?? "不明",
            listing.Hq,
            reviewedAt,
            latestSale);
    }

    private static LatestSaleResult? FindLatestSale(UniversalisMarketData marketData, UniversalisItemQuality quality)
    {
        var shouldUseHq = quality == UniversalisItemQuality.HighQuality;
        var sale = marketData.RecentHistory
            .Where(entry => entry.PricePerUnit > 0 && entry.Hq == shouldUseHq)
            .OrderByDescending(entry => entry.Timestamp)
            .FirstOrDefault();

        if (sale == null)
        {
            return null;
        }

        DateTimeOffset? soldAt = null;
        if (sale.Timestamp > 0)
        {
            soldAt = DateTimeOffset.FromUnixTimeSeconds(sale.Timestamp);
        }

        return new LatestSaleResult(
            sale.PricePerUnit,
            sale.Quantity,
            sale.WorldName ?? marketData.WorldName ?? marketData.DcName ?? "不明",
            soldAt);
    }

    private string GetItemName(uint itemId)
    {
        var item = _dataManager.Excel.GetSheet<Item>().GetRowOrDefault(itemId);
        return item?.Name.ToString() ?? $"Item {itemId}";
    }

    private bool TryGetCachedResult(string requestKey, out LowestPriceResult result)
    {
        lock (_cacheLock)
        {
            if (_resultCache.TryGetValue(requestKey, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt <= CacheDuration)
            {
                result = cached.Result;
                return true;
            }
        }

        result = default!;
        return false;
    }

    private void SetCachedResult(string requestKey, LowestPriceResult result)
    {
        lock (_cacheLock)
        {
            _resultCache[requestKey] = new CachedResult(result, DateTimeOffset.UtcNow);
        }
    }

    private void PostResult(string itemName, LowestPriceResult result, bool fromCache)
    {
        var quality = result.Hq ? "HQ" : "NQ";
        var cacheLabel = fromCache ? " / cache" : string.Empty;
        var listingLabel = result.PricePerUnit.HasValue
            ? $"最安単価 {result.PricePerUnit.Value:N0} gil / {result.WorldName} / x{result.Quantity.GetValueOrDefault():N0} / {FormatReviewAge(result.ReviewedAt)}"
            : "出品なし";
        var saleLabel = result.LatestSale == null
            ? " / 最終販売なし"
            : $" / 最終販売 {result.LatestSale.PricePerUnit:N0} gil / {result.LatestSale.WorldName} / x{result.LatestSale.Quantity:N0} / {FormatSaleAge(result.LatestSale.SoldAt)}";
        var message = $"[Universalis] {itemName} ({quality}): {listingLabel}{saleLabel}{cacheLabel}";
        _chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = new SeStringBuilder().AddText(message).Build(),
        });
    }

    private static string FormatQuality(UniversalisItemQuality quality)
    {
        return quality == UniversalisItemQuality.HighQuality ? "HQ" : "NQ";
    }

    private void PostError(string message)
    {
        _chatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = new SeStringBuilder().AddText($"[Universalis] {message}").Build(),
        });
    }

    private static string FormatReviewAge(DateTimeOffset? reviewedAt)
    {
        if (reviewedAt == null)
        {
            return "確認時刻不明";
        }

        var elapsed = DateTimeOffset.UtcNow - reviewedAt.Value.ToUniversalTime();
        if (elapsed.TotalMinutes < 1)
        {
            return "たった今確認";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}分前確認";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)}時間前確認";
        }

        return $"約{Math.Max(1, (int)elapsed.TotalDays)}日前確認 / データが古い可能性あり";
    }

    private static string FormatSaleAge(DateTimeOffset? soldAt)
    {
        if (soldAt == null)
        {
            return "販売時刻不明";
        }

        var elapsed = DateTimeOffset.UtcNow - soldAt.Value.ToUniversalTime();
        if (elapsed.TotalMinutes < 1)
        {
            return "たった今販売";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}分前販売";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)}時間前販売";
        }

        return $"約{Math.Max(1, (int)elapsed.TotalDays)}日前販売";
    }

    private static string GetPluginVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private sealed record CachedResult(LowestPriceResult Result, DateTimeOffset CachedAt);

    private sealed record LowestPriceResult(
        long? PricePerUnit,
        long? Total,
        long? Quantity,
        string WorldName,
        bool Hq,
        DateTimeOffset? ReviewedAt,
        LatestSaleResult? LatestSale);

    private sealed record LatestSaleResult(
        long PricePerUnit,
        long Quantity,
        string WorldName,
        DateTimeOffset? SoldAt);

    private sealed class UniversalisDataCenter
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("worlds")]
        public IReadOnlyList<uint> Worlds { get; set; } = Array.Empty<uint>();
    }

    private sealed class UniversalisMarketData
    {
        [JsonPropertyName("dcName")]
        public string? DcName { get; set; }

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("lastUploadTime")]
        public long LastUploadTime { get; set; }

        [JsonPropertyName("listings")]
        public List<UniversalisListing> Listings { get; set; } = new();

        [JsonPropertyName("recentHistory")]
        public List<UniversalisHistoryEntry> RecentHistory { get; set; } = new();
    }

    private sealed class UniversalisListing
    {
        [JsonPropertyName("pricePerUnit")]
        public long PricePerUnit { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("hq")]
        public bool Hq { get; set; }

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("lastReviewTime")]
        public long LastReviewTime { get; set; }
    }

    private sealed class UniversalisHistoryEntry
    {
        [JsonPropertyName("pricePerUnit")]
        public long PricePerUnit { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonPropertyName("hq")]
        public bool Hq { get; set; }

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}

public enum UniversalisItemQuality
{
    Normal,
    HighQuality,
}

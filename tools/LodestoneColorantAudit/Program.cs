using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var options = LodestoneAuditOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(LodestoneAuditOptions.HelpText);
    return 0;
}

try
{
    if (options.ItemUrls.Count == 0)
    {
        Console.Error.WriteLine("At least one --item-url is required.");
        return 1;
    }

    Directory.CreateDirectory(options.CacheDirectory);

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
        "XIV-Mini-Util-LodestoneColorantAudit/1.0 (+local development tool)");

    var records = new List<LodestoneColorantRecord>();
    foreach (var itemUrl in options.ItemUrls)
    {
        records.Add(await FetchAndParseAsync(httpClient, itemUrl, options.CacheDirectory));
    }

    var document = new LodestoneColorantDocument(
        DateTimeOffset.UtcNow.ToString("O"),
        options.Query,
        options.Patch,
        "itemUrls",
        records,
        new LodestoneColorantSummary(
            records.Count,
            records.Count(record => record.ShopSaleStatus == "available"),
            records.Count(record => record.ShopSaleStatus == "none"),
            records.Count(record => record.ShopSaleStatus == "unknown"),
            records.Count(record => record.ParseStatus is "fetch_failed" or "parse_failed"),
            records.Count(record => record.ParseStatus == "cached")));

    var outputPath = Path.GetFullPath(options.OutputPath);
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, jsonOptions), Encoding.UTF8);

    Console.WriteLine($"Lodestone audit written: {outputPath}");
    Console.WriteLine($"totalRecords: {document.Summary.TotalRecords}");
    Console.WriteLine($"availableSales: {document.Summary.AvailableSales}");
    Console.WriteLine($"noSales: {document.Summary.NoSales}");
    Console.WriteLine($"unknown: {document.Summary.Unknown}");
    Console.WriteLine($"parseFailed: {document.Summary.ParseFailed}");
    Console.WriteLine($"cachedRecords: {document.Summary.CachedRecords}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"LodestoneColorantAudit failed: {ex.Message}");
    return 1;
}

static async Task<LodestoneColorantRecord> FetchAndParseAsync(
    HttpClient httpClient,
    string itemUrl,
    string cacheDirectory)
{
    var fetchedAt = DateTimeOffset.UtcNow.ToString("O");
    var cachePath = Path.Combine(cacheDirectory, $"{GetUrlSlug(itemUrl)}.html");
    string html;
    var parseStatus = "ok";
    string? parseWarning = null;

    try
    {
        html = await httpClient.GetStringAsync(itemUrl);
        await File.WriteAllTextAsync(cachePath, html, Encoding.UTF8);
    }
    catch (Exception ex)
    {
        if (File.Exists(cachePath))
        {
            html = await File.ReadAllTextAsync(cachePath, Encoding.UTF8);
            parseStatus = "cached";
            parseWarning = $"Fetch failed; used cached HTML. {ex.Message}";
        }
        else
        {
            return new LodestoneColorantRecord(
                null,
                null,
                itemUrl,
                itemUrl,
                fetchedAt,
                "unknown",
                null,
                null,
                "fetch_failed",
                ex.Message);
        }
    }

    try
    {
        var itemName = ParseItemName(html);
        var looksLikeItemPage = LooksLikeLodestoneItemPage(html, itemUrl);
        var priceResult = ParseShopPrice(html);
        var noSaleEvidence = HtmlContainsExplicitNoShopSale(html);
        var saleStatus = ResolveShopSaleStatus(looksLikeItemPage, priceResult.Price, noSaleEvidence);
        var saleEvidence = saleStatus switch
        {
            "available" => priceResult.MatchedText,
            "none" => noSaleEvidence,
            _ => null,
        };
        var statusWarning = BuildParseWarning(saleStatus, looksLikeItemPage);

        return new LodestoneColorantRecord(
            itemName,
            itemName == null ? null : NormalizeItemName(itemName),
            itemUrl,
            itemUrl,
            fetchedAt,
            saleStatus,
            priceResult.Price,
            saleEvidence,
            parseStatus,
            CombineWarnings(parseWarning, statusWarning));
    }
    catch (Exception ex)
    {
        return new LodestoneColorantRecord(
            null,
            null,
            itemUrl,
            itemUrl,
            fetchedAt,
            "unknown",
            null,
            null,
            "parse_failed",
            ex.Message);
    }
}

static string? ParseItemName(string html)
{
    var titleMatch = Regex.Match(
        html,
        @"<title>\s*エオルゼアデータベース「(?<name>.*?)」",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    if (titleMatch.Success)
    {
        var titleName = CleanHtmlText(titleMatch.Groups["name"].Value);
        if (!string.IsNullOrWhiteSpace(titleName))
        {
            return titleName;
        }
    }

    var h2Match = Regex.Match(
        html,
        @"<h2[^>]*>\s*(?<name>.*?)\s*</h2>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    if (h2Match.Success)
    {
        var name = CleanHtmlText(h2Match.Groups["name"].Value);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
    }

    return null;
}

static bool LooksLikeLodestoneItemPage(string html, string sourceUrl)
{
    return Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
        && uri.Host.Equals("jp.finalfantasyxiv.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.Contains("/lodestone/playguide/db/item/", StringComparison.OrdinalIgnoreCase)
        && html.Contains("エオルゼアデータベース", StringComparison.Ordinal)
        && html.Contains("playguide/db/item", StringComparison.OrdinalIgnoreCase)
        && Regex.IsMatch(
            html,
            @"<title>\s*エオルゼアデータベース「.+?」",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
}

static ShopPriceParseResult ParseShopPrice(string html)
{
    var match = Regex.Match(
        html,
        @"SHOP販売価格:\s*(?:<[^>]+>)*\s*(?<price>[0-9,]+)\s*Gil",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);
    if (!match.Success)
    {
        return new ShopPriceParseResult(null, null);
    }

    var matchedText = CleanHtmlText(match.Value);
    return int.TryParse(match.Groups["price"].Value.Replace(",", string.Empty, StringComparison.Ordinal), out var price)
        ? new ShopPriceParseResult(price, matchedText)
        : new ShopPriceParseResult(null, matchedText);
}

static string? HtmlContainsExplicitNoShopSale(string html)
{
    foreach (var pattern in new[]
             {
                 "SHOP販売なし",
                 "SHOP販売価格なし",
                 "ショップ販売なし",
                 "販売ショップNPCなし",
                 "販売ショップNPCはありません",
                 "ショップで販売されていません",
             })
    {
        if (html.Contains(pattern, StringComparison.Ordinal))
        {
            return pattern;
        }
    }

    return null;
}

static string ResolveShopSaleStatus(bool looksLikeItemPage, int? shopPrice, string? noSaleEvidence)
{
    if (shopPrice.HasValue)
    {
        return "available";
    }

    if (!looksLikeItemPage)
    {
        return "unknown";
    }

    return noSaleEvidence == null ? "unknown" : "none";
}

static string? BuildParseWarning(string saleStatus, bool looksLikeItemPage)
{
    if (!looksLikeItemPage)
    {
        return "HTML did not look like a Lodestone item page.";
    }

    return saleStatus == "unknown"
        ? "SHOP sale status could not be determined from explicit evidence."
        : null;
}

static string? CombineWarnings(params string?[] warnings)
{
    var values = warnings
        .Where(warning => !string.IsNullOrWhiteSpace(warning))
        .Select(warning => warning!.Trim())
        .ToArray();

    return values.Length == 0 ? null : string.Join(" ", values);
}

static string CleanHtmlText(string value)
{
    var withoutTags = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
    return WebUtility.HtmlDecode(withoutTags).Trim();
}

static string NormalizeItemName(string name)
{
    var builder = new StringBuilder(name.Length);
    foreach (var character in name.Normalize(NormalizationForm.FormKC))
    {
        if (!char.IsWhiteSpace(character) && character != ':' && character != '：')
        {
            builder.Append(character);
        }
    }

    return builder.ToString();
}

static string GetUrlSlug(string url)
{
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            return string.Join("-", parts.TakeLast(2));
        }
    }

    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
}

internal sealed record LodestoneAuditOptions(
    IReadOnlyList<string> ItemUrls,
    string OutputPath,
    string CacheDirectory,
    string Query,
    string Patch,
    bool ShowHelp)
{
    public const string DefaultOutputPath = @"artifacts\shop-snapshot\lodestone-colorants.json";
    public const string DefaultCacheDirectory = @"artifacts\shop-snapshot\cache\lodestone";

    public static string HelpText =>
        """
        Usage:
          dotnet run --project tools/LodestoneColorantAudit -- [options]

        Options:
          --item-url <url>   Lodestone item URL. Repeatable.
          --out <path>       Output JSON path. Default: artifacts\shop-snapshot\lodestone-colorants.json
          --cache-dir <path> HTML cache directory. Default: artifacts\shop-snapshot\cache\lodestone
          --query <text>     Query label stored in JSON. Default: カララント
          --patch <text>     Patch label stored in JSON. Default: latest
          --help             Show help.
        """;

    public static LodestoneAuditOptions Parse(IReadOnlyList<string> args)
    {
        var itemUrls = new List<string>();
        var outputPath = DefaultOutputPath;
        var cacheDirectory = DefaultCacheDirectory;
        var query = "カララント";
        var patch = "latest";
        var showHelp = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--item-url":
                    itemUrls.Add(ReadValue(args, ref i, arg));
                    break;
                case "--out":
                    outputPath = ReadValue(args, ref i, arg);
                    break;
                case "--cache-dir":
                    cacheDirectory = ReadValue(args, ref i, arg);
                    break;
                case "--query":
                    query = ReadValue(args, ref i, arg);
                    break;
                case "--patch":
                    patch = ReadValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new LodestoneAuditOptions(itemUrls, outputPath, cacheDirectory, query, patch, showHelp);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}

internal sealed record LodestoneColorantDocument(
    string GeneratedAt,
    string Query,
    string Patch,
    string SourceMode,
    IReadOnlyList<LodestoneColorantRecord> Records,
    LodestoneColorantSummary Summary);

internal sealed record LodestoneColorantRecord(
    string? ItemName,
    string? NormalizedItemName,
    string ItemPageUrl,
    string SourceUrl,
    string FetchedAt,
    string ShopSaleStatus,
    int? ShopPrice,
    string? SaleEvidence,
    string ParseStatus,
    string? ParseWarning);

internal sealed record LodestoneColorantSummary(
    int TotalRecords,
    int AvailableSales,
    int NoSales,
    int Unknown,
    int ParseFailed,
    int CachedRecords);

internal sealed record ShopPriceParseResult(int? Price, string? MatchedText);

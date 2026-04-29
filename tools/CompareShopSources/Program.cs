using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

var options = CompareOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(CompareOptions.HelpText);
    return 0;
}

try
{
    if (!File.Exists(options.SnapshotPath))
    {
        Console.Error.WriteLine($"Snapshot JSON does not exist: {options.SnapshotPath}");
        return 1;
    }

    if (!File.Exists(options.LodestonePath))
    {
        Console.Error.WriteLine($"Lodestone JSON does not exist: {options.LodestonePath}");
        return 1;
    }

    var snapshot = await ReadSnapshotAsync(options.SnapshotPath);
    var lodestone = await ReadLodestoneAsync(options.LodestonePath);
    var result = Compare(snapshot, lodestone);

    var jsonPath = Path.GetFullPath(options.JsonOutputPath);
    var markdownPath = Path.GetFullPath(options.MarkdownOutputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath) ?? ".");
    Directory.CreateDirectory(Path.GetDirectoryName(markdownPath) ?? ".");

    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(result, jsonOptions), Encoding.UTF8);
    await File.WriteAllTextAsync(markdownPath, BuildMarkdown(result), Encoding.UTF8);

    Console.WriteLine($"Diff JSON written: {jsonPath}");
    Console.WriteLine($"Diff markdown written: {markdownPath}");
    Console.WriteLine($"p0LodestoneAvailableSnapshotMissing: {result.Summary.P0LodestoneAvailableSnapshotMissing}");
    Console.WriteLine($"p1LodestoneItemUnmapped: {result.Summary.P1LodestoneItemUnmapped}");
    Console.WriteLine($"p2SnapshotAvailableLodestoneNone: {result.Summary.P2SnapshotAvailableLodestoneNone}");
    Console.WriteLine($"p2SnapshotAvailableLodestoneNoneDistinctItems: {result.Summary.P2SnapshotAvailableLodestoneNoneDistinctItems}");
    Console.WriteLine($"p3NameFallbackReference: {result.Summary.P3NameFallbackReference}");
    Console.WriteLine($"p4AvailableButLocationMissing: {result.Summary.P4AvailableButLocationMissing}");
    Console.WriteLine($"p4AvailableButLocationMissingDistinctItems: {result.Summary.P4AvailableButLocationMissingDistinctItems}");
    Console.WriteLine($"lodestoneUnknownRecords: {result.Summary.LodestoneUnknownRecords}");
    Console.WriteLine($"lodestoneUnknownDistinctItems: {result.Summary.LodestoneUnknownDistinctItems}");
    Console.WriteLine($"lodestoneCachedRecords: {result.Summary.LodestoneCachedRecords}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CompareShopSources failed: {ex.Message}");
    return 1;
}

static async Task<SnapshotInput> ReadSnapshotAsync(string path)
{
    await using var stream = File.OpenRead(path);
    using var document = await JsonDocument.ParseAsync(stream);
    var records = document.RootElement.GetProperty("records")
        .EnumerateArray()
        .Select(record => new SnapshotRecord(
            record.GetProperty("itemId").GetUInt32(),
            record.GetProperty("itemName").GetString() ?? string.Empty,
            NormalizeItemName(record.GetProperty("itemName").GetString() ?? string.Empty),
            record.GetProperty("colorantDetection").GetString() ?? "none",
            record.GetProperty("shopType").GetString() ?? string.Empty,
            record.GetProperty("shopId").GetUInt32(),
            record.GetProperty("shopName").GetString() ?? string.Empty,
            record.GetProperty("npcName").GetString() ?? string.Empty,
            record.GetProperty("territoryId").GetUInt32(),
            record.GetProperty("areaName").GetString() ?? string.Empty,
            record.GetProperty("mapX").GetSingle(),
            record.GetProperty("mapY").GetSingle(),
            record.GetProperty("price").GetInt32()))
        .ToList();

    return new SnapshotInput(path, records);
}

static async Task<LodestoneInput> ReadLodestoneAsync(string path)
{
    await using var stream = File.OpenRead(path);
    using var document = await JsonDocument.ParseAsync(stream);
    var records = document.RootElement.GetProperty("records")
        .EnumerateArray()
        .Select(record => new LodestoneRecord(
            record.TryGetProperty("itemName", out var itemName) ? itemName.GetString() : null,
            record.TryGetProperty("normalizedItemName", out var normalized) ? normalized.GetString() : null,
            record.GetProperty("itemPageUrl").GetString() ?? string.Empty,
            record.GetProperty("shopSaleStatus").GetString() ?? "unknown",
            record.TryGetProperty("shopPrice", out var price) && price.ValueKind == JsonValueKind.Number
                ? price.GetInt32()
                : null,
            record.GetProperty("parseStatus").GetString() ?? "parse_failed"))
        .ToList();

    return new LodestoneInput(path, records);
}

static CompareDocument Compare(SnapshotInput snapshot, LodestoneInput lodestone)
{
    var snapshotByNormalized = snapshot.Records
        .GroupBy(record => record.NormalizedItemName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    var snapshotByName = snapshot.Records
        .GroupBy(record => record.ItemName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

    var issues = new List<CompareIssue>();
    var mappedItemIds = new HashSet<uint>();

    foreach (var lodestoneRecord in lodestone.Records)
    {
        var matches = FindSnapshotMatches(lodestoneRecord, snapshotByNormalized, snapshotByName);
        if (matches.Count == 0)
        {
            if (lodestoneRecord.ShopSaleStatus == "available")
            {
                issues.Add(CompareIssue.FromLodestone("P1", "Lodestone item name could not be mapped to snapshot itemId.", lodestoneRecord));
            }

            continue;
        }

        foreach (var itemId in matches.Select(record => record.ItemId).Distinct())
        {
            mappedItemIds.Add(itemId);
        }

        var stainSheetMatches = matches
            .Where(record => record.ColorantDetection == "stainSheet")
            .ToList();
        var nameFallbackMatches = matches
            .Where(record => record.ColorantDetection == "nameFallback")
            .ToList();

        if (lodestoneRecord.ShopSaleStatus == "available" && stainSheetMatches.Count == 0)
        {
            issues.Add(CompareIssue.FromLodestone("P0", "Lodestone has shop sale, but snapshot has no stainSheet sale.", lodestoneRecord));
        }

        if (lodestoneRecord.ShopSaleStatus == "none" && stainSheetMatches.Count > 0)
        {
            foreach (var snapshotRecord in stainSheetMatches)
            {
                issues.Add(CompareIssue.FromSnapshot("P2", "Snapshot has stainSheet sale, but Lodestone reports no shop sale.", snapshotRecord, lodestoneRecord));
            }
        }

        if (nameFallbackMatches.Count > 0)
        {
            foreach (var snapshotRecord in nameFallbackMatches)
            {
                issues.Add(CompareIssue.FromSnapshot("P3", "Snapshot nameFallback colorant sale is reference-only.", snapshotRecord, lodestoneRecord));
            }
        }

        if (lodestoneRecord.ShopSaleStatus == "available" && stainSheetMatches.Any(IsLocationMissing))
        {
            foreach (var snapshotRecord in stainSheetMatches.Where(IsLocationMissing))
            {
                issues.Add(CompareIssue.FromSnapshot("P4", "Lodestone and snapshot both have sale, but snapshot location is missing.", snapshotRecord, lodestoneRecord));
            }
        }
    }

    foreach (var snapshotRecord in snapshot.Records.Where(record => record.ColorantDetection == "nameFallback" && !mappedItemIds.Contains(record.ItemId)))
    {
        issues.Add(CompareIssue.FromSnapshot("P3", "Snapshot nameFallback colorant sale is not confirmed by Lodestone input.", snapshotRecord, null));
    }

    var orderedIssues = issues
        .DistinctBy(issue => (
            issue.Priority,
            issue.ItemId,
            issue.ItemName,
            issue.LodestoneItemName,
            issue.ColorantDetection,
            issue.ShopType,
            issue.ShopId,
            issue.ShopName,
            issue.NpcName,
            issue.LodestoneStatus,
            issue.LodestoneUrl))
        .OrderBy(issue => issue.Priority, StringComparer.Ordinal)
        .ThenBy(issue => issue.ItemName, StringComparer.Ordinal)
        .ThenBy(issue => issue.ItemId ?? 0)
        .ThenBy(issue => issue.ShopType, StringComparer.Ordinal)
        .ThenBy(issue => issue.ShopId ?? 0)
        .ToList();

    return new CompareDocument(
        DateTimeOffset.UtcNow.ToString("O"),
        snapshot.Path,
        lodestone.Path,
        new CompareSummary(
            orderedIssues.Count(issue => issue.Priority == "P0"),
            orderedIssues.Count(issue => issue.Priority == "P1"),
            orderedIssues.Count(issue => issue.Priority == "P2"),
            CountDistinctItems(orderedIssues, "P2"),
            orderedIssues.Count(issue => issue.Priority == "P3"),
            orderedIssues.Count(issue => issue.Priority == "P4"),
            CountDistinctItems(orderedIssues, "P4"),
            lodestone.Records.Count(record => record.ShopSaleStatus == "unknown"),
            CountDistinctLodestoneItems(lodestone.Records, "unknown"),
            lodestone.Records.Count(record => record.ParseStatus == "cached")),
        orderedIssues);
}

static int CountDistinctItems(IEnumerable<CompareIssue> issues, string priority)
{
    return issues
        .Where(issue => issue.Priority == priority)
        .Select(issue => issue.ItemId?.ToString() ?? issue.ItemName ?? issue.LodestoneItemName ?? string.Empty)
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Distinct(StringComparer.Ordinal)
        .Count();
}

static int CountDistinctLodestoneItems(IEnumerable<LodestoneRecord> records, string shopSaleStatus)
{
    return records
        .Where(record => record.ShopSaleStatus == shopSaleStatus)
        .Select(record => record.NormalizedItemName ?? record.ItemName ?? record.ItemPageUrl)
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Distinct(StringComparer.Ordinal)
        .Count();
}

static IReadOnlyList<SnapshotRecord> FindSnapshotMatches(
    LodestoneRecord lodestoneRecord,
    IReadOnlyDictionary<string, List<SnapshotRecord>> snapshotByNormalized,
    IReadOnlyDictionary<string, List<SnapshotRecord>> snapshotByName)
{
    if (!string.IsNullOrWhiteSpace(lodestoneRecord.NormalizedItemName)
        && snapshotByNormalized.TryGetValue(lodestoneRecord.NormalizedItemName, out var normalizedMatches))
    {
        return normalizedMatches;
    }

    if (!string.IsNullOrWhiteSpace(lodestoneRecord.ItemName)
        && snapshotByName.TryGetValue(lodestoneRecord.ItemName, out var nameMatches))
    {
        return nameMatches;
    }

    return [];
}

static bool IsLocationMissing(SnapshotRecord record)
{
    return record.TerritoryId == 0 || record.MapX == 0 || record.MapY == 0;
}

static string BuildMarkdown(CompareDocument result)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Colorant Shop Source Diff");
    builder.AppendLine();
    builder.AppendLine("## Summary");
    builder.AppendLine();
    builder.AppendLine($"- P0 Lodestone販売あり / snapshot stainSheet販売なし: {result.Summary.P0LodestoneAvailableSnapshotMissing}");
    builder.AppendLine($"- P1 Lodestone item名を snapshot itemId へ逆引き不可: {result.Summary.P1LodestoneItemUnmapped}");
    builder.AppendLine($"- P2 snapshot stainSheet販売あり / Lodestone販売なし: {result.Summary.P2SnapshotAvailableLodestoneNone}");
    builder.AppendLine($"- P2 distinct items: {result.Summary.P2SnapshotAvailableLodestoneNoneDistinctItems}");
    builder.AppendLine($"- P3 snapshot nameFallback販売あり / Lodestone照合未確認: {result.Summary.P3NameFallbackReference}");
    builder.AppendLine($"- P4 Lodestone販売あり / snapshot販売あり / location missing: {result.Summary.P4AvailableButLocationMissing}");
    builder.AppendLine($"- P4 distinct items: {result.Summary.P4AvailableButLocationMissingDistinctItems}");
    builder.AppendLine($"- Lodestone unknown records: {result.Summary.LodestoneUnknownRecords}");
    builder.AppendLine($"- Lodestone unknown distinct items: {result.Summary.LodestoneUnknownDistinctItems}");
    builder.AppendLine($"- Lodestone cached records: {result.Summary.LodestoneCachedRecords}");
    builder.AppendLine();
    builder.AppendLine("## Issues");
    builder.AppendLine();
    builder.AppendLine("| Priority | Item | Detection | Shop | NPC | Price | Note |");
    builder.AppendLine("| --- | --- | --- | --- | --- | ---: | --- |");

    foreach (var issue in result.Issues)
    {
        builder.AppendLine(
            $"| {issue.Priority} | {EscapeMarkdown(issue.ItemName ?? issue.LodestoneItemName ?? "(unknown)")} | {issue.ColorantDetection ?? "-"} | {EscapeMarkdown(issue.ShopName ?? "-")} | {EscapeMarkdown(issue.NpcName ?? "-")} | {issue.Price?.ToString() ?? "-"} | {EscapeMarkdown(issue.Note)} |");
    }

    return builder.ToString();
}

static string EscapeMarkdown(string value)
{
    return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
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

internal sealed record CompareOptions(
    string SnapshotPath,
    string LodestonePath,
    string JsonOutputPath,
    string MarkdownOutputPath,
    bool ShowHelp)
{
    public const string DefaultSnapshotPath = @"artifacts\shop-snapshot\shop-snapshot.json";
    public const string DefaultLodestonePath = @"artifacts\shop-snapshot\lodestone-colorants.json";
    public const string DefaultJsonOutputPath = @"artifacts\shop-snapshot\colorant-diff.json";
    public const string DefaultMarkdownOutputPath = @"artifacts\shop-snapshot\colorant-diff.md";

    public static string HelpText =>
        """
        Usage:
          dotnet run --project tools/CompareShopSources -- [options]

        Options:
          --snapshot <path>  ShopDataSnapshot JSON. Default: artifacts\shop-snapshot\shop-snapshot.json
          --lodestone <path> LodestoneColorantAudit JSON. Default: artifacts\shop-snapshot\lodestone-colorants.json
          --json-out <path>  Diff JSON output. Default: artifacts\shop-snapshot\colorant-diff.json
          --md-out <path>    Diff markdown output. Default: artifacts\shop-snapshot\colorant-diff.md
          --help             Show help.
        """;

    public static CompareOptions Parse(IReadOnlyList<string> args)
    {
        var snapshotPath = DefaultSnapshotPath;
        var lodestonePath = DefaultLodestonePath;
        var jsonOutputPath = DefaultJsonOutputPath;
        var markdownOutputPath = DefaultMarkdownOutputPath;
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
                case "--snapshot":
                    snapshotPath = ReadValue(args, ref i, arg);
                    break;
                case "--lodestone":
                    lodestonePath = ReadValue(args, ref i, arg);
                    break;
                case "--json-out":
                    jsonOutputPath = ReadValue(args, ref i, arg);
                    break;
                case "--md-out":
                    markdownOutputPath = ReadValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new CompareOptions(snapshotPath, lodestonePath, jsonOutputPath, markdownOutputPath, showHelp);
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

internal sealed record SnapshotInput(string Path, IReadOnlyList<SnapshotRecord> Records);

internal sealed record LodestoneInput(string Path, IReadOnlyList<LodestoneRecord> Records);

internal sealed record SnapshotRecord(
    uint ItemId,
    string ItemName,
    string NormalizedItemName,
    string ColorantDetection,
    string ShopType,
    uint ShopId,
    string ShopName,
    string NpcName,
    uint TerritoryId,
    string AreaName,
    float MapX,
    float MapY,
    int Price);

internal sealed record LodestoneRecord(
    string? ItemName,
    string? NormalizedItemName,
    string ItemPageUrl,
    string ShopSaleStatus,
    int? ShopPrice,
    string ParseStatus);

internal sealed record CompareDocument(
    string GeneratedAt,
    string SnapshotPath,
    string LodestonePath,
    CompareSummary Summary,
    IReadOnlyList<CompareIssue> Issues);

internal sealed record CompareSummary(
    int P0LodestoneAvailableSnapshotMissing,
    int P1LodestoneItemUnmapped,
    int P2SnapshotAvailableLodestoneNone,
    int P2SnapshotAvailableLodestoneNoneDistinctItems,
    int P3NameFallbackReference,
    int P4AvailableButLocationMissing,
    int P4AvailableButLocationMissingDistinctItems,
    int LodestoneUnknownRecords,
    int LodestoneUnknownDistinctItems,
    int LodestoneCachedRecords);

internal sealed record CompareIssue(
    string Priority,
    string Note,
    uint? ItemId,
    string? ItemName,
    string? LodestoneItemName,
    string? ColorantDetection,
    string? ShopType,
    uint? ShopId,
    string? ShopName,
    string? NpcName,
    int? Price,
    string? LodestoneStatus,
    string? LodestoneUrl)
{
    public static CompareIssue FromLodestone(string priority, string note, LodestoneRecord lodestone)
    {
        return new CompareIssue(
            priority,
            note,
            null,
            null,
            lodestone.ItemName,
            null,
            null,
            null,
            null,
            null,
            lodestone.ShopPrice,
            lodestone.ShopSaleStatus,
            lodestone.ItemPageUrl);
    }

    public static CompareIssue FromSnapshot(string priority, string note, SnapshotRecord snapshot, LodestoneRecord? lodestone)
    {
        return new CompareIssue(
            priority,
            note,
            snapshot.ItemId,
            snapshot.ItemName,
            lodestone?.ItemName,
            snapshot.ColorantDetection,
            snapshot.ShopType,
            snapshot.ShopId,
            snapshot.ShopName,
            snapshot.NpcName,
            snapshot.Price,
            lodestone?.ShopSaleStatus,
            lodestone?.ItemPageUrl);
    }
}

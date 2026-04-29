using System.Text.Json;
using ShopDataSnapshot.Core;

var options = SnapshotOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(SnapshotOptions.HelpText);
    return 0;
}

try
{
    if (!Directory.Exists(options.GamePath))
    {
        Console.Error.WriteLine($"Game path does not exist: {options.GamePath}");
        return 1;
    }

    var builder = new ShopSnapshotBuilder();
    var snapshot = builder.Build(options);

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
    };
    File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, jsonOptions));

    Console.WriteLine($"Snapshot written: {outputPath}");
    Console.WriteLine($"totalRecords: {snapshot.Summary.TotalRecords}");
    Console.WriteLine($"uniqueItems: {snapshot.Summary.UniqueItems}");
    Console.WriteLine($"colorantItems: {snapshot.Summary.ColorantItems}");
    Console.WriteLine($"stainSheetColorantItemsInSnapshot: {snapshot.Summary.StainSheetColorantItemsInSnapshot}");
    Console.WriteLine($"stainSheetItemIds: {snapshot.Summary.StainSheetItemIds}");
    Console.WriteLine($"stainRawFallbackUsed: {snapshot.Summary.StainRawFallbackUsed}");
    Console.WriteLine($"nameFallbackColorantItems: {snapshot.Summary.NameFallbackColorantItems}");
    Console.WriteLine($"gilShopRecords: {snapshot.Summary.GilShopRecords}");
    Console.WriteLine($"specialShopRecords: {snapshot.Summary.SpecialShopRecords}");
    Console.WriteLine($"missingNpcLocationRecords: {snapshot.Summary.MissingNpcLocationRecords}");
    Console.WriteLine($"missingNpcLocationUniqueNpcs: {snapshot.Summary.MissingNpcLocationUniqueNpcs}");
    Console.WriteLine($"missingNpcLocationUniqueShops: {snapshot.Summary.MissingNpcLocationUniqueShops}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ShopDataSnapshot failed: {ex.Message}");
    return 1;
}

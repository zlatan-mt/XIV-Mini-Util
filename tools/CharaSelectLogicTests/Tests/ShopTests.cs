// Path: tools/CharaSelectLogicTests/Tests/ShopTests.cs
// Description: Registers regression tests for the Shop responsibility
// Reason: Keeps the former monolithic runner maintainable
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;
using System.Text;
using System.Text.Json;
using XivMiniUtil;
using XivMiniUtil.Services.CharaSelect;
using XivMiniUtil.Services.Market;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.TitleBackground;

internal static partial class TestRunner
{
    private static void AddShopTests(List<LogicTestCase> tests)
    {
        void Test(int order, string name, Func<bool> assertion) =>
            tests.Add(new LogicTestCase(order, name, assertion));

Test(8, "shop item id normalization handles normal hq and zero ids", () =>
{
    return ContextMenuService.NormalizeItemId(42) == 42
        && ContextMenuService.NormalizeItemId(500042) == 42
        && ContextMenuService.NormalizeItemId(1000042) == 42
        && ContextMenuService.NormalizeItemId(0) == 0
        && ContextMenuService.GetItemQuality(42) == UniversalisItemQuality.Normal
        && ContextMenuService.GetItemQuality(1000042) == UniversalisItemQuality.HighQuality
        && ContextMenuService.GetItemQuality(1000000) == UniversalisItemQuality.HighQuality;
});

Test(9, "shop names fall back to stable labels when source names are missing", () =>
{
    var gilNames = new Dictionary<uint, string>
    {
        [10] = "素材屋",
        [11] = string.Empty,
    };

    return ShopNameFormatter.GetGilShopName(10, gilNames) == "素材屋"
        && ShopNameFormatter.GetGilShopName(11, gilNames) == "ショップ#11"
        && ShopNameFormatter.GetGilShopName(12, gilNames) == "ショップ#12"
        && ShopNameFormatter.GetSpecialShopName(20, "交換員") == "交換員"
        && ShopNameFormatter.GetSpecialShopName(21, null) == "特殊ショップ#21"
        && ShopNameFormatter.GetSpecialShopName(22, string.Empty) == "特殊ショップ#22";
});

Test(10, "shop location validator rejects placeholder or missing locations", () =>
{
    var valid = new NpcShopInfo(1, "NPC", 10, "Shop", 128, "リムサ・ロミンサ", string.Empty, 1, 10f, 20f);
    var missingTerritory = valid with { TerritoryTypeId = 0 };
    var missingArea = valid with { AreaName = " " };
    var unknownArea = valid with { AreaName = "Unknown" };
    var noneArea = valid with { AreaName = "None" };
    var japaneseUnknownArea = valid with { AreaName = "不明" };

    return ShopLocationValidator.IsValid(valid)
        && !ShopLocationValidator.IsValid(missingTerritory)
        && !ShopLocationValidator.IsValid(missingArea)
        && !ShopLocationValidator.IsValid(unknownArea)
        && !ShopLocationValidator.IsValid(noneArea)
        && !ShopLocationValidator.IsValid(japaneseUnknownArea);
});

Test(330, "settings tab is split into shop partial", () =>
{
    var root = FindRepositoryRoot();
    var shopFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.Shop.cs");
    var mainFile = Path.Combine(root, "projects", "XIV-Mini-Util", "Windows", "Components", "SettingsTab.cs");
    var shopText = File.ReadAllText(shopFile);
    var mainText = File.ReadAllText(mainFile);
    return shopText.Contains("private void DrawShopSearchSettings()", StringComparison.Ordinal)
        && shopText.Contains("private void UpdateFilteredTerritories(", StringComparison.Ordinal)
        && !mainText.Contains("private void DrawShopSearchSettings()", StringComparison.Ordinal);
});

Test(434, "shop item identity normalizes variants independently from context menu", () =>
{
    return ShopItemIdentity.Normalize(42) == 42
        && ShopItemIdentity.Normalize(500042) == 42
        && ShopItemIdentity.Normalize(1000042) == 42
        && ShopItemIdentity.Normalize(0) == 0
        && ShopItemIdentity.GetQuality(42) == UniversalisItemQuality.Normal
        && ShopItemIdentity.GetQuality(1000042) == UniversalisItemQuality.HighQuality;
});

Test(435, "colorant item text parser removes markers tags and quantity suffixes", () =>
{
    var normalizedJapanese = ColorantItemTextParser.NormalizeItemLabel(
        "\u0001\u0002テレビン油HQ");
    var normalizedEnglish = ColorantItemTextParser.NormalizeItemLabel(
        "***TurpentineIH");
    var quantityTrimmed = ColorantItemTextParser.TrimItemLabelSuffixes(
        "テレビン油 所持 12");

    return normalizedJapanese == "テレビン油"
        && normalizedEnglish == "Turpentine"
        && quantityTrimmed == "テレビン油"
        && ColorantItemTextParser.IsIgnorableUiText("このカララントは使用できません")
        && !ColorantItemTextParser.IsIgnorableUiText("テレビン油");
});

Test(436, "shop cache build coordinator reuses initial task and advances rebuild generation", () =>
{
    var coordinator = new ShopCacheBuildCoordinator();
    var firstGeneration = 0;
    var rebuildGeneration = 0;
    var first = coordinator.Start(
        rebuild: false,
        (generation, _) =>
        {
            firstGeneration = generation;
            return Task.CompletedTask;
        });
    var reused = coordinator.Start(
        rebuild: false,
        (_, _) => Task.FromException(new InvalidOperationException("must not run")));
    first.GetAwaiter().GetResult();
    var rebuilt = coordinator.Start(
        rebuild: true,
        (generation, _) =>
        {
            rebuildGeneration = generation;
            return Task.CompletedTask;
        });

    rebuilt.GetAwaiter().GetResult();
    return ReferenceEquals(first, reused)
        && firstGeneration == 1
        && rebuildGeneration == 2
        && coordinator.Generation == 2
        && coordinator.IsCurrent(2)
        && !coordinator.IsCurrent(1);
});

Test(437, "colorant addon text extraction preserves legacy string8 alias handling", () =>
{
    var root = FindRepositoryRoot();
    var resolver = File.ReadAllText(Path.Combine(
        root,
        "projects",
        "XIV-Mini-Util",
        "Services",
        "Shop",
        "ColorantItemResolver.cs"));
    var extractString = ExtractMethodBody(
        resolver,
        "private static unsafe string ExtractString(AtkValue value)");

#pragma warning disable CS0618
    var aliasesConstString =
        FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8
        == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ConstString;
#pragma warning restore CS0618

    return aliasesConstString
        && extractString.Contains(
            "case FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8:",
            StringComparison.Ordinal);
});

    }
}

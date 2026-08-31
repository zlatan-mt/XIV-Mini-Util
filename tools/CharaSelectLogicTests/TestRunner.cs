// Path: tools/CharaSelectLogicTests/TestRunner.cs
// Description: Registers responsibility-specific test groups and preserves execution order
// Reason: Keeps all test names, ordering, and failure output compatible
// RELEVANT FILES: tools/CharaSelectLogicTests/Tests/MateriaRetrievalTests.cs, tools/CharaSelectLogicTests/Program.cs, tools/CharaSelectLogicTests/TestHelpers.cs

internal static partial class TestRunner
{
    public static int Run()
    {
        var tests = new List<LogicTestCase>(439);
        AddConfigurationTests(tests);
        AddCharaSelectTests(tests);
        AddTitleBackgroundQuickCheckTests(tests);
        AddTitleBackgroundSafetyTests(tests);
        AddTitleBackgroundUiContractTests(tests);
        AddShopTests(tests);
        AddSubmarineTests(tests);
        AddMateriaRetrievalTests(tests);

        var context = new TestContext();
        foreach (var test in tests.OrderBy(test => test.Order))
        {
            context.Run(test);
        }

        return context.Complete();
    }
}

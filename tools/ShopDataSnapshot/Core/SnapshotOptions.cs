namespace ShopDataSnapshot.Core;

public sealed record SnapshotOptions(
    string GamePath,
    string Language,
    string OutputPath,
    bool ShowHelp)
{
    public const string DefaultGamePath = @"D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn";
    public const string DefaultLanguage = "ja";
    public const string DefaultOutputPath = @"artifacts\shop-snapshot\shop-snapshot.json";

    public static string HelpText =>
        """
        Usage:
          dotnet run --project tools/ShopDataSnapshot -- [options]

        Options:
          --game-path <path>  FFXIV install path. Default: D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn
          --language <lang>   Excel language: ja, en, de, fr, zh, zht, ko. Default: ja
          --out <path>        Output JSON path. Default: artifacts\shop-snapshot\shop-snapshot.json
          --help              Show help.
        """;

    public static SnapshotOptions Parse(IReadOnlyList<string> args)
    {
        var gamePath = DefaultGamePath;
        var language = DefaultLanguage;
        var outputPath = DefaultOutputPath;
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
                case "--game-path":
                    gamePath = ReadValue(args, ref i, arg);
                    break;
                case "--language":
                    language = ReadValue(args, ref i, arg);
                    break;
                case "--out":
                    outputPath = ReadValue(args, ref i, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new SnapshotOptions(gamePath, language, outputPath, showHelp);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}

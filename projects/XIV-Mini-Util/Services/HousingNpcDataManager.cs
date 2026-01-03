// Path: projects/XIV-Mini-Util/Services/HousingNpcDataManager.cs
// Description: ハウジングNPC用のマスターデータを読み込み検証する
// Reason: ゲームデータ取得失敗時のフォールバックを安全に運用するため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Plugin.cs, projects/XIV-Mini-Util/Resources/housing_npc_master.json
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services;

public sealed class HousingNpcDataManager
{
    private const string ResourceLogicalName = "XivMiniUtil.Resources.housing_npc_master.json";
    private const string ConfigFileName = "housing_npc_master.json";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;
    private readonly object _lock = new();

    private bool _loaded;
    private HousingNpcDataSnapshot _snapshot = HousingNpcDataSnapshot.Empty;

    public HousingNpcDataManager(
        IDalamudPluginInterface pluginInterface,
        IDataManager dataManager,
        IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _dataManager = dataManager;
        _pluginLog = pluginLog;
    }

    public IReadOnlyList<uint> GetItems(HousingNpcType npcType)
    {
        EnsureLoaded();
        return _snapshot.Items.TryGetValue(npcType, out var list)
            ? list
            : Array.Empty<uint>();
    }

    public HousingNpcDiagnostics GetDiagnostics()
    {
        EnsureLoaded();
        return _snapshot.ToDiagnostics();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }

            _snapshot = LoadInternal();
            _loaded = true;
        }
    }

    private HousingNpcDataSnapshot LoadInternal()
    {
        var configPath = Path.Combine(_pluginInterface.ConfigDirectory.FullName, ConfigFileName);
        var embeddedJson = ReadEmbeddedJson();
        var itemSheet = _dataManager.GetExcelSheet<Item>();

        if (itemSheet == null)
        {
            _pluginLog.Error("Housing NPCマスター: Itemシートの取得に失敗しました。");
            return HousingNpcDataSnapshot.Empty;
        }

        // Config優先、失敗時はEmbeddedにフォールバックする。
        if (File.Exists(configPath))
        {
            var configJson = TryReadFile(configPath);
            if (!string.IsNullOrWhiteSpace(configJson))
            {
                if (TryParseMaster(configJson, out var master, out var error))
                {
                    var snapshot = BuildSnapshot(master, HousingNpcDataSource.Config, itemSheet);
                    if (snapshot.TotalItems > 0)
                    {
                        LogSummary(snapshot);
                        return snapshot;
                    }

                    _pluginLog.Warning("Housing NPCマスター: Configファイルが空のためEmbeddedにフォールバックします。");
                }
                else
                {
                    _pluginLog.Warning($"Housing NPCマスター: Config解析失敗。Embeddedにフォールバックします。詳細: {error}");
                }
            }
            else
            {
                _pluginLog.Warning("Housing NPCマスター: Config読み込み失敗。Embeddedにフォールバックします。");
            }
        }

        if (string.IsNullOrWhiteSpace(embeddedJson))
        {
            _pluginLog.Error("Housing NPCマスター: 埋め込みリソースの読み込みに失敗しました。");
            return HousingNpcDataSnapshot.Empty;
        }

        if (!TryParseMaster(embeddedJson, out var embeddedMaster, out var embeddedError))
        {
            _pluginLog.Error($"Housing NPCマスター: 埋め込みJSON解析失敗: {embeddedError}");
            return HousingNpcDataSnapshot.Empty;
        }

        var embeddedSnapshot = BuildSnapshot(embeddedMaster, HousingNpcDataSource.Embedded, itemSheet);

        if (!File.Exists(configPath))
        {
            TryWriteInitialConfig(configPath, embeddedJson);
        }

        LogSummary(embeddedSnapshot);
        return embeddedSnapshot;
    }

    private static bool TryParseMaster(string json, out HousingNpcMaster master, out string error)
    {
        master = new HousingNpcMaster();
        error = string.Empty;

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            var parsed = JsonSerializer.Deserialize<HousingNpcMaster>(json, options);
            if (parsed == null)
            {
                error = "JSONの読み込み結果が空です。";
                return false;
            }

            master = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private HousingNpcDataSnapshot BuildSnapshot(
        HousingNpcMaster master,
        HousingNpcDataSource source,
        Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var items = new Dictionary<HousingNpcType, List<uint>>();
        var excluded = new Dictionary<string, List<uint>>(StringComparer.Ordinal);
        var npcTypeMap = BuildNpcTypeMap();

        if (master.NpcTypes != null)
        {
            foreach (var (key, value) in master.NpcTypes)
            {
                if (!npcTypeMap.TryGetValue(key, out var npcType))
                {
                    AddExcluded(excluded, "UnknownNpcType", value);
                    continue;
                }

                // 強制除外・存在確認・重複排除をここで一括で実施する。
                var list = new List<uint>();
                var seen = new HashSet<uint>();

                foreach (var id in value ?? Array.Empty<uint>())
                {
                    if (!seen.Add(id))
                    {
                        AddExcluded(excluded, "Duplicate", id);
                        continue;
                    }

                    if (id == 0)
                    {
                        AddExcluded(excluded, "InvalidId", id);
                        continue;
                    }

                    if (IsForcedExcluded(id))
                    {
                        AddExcluded(excluded, "Forced Exclusion", id);
                        continue;
                    }

                    var item = itemSheet.GetRow(id);
                    if (item.RowId == 0)
                    {
                        AddExcluded(excluded, "MissingItem", id);
                        continue;
                    }

                    var name = item.Name.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        AddExcluded(excluded, "MissingName", id);
                        continue;
                    }

                    list.Add(id);
                }

                items[npcType] = list;
            }
        }

        EnsureNpcTypeKeys(items);
        return new HousingNpcDataSnapshot(master.Version, source, items, excluded);
    }

    private static Dictionary<string, HousingNpcType> BuildNpcTypeMap()
    {
        return new Dictionary<string, HousingNpcType>(StringComparer.Ordinal)
        {
            ["MaterialSupplier"] = HousingNpcType.MaterialSupplier,
            ["Junkmonger"] = HousingNpcType.Junkmonger,
        };
    }

    private static void EnsureNpcTypeKeys(Dictionary<HousingNpcType, List<uint>> items)
    {
        if (!items.ContainsKey(HousingNpcType.MaterialSupplier))
        {
            items[HousingNpcType.MaterialSupplier] = new List<uint>();
        }

        if (!items.ContainsKey(HousingNpcType.Junkmonger))
        {
            items[HousingNpcType.Junkmonger] = new List<uint>();
        }
    }

    private static bool IsForcedExcluded(uint itemId)
    {
        return itemId is >= 2 and <= 19;
    }

    private static void AddExcluded(Dictionary<string, List<uint>> excluded, string reason, uint itemId)
    {
        if (!excluded.TryGetValue(reason, out var list))
        {
            list = new List<uint>();
            excluded[reason] = list;
        }

        list.Add(itemId);
    }

    private static void AddExcluded(Dictionary<string, List<uint>> excluded, string reason, IEnumerable<uint>? itemIds)
    {
        if (itemIds == null)
        {
            return;
        }

        foreach (var id in itemIds)
        {
            AddExcluded(excluded, reason, id);
        }
    }

    private string ReadEmbeddedJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceLogicalName);
        if (stream == null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string TryReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _pluginLog.Warning($"Housing NPCマスター: ファイル読み込み失敗 {path} - {ex.Message}");
            return string.Empty;
        }
    }

    private void TryWriteInitialConfig(string path, string contents)
    {
        try
        {
            Directory.CreateDirectory(_pluginInterface.ConfigDirectory.FullName);
            File.WriteAllText(path, contents, Encoding.UTF8);
            _pluginLog.Information($"Housing NPCマスター: 初回Config出力 {path}");
        }
        catch (Exception ex)
        {
            _pluginLog.Warning($"Housing NPCマスター: 初回Config出力失敗 {path} - {ex.Message}");
        }
    }

    private void LogSummary(HousingNpcDataSnapshot snapshot)
    {
        var materialCount = snapshot.Items.TryGetValue(HousingNpcType.MaterialSupplier, out var material)
            ? material.Count
            : 0;
        var junkCount = snapshot.Items.TryGetValue(HousingNpcType.Junkmonger, out var junk)
            ? junk.Count
            : 0;

        _pluginLog.Information($"Housing NPCマスター読み込み: Source={snapshot.Source} Version={snapshot.Version} Material={materialCount} Junkmonger={junkCount} Excluded={snapshot.TotalExcluded}");
    }

    public sealed record HousingNpcDiagnostics(
        int Version,
        string Source,
        IReadOnlyDictionary<HousingNpcType, int> ValidCounts,
        IReadOnlyDictionary<string, List<uint>> ExcludedByReason,
        int TotalExcluded);

    private sealed record HousingNpcDataSnapshot(
        int Version,
        HousingNpcDataSource Source,
        Dictionary<HousingNpcType, List<uint>> Items,
        Dictionary<string, List<uint>> ExcludedByReason)
    {
        public static HousingNpcDataSnapshot Empty { get; } = new(
            0,
            HousingNpcDataSource.Embedded,
            new Dictionary<HousingNpcType, List<uint>>(),
            new Dictionary<string, List<uint>>(StringComparer.Ordinal));

        public int TotalItems => Items.Values.Sum(list => list.Count);
        public int TotalExcluded => ExcludedByReason.Values.Sum(list => list.Count);

        public HousingNpcDiagnostics ToDiagnostics()
        {
            var counts = new Dictionary<HousingNpcType, int>
            {
                [HousingNpcType.MaterialSupplier] = Items.TryGetValue(HousingNpcType.MaterialSupplier, out var material) ? material.Count : 0,
                [HousingNpcType.Junkmonger] = Items.TryGetValue(HousingNpcType.Junkmonger, out var junk) ? junk.Count : 0,
            };

            return new HousingNpcDiagnostics(
                Version,
                Source.ToString(),
                counts,
                ExcludedByReason,
                TotalExcluded);
        }
    }

    private enum HousingNpcDataSource
    {
        Config,
        Embedded,
    }

    private sealed class HousingNpcMaster
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, uint[]>? NpcTypes { get; set; }
    }
}

// Path: projects/XIV-Mini-Util/Services/SubmarineDataStorage.cs
using Dalamud.Plugin;
using System.Text.Json;
using XivMiniUtil.Models.Submarine;

namespace XivMiniUtil.Services;

public class SubmarineDataStorage
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly string _storagePath;
    private readonly object _lock = new();

    private Dictionary<ulong, CharacterSubmarines> _cache = new();
    private bool _isDirty = false;
    private DateTime _lastDirtyTime = DateTime.MinValue;
    private const int SaveDebounceSeconds = 30;

    public SubmarineDataStorage(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        _storagePath = Path.Combine(_pluginInterface.ConfigDirectory.FullName, "submarine_data.json");
        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_storagePath))
            {
                _cache = new Dictionary<ulong, CharacterSubmarines>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_storagePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _cache = new Dictionary<ulong, CharacterSubmarines>();
                    return;
                }

                _cache = JsonSerializer.Deserialize<Dictionary<ulong, CharacterSubmarines>>(json) 
                         ?? new Dictionary<ulong, CharacterSubmarines>();
            }
            catch
            {
                // エラー時は空で初期化（ログ出力推奨）
                _cache = new Dictionary<ulong, CharacterSubmarines>();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
                _isDirty = false;
            }
            catch
            {
                // 保存失敗時は次回リトライ
            }
        }
    }

    public void Update(ulong contentId, string characterName, List<SubmarineData> submarines)
    {
        lock (_lock)
        {
            _cache[contentId] = new CharacterSubmarines
            {
                CharacterName = characterName,
                Submarines = submarines
            };
            _isDirty = true;
            _lastDirtyTime = DateTime.UtcNow;
        }
    }
    
    // 通知時刻のみ更新する場合など
    public void UpdateSubmarine(ulong contentId, SubmarineData updatedSubmarine)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(contentId, out var charInfo))
            {
                var index = charInfo.Submarines.FindIndex(s => s.Name == updatedSubmarine.Name);
                if (index >= 0)
                {
                    charInfo.Submarines[index] = updatedSubmarine;
                    _isDirty = true;
                    _lastDirtyTime = DateTime.UtcNow;
                }
            }
        }
    }

    public IReadOnlyDictionary<ulong, CharacterSubmarines> GetAll()
    {
        lock (_lock)
        {
            // ディープコピーまではしないが、参照渡し
            return new Dictionary<ulong, CharacterSubmarines>(_cache);
        }
    }

    public void CheckAndSaveIfNeeded()
    {
        if (!_isDirty) return;

        if ((DateTime.UtcNow - _lastDirtyTime).TotalSeconds >= SaveDebounceSeconds)
        {
            Save();
        }
    }
}

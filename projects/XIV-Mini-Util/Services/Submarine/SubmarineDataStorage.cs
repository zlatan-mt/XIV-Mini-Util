// Path: projects/XIV-Mini-Util/Services/SubmarineDataStorage.cs
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XivMiniUtil.Models.Submarine;

namespace XivMiniUtil.Services.Submarine;

public sealed class SubmarineDataStorage : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _storagePath;
    private readonly string _tempStoragePath;
    private readonly IPluginLog _pluginLog;
    private readonly object _lock = new();
    private readonly Mutex _fileMutex;

    private Dictionary<ulong, CharacterSubmarines> _cache = new();
    private readonly HashSet<ulong> _dirtyContentIds = new();
    private DateTime _lastDirtyTime = DateTime.MinValue;
    private DateTime _lastLoadedWriteTimeUtc = DateTime.MinValue;

    private const int SaveDebounceSeconds = 30;

    public SubmarineDataStorage(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
        _storagePath = Path.Combine(pluginInterface.ConfigDirectory.FullName, "submarine_data.json");
        _tempStoragePath = $"{_storagePath}.{Environment.ProcessId}.tmp";
        _fileMutex = new Mutex(false, BuildMutexName(_storagePath));
        Load();
    }

    public void Dispose()
    {
        _fileMutex.Dispose();
    }

    public void Load()
    {
        lock (_lock)
        {
            LoadLatestCacheUnsafe(force: true);
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            if (_dirtyContentIds.Count == 0)
            {
                return;
            }

            try
            {
                var mergedCache = ExecuteWithFileLock(() =>
                {
                    var cache = BuildMergedCacheUnsafe();
                    WriteCacheToDiskUnsafe(cache);
                    return cache;
                });
                _cache = mergedCache;
                _dirtyContentIds.Clear();
                _lastDirtyTime = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _pluginLog.Warning(ex, "Failed to save submarine data. The current process will retry later.");
            }
        }
    }

    public void Update(ulong contentId, string characterName, List<SubmarineData> submarines)
    {
        lock (_lock)
        {
            LoadLatestCacheUnsafe();

            _cache[contentId] = new CharacterSubmarines
            {
                CharacterName = characterName,
                Submarines = submarines.Select(CloneSubmarineData).ToList(),
            };
            MarkDirty(contentId);
        }
    }

    public void UpdateSubmarine(ulong contentId, SubmarineData updatedSubmarine)
    {
        lock (_lock)
        {
            LoadLatestCacheUnsafe();

            if (_cache.TryGetValue(contentId, out var charInfo))
            {
                var index = charInfo.Submarines.FindIndex(submarine => submarine.Name == updatedSubmarine.Name);
                if (index >= 0)
                {
                    charInfo.Submarines[index] = CloneSubmarineData(updatedSubmarine);
                    MarkDirty(contentId);
                }
            }
        }
    }

    public IReadOnlyDictionary<ulong, CharacterSubmarines> GetAll()
    {
        lock (_lock)
        {
            LoadLatestCacheUnsafe();
            return CloneCache(_cache);
        }
    }

    public CharacterSubmarines? Get(ulong contentId)
    {
        lock (_lock)
        {
            LoadLatestCacheUnsafe();
            return _cache.TryGetValue(contentId, out var charInfo)
                ? CloneCharacterSubmarines(charInfo)
                : null;
        }
    }

    public void CheckAndSaveIfNeeded()
    {
        if (_dirtyContentIds.Count == 0)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastDirtyTime).TotalSeconds >= SaveDebounceSeconds)
        {
            Save();
        }
    }

    private void LoadLatestCacheUnsafe(bool force = false)
    {
        var currentWriteTimeUtc = GetStorageWriteTimeUtc();
        if (!force && currentWriteTimeUtc <= _lastLoadedWriteTimeUtc)
        {
            return;
        }

        try
        {
            _cache = ExecuteWithFileLock(BuildMergedCacheUnsafe);
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "Failed to reload submarine data from disk. Keeping the in-memory cache.");
        }
    }

    private Dictionary<ulong, CharacterSubmarines> BuildMergedCacheUnsafe()
    {
        var mergedCache = ReadCacheFromDiskUnsafe();
        MergeDirtyEntriesUnsafe(mergedCache);
        return mergedCache;
    }

    private void MergeDirtyEntriesUnsafe(Dictionary<ulong, CharacterSubmarines> mergedCache)
    {
        foreach (var contentId in _dirtyContentIds)
        {
            if (_cache.TryGetValue(contentId, out var charInfo))
            {
                mergedCache[contentId] = CloneCharacterSubmarines(charInfo);
            }
        }
    }

    private Dictionary<ulong, CharacterSubmarines> ReadCacheFromDiskUnsafe()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                _lastLoadedWriteTimeUtc = DateTime.MinValue;
                return new Dictionary<ulong, CharacterSubmarines>();
            }

            var json = File.ReadAllText(_storagePath);
            _lastLoadedWriteTimeUtc = GetStorageWriteTimeUtc();

            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<ulong, CharacterSubmarines>();
            }

            var diskCache = JsonSerializer.Deserialize<Dictionary<ulong, CharacterSubmarines>>(json)
                ?? new Dictionary<ulong, CharacterSubmarines>();
            return CloneCache(diskCache);
        }
        catch (Exception ex)
        {
            _pluginLog.Warning(ex, "Failed to read submarine data from disk. Using an empty cache instead.");
            _lastLoadedWriteTimeUtc = DateTime.MinValue;
            return new Dictionary<ulong, CharacterSubmarines>();
        }
    }

    private T ExecuteWithFileLock<T>(Func<T> action)
    {
        using var fileLock = AcquireFileLock();
        return action();
    }

    private void WriteCacheToDiskUnsafe(Dictionary<ulong, CharacterSubmarines> cache)
    {
        try
        {
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            File.WriteAllText(_tempStoragePath, json);
            File.Move(_tempStoragePath, _storagePath, true);
            _lastLoadedWriteTimeUtc = GetStorageWriteTimeUtc();
        }
        finally
        {
            if (File.Exists(_tempStoragePath))
            {
                File.Delete(_tempStoragePath);
            }
        }
    }

    private void MarkDirty(ulong contentId)
    {
        _dirtyContentIds.Add(contentId);
        _lastDirtyTime = DateTime.UtcNow;
    }

    private IDisposable AcquireFileLock()
    {
        try
        {
            if (!_fileMutex.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out while waiting for the submarine storage file lock.");
            }
        }
        catch (AbandonedMutexException)
        {
            _pluginLog.Warning("Recovered an abandoned submarine storage file lock.");
        }

        return new MutexScope(_fileMutex);
    }

    private static string BuildMutexName(string storagePath)
    {
        var pathBytes = Encoding.UTF8.GetBytes(storagePath.ToLowerInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(pathBytes));
        return $"Local\\XivMiniUtil.SubmarineStorage.{hash}";
    }

    private DateTime GetStorageWriteTimeUtc()
    {
        return File.Exists(_storagePath)
            ? File.GetLastWriteTimeUtc(_storagePath)
            : DateTime.MinValue;
    }

    private static Dictionary<ulong, CharacterSubmarines> CloneCache(Dictionary<ulong, CharacterSubmarines> source)
    {
        return source.ToDictionary(pair => pair.Key, pair => CloneCharacterSubmarines(pair.Value));
    }

    private static CharacterSubmarines CloneCharacterSubmarines(CharacterSubmarines source)
    {
        return new CharacterSubmarines
        {
            CharacterName = source.CharacterName,
            Submarines = source.Submarines.Select(CloneSubmarineData).ToList(),
        };
    }

    private static SubmarineData CloneSubmarineData(SubmarineData source)
    {
        return new SubmarineData
        {
            Name = source.Name,
            Rank = source.Rank,
            ReturnTime = source.ReturnTime,
            RegisterTime = source.RegisterTime,
            LastNotifiedReturnTime = source.LastNotifiedReturnTime,
            Status = source.Status,
        };
    }

    private sealed class MutexScope : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        public MutexScope(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _mutex.ReleaseMutex();
            _disposed = true;
        }
    }
}

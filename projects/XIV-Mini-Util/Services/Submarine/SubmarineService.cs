// Path: projects/XIV-Mini-Util/Services/SubmarineService.cs
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Text;
using XivMiniUtil.Models.Submarine;
using XivMiniUtil.Services.Notification;

namespace XivMiniUtil.Services.Submarine;

public sealed unsafe class SubmarineService : IDisposable
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPlayerState _playerState;
    private readonly IPluginLog _pluginLog;
    private readonly Configuration _configuration;
    private readonly SubmarineDataStorage _storage;
    private readonly DiscordService _discordService;

    private DateTime _lastPollTime = DateTime.MinValue;
    private const double PollIntervalSeconds = 5.0;

    // 初回ロード済みキャラクターを追跡（ログイン時の誤通知防止）
    private readonly HashSet<ulong> _processedInitialLoad = new();

    public SubmarineService(
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IPluginLog pluginLog,
        Configuration configuration,
        SubmarineDataStorage storage,
        DiscordService discordService)
    {
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _playerState = playerState;
        _pluginLog = pluginLog;
        _configuration = configuration;
        _storage = storage;
        _discordService = discordService;

        _framework.Update += OnUpdate;
        _clientState.Logout += OnLogout;
        _clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!_configuration.SubmarineTrackerEnabled) return;

        var now = DateTime.UtcNow;

        // 1. データ保存のデバウンスチェック
        _storage.CheckAndSaveIfNeeded();

        // 2. ポーリング間隔チェック
        if ((now - _lastPollTime).TotalSeconds < PollIntervalSeconds) return;
        _lastPollTime = now;

        // 3. メモリ読み取り
        var data = TryReadSubmarineData(now);
        if (data == null) return;

        var (cid, characterName, world, currentSubs) = data.Value;

        // 4. 出航通知チェック（エラーハンドリング付き）
        try
        {
            CheckAndNotifyDispatch(cid, characterName, world, currentSubs);
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Failed to check/notify dispatch.");
        }

        // 5. ストレージ更新（通知チェック後に実行）
        _storage.Update(cid, characterName, currentSubs);
    }

    private void OnLogout(int type, int code)
    {
        // キャラクター切り替え時に初回ロードフラグをクリア
        _processedInitialLoad.Clear();
        _storage.Save();
    }

    private void OnTerritoryChanged(ushort obj)
    {
        _storage.Save();
    }

    /// <summary>
    /// 潜水艦データをメモリから読み取る
    /// </summary>
    /// <returns>読み取り成功時: (ContentId, CharacterName, World, Submarines), 失敗時: null</returns>
    private (ulong ContentId, string CharacterName, string World, List<SubmarineData> Submarines)? TryReadSubmarineData(DateTime nowUtc)
    {
        // ログインチェック
        var player = _objectTable.LocalPlayer;
        if (!_clientState.IsLoggedIn || player == null) return null;

        var housingManager = HousingManager.Instance();
        if (housingManager == null) return null;

        var workshopTerritory = housingManager->WorkshopTerritory;
        if (workshopTerritory == null) return null;

        // 潜水艦データへのポインタ (HousingWorkshopSubmersibleData)
        var submersible = &workshopTerritory->Submersible;

        // _data は internal なのでポインタキャストでアクセス
        // HousingWorkshopSubmersibleData の先頭 (Offset 0) が _data (FixedSizeArray4<HousingWorkshopSubmersibleSubData>)
        var subDataPtr = (HousingWorkshopSubmersibleSubData*)submersible;

        var cid = _playerState.ContentId;
        var characterName = player.Name.TextValue;
        var world = player.HomeWorld.Value.Name.ToString();
        var submarines = new List<SubmarineData>();

        // 既存データを取得（LastNotifiedReturnTime引き継ぎ用）
        var existingData = _storage.Get(cid);

        for (int i = 0; i < 4; i++)
        {
            // ポインタ演算でi番目の要素にアクセス
            var subData = subDataPtr[i];

            // ランク0は未登録とみなす
            if (subData.RankId == 0) continue;

            // 名前取得
            // _name は internal (Offset 0x22, FixedSizeArray20<byte>)
            // subDataのアドレス + 0x22
            var namePtr = (byte*)&subData + 0x22;

            var nameBytes = new byte[20];
            for (int j = 0; j < 20; j++)
            {
                nameBytes[j] = namePtr[j];
            }
            // null終端まで
            int len = 0;
            while (len < 20 && nameBytes[len] != 0) len++;
            var name = Encoding.UTF8.GetString(nameBytes, 0, len);

            if (string.IsNullOrEmpty(name)) continue;

            var registerTime = DateTime.UnixEpoch.AddSeconds(subData.RegisterTime);
            var returnTime = DateTime.UnixEpoch.AddSeconds(subData.ReturnTime);

            var status = SubmarineStatus.Unknown;
            if (returnTime <= DateTime.UnixEpoch.AddSeconds(1)) // 0 or very small
            {
                status = SubmarineStatus.Completed; // or idle?
                // 帰還時間が0の場合は、未出港か完了後回収済み
            }
            else if (nowUtc < returnTime)
            {
                status = SubmarineStatus.Exploring;
            }
            else
            {
                status = SubmarineStatus.Completed;
            }

            var submarine = new SubmarineData
            {
                Name = name,
                Rank = subData.RankId,
                RegisterTime = registerTime,
                ReturnTime = returnTime,
                Status = status
            };

            // 既存データから LastNotifiedReturnTime を引き継ぐ
            var existing = existingData?.Submarines.FirstOrDefault(s => s.Name == name);
            if (existing != null)
            {
                submarine.LastNotifiedReturnTime = existing.LastNotifiedReturnTime;
            }

            submarines.Add(submarine);
        }

        return (cid, characterName, world, submarines);
    }

    /// <summary>
    /// 全艦出航時の通知をチェック・送信する
    /// </summary>
    private void CheckAndNotifyDispatch(ulong cid, string characterName, string world, List<SubmarineData> currentSubs)
    {
        if (!_configuration.SubmarineNotificationEnabled) return;

        // 1. 初回ロードチェック（ログイン直後の誤通知防止）
        if (!_processedInitialLoad.Contains(cid))
        {
            _processedInitialLoad.Add(cid);
            _pluginLog.Debug($"[Submarine] Initial load for CID {cid}, skipping notification check.");
            return;
        }

        // 2. 出航状態の比較
        int currentExploring = currentSubs.Count(s => s.Status == SubmarineStatus.Exploring);
        int totalSubs = currentSubs.Count;

        var prevData = _storage.Get(cid);
        int prevExploring = prevData?.Submarines.Count(s => s.Status == SubmarineStatus.Exploring) ?? 0;

        // 3. トリガー条件: 全艦出航（0隻除外）、かつ前回は全艦出航ではなかった
        if (totalSubs > 0 && currentExploring == totalSubs && prevExploring < totalSubs)
        {
            _pluginLog.Info($"[Submarine] All {totalSubs} submarines dispatched for {characterName}@{world}. Sending notification.");

            // 探索中の潜水艦のみを通知（全艦のはずだが念のためフィルタ）
            var exploringSubs = currentSubs.Where(s => s.Status == SubmarineStatus.Exploring).ToList();
            _ = _discordService.SendDispatchNotificationAsync(characterName, world, exploringSubs);
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _clientState.Logout -= OnLogout;
        _clientState.TerritoryChanged -= OnTerritoryChanged;

        _storage.Save();
    }
}

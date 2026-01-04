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
        TryReadSubmarineData(now);

        // 4. 通知チェック
        CheckNotifications(now);
    }

    private void OnLogout(int type, int code)
    {
        _storage.Save();
    }

    private void OnTerritoryChanged(ushort obj)
    {
        _storage.Save();
    }

    private void TryReadSubmarineData(DateTime nowUtc)
    {
        // ログインチェック
        if (!_clientState.IsLoggedIn || _objectTable.LocalPlayer == null) return;
        
        var housingManager = HousingManager.Instance();
        if (housingManager == null) return;

        var workshopTerritory = housingManager->WorkshopTerritory;
        if (workshopTerritory == null) return;

        // 潜水艦データへのポインタ (HousingWorkshopSubmersibleData)
        var submersible = &workshopTerritory->Submersible;
        
        // _data は internal なのでポインタキャストでアクセス
        // HousingWorkshopSubmersibleData の先頭 (Offset 0) が _data (FixedSizeArray4<HousingWorkshopSubmersibleSubData>)
        var subDataPtr = (HousingWorkshopSubmersibleSubData*)submersible;

        var cid = _playerState.ContentId;
        var characterName = _objectTable.LocalPlayer!.Name.TextValue;
        var submarines = new List<SubmarineData>();

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
                // RegisterTimeが有効なら回収済み待機中かもしれないが、
                // ここでは単純にReturnTimeのみで判断
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
            var existing = _storage.GetAll().TryGetValue(cid, out var existingChar) 
                ? existingChar.Submarines.FirstOrDefault(s => s.Name == name) 
                : null;
            
            if (existing != null)
            {
                submarine.LastNotifiedReturnTime = existing.LastNotifiedReturnTime;
            }

            submarines.Add(submarine);
        }

        // 0隻でもリスト更新（空リストとして保存され、ハウス入室済みフラグ代わりになる）
        _storage.Update(cid, characterName, submarines);
    }

    private void CheckNotifications(DateTime nowUtc)
    {
        if (!_configuration.SubmarineNotificationEnabled) return;

        var allData = _storage.GetAll();
        foreach (var kvp in allData)
        {
            var cid = kvp.Key;
            var charInfo = kvp.Value;
            var characterName = charInfo.CharacterName;
            
            var notifiedSubmarines = new List<string>();
            bool needsSave = false;

            foreach (var sub in charInfo.Submarines)
            {
                // 帰還済み かつ まだ通知していない
                // ReturnTimeが有効(UnixEpoch+1以上)であること
                if (sub.ReturnTime > DateTime.UnixEpoch.AddSeconds(1) &&
                    sub.ReturnTime <= nowUtc &&
                    sub.LastNotifiedReturnTime != sub.ReturnTime)
                {
                    notifiedSubmarines.Add(sub.Name);
                    sub.LastNotifiedReturnTime = sub.ReturnTime;
                    needsSave = true;
                    
                    // ストレージ内のオブジェクトを更新（参照渡しなら不要だが念のためUpdateSubmarineを呼ぶか、
                    // ここではメモリ上のオブジェクトを書き換えて一括Updateする）
                }
            }

            if (notifiedSubmarines.Count > 0)
            {
                // 通知送信 (Fire and forget)
                // ローカル時刻に変換して通知
                var localReturnTime = notifiedSubmarines.Select(n => charInfo.Submarines.First(s => s.Name == n).ReturnTime).Max().ToLocalTime();
                
                _ = _discordService.SendVoyageCompletionAsync(characterName, notifiedSubmarines, localReturnTime);

                if (needsSave)
                {
                    _storage.Update(cid, characterName, charInfo.Submarines);
                }
            }
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

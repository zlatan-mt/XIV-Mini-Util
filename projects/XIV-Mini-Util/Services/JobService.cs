// Path: projects/XIV-Mini-Util/Services/JobService.cs
// Description: 現在のジョブ情報を取得し条件判定を提供する
// Reason: 分解の実行条件を一箇所で判断できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Models/DomainModels.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services;

public sealed class JobService
{
    private static readonly HashSet<uint> CrafterJobIds = new()
    {
        8, 9, 10, 11, 12, 13, 14, 15,
    };

    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _pluginLog;

    public JobService(IObjectTable objectTable, IPluginLog pluginLog)
    {
        _objectTable = objectTable;
        _pluginLog = pluginLog;
    }

    public uint? CurrentClassJobId
    {
        get
        {
            var player = _objectTable.LocalPlayer as PlayerCharacter;
            return player?.ClassJob?.Id;
        }
    }

    public bool IsCrafter => CurrentClassJobId.HasValue && CrafterJobIds.Contains(CurrentClassJobId.Value);

    public bool IsBattleJob => CurrentClassJobId.HasValue && !CrafterJobIds.Contains(CurrentClassJobId.Value);

    public bool CheckJobCondition(JobCondition condition)
    {
        if (!CurrentClassJobId.HasValue)
        {
            _pluginLog.Warning("ジョブ情報が取得できないため条件チェックを失敗扱いにします。");
            return false;
        }

        return condition switch
        {
            JobCondition.Any => true,
            JobCondition.CrafterOnly => IsCrafter,
            JobCondition.BattleOnly => IsBattleJob,
            _ => false,
        };
    }
}

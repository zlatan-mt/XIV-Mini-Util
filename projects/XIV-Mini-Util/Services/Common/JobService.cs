// Path: projects/XIV-Mini-Util/Services/JobService.cs
// Description: 現在のジョブ情報を取得し条件判定を提供する
// Reason: 分解の実行条件を一箇所で判断できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Models/Common/JobCondition.cs, projects/XIV-Mini-Util/Services/Desynth/DesynthService.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
using Dalamud.Plugin.Services;
using XivMiniUtil;

namespace XivMiniUtil.Services.Common;

public sealed class JobService
{
    private static readonly HashSet<uint> CrafterJobIds = new()
    {
        8, 9, 10, 11, 12, 13, 14, 15,
    };

    private readonly IPlayerState _playerState;
    private readonly IPluginLog _pluginLog;

    public JobService(IPlayerState playerState, IPluginLog pluginLog)
    {
        _playerState = playerState;
        _pluginLog = pluginLog;
    }

    public uint? CurrentClassJobId
    {
        get
        {
            if (!_playerState.IsLoaded)
            {
                return null;
            }

            var classJob = _playerState.ClassJob;
            return classJob.RowId == 0 ? null : classJob.RowId;
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

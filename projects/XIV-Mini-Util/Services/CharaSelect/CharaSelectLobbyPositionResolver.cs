// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectLobbyPositionResolver.cs
// Description: キャラ選択背景用のLobby position候補を解決する
// Reason: UpdateLoginPosition/OpenLoginWaitDialogへ渡す値をゲーム非依存で検証するため
namespace XivMiniUtil.Services.CharaSelect;

internal readonly record struct CharaSelectLobbyCandidate(uint RowId, uint Type, uint Param, uint Link);

internal static class CharaSelectLobbyPositionResolver
{
    public static int ResolveByTerritory(
        IEnumerable<CharaSelectLobbyCandidate> candidates,
        ushort territoryTypeId,
        int fallbackPosition)
    {
        if (territoryTypeId == 0)
        {
            return fallbackPosition;
        }

        var direct = FindBest(candidates, territoryTypeId, candidate => candidate.Param == territoryTypeId);
        if (direct.HasValue)
        {
            return direct.Value;
        }

        var linked = FindBest(candidates, territoryTypeId, candidate => candidate.Link == territoryTypeId);
        return linked ?? fallbackPosition;
    }

    private static int? FindBest(
        IEnumerable<CharaSelectLobbyCandidate> candidates,
        ushort territoryTypeId,
        Func<CharaSelectLobbyCandidate, bool> predicate)
    {
        var best = default(CharaSelectLobbyCandidate);
        var hasBest = false;

        foreach (var candidate in candidates)
        {
            if (candidate.RowId > int.MaxValue || !predicate(candidate))
            {
                continue;
            }

            if (!hasBest
                || Score(candidate, territoryTypeId) > Score(best, territoryTypeId)
                || (Score(candidate, territoryTypeId) == Score(best, territoryTypeId) && candidate.RowId < best.RowId))
            {
                best = candidate;
                hasBest = true;
            }
        }

        return hasBest ? (int)best.RowId : null;
    }

    private static int Score(CharaSelectLobbyCandidate candidate, ushort territoryTypeId)
    {
        var score = 0;
        if (candidate.Param == territoryTypeId)
        {
            score += 4;
        }

        if (candidate.Link == territoryTypeId)
        {
            score += 2;
        }

        if (candidate.Type != 0)
        {
            score += 1;
        }

        return score;
    }
}

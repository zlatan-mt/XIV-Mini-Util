// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.Emotes.cs
// Description: CharaSelect のエモート記録・再生を管理する
// Reason: emote replay logic を本体状態管理から分離するため
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    private void SaveExecutedEmote(uint emoteId)
    {
        var contentId = _playerState.ContentId;
        if (contentId == 0)
        {
            return;
        }

        var saveEmoteId = emoteId == ChangePoseEmoteId ? GetChangePoseEmoteId() : emoteId;
        if (saveEmoteId == 0 || !IsRecordableEmote(saveEmoteId))
        {
            return;
        }

        _configuration.CharaSelectLastRecordedEmotes[contentId] = saveEmoteId;
        _configuration.Save();
    }

    private void SelectPreset(Func<Dictionary<ulong, List<uint>>, Dictionary<ulong, int>, ulong, bool> selector)
    {
        var contentId = ActiveContentId;
        if (contentId == 0)
        {
            return;
        }

        if (!selector(_configuration.CharaSelectEmotePresets, _configuration.CharaSelectActiveEmotePresetIndexes, contentId))
        {
            return;
        }

        _configuration.Save();
        ReplayAfterActivePresetChanged();
    }

    private void ReplayAfterActivePresetChanged()
    {
        ClearReplayState();
        if (!_clientState.IsLoggedIn)
        {
            ReplaySelectedEmote();
        }
    }

    private bool TryGetLastRecordedEmote(ulong contentId, out uint emoteId)
    {
        if (contentId != 0
            && _configuration.CharaSelectLastRecordedEmotes.TryGetValue(contentId, out emoteId)
            && emoteId != 0)
        {
            return true;
        }

        emoteId = 0;
        return false;
    }

    private string GetEmoteDisplayName(uint emoteId)
    {
        try
        {
            var emoteSheet = _dataManager.GetExcelSheet<Emote>();
            var emote = emoteSheet.GetRow(emoteId);
            var name = emote.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"不明 (ID: {emoteId})" : name;
        }
        catch
        {
            return $"不明 (ID: {emoteId})";
        }
    }

    private ushort ResolveCharacterVoiceId(CharaSelectCharacterEntry* entry)
    {
        var clientData = entry->ClientSelectData;
        if (_configuration.CharaSelectVoiceIds.TryGetValue(entry->ContentId, out var savedVoiceId) && savedVoiceId != 0)
        {
            return savedVoiceId;
        }

        try
        {
            var charaMakeTypeSheet = _dataManager.GetExcelSheet<CharaMakeType>();
            foreach (var charaMakeType in charaMakeTypeSheet)
            {
                if (charaMakeType.Race.RowId != clientData.CustomizeData.Race
                    || charaMakeType.Tribe.RowId != clientData.CustomizeData.Tribe
                    || charaMakeType.Gender != clientData.CustomizeData.Sex)
                {
                    continue;
                }

                return CharaSelectVoiceIdResolver.Resolve(
                    clientData.VoiceId,
                    charaMakeType.VoiceStruct.Count,
                    index => charaMakeType.VoiceStruct[index]);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to resolve CharaSelect voice id.");
        }

        return clientData.VoiceId;
    }

    private void SaveLoggedInVoiceId()
    {
        var contentId = _playerState.ContentId;
        var localPlayer = _objectTable.LocalPlayer;
        if (contentId == 0 || localPlayer == null || localPlayer.Address == nint.Zero)
        {
            return;
        }

        var character = (Character*)localPlayer.Address;
        var voiceId = character->Vfx.VoiceId;
        if (voiceId == 0)
        {
            return;
        }

        if (!_configuration.CharaSelectVoiceIds.TryGetValue(contentId, out var currentVoiceId) || currentVoiceId != voiceId)
        {
            _configuration.CharaSelectVoiceIds[contentId] = voiceId;
            _configuration.Save();
        }
    }

    private uint GetChangePoseEmoteId()
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return 0;
        }

        var poseIndex = playerState->SelectedPoses[0];
        return poseIndex < ChangePoseEmoteIds.Length ? ChangePoseEmoteIds[poseIndex] : 0;
    }

    private bool IsRecordableEmote(uint emoteId)
    {
        if (AlwaysExcludedEmoteIds.Contains(emoteId))
        {
            return false;
        }

        if (!TryGetEmote(emoteId, out var emote))
        {
            return false;
        }

        if (emote.Icon == 0 && emoteId != ChangePoseEmoteId)
        {
            return false;
        }

        var introId = GetTimelineRowId(emote, 1);
        var loopId = GetTimelineRowId(emote, 0);
        return introId != 0 || loopId != 0;
    }

    private bool PlayEmote(uint emoteId)
    {
        var entry = _currentEntry;
        if (!_configuration.CharaSelectEmoteEnabled || entry == null || entry.Character == null)
        {
            return false;
        }

        if (!TryGetEmote(emoteId, out var emote))
        {
            ResetEmoteMode();
            return false;
        }

        var character = entry.Character;
        if (entry.VoiceId != 0)
        {
            CharaSelectCharacterApplier.ApplyVoice(character, entry.VoiceId);
        }

        var introId = GetTimelineRowId(emote, 1);
        var loopId = GetTimelineRowId(emote, 0);

        if (introId == 0 && loopId == 0)
        {
            ResetEmoteMode();
            return false;
        }

        var plan = CharaSelectEmotePlaybackPlanner.Create(
            emote.EmoteMode.RowId,
            emote.EmoteMode.RowId != 0 ? emote.EmoteMode.Value.ConditionMode : (byte)CharacterModes.Normal,
            introId,
            loopId);

        if (!plan.HasTimeline)
        {
            ResetEmoteMode();
            return false;
        }

        character->SetMode(plan.Mode, plan.ModeParam);

        if (plan.IntroTimelineId != 0 && plan.LoopTimelineId != 0)
        {
            character->Timeline.PlayActionTimeline(plan.IntroTimelineId, plan.LoopTimelineId, null);
        }
        else
        {
            character->Timeline.TimelineSequencer.PlayTimeline(plan.LoopTimelineId != 0 ? plan.LoopTimelineId : plan.IntroTimelineId, null);
        }

        _replayTracker.MarkReplayed(entry.ContentId, emoteId, (nint)character);
        return true;
    }

    private void ResetEmoteMode()
    {
        var entry = _currentEntry;
        if (entry == null || entry.Character == null)
        {
            return;
        }

        var character = entry.Character;
        character->SetMode(CharacterModes.Normal, 0);
        character->Timeline.TimelineSequencer.PlayTimeline(IdleTimelineId, null);
    }

    private void CleanupCharaSelect()
    {
        ResetEmoteMode();
        ClearReplayState();
        _currentEntry = null;
    }

    private void ReplaySelectedEmoteIfNeeded(bool force = false)
    {
        var entry = _currentEntry;
        if (entry == null || CurrentSelectedEmoteId is not { } emoteId)
        {
            return;
        }

        if (!_replayTracker.ShouldReplay(entry.ContentId, emoteId, (nint)entry.Character, force))
        {
            return;
        }

        MarkSceneEmoteReplay(PlayEmote(emoteId));
    }

    private void ScheduleDelayedReplay()
    {
        var entry = _currentEntry;
        if (entry == null || CurrentSelectedEmoteId is not { } emoteId)
        {
            return;
        }

        _delayedReplayContentId = entry.ContentId;
        _delayedReplayEmoteId = emoteId;
        _delayedReplayCharacterAddress = (nint)entry.Character;
        _delayedReplayFrames = DelayedReplayFrameDelay;
    }

    private bool IsDelayedReplayPending(ulong contentId, Character* character)
    {
        return _delayedReplayFrames > 0
            && _delayedReplayContentId == contentId
            && _delayedReplayCharacterAddress == (nint)character;
    }

    private void ProcessDelayedReplay()
    {
        if (_delayedReplayFrames <= 0)
        {
            return;
        }

        _delayedReplayFrames--;
        if (_delayedReplayFrames is 45 or 30 or 15)
        {
            // _currentEntry は OnFrameworkUpdate の 10 フレーム毎の poll（UpdateCurrentEntry）でしか
            // 更新されず、ProcessDelayedReplay はその poll より先に呼ばれる。つまり _currentEntry.Character
            // は「schedule 時点からの経過フレーム内に native 側で actor が再生成されていても、次の poll
            // まで古いままの参照」になりうる。schedule 時の値と _currentEntry の値を比較するだけでは
            // 両方とも同じ古い参照であれば一致してしまい検出できないため、書込み直前に canonical resolver
            // （TryResolveCurrentCharaSelectActor）で今の native 状態を解決し直し、その結果と照合する。
            var voiceResolved = TryResolveCurrentCharaSelectActor(out var voiceContext);
            var voiceEntry = _currentEntry;
            if (CharaSelectDelayedReplayGate.ShouldApplyDelayedVoice(
                    voiceResolved && voiceContext.Valid,
                    voiceContext.ContentId,
                    voiceContext.CharacterAddress,
                    _delayedReplayContentId,
                    _delayedReplayCharacterAddress,
                    voiceEntry != null,
                    voiceEntry?.ContentId ?? 0,
                    voiceEntry?.VoiceId ?? 0))
            {
                CharaSelectCharacterApplier.ApplyVoice(
                    (Character*)voiceContext.CharacterAddress,
                    voiceEntry!.VoiceId);
            }
        }

        if (_delayedReplayFrames > 0)
        {
            return;
        }

        // 同じ理由で、最終確認も _currentEntry の値同士の比較だけに留めず、この時点の native 状態を
        // 解決し直してから予約値と照合する（PlayEmote 自体は既存どおり _currentEntry 経由で書く。
        // ここでの再解決は「今 native 側が指す actor が予約時点と同一か」の確認のみに使う）。
        var finalResolved = TryResolveCurrentCharaSelectActor(out var finalContext);
        var finalEntry = _currentEntry;
        var currentSelectedEmoteId = CurrentSelectedEmoteId;
        if (!CharaSelectDelayedReplayGate.ShouldPlayDelayedEmote(
                finalResolved && finalContext.Valid,
                finalContext.ContentId,
                finalContext.CharacterAddress,
                _delayedReplayContentId,
                _delayedReplayCharacterAddress,
                finalEntry != null,
                finalEntry?.ContentId ?? 0,
                currentSelectedEmoteId.HasValue,
                currentSelectedEmoteId ?? 0,
                _delayedReplayEmoteId))
        {
            return;
        }

        MarkSceneEmoteReplay(PlayEmote(_delayedReplayEmoteId));
    }

    private void ClearReplayState()
    {
        _replayTracker.Clear();
        _delayedReplayFrames = 0;
        _delayedReplayContentId = 0;
        _delayedReplayEmoteId = 0;
        _delayedReplayCharacterAddress = nint.Zero;
    }
}


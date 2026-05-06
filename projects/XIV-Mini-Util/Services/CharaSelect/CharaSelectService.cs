// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.cs
// Description: キャラ選択画面のエモート再生と記録状態を管理する
// Reason: unsafe hookとUI設定をPlugin本体から分離するため
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe class CharaSelectService : IDisposable
{
    private const ushort IdleTimelineId = 3;
    private const uint ChangePoseEmoteId = 90;
    private const int DelayedReplayFrameDelay = 60;
    private static readonly uint[] ChangePoseEmoteIds = [91, 92, 93, 107, 108, 218, 219];
    private static readonly HashSet<uint> AlwaysExcludedEmoteIds =
    [
        50, // 座る
        52, // 居眠り
        88, // グループポーズ
    ];

    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IPlayerState _playerState;
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Configuration _configuration;

    private Hook<AgentLobby.Delegates.UpdateCharaSelectDisplay>? _updateCharaSelectDisplayHook;
    private Hook<EmoteManager.Delegates.ExecuteEmote>? _executeEmoteHook;
    private Hook<AgentLobby.Delegates.OpenLoginWaitDialog>? _openLoginWaitDialogHook;
    private CharaSelectCharacterState? _currentEntry;
    private readonly CharaSelectReplayTracker _replayTracker = new();
    private bool _disposed;
    private bool _isRecordingEmote;
    private int _frameworkPollFrame;
    private int _delayedReplayFrames;
    private ulong _delayedReplayContentId;
    private uint _delayedReplayEmoteId;
    private nint _delayedReplayCharacterAddress;
    private IReadOnlyList<string> _lastVoiceDiagnosticLines = [];

    public CharaSelectService(
        IGameInteropProvider gameInteropProvider,
        IFramework framework,
        IClientState clientState,
        IObjectTable objectTable,
        IPlayerState playerState,
        IDataManager dataManager,
        IPluginLog log,
        Configuration configuration)
    {
        _gameInteropProvider = gameInteropProvider;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _playerState = playerState;
        _dataManager = dataManager;
        _log = log;
        _configuration = configuration;

        InitializeHooks();
        _framework.Update += OnFrameworkUpdate;
    }

    public bool IsRecordingEmote => _isRecordingEmote;
    public ulong ActiveContentId => _currentEntry?.ContentId ?? _playerState.ContentId;

    public uint? CurrentSelectedEmoteId
    {
        get
        {
            var contentId = ActiveContentId;
            if (contentId == 0 || !_configuration.CharaSelectSelectedEmotes.TryGetValue(contentId, out var emoteId) || emoteId == 0)
            {
                return null;
            }

            return emoteId;
        }
    }

    public void SetEmoteEnabled(bool enabled)
    {
        _configuration.CharaSelectEmoteEnabled = enabled;
        _configuration.Save();

        if (!enabled)
        {
            StopRecordingEmote();
            ResetEmoteMode();
            ClearReplayState();
            return;
        }

        ReplaySelectedEmote();
    }

    public void SetPreloadTerritoryEnabled(bool enabled)
    {
        _configuration.CharaSelectPreloadTerritoryEnabled = enabled;
        _configuration.Save();

        ApplyPreloadTerritoryHookState(enabled);
    }

    public void SyncFromConfiguration()
    {
        if (!_configuration.CharaSelectEmoteEnabled)
        {
            StopRecordingEmote();
            ResetEmoteMode();
        }

        ApplyPreloadTerritoryHookState(_configuration.CharaSelectPreloadTerritoryEnabled);
    }

    private void ApplyPreloadTerritoryHookState(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _openLoginWaitDialogHook?.Enable();
            }
            else
            {
                _openLoginWaitDialogHook?.Disable();
                TryUnloadPrefetchLayout();
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to toggle CharaSelect preload territory hook.");
        }
    }

    public void StartRecordingEmote()
    {
        _isRecordingEmote = true;
    }

    public void StopRecordingEmote()
    {
        _isRecordingEmote = false;
    }

    public void ClearSelectedEmote()
    {
        var contentId = ActiveContentId;
        if (contentId == 0)
        {
            return;
        }

        if (_configuration.CharaSelectSelectedEmotes.Remove(contentId))
        {
            _configuration.Save();
        }

        ResetEmoteMode();
        ClearReplayState();
    }

    public void ReplaySelectedEmote()
    {
        if (!_configuration.CharaSelectEmoteEnabled || CurrentSelectedEmoteId is not { } emoteId)
        {
            return;
        }

        PlayEmote(emoteId);
    }

    public string GetCurrentSelectedEmoteDisplayName()
    {
        var contentId = ActiveContentId;
        if (contentId == 0)
        {
            return "キャラクター未選択";
        }

        if (!_configuration.CharaSelectSelectedEmotes.TryGetValue(contentId, out var emoteId) || emoteId == 0)
        {
            return "なし";
        }

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

    public IReadOnlyList<string> GetVoiceDiagnosticLines()
    {
        var lines = new List<string>();

        try
        {
            var agent = AgentLobby.Instance();
            if (agent == null)
            {
                lines.Add("AgentLobby: null");
                return lines;
            }

            var selectedIndex = agent->SelectedCharacterIndex;
            var normalizedIndex = selectedIndex >= 100 ? selectedIndex - 100 : selectedIndex;
            lines.Add($"Agent: selected={selectedIndex}, normalized={normalizedIndex}, worldIndex={agent->WorldIndex}, isLoggedIn={agent->IsLoggedIn}");

            if (normalizedIndex < 0 || normalizedIndex >= 40)
            {
                lines.Add("Entry: invalid selected index");
                AppendLastVoiceDiagnosticLines(lines);
                return lines;
            }

            var entry = agent->LobbyData.GetCharacterEntryByIndex(0, agent->WorldIndex, normalizedIndex);
            var character = CharaSelectCharacterList.GetCurrentCharacter();
            if (entry == null)
            {
                lines.Add("Entry: null");
                return lines;
            }

            var clientData = entry->ClientSelectData;
            var resolvedVoiceId = ResolveCharacterVoiceId(entry);
            lines.Add($"Entry: content={entry->ContentId}, race={clientData.Race}, tribe={clientData.Tribe}, sex={clientData.Sex}, customizeRace={clientData.CustomizeData.Race}, customizeTribe={clientData.CustomizeData.Tribe}, customizeSex={clientData.CustomizeData.Sex}, rawVoice={clientData.VoiceId}, resolvedVoice={resolvedVoiceId}");

            if (character == null)
            {
                lines.Add("Character: null");
            }
            else
            {
                lines.Add($"Character: vfxVoice={character->Vfx.VoiceId}, drawRace={character->DrawData.CustomizeData.Race}, drawTribe={character->DrawData.CustomizeData.Tribe}, drawSex={character->DrawData.CustomizeData.Sex}");
            }

            AppendVoiceTableDiagnostics(lines, clientData.Race, clientData.Tribe, clientData.Sex, clientData.VoiceId);
        }
        catch (Exception ex)
        {
            lines.Add($"Error: {ex.GetType().Name}: {ex.Message}");
        }

        return lines;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRecordingEmote();
        ResetEmoteMode();
        ClearReplayState();
        TryUnloadPrefetchLayout();
        _currentEntry = null;
        _framework.Update -= OnFrameworkUpdate;
        DisposeHook(_openLoginWaitDialogHook);
        DisposeHook(_executeEmoteHook);
        DisposeHook(_updateCharaSelectDisplayHook);
    }

    private void InitializeHooks()
    {
        try
        {
            _updateCharaSelectDisplayHook = _gameInteropProvider.HookFromAddress<AgentLobby.Delegates.UpdateCharaSelectDisplay>(
                AgentLobby.MemberFunctionPointers.UpdateCharaSelectDisplay,
                UpdateCharaSelectDisplayDetour);
            _executeEmoteHook = _gameInteropProvider.HookFromAddress<EmoteManager.Delegates.ExecuteEmote>(
                EmoteManager.MemberFunctionPointers.ExecuteEmote,
                ExecuteEmoteDetour);
            _openLoginWaitDialogHook = _gameInteropProvider.HookFromAddress<AgentLobby.Delegates.OpenLoginWaitDialog>(
                AgentLobby.MemberFunctionPointers.OpenLoginWaitDialog,
                OpenLoginWaitDialogDetour);

            _updateCharaSelectDisplayHook.Enable();
            _executeEmoteHook.Enable();

            if (_configuration.CharaSelectPreloadTerritoryEnabled)
            {
                _openLoginWaitDialogHook.Enable();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize CharaSelect hooks.");
            DisposeHook(_openLoginWaitDialogHook);
            DisposeHook(_executeEmoteHook);
            DisposeHook(_updateCharaSelectDisplayHook);
            _openLoginWaitDialogHook = null;
            _executeEmoteHook = null;
            _updateCharaSelectDisplayHook = null;
            _configuration.CharaSelectEmoteEnabled = false;
            _configuration.CharaSelectPreloadTerritoryEnabled = false;
            _configuration.Save();
        }
    }

    private bool UpdateCharaSelectDisplayDetour(AgentLobby* agent, sbyte index, bool a2)
    {
        var result = _updateCharaSelectDisplayHook?.Original(agent, index, a2) ?? false;

        try
        {
            UpdateCurrentEntry(agent, index);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to update CharaSelect character state.");
        }

        return result;
    }

    private bool ExecuteEmoteDetour(EmoteManager* manager, ushort emoteId, EmoteController.PlayEmoteOption* playEmoteOption)
    {
        var result = _executeEmoteHook?.Original(manager, emoteId, playEmoteOption) ?? false;

        try
        {
            if (result && _isRecordingEmote && _clientState.IsLoggedIn)
            {
                SaveLoggedInVoiceId();
                SaveExecutedEmote(emoteId);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to record CharaSelect emote.");
        }

        return result;
    }

    private void OpenLoginWaitDialogDetour(AgentLobby* agent, int position)
    {
        _openLoginWaitDialogHook?.Original(agent, position);

        try
        {
            if (_configuration.CharaSelectPreloadTerritoryEnabled)
            {
                PreloadLoginTerritory();
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to preload CharaSelect login territory.");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed || _clientState.IsLoggedIn)
        {
            return;
        }

        ProcessDelayedReplay();

        _frameworkPollFrame = (_frameworkPollFrame + 1) % 10;
        if (_frameworkPollFrame != 0)
        {
            return;
        }

        try
        {
            var agent = AgentLobby.Instance();
            if (agent == null || agent->IsLoggedIn)
            {
                return;
            }

            var index = agent->SelectedCharacterIndex;
            if (index < 40)
            {
                UpdateCurrentEntry(agent, (sbyte)index, scheduleDelayedReplay: true);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to poll CharaSelect character state.");
        }
    }

    private void UpdateCurrentEntry(AgentLobby* agent, sbyte index, bool scheduleDelayedReplay = false)
    {
        if (agent == null || index < 0)
        {
            CleanupCharaSelect();
            return;
        }

        var normalizedIndex = index >= 100 ? index - 100 : index;
        var entry = agent->LobbyData.GetCharacterEntryByIndex(0, agent->WorldIndex, normalizedIndex);
        var character = CharaSelectCharacterList.GetCurrentCharacter();

        if (entry == null || character == null || entry->ContentId == 0)
        {
            CleanupCharaSelect();
            return;
        }

        var voiceId = ResolveCharacterVoiceId(entry);
        CharaSelectCharacterApplier.ApplyVoice(character, voiceId);
        _lastVoiceDiagnosticLines = BuildVoiceDiagnosticLines(agent, index, normalizedIndex, entry, character, voiceId);

        var isFirstEntry = _currentEntry == null;
        var sameEntry = _currentEntry?.ContentId == entry->ContentId
            && _currentEntry.Character == character;
        if (sameEntry)
        {
            if (IsDelayedReplayPending(entry->ContentId, character))
            {
                return;
            }

            ReplaySelectedEmoteIfNeeded();
            return;
        }

        _currentEntry = new CharaSelectCharacterState(
            character,
            entry->ContentId,
            entry->ClientSelectData.TerritoryType,
            entry->ClientSelectData.CurrentClass,
            voiceId);

        if (scheduleDelayedReplay || isFirstEntry)
        {
            ScheduleDelayedReplay();
            return;
        }

        ReplaySelectedEmoteIfNeeded(force: true);
    }

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

        _configuration.CharaSelectSelectedEmotes[contentId] = saveEmoteId;
        _configuration.Save();
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

    private IReadOnlyList<string> BuildVoiceDiagnosticLines(
        AgentLobby* agent,
        sbyte selectedIndex,
        int normalizedIndex,
        CharaSelectCharacterEntry* entry,
        Character* character,
        ushort resolvedVoiceId)
    {
        var lines = new List<string>
        {
            $"LastAgent: selected={selectedIndex}, normalized={normalizedIndex}, worldIndex={agent->WorldIndex}, isLoggedIn={agent->IsLoggedIn}",
        };

        var clientData = entry->ClientSelectData;
        lines.Add($"LastEntry: content={entry->ContentId}, race={clientData.Race}, tribe={clientData.Tribe}, sex={clientData.Sex}, customizeRace={clientData.CustomizeData.Race}, customizeTribe={clientData.CustomizeData.Tribe}, customizeSex={clientData.CustomizeData.Sex}, rawVoice={clientData.VoiceId}, resolvedVoice={resolvedVoiceId}");
        lines.Add($"LastCharacter: vfxVoice={character->Vfx.VoiceId}, drawRace={character->DrawData.CustomizeData.Race}, drawTribe={character->DrawData.CustomizeData.Tribe}, drawSex={character->DrawData.CustomizeData.Sex}");
        AppendVoiceTableDiagnostics(lines, clientData.CustomizeData.Race, clientData.CustomizeData.Tribe, clientData.CustomizeData.Sex, clientData.VoiceId);
        return lines;
    }

    private void AppendLastVoiceDiagnosticLines(List<string> lines)
    {
        if (_lastVoiceDiagnosticLines.Count == 0)
        {
            lines.Add("Last: none");
            return;
        }

        lines.Add("Last: cached chara-select voice diagnostics");
        lines.AddRange(_lastVoiceDiagnosticLines);
    }

    private void AppendVoiceTableDiagnostics(List<string> lines, byte race, byte tribe, byte sex, byte rawVoiceId)
    {
        try
        {
            var charaMakeTypeSheet = _dataManager.GetExcelSheet<CharaMakeType>();
            var matchCount = 0;
            foreach (var charaMakeType in charaMakeTypeSheet)
            {
                if (charaMakeType.Race.RowId != race || charaMakeType.Tribe.RowId != tribe)
                {
                    continue;
                }

                matchCount++;
                var rawCandidate = rawVoiceId < charaMakeType.VoiceStruct.Count
                    ? charaMakeType.VoiceStruct[rawVoiceId]
                    : (byte)0;
                var oneBasedIndex = rawVoiceId > 0 ? rawVoiceId - 1 : -1;
                var oneBasedCandidate = oneBasedIndex >= 0 && oneBasedIndex < charaMakeType.VoiceStruct.Count
                    ? charaMakeType.VoiceStruct[oneBasedIndex]
                    : (byte)0;
                var voices = string.Join(",", charaMakeType.VoiceStruct.Take(Math.Min(12, charaMakeType.VoiceStruct.Count)));
                lines.Add($"VoiceTable: row={charaMakeType.RowId}, gender={charaMakeType.Gender}, sexMatch={charaMakeType.Gender == sex}, count={charaMakeType.VoiceStruct.Count}, rawIdx={rawCandidate}, oneIdx={oneBasedCandidate}, first={voices}");
            }

            if (matchCount == 0)
            {
                lines.Add("VoiceTable: no race/tribe match");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"VoiceTable: {ex.GetType().Name}: {ex.Message}");
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

    private void PlayEmote(uint emoteId)
    {
        var entry = _currentEntry;
        if (!_configuration.CharaSelectEmoteEnabled || entry == null || entry.Character == null)
        {
            return;
        }

        if (!TryGetEmote(emoteId, out var emote))
        {
            ResetEmoteMode();
            return;
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
            return;
        }

        var plan = CharaSelectEmotePlaybackPlanner.Create(
            emote.EmoteMode.RowId,
            emote.EmoteMode.RowId != 0 ? emote.EmoteMode.Value.ConditionMode : (byte)CharacterModes.Normal,
            introId,
            loopId);

        if (!plan.HasTimeline)
        {
            ResetEmoteMode();
            return;
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

        PlayEmote(emoteId);
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

        var entry = _currentEntry;
        _delayedReplayFrames--;
        if (_delayedReplayFrames is 45 or 30 or 15)
        {
            if (entry?.VoiceId is > 0 && entry.Character != null)
            {
                CharaSelectCharacterApplier.ApplyVoice(entry.Character, entry.VoiceId);
            }
        }

        if (_delayedReplayFrames > 0)
        {
            return;
        }

        if (entry == null
            || entry.ContentId != _delayedReplayContentId
            || (nint)entry.Character != _delayedReplayCharacterAddress
            || CurrentSelectedEmoteId != _delayedReplayEmoteId)
        {
            return;
        }

        PlayEmote(_delayedReplayEmoteId);
    }

    private void ClearReplayState()
    {
        _replayTracker.Clear();
        _delayedReplayFrames = 0;
        _delayedReplayContentId = 0;
        _delayedReplayEmoteId = 0;
        _delayedReplayCharacterAddress = nint.Zero;
    }

    private void PreloadLoginTerritory()
    {
        var territoryTypeId = NormalizeHousingTerritory(_currentEntry?.TerritoryTypeId ?? 0);
        if (territoryTypeId == 0)
        {
            return;
        }

        var territorySheet = _dataManager.GetExcelSheet<TerritoryType>();
        var territory = territorySheet.GetRow(territoryTypeId);
        var bg = territory.Bg.ToString();
        if (string.IsNullOrWhiteSpace(bg))
        {
            return;
        }

        var layoutWorld = LayoutWorld.Instance();
        TryUnloadPrefetchLayout();
        layoutWorld->LoadPrefetchLayout(0, bg, 0, 0, territoryTypeId, null, 0);
    }

    private static ushort NormalizeHousingTerritory(ushort territoryTypeId)
    {
        return territoryTypeId switch
        {
            282 or 384 or 608 or 609 => 339,
            283 or 385 or 610 or 611 => 340,
            284 or 386 or 612 or 613 => 341,
            649 or 650 or 651 or 652 => 641,
            980 or 981 or 982 or 983 => 979,
            _ => territoryTypeId,
        };
    }

    private bool TryGetEmote(uint emoteId, out Emote emote)
    {
        try
        {
            var emoteSheet = _dataManager.GetExcelSheet<Emote>();
            emote = emoteSheet.GetRow(emoteId);
            return true;
        }
        catch
        {
            emote = default;
            return false;
        }
    }

    private static ushort GetTimelineRowId(Emote emote, int index)
    {
        if (index < 0 || index >= emote.ActionTimeline.Count)
        {
            return 0;
        }

        var rowId = emote.ActionTimeline[index].RowId;
        return rowId > ushort.MaxValue ? (ushort)0 : (ushort)rowId;
    }

    private void TryUnloadPrefetchLayout()
    {
        try
        {
            LayoutWorld.UnloadPrefetchLayout();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to unload CharaSelect prefetch layout.");
        }
    }

    private void DisposeHook<T>(Hook<T>? hook)
        where T : Delegate
    {
        try
        {
            hook?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to dispose CharaSelect hook.");
        }
    }
}

// Path: projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectService.Diagnostics.cs
// Description: ボイス・シーン構成の診断ライン生成
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace XivMiniUtil.Services.CharaSelect;

public sealed unsafe partial class CharaSelectService
{
    public IReadOnlyList<string> GetVoiceDiagnosticLines()
    {
        var lines = new List<string>();
        lines.AddRange(GetSceneCompositionDiagnosticLines());

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

    public IReadOnlyList<string> GetSceneCompositionDiagnosticLines()
    {
        var diagnostic = CharaSelectSceneCompositionPlanner.BuildDiagnostic(
            _configuration,
            GetCurrentSelectedEmoteDisplayName(),
            _currentEntry == null || _currentEntry.Character == null ? "Unknown" : "True",
            _lastSceneObservation);
        return CharaSelectSceneCompositionPlanner.BuildDiagnosticLines(diagnostic);
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
}

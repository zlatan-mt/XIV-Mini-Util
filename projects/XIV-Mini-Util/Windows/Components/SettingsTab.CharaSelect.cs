// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.CharaSelect.cs
// Description: キャラクター選択・撮影構成関連の設定UI
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.CharaSelect;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private void DrawCharaSelectSettings()
    {
        ImGui.Text("ログイン / タイトル背景");
        ImGui.Separator();

        DrawCharaSelectSceneCompositionSettings();

        ImGui.Spacing();
        ImGui.Separator();

        var emoteEnabled = _configuration.CharaSelectEmoteEnabled;
        if (ImGui.Checkbox("キャラ選択画面で保存したエモートを再生する", ref emoteEnabled))
        {
            _charaSelectService.SetEmoteEnabled(emoteEnabled);
        }

        ImGui.TextDisabled("ログイン中に記録したエモートを、次回以降のキャラ選択画面で再生します。");

        if (!emoteEnabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Spacing();
        ImGui.Text($"現在の保存エモート: {_charaSelectService.GetCurrentSelectedEmoteDisplayName()}");
        ImGui.Text($"最後に記録したエモート: {_charaSelectService.GetLastRecordedEmoteDisplayName()}");

        if (ImGui.Button("前へ"))
        {
            _charaSelectService.SelectPreviousEmote();
        }

        ImGui.SameLine();
        if (ImGui.Button("次へ"))
        {
            _charaSelectService.SelectNextEmote();
        }

        ImGui.SameLine();
        if (ImGui.Button("再生"))
        {
            _charaSelectService.ReplaySelectedEmote();
        }

        if (_charaSelectService.IsRecordingEmote)
        {
            if (ImGui.Button("記録停止"))
            {
                _charaSelectService.StopRecordingEmote();
            }
        }
        else
        {
            if (ImGui.Button("記録開始"))
            {
                _charaSelectService.StartRecordingEmote();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("現在スロットへ保存"))
        {
            _charaSelectService.SaveLastRecordedEmoteToActiveSlot();
        }

        ImGui.SameLine();
        if (ImGui.Button("追加保存"))
        {
            _charaSelectService.AppendLastRecordedEmotePreset();
        }

        ImGui.SameLine();
        if (ImGui.Button("削除"))
        {
            _charaSelectService.ClearSelectedEmote();
        }

        if (_charaSelectService.IsRecordingEmote)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "記録中: 保存したいエモートを実行してください。");
        }

        if (!emoteEnabled)
        {
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawTitleBackgroundSettings();
    }

    private void DrawCharaSelectSceneCompositionSettings()
    {
        ImGui.Text("通常: キャラを残す撮影構成");
        ImGui.TextWrapped("キャラ選択画面の本物の選択キャラクターを残したまま、場所・立ち位置・エモートを構成します。背景だけ実験とは同時に使いません。");

        var enabled = _configuration.CharaSelectSceneCompositionEnabled;
        if (ImGui.Checkbox("撮影構成を有効にする", ref enabled))
        {
            if (enabled && _configuration.TitleBackgroundOverrideEnabled)
            {
                _titleScreenBackgroundService.SetEnabled(false);
            }

            _charaSelectService.SetSceneCompositionEnabled(enabled);
        }

        var selectedProfile = _charaSelectService.CurrentSceneProfile;
        DrawSceneCompositionDecisionSummary(selectedProfile);

        if (ImGui.BeginCombo("撮影 profile##CharaSelectSceneProfile", _charaSelectService.GetCurrentSceneProfileLabel()))
        {
            foreach (var profile in _charaSelectService.SceneProfiles)
            {
                if (ImGui.Selectable(CharaSelectSceneProfileRegistry.BuildLabel(profile), selectedProfile.Id == profile.Id))
                {
                    _charaSelectService.SetSceneProfileId(profile.Id);
                }
            }

            ImGui.EndCombo();
        }

        var useTerritory = _configuration.CharaSelectSceneUseProfileTerritory;
        if (ImGui.Checkbox("profile の場所を使う", ref useTerritory))
        {
            _charaSelectService.SetSceneUseProfileTerritory(useTerritory);
        }

        var stageStrategy = _configuration.CharaSelectSceneStageStrategy;
        if (ImGui.BeginCombo("場所変更ルート##CharaSelectStageStrategy", GetStageStrategyLabel(stageStrategy)))
        {
            foreach (CharaSelectStageStrategy strategy in Enum.GetValues(typeof(CharaSelectStageStrategy)))
            {
                if (ImGui.Selectable(GetStageStrategyLabel(strategy), stageStrategy == strategy))
                {
                    _charaSelectService.SetSceneStageStrategy(strategy);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled(GetStageStrategyDescription(stageStrategy));

        var useSavedEmote = _configuration.CharaSelectSceneUseSavedEmote;
        if (ImGui.Checkbox("保存済みエモートを再生する", ref useSavedEmote))
        {
            _charaSelectService.SetSceneUseSavedEmote(useSavedEmote);
        }

        ImGui.Text($"現在の保存エモート: {_charaSelectService.GetCurrentSelectedEmoteDisplayName()}");
        ImGui.Text($"最後に記録したエモート: {_charaSelectService.GetLastRecordedEmoteDisplayName()}");

        var usePosition = _configuration.CharaSelectSceneUseProfilePosition;
        if (ImGui.Checkbox("profile の立ち位置を使う（Phase3Aでは観測のみ）", ref usePosition))
        {
            _charaSelectService.SetSceneUseProfilePosition(usePosition);
        }

        var placementMode = _configuration.CharaSelectScenePlacementMode;
        if (ImGui.BeginCombo("立ち位置 mode##CharaSelectScenePlacementMode", placementMode.ToString()))
        {
            foreach (CharaSelectScenePlacementMode mode in Enum.GetValues(typeof(CharaSelectScenePlacementMode)))
            {
                if (ImGui.Selectable(mode.ToString(), placementMode == mode))
                {
                    _charaSelectService.SetScenePlacementMode(mode);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.Button("撮影構成を適用"))
        {
            if (_configuration.TitleBackgroundOverrideEnabled)
            {
                _titleScreenBackgroundService.SetEnabled(false);
            }

            _charaSelectService.ApplyCurrentSceneProfile();
        }

        ImGui.SameLine();
        if (ImGui.Button("再生テスト##CharaSelectSceneReplay"))
        {
            _charaSelectService.ReplaySelectedEmote();
        }

        ImGui.TextDisabled("Phase3A は position write を行いません。OneShotAfterDisplay は Phase3B 用の枠です。");

        ImGui.Spacing();
        ImGui.Text("SS判定");
        var characterVisible = _configuration.LastSceneProfileCharacterVisibleResult;
        if (DrawSceneBinaryResultCombo("キャラ本体##SceneCharacterVisible", ref characterVisible, "見えた", "見えない"))
        {
            _charaSelectService.SetSceneCharacterVisibleResult(characterVisible);
        }

        var locationChanged = _configuration.LastSceneProfileLocationChangedResult;
        if (DrawSceneBinaryResultCombo("場所##SceneLocationChanged", ref locationChanged, "変わった", "変わらない"))
        {
            _charaSelectService.SetSceneLocationChangedResult(locationChanged);
        }

        ImGui.TextWrapped("場所が default のままなら「場所=変わらない」を選んでください。これは TerritoryTypeId の差し替えだけでは見た目のステージが変わっていないことを意味します。");

        var emotePlayed = _configuration.LastSceneProfileEmotePlayedResult;
        if (DrawSceneBinaryResultCombo("エモート##SceneEmotePlayed", ref emotePlayed, "再生した", "再生しない"))
        {
            _charaSelectService.SetSceneEmotePlayedResult(emotePlayed);
        }

        var brightness = _configuration.LastSceneProfileBrightnessResult;
        if (DrawSceneBrightnessResultCombo("明るさ##SceneBrightness", ref brightness))
        {
            _charaSelectService.SetSceneBrightnessResult(brightness);
        }

        if (ImGui.CollapsingHeader("Stage probe 詳細 / 開発者向け"))
        {
            ImGui.TextDisabled($"Route: {CharaSelectSceneCompositionPlanner.ForegroundPreservingRoute}");
            ImGui.TextDisabled($"Expected character visible: {selectedProfile.CharacterExpectedVisible}");
            ImGui.TextDisabled($"Brightness: {selectedProfile.ExpectedBrightness}");
            ImGui.TextDisabled($"Recommended: {selectedProfile.RecommendedAction}");
            ImGui.TextDisabled($"Profile territory: {selectedProfile.TerritoryTypeId}");
            ImGui.TextDisabled($"Profile path: {selectedProfile.TerritoryPath}");
            ImGui.TextDisabled($"Stage strategy last result: {_configuration.CharaSelectSceneStageStrategyLastResult}");
            ImGui.TextDisabled($"Stage strategy last reason: {_configuration.CharaSelectSceneStageStrategyLastReason}");
            ImGui.TextDisabled("Lobby position / Layout prefetch / ClientSelectData details は /xmucdiag に出力します。");
        }
    }

    private void DrawSceneCompositionDecisionSummary(CharaSelectSceneProfile selectedProfile)
    {
        var verdict = GetSceneCompositionVerdictLabel();
        var nextAction = CharaSelectSceneCompositionPlanner.BuildNextAction(_configuration);
        var statusColor = GetSceneCompositionVerdictColor();

        ImGui.Spacing();
        ImGui.Text("現在の判定");
        ImGui.TextColored(statusColor, verdict);
        ImGui.TextWrapped(
            $"結果: キャラ={GetSceneBinaryResultLabel(_configuration.LastSceneProfileCharacterVisibleResult, "見えた", "見えない")} / " +
            $"場所={GetSceneBinaryResultLabel(_configuration.LastSceneProfileLocationChangedResult, "変わった", "変わらない")} / " +
            $"エモート={GetSceneBinaryResultLabel(_configuration.LastSceneProfileEmotePlayedResult, "再生した", "再生しない")} / " +
            $"明るさ={GetSceneBrightnessResultLabel(_configuration.LastSceneProfileBrightnessResult)}");
        ImGui.TextWrapped($"次: {GetSceneNextActionLabel(nextAction)}");

        if (_configuration.CharaSelectSceneCompositionEnabled && _configuration.TitleBackgroundOverrideEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), "背景だけ実験が同時に有効です。撮影構成を使う場合は背景だけ実験を無効にしてください。");
        }

        ImGui.TextDisabled($"期待: キャラ={BoolToVisibleLabel(selectedProfile.CharacterExpectedVisible)} / 明るさ={selectedProfile.ExpectedBrightness}");
    }

    private string GetSceneCompositionVerdictLabel()
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return "無効: 撮影構成はまだ使いません";
        }

        if (_configuration.TitleBackgroundOverrideEnabled)
        {
            return "要確認: 背景だけ実験と競合しています";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return "OK: キャラ・場所・エモートを確認済み";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No)
        {
            return "Blocked: キャラ本体が見えていません";
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No)
        {
            return "Blocked: 場所が変わっていません";
        }

        return "未確認: SS判定を入力してください";
    }

    private Vector4 GetSceneCompositionVerdictColor()
    {
        if (!_configuration.CharaSelectSceneCompositionEnabled)
        {
            return new Vector4(0.7f, 0.7f, 0.7f, 1f);
        }

        if (_configuration.TitleBackgroundOverrideEnabled
            || _configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.No
            || (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
                && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.No))
        {
            return new Vector4(1f, 0.45f, 0.45f, 1f);
        }

        if (_configuration.LastSceneProfileCharacterVisibleResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileLocationChangedResult == CharaSelectSceneBinaryResult.Yes
            && _configuration.LastSceneProfileEmotePlayedResult == CharaSelectSceneBinaryResult.Yes)
        {
            return new Vector4(0.3f, 0.8f, 0.45f, 1f);
        }

        return new Vector4(1f, 0.75f, 0.35f, 1f);
    }

    private static string GetSceneNextActionLabel(string nextAction)
    {
        return nextAction switch
        {
            "enable-scene-composition-and-select-profile" => "撮影構成を有効化し、profile を選びます",
            "disable-title-background-route-and-verify-foreground" => "背景だけ実験を無効化し、キャラ本体が残るか確認します",
            "inspect-character-visibility-route" => "キャラ本体が消える原因を診断します",
            "discover-visible-stage-source" => "キャラ本体を残したまま場所を変える source を探します",
            "fix-emote-replay-route" => "保存エモートの再生ルートを確認します",
            "implement-one-shot-placement" => "一回だけの立ち位置適用へ進めます",
            "verify-with-screenshot-and-set-manual-results" => "SSで確認し、下の判定を入力します",
            "verify-character-visible-background-and-emote-with-screenshot" => "SSでキャラ・場所・エモートを確認します",
            _ => nextAction,
        };
    }

    private static string BoolToVisibleLabel(bool value)
    {
        return value ? "表示される想定" : "表示されない想定";
    }

    private static bool DrawSceneBinaryResultCombo(
        string label,
        ref CharaSelectSceneBinaryResult result,
        string yesLabel,
        string noLabel)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, GetSceneBinaryResultLabel(result, yesLabel, noLabel)))
        {
            foreach (CharaSelectSceneBinaryResult candidate in Enum.GetValues(typeof(CharaSelectSceneBinaryResult)))
            {
                if (ImGui.Selectable(GetSceneBinaryResultLabel(candidate, yesLabel, noLabel), result == candidate))
                {
                    result = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static bool DrawSceneBrightnessResultCombo(string label, ref CharaSelectSceneBrightnessResult result)
    {
        var changed = false;
        if (ImGui.BeginCombo(label, GetSceneBrightnessResultLabel(result)))
        {
            foreach (CharaSelectSceneBrightnessResult candidate in Enum.GetValues(typeof(CharaSelectSceneBrightnessResult)))
            {
                if (ImGui.Selectable(GetSceneBrightnessResultLabel(candidate), result == candidate))
                {
                    result = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private static string GetSceneBinaryResultLabel(
        CharaSelectSceneBinaryResult result,
        string yesLabel,
        string noLabel)
    {
        return result switch
        {
            CharaSelectSceneBinaryResult.Unknown => "未確認",
            CharaSelectSceneBinaryResult.Yes => yesLabel,
            CharaSelectSceneBinaryResult.No => noLabel,
            _ => "未確認",
        };
    }

    private static string GetSceneBrightnessResultLabel(CharaSelectSceneBrightnessResult result)
    {
        return result switch
        {
            CharaSelectSceneBrightnessResult.Unknown => "未確認",
            CharaSelectSceneBrightnessResult.Dark => "暗い",
            CharaSelectSceneBrightnessResult.Acceptable => "許容",
            CharaSelectSceneBrightnessResult.Bright => "明るい",
            _ => "未確認",
        };
    }

    private static string GetStageStrategyLabel(CharaSelectStageStrategy strategy)
    {
        return strategy switch
        {
            CharaSelectStageStrategy.Disabled => "無効",
            CharaSelectStageStrategy.ObserveOnly => "診断のみ",
            CharaSelectStageStrategy.ClientSelectDataTerritoryPatch => "現在の安全 route",
            CharaSelectStageStrategy.LobbyPositionPatch => "Lobby position 実験",
            CharaSelectStageStrategy.LobbySheetResolvedPatch => "Lobby sheet 実験",
            CharaSelectStageStrategy.LayoutPrefetchOnly => "Layout prefetch のみ",
            CharaSelectStageStrategy.TitleBackgroundFullSceneFallback => "背景だけ fallback",
            _ => strategy.ToString(),
        };
    }

    private static string GetStageStrategyDescription(CharaSelectStageStrategy strategy)
    {
        return strategy switch
        {
            CharaSelectStageStrategy.ObserveOnly => "見た目は変えず、Character Select 中の stage source 候補だけを記録します。",
            CharaSelectStageStrategy.ClientSelectDataTerritoryPatch => "既存の ClientSelectData TerritoryTypeId 差し替えを使います。SSで場所が default のままなら source 未解決として扱います。",
            CharaSelectStageStrategy.TitleBackgroundFullSceneFallback => "背景だけ差し替え実験です。最終目的 route ではなく、キャラ本体は消える想定です。",
            CharaSelectStageStrategy.Disabled => "stage strategy を使いません。",
            _ => "現時点では安全な foreground-preserving 実装が未確定です。/xmucdiag で unavailable として出します。",
        };
    }
}

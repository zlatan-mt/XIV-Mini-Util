// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs
// Description: タイトル背景・Character Select背景差し替え関連の設定UI
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.TitleBackground;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private void DrawTitleBackgroundSettings()
    {
        ImGui.Text("Title Background");
        ImGui.TextWrapped("Character Select の背景を n4f4 recommended に設定します。");

        if (_configuration.TitleBackgroundSettingsDisplayMode == TitleBackgroundSettingsDisplayMode.Simple)
        {
            DrawTitleBackgroundSimplePanel();
            DrawTitleBackgroundAdvancedDrawer();
            return;
        }

        DrawTitleBackgroundStatusSummary();
        DrawTitleBackgroundQuickActions();
        DrawTitleBackgroundDisplayModeSelector();
        DrawTitleBackgroundKnownLimitation();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Advanced Settings");
        var runtimeMode = _configuration.TitleBackgroundRuntimeMode;
        if (ImGui.BeginCombo("実行範囲##TitleBackgroundRuntimeMode", GetTitleBackgroundRuntimeModeLabel(runtimeMode)))
        {
            foreach (TitleBackgroundRuntimeMode mode in Enum.GetValues(typeof(TitleBackgroundRuntimeMode)))
            {
                if (!TitleBackgroundRuntimeModeHelper.IsRuntimeModeSelectable(mode))
                {
                    continue;
                }

                if (ImGui.Selectable(GetTitleBackgroundRuntimeModeLabel(mode), runtimeMode == mode))
                {
                    _configuration.TitleBackgroundRuntimeMode = mode;
                    _configuration.Save();
                    _titleScreenBackgroundService.ReloadNativeIntegration();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Title + CharaSelect は未実装のため、実機確認までは選択肢から外しています。");

        DrawTitleBackgroundCharacterSelectDeliveryModes();
        ImGui.Spacing();
        DrawTitleBackgroundEffectiveCandidateDetails();
        ImGui.Spacing();
        DrawTitleBackgroundCharacterCompositionBridgeDiagnostics();

        if (ImGui.Button("解除"))
        {
            _titleScreenBackgroundService.ClearOverride();
        }

        ImGui.SameLine();
        if (ImGui.Button("入力をクリア"))
        {
            ClearTitleBackgroundInputs();
        }

        if (!string.IsNullOrWhiteSpace(_configuration.TitleBackgroundTerritoryPath))
        {
            ImGui.TextDisabled($"LVB想定パス: {TitleBackgroundPathHelper.BuildLvbPath(_configuration.TitleBackgroundTerritoryPath)}");
        }

        if (_configuration.TitleBackgroundSettingsDisplayMode != TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Developer Diagnostics");
        DrawTitleBackgroundPresetSettings();
        ImGui.Spacing();
        DrawTitleBackgroundPhase2Settings();
        ImGui.Spacing();
        DrawTitleBackgroundAdvancedSettings();
    }

    private void DrawTitleBackgroundSimplePanel()
    {
        var summary = TitleBackgroundQuickCheckUiPresenter.BuildSimpleSummary(_configuration);
        var statusColor = summary.Status switch
        {
            TitleBackgroundSimpleUiStatus.Working => new Vector4(0.3f, 0.8f, 0.45f, 1f),
            TitleBackgroundSimpleUiStatus.Failed => new Vector4(1f, 0.45f, 0.45f, 1f),
            TitleBackgroundSimpleUiStatus.Ready => new Vector4(0.3f, 0.65f, 1f, 1f),
            _ => new Vector4(1f, 0.75f, 0.35f, 1f),
        };

        ImGui.Spacing();
        ImGui.Text("Character Select Background");
        var offSelected = !_configuration.TitleBackgroundOverrideEnabled;
        if (ImGui.RadioButton("OFF##TitleBackgroundSimpleOff", offSelected))
        {
            _titleScreenBackgroundService.SetEnabled(false);
        }

        ImGui.SameLine();
        var recommendedSelected = TitleBackgroundQuickCheckUiPresenter.IsSimpleAutoSetupConfigured(_configuration);
        if (ImGui.RadioButton("n4f4 recommended##TitleBackgroundSimpleN4F4", recommendedSelected))
        {
            _titleScreenBackgroundService.RunSimpleAutoSetup();
        }

        ImGui.TextColored(statusColor, summary.StatusLine);
        ImGui.TextWrapped(summary.ResultLine);
        ImGui.TextDisabled(summary.NextActionLine);

        if (ImGui.Button("Auto Setup##TitleBackgroundSimpleAutoSetup"))
        {
            _titleScreenBackgroundService.RunSimpleAutoSetup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Check##TitleBackgroundSimpleCheck"))
        {
            _titleScreenBackgroundService.RunSimpleCheck();
        }
    }

    private void DrawTitleBackgroundAdvancedDrawer()
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Advanced##TitleBackgroundAdvancedDrawer"))
        {
            return;
        }

        DrawTitleBackgroundDisplayModeSelector();
        ImGui.TextDisabled("Switch to Advanced or Developer Diagnostics to inspect raw QuickCheck and camera details.");
    }

    private void DrawTitleBackgroundStatusSummary()
    {
        var summary = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        var statusColor = summary.Level switch
        {
            TitleBackgroundQuickCheckLevel.OK => new Vector4(0.3f, 0.8f, 0.45f, 1f),
            TitleBackgroundQuickCheckLevel.WARN => new Vector4(1f, 0.75f, 0.35f, 1f),
            TitleBackgroundQuickCheckLevel.NG => new Vector4(1f, 0.45f, 0.45f, 1f),
            _ => new Vector4(0.7f, 0.7f, 0.7f, 1f),
        };

        ImGui.Spacing();
        ImGui.Text($"Title Background: {(_configuration.TitleBackgroundOverrideEnabled ? "ON" : "OFF")}");
        ImGui.Text(summary.CandidateLine);
        ImGui.TextColored(statusColor, summary.StatusLine);
        if (_configuration.TitleBackgroundOverrideEnabled && !_configuration.TitleBackgroundIntegratedCompositionEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), "Integrated composition is OFF. Re-enable Character Select Background or reset Title Background settings.");
        }

        ImGui.TextWrapped(summary.NextActionLine);
        ImGui.TextDisabled(_titleScreenBackgroundService.GetStatusText());
    }

    private void DrawTitleBackgroundQuickActions()
    {
        ImGui.Spacing();
        ImGui.Text("Quick Actions");

        var enabled = _configuration.TitleBackgroundOverrideEnabled;
        if (ImGui.Checkbox("Enable Character Select Background", ref enabled))
        {
            if (enabled && _configuration.CharaSelectSceneCompositionEnabled)
            {
                _charaSelectService.DisableSceneCompositionForTitleBackgroundRoute();
            }

            if (enabled && _configuration.TitleBackgroundRuntimeMode == TitleBackgroundRuntimeMode.ResolveOnly)
            {
                _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.CharaSelectOnly;
                _configuration.Save();
            }

            _titleScreenBackgroundService.SetEnabled(enabled);
        }

        DrawTitleBackgroundOverrideCandidateSelector(showManualSlot: false);
        DrawTitleBackgroundCameraFramingSelector();
        ImGui.TextDisabled("Camera framing is handled by Title Background.");
        ImGui.TextDisabled("Title Background uses integrated character composition.");
        DrawTitleBackgroundCharacterVisualStatusSelector();

        if (ImGui.Button("Start QuickCheck"))
        {
            _titleScreenBackgroundService.StartQuickCheck();
        }

        ImGui.SameLine();
        if (ImGui.Button("Run Check"))
        {
            _titleScreenBackgroundService.RunQuickCheck();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset Check"))
        {
            _titleScreenBackgroundService.ResetQuickCheck();
        }

        var summary = TitleBackgroundQuickCheckUiPresenter.BuildSummary(_configuration);
        ImGui.Text(summary.LastResultLine);
        ImGui.Text(summary.LastReasonLine);
        ImGui.Text(summary.NextActionLine);
        ImGui.Text(summary.DetailLine);
        if (!string.IsNullOrWhiteSpace(_configuration.TitleBackgroundLastQuickCheckTime))
        {
            ImGui.Text($"Last Check Time: {_configuration.TitleBackgroundLastQuickCheckTime}");
        }
    }

    private void DrawTitleBackgroundDisplayModeSelector()
    {
        var displayMode = _configuration.TitleBackgroundSettingsDisplayMode;
        ImGui.Spacing();
        if (ImGui.BeginCombo("Display Mode##TitleBackgroundSettingsDisplayMode", GetTitleBackgroundSettingsDisplayModeLabel(displayMode)))
        {
            foreach (TitleBackgroundSettingsDisplayMode mode in Enum.GetValues(typeof(TitleBackgroundSettingsDisplayMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundSettingsDisplayModeLabel(mode), displayMode == mode))
                {
                    _configuration.TitleBackgroundSettingsDisplayMode = mode;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
    }

    private static void DrawTitleBackgroundKnownLimitation()
    {
        ImGui.Spacing();
        ImGui.Text("Known limitation:");
        ImGui.TextWrapped("Character source is not resolved by diagnostics; visual confirmation is required.");
        ImGui.TextDisabled("Character may appear off-center or too small depending on camera framing.");
    }

    private void DrawTitleBackgroundCharacterCompositionBridgeDiagnostics()
    {
        var bridge = _charaSelectService.GetTitleBackgroundCharacterCompositionBridgeSnapshot();
        ImGui.Text("Character Composition Bridge");
        ImGui.TextDisabled($"Integrated composition: {_configuration.TitleBackgroundIntegratedCompositionEnabled}");
        ImGui.TextDisabled($"Bridge enabled: {bridge.Enabled}");
        ImGui.TextDisabled($"Bridge invoked: {bridge.Invoked}");
        ImGui.TextDisabled($"Bridge source: {bridge.Source}");
        ImGui.TextDisabled($"Bridge reason: {bridge.Reason}");
        ImGui.TextDisabled($"Applied stage/character/camera: {bridge.AppliedStage}/{bridge.AppliedCharacter}/{bridge.AppliedCamera}");
        ImGui.TextDisabled($"Legacy shooting composition: {_configuration.CharaSelectSceneCompositionEnabled}");
        ImGui.Spacing();
        ImGui.Text("Camera Profile Compare");
        if (ImGui.Button("Capture legacy visible camera##TitleBackgroundCaptureLegacyCamera"))
        {
            if (_titleScreenBackgroundService.CaptureLegacyVisibleCameraProfile(out var message))
            {
                _titleBackgroundCameraProfileMessage = message;
                _titleBackgroundCameraProfileMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundCameraProfileMessage = message;
                _titleBackgroundCameraProfileMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Save as n4f4 visible profile##TitleBackgroundSaveN4F4VisibleProfile"))
        {
            if (_titleScreenBackgroundService.CaptureLegacyVisibleCameraProfile(out var message))
            {
                _titleBackgroundCameraProfileMessage = message;
                _titleBackgroundCameraProfileMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundCameraProfileMessage = message;
                _titleBackgroundCameraProfileMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear captured profile##TitleBackgroundClearCapturedCamera"))
        {
            _titleScreenBackgroundService.ClearLegacyVisibleCameraProfile();
            _titleBackgroundCameraProfileMessage = "captured profile cleared";
            _titleBackgroundCameraProfileMessageColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);
        }

        if (!string.IsNullOrWhiteSpace(_titleBackgroundCameraProfileMessage))
        {
            ImGui.TextColored(_titleBackgroundCameraProfileMessageColor, _titleBackgroundCameraProfileMessage);
        }

        foreach (var line in _titleScreenBackgroundService.GetTitleBackgroundCameraProfileDiagnosticLines())
        {
            ImGui.TextDisabled(line);
        }
    }

    private void DrawTitleBackgroundPresetSettings()
    {
        var selectedPresetId = string.IsNullOrEmpty(_titleBackgroundPendingPresetId)
            ? _configuration.TitleBackgroundSelectedPresetId
            : _titleBackgroundPendingPresetId;
        var selectedLabel = "未選択 / Custom";
        if (TitleBackgroundBuiltInPresetCatalog.TryGetById(selectedPresetId, out var selectedEntry))
        {
            selectedLabel = selectedEntry.DisplayName;
            if (!string.IsNullOrEmpty(_titleBackgroundPendingPresetId))
            {
                selectedLabel += " (未適用)";
            }
        }

        if (ImGui.BeginCombo("背景 preset##TitleBackgroundPreset", selectedLabel))
        {
            if (ImGui.Selectable("未選択 / Custom", string.IsNullOrEmpty(selectedPresetId)))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _configuration.TitleBackgroundSelectedPresetId = string.Empty;
                _configuration.Save();
            }

            foreach (var entry in TitleBackgroundBuiltInPresetCatalog.Presets)
            {
                if (ImGui.Selectable(entry.DisplayName, string.Equals(selectedPresetId, entry.Id, StringComparison.Ordinal)))
                {
                    _titleBackgroundPendingPresetId = entry.Id;
                }
            }

            ImGui.EndCombo();
        }

        if (TitleBackgroundBuiltInPresetCatalog.Presets.Count == 0)
        {
            ImGui.TextDisabled("実機確認済みの built-in preset はまだありません。");
        }

        if (ImGui.Button("preset を適用"))
        {
            var presetId = string.IsNullOrWhiteSpace(_titleBackgroundPendingPresetId)
                ? _configuration.TitleBackgroundSelectedPresetId
                : _titleBackgroundPendingPresetId;
            if (string.IsNullOrWhiteSpace(presetId))
            {
                _titleBackgroundPresetMessage = "preset が未選択です。";
                _titleBackgroundPresetMessageColor = new Vector4(1f, 0.75f, 0.35f, 1f);
            }
            else if (_titleScreenBackgroundService.TryApplyBuiltInPreset(presetId, out var errorMessage))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _titleBackgroundPresetMessage = "preset を適用しました。";
                _titleBackgroundPresetMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundPresetMessage = $"preset 適用失敗: {errorMessage}";
                _titleBackgroundPresetMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        if (!string.IsNullOrWhiteSpace(_titleBackgroundPresetMessage))
        {
            ImGui.TextColored(_titleBackgroundPresetMessageColor, _titleBackgroundPresetMessage);
        }
    }

    private void DrawTitleBackgroundPhase2Settings()
    {
        if (!ImGui.CollapsingHeader("詳細設定 / 診断"))
        {
            return;
        }

        ImGui.TextDisabled("手入力と現在値保存は debug 補助です。通常は built-in preset を選んで適用します。");
        ImGui.TextDisabled("CharacterPosition は将来のキャラクター配置用で、Camera Focus とは別の値です。");

        DrawTitleBackgroundCharacterSelectDeliveryModes();
        ImGui.Spacing();
        DrawTitleBackgroundEffectiveCandidateDetails();
        DrawTitleBackgroundManualCandidateSlot1(BuildTitleBackgroundManualCandidateSlots()[0]);
        ImGui.Spacing();

        var territoryPath = _configuration.TitleBackgroundTerritoryPath;
        if (ImGui.InputTextWithHint("TerritoryPath##TitleBackgroundTerritoryPath", "ffxiv/.../level/...", ref territoryPath, 256))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundTerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        if (ImGui.Button("現在値を debug 保存"))
        {
            _titleScreenBackgroundService.CaptureCurrentLocationAndCamera();
        }

        ImGui.SameLine();
        var cameraOverrideEnabled = _configuration.TitleBackgroundCameraOverrideEnabled;
        if (ImGui.Checkbox("カメラ調整を有効化（実験）", ref cameraOverrideEnabled))
        {
            _titleScreenBackgroundService.SetCameraOverrideEnabled(cameraOverrideEnabled);
        }

        ImGui.TextDisabled(cameraOverrideEnabled
            ? "Camera override: ON。CharaSelectOnly の scene 差し替え後に FixOn hook で適用します。"
            : "Camera override: OFF。camera 値は上書きしません。FixOn hook 有効時は passthrough 観測のみ行います。");
        ImGui.TextDisabled("hook状態と保存失敗理由は /xmutbgdiag でも確認できます。");

        DrawTitleBackgroundCaptureResult();

        ImGui.Spacing();

        var cameraX = _configuration.TitleBackgroundCameraX;
        var cameraY = _configuration.TitleBackgroundCameraY;
        var cameraZ = _configuration.TitleBackgroundCameraZ;
        if (DrawTitleBackgroundVectorInput("Camera", ref cameraX, ref cameraY, ref cameraZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCameraX = cameraX;
            _configuration.TitleBackgroundCameraY = cameraY;
            _configuration.TitleBackgroundCameraZ = cameraZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var focusX = _configuration.TitleBackgroundFocusX;
        var focusY = _configuration.TitleBackgroundFocusY;
        var focusZ = _configuration.TitleBackgroundFocusZ;
        if (DrawTitleBackgroundVectorInput("Focus", ref focusX, ref focusY, ref focusZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundFocusX = focusX;
            _configuration.TitleBackgroundFocusY = focusY;
            _configuration.TitleBackgroundFocusZ = focusZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
        ImGui.TextDisabled("Focus は FixOn focus 引数へ渡す値です。実機観測では post-FixOn LookAtVector に反映されます。");

        var fovY = _configuration.TitleBackgroundFovY;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputFloat("FOV Y##TitleBackgroundFovY", ref fovY, 1f, 5f, "%.2f"))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(fovY);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.Spacing();
        var characterX = _configuration.TitleBackgroundCharacterPositionX;
        var characterY = _configuration.TitleBackgroundCharacterPositionY;
        var characterZ = _configuration.TitleBackgroundCharacterPositionZ;
        if (DrawTitleBackgroundVectorInput("Character", ref characterX, ref characterY, ref characterZ))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterPositionX = characterX;
            _configuration.TitleBackgroundCharacterPositionY = characterY;
            _configuration.TitleBackgroundCharacterPositionZ = characterZ;
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
        ImGui.TextDisabled("Character は将来の配置/preset補助用です。Focus の代用には使いません。");

        if (ImGui.Button("カメラ値を適用"))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCameraX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraX);
            _configuration.TitleBackgroundCameraY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraY);
            _configuration.TitleBackgroundCameraZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundCameraZ);
            _configuration.TitleBackgroundFocusX = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusX);
            _configuration.TitleBackgroundFocusY = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusY);
            _configuration.TitleBackgroundFocusZ = TitleBackgroundPreset.SanitizeCoordinate(_configuration.TitleBackgroundFocusZ);
            _configuration.TitleBackgroundFovY = TitleBackgroundPreset.ClampFovY(_configuration.TitleBackgroundFovY);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }
    }

    private void DrawTitleBackgroundCharacterSelectDeliveryModes()
    {
        var backgroundMode = _configuration.TitleBackgroundCharacterSelectBackgroundMode;
        var lightingMode = _configuration.TitleBackgroundCharacterSelectLightingMode;
        var changed = false;

        if (ImGui.BeginCombo("背景の扱い##TitleBackgroundCharacterSelectBackgroundMode", GetTitleBackgroundCharacterSelectBackgroundModeLabel(backgroundMode)))
        {
            foreach (TitleBackgroundCharacterSelectBackgroundMode candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectBackgroundMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterSelectBackgroundModeLabel(candidate), backgroundMode == candidate))
                {
                    backgroundMode = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeTooltip(backgroundMode));
        }

        if (ImGui.BeginCombo("明るさの扱い##TitleBackgroundCharacterSelectLightingMode", GetTitleBackgroundCharacterSelectLightingModeLabel(lightingMode)))
        {
            foreach (TitleBackgroundCharacterSelectLightingMode candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectLightingMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterSelectLightingModeLabel(candidate), lightingMode == candidate))
                {
                    lightingMode = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped("Character Select は background-only MVP です。custom override は selectedPresetId=none として扱い、キャラクター本体は表示されない想定です。");

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCharacterSelectBackgroundMode = backgroundMode;
        _configuration.TitleBackgroundCharacterSelectLightingMode = lightingMode;
        _configuration.Save();
        _titleScreenBackgroundService.ApplyFromConfiguration();
    }

    private void DrawTitleBackgroundOverrideCandidateSelector(bool showManualSlot)
    {
        var manualSlots = BuildTitleBackgroundManualCandidateSlots();
        var availableCandidates = TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(manualSlots);
        var selectedCandidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            availableCandidates);
        var selectedLabel = GetTitleBackgroundOverrideCandidateLabel(selectedCandidate);

        if (ImGui.BeginCombo("背景候補##TitleBackgroundOverrideCandidate", selectedLabel))
        {
            foreach (var candidate in availableCandidates)
            {
                if (ImGui.Selectable(GetTitleBackgroundOverrideCandidateLabel(candidate), selectedCandidate.Id == candidate.Id))
                {
                    ApplyTitleBackgroundOverrideCandidate(candidate);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped("背景のみモードではロビーシーン全体を差し替えます。選択キャラクター本体は表示されない想定です。");
        if (showManualSlot)
        {
            DrawTitleBackgroundManualCandidateSlot1(manualSlots[0]);
        }
    }

    private void DrawTitleBackgroundEffectiveCandidateDetails()
    {
        var manualSlots = BuildTitleBackgroundManualCandidateSlots();
        var candidate = TitleBackgroundCharacterSelectOverrideCandidateRegistry.ResolveFromConfig(
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId,
            _configuration.TitleBackgroundTerritoryPath,
            _configuration.TitleBackgroundTerritoryTypeId,
            _configuration.TitleBackgroundLayoutLayerFilterKey,
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildAvailableCandidates(manualSlots));

        ImGui.Text("Current effective candidate");
        ImGui.TextWrapped(GetTitleBackgroundOverrideCandidateLabel(candidate));
        ImGui.TextDisabled($"Path: {candidate.TerritoryPath}");
        ImGui.TextDisabled($"TerritoryId: {candidate.TerritoryId} / LayerFilterKey: {candidate.LayerFilterKey}");
        ImGui.TextDisabled($"Source: {candidate.Source} / Compatibility: {candidate.ExpectedCompatibility}");
    }

    private void ApplyTitleBackgroundOverrideCandidate(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _titleBackgroundPendingPresetId = string.Empty;
        TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(_configuration, candidate);
        _configuration.Save();
        _titleScreenBackgroundService.ApplyFromConfiguration();
    }

    private IReadOnlyList<TitleBackgroundCharacterSelectManualCandidateSlot> BuildTitleBackgroundManualCandidateSlots()
    {
        return
        [
            TitleBackgroundCharacterSelectOverrideCandidateRegistry.BuildManualSlot(
                1,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey,
                _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness),
        ];
    }

    private void DrawTitleBackgroundManualCandidateSlot1(TitleBackgroundCharacterSelectManualCandidateSlot slot)
    {
        if (!ImGui.TreeNode("Manual candidate slot 1##TitleBackgroundManualCandidate1"))
        {
            return;
        }

        var changed = false;
        var enabled = _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled;
        if (ImGui.Checkbox("Enable manual candidate slot 1##TitleBackgroundManualCandidate1Enabled", ref enabled))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled = enabled;
            changed = true;
        }

        var displayName = _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName;
        if (ImGui.InputTextWithHint("Display name##TitleBackgroundManualCandidate1DisplayName", "Manual candidate slot 1", ref displayName, 128))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName = displayName.Trim();
            changed = true;
        }

        var territoryPath = _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath;
        if (ImGui.InputTextWithHint("Territory path##TitleBackgroundManualCandidate1TerritoryPath", "ex5/.../level/...", ref territoryPath, 256))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = TitleBackgroundPathHelper.NormalizeTerritoryPathInput(territoryPath);
            changed = true;
        }

        var territoryId = (int)_configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Territory id##TitleBackgroundManualCandidate1TerritoryId", ref territoryId))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId = (uint)Math.Clamp(territoryId, 0, int.MaxValue);
            changed = true;
        }

        var layerFilterKey = (int)_configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("Layer filter key##TitleBackgroundManualCandidate1LayerFilterKey", ref layerFilterKey))
        {
            _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = (uint)Math.Clamp(layerFilterKey, 0, int.MaxValue);
            changed = true;
        }

        var expectedBrightness = _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness;
        if (ImGui.BeginCombo("Expected brightness##TitleBackgroundManualCandidate1ExpectedBrightness", expectedBrightness.ToString()))
        {
            foreach (TitleBackgroundCharacterSelectExpectedBrightness candidate in Enum.GetValues(typeof(TitleBackgroundCharacterSelectExpectedBrightness)))
            {
                if (ImGui.Selectable(candidate.ToString(), expectedBrightness == candidate))
                {
                    _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = candidate;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled($"Manual slot status: {(slot.Valid ? "valid" : slot.ValidationError)}");

        if (changed)
        {
            var updatedSlot = BuildTitleBackgroundManualCandidateSlots()[0];
            if (_configuration.TitleBackgroundCharacterSelectOverrideCandidateId == TitleBackgroundCharacterSelectOverrideCandidateRegistry.ManualSlot1CandidateId
                && TitleBackgroundCharacterSelectOverrideCandidateRegistry.TryCreateManualCandidate(updatedSlot, out var candidate))
            {
                TitleBackgroundCharacterSelectOverrideCandidateRegistry.ApplyToConfiguration(_configuration, candidate);
            }

            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.TreePop();
    }

    private void DrawTitleBackgroundCaptureResult()
    {
        var result = _titleScreenBackgroundService.LastCameraCaptureResult;
        if (!result.HasRun)
        {
            ImGui.TextDisabled("最後の保存結果: なし");
            return;
        }

        var color = result.Success
            ? new Vector4(0.3f, 0.8f, 0.45f, 1f)
            : new Vector4(1f, 0.45f, 0.45f, 1f);
        ImGui.TextColored(color, result.Success ? "最後の保存結果: 成功" : $"最後の保存結果: 失敗 - {result.FailureReason}");

        foreach (var message in result.Messages.Take(10))
        {
            ImGui.TextDisabled(message);
        }
    }

    private void DrawTitleBackgroundAdvancedSettings()
    {
        if (!ImGui.CollapsingHeader("native 診断"))
        {
            return;
        }

        var territoryTypeId = (int)_configuration.TitleBackgroundTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("TerritoryTypeId##TitleBackgroundTerritoryTypeId", ref territoryTypeId))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundTerritoryTypeId = (uint)Math.Clamp(territoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layoutTerritoryTypeId = (int)_configuration.TitleBackgroundLayoutTerritoryTypeId;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutTerritoryTypeId##TitleBackgroundLayoutTerritoryTypeId", ref layoutTerritoryTypeId))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundLayoutTerritoryTypeId = (uint)Math.Clamp(layoutTerritoryTypeId, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        var layerFilterKey = (int)_configuration.TitleBackgroundLayoutLayerFilterKey;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("LayoutLayerFilterKey##TitleBackgroundLayoutLayerFilterKey", ref layerFilterKey))
        {
            ClearTitleBackgroundSelectedPreset();
            _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
            _configuration.TitleBackgroundLayoutLayerFilterKey = (uint)Math.Clamp(layerFilterKey, 0, int.MaxValue);
            _configuration.Save();
            _titleScreenBackgroundService.ApplyFromConfiguration();
        }

        ImGui.Spacing();
        if (ImGui.Button("最後に観測した scene を override 値へコピー（検証用）"))
        {
            if (_titleScreenBackgroundService.TryCopyLastObservedCreateSceneToOverrideConfiguration(out var errorMessage))
            {
                _titleBackgroundPendingPresetId = string.Empty;
                _titleBackgroundSceneCopyMessage = "最後に観測した scene を override 値へコピーしました。";
                _titleBackgroundSceneCopyMessageColor = new Vector4(0.3f, 0.8f, 0.45f, 1f);
            }
            else
            {
                _titleBackgroundSceneCopyMessage = $"scene コピー失敗: {errorMessage}";
                _titleBackgroundSceneCopyMessageColor = new Vector4(1f, 0.45f, 0.45f, 1f);
            }
        }

        ImGui.TextDisabled("同じ scene を再指定する smoke test 用です。見た目の変化は想定しません。");
        ImGui.TextDisabled("CharaSelectOnly smoke ではカメラ調整を無効のままにしてください。コピー時にも Camera override は OFF にします。");
        if (!string.IsNullOrWhiteSpace(_titleBackgroundSceneCopyMessage))
        {
            ImGui.TextColored(_titleBackgroundSceneCopyMessageColor, _titleBackgroundSceneCopyMessage);
        }

        ImGui.Spacing();
        ImGui.Text("native signature");
        ImGui.TextDisabled("signature は場所IDやカットシーンIDではなく、ゲーム実行ファイル内の処理を探すための機械語の目印です。");
        ImGui.TextDisabled("現行clientで独自確認した値だけを入力します。既定では空のままfail-closedします。");
        DrawTitleBackgroundResolverModeInputs();
        DrawTitleBackgroundSignatureInputs();

        if (ImGui.Button("address再解決"))
        {
            NormalizeTitleBackgroundSignatures();
            _configuration.Save();
            _titleScreenBackgroundService.ReloadNativeIntegration();
        }

        ImGui.SameLine();
        if (ImGui.Button("signatureをクリア"))
        {
            _configuration.TitleBackgroundCreateSceneSignature = string.Empty;
            _configuration.TitleBackgroundFixOnSignature = string.Empty;
            _configuration.TitleBackgroundLobbyUpdateSignature = string.Empty;
            _configuration.TitleBackgroundLoadLobbySceneSignature = string.Empty;
            _configuration.TitleBackgroundLobbyCurrentMapSignature = string.Empty;
            _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = string.Empty;
            _configuration.TitleBackgroundSetCameraCurveMidPointSignature = string.Empty;
            _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = string.Empty;
            _configuration.Save();
            _titleScreenBackgroundService.ReloadNativeIntegration();
        }

        ImGui.TextDisabled("BGM / 天候 / 時刻は後続接続用の設定枠です。今回のカメラ保存には使いません。");
    }

    private void DrawTitleBackgroundSignatureInputs()
    {
        var createSceneSignature = _configuration.TitleBackgroundCreateSceneSignature;
        var fixOnSignature = _configuration.TitleBackgroundFixOnSignature;
        var lobbyUpdateSignature = _configuration.TitleBackgroundLobbyUpdateSignature;
        var loadLobbySceneSignature = _configuration.TitleBackgroundLoadLobbySceneSignature;
        var lobbyCurrentMapSignature = _configuration.TitleBackgroundLobbyCurrentMapSignature;
        var calculateLobbyCameraLookAtYSignature = _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature;
        var setCameraCurveMidPointSignature = _configuration.TitleBackgroundSetCameraCurveMidPointSignature;
        var calculateCameraCurveLowAndHighPointSignature = _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature;
        var changed = DrawTitleBackgroundSignatureInput("CreateScene", ref createSceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("FixOn", ref fixOnSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyUpdate", ref lobbyUpdateSignature);
        changed |= DrawTitleBackgroundSignatureInput("LoadLobbyScene", ref loadLobbySceneSignature);
        changed |= DrawTitleBackgroundSignatureInput("LobbyCurrentMap", ref lobbyCurrentMapSignature);
        changed |= DrawTitleBackgroundSignatureInput("CalculateLobbyCameraLookAtY", ref calculateLobbyCameraLookAtYSignature);
        changed |= DrawTitleBackgroundSignatureInput("SetCameraCurveMidPoint", ref setCameraCurveMidPointSignature);
        changed |= DrawTitleBackgroundSignatureInput("CalculateCameraCurveLowAndHighPoint", ref calculateCameraCurveLowAndHighPointSignature);

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCreateSceneSignature = createSceneSignature.Trim();
        _configuration.TitleBackgroundFixOnSignature = fixOnSignature.Trim();
        _configuration.TitleBackgroundLobbyUpdateSignature = lobbyUpdateSignature.Trim();
        _configuration.TitleBackgroundLoadLobbySceneSignature = loadLobbySceneSignature.Trim();
        _configuration.TitleBackgroundLobbyCurrentMapSignature = lobbyCurrentMapSignature.Trim();
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = calculateLobbyCameraLookAtYSignature.Trim();
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = setCameraCurveMidPointSignature.Trim();
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = calculateCameraCurveLowAndHighPointSignature.Trim();
        _configuration.Save();
    }

    private void DrawTitleBackgroundResolverModeInputs()
    {
        var createSceneMode = _configuration.TitleBackgroundCreateSceneResolverMode;
        var lobbyUpdateMode = _configuration.TitleBackgroundLobbyUpdateResolverMode;
        var changed = DrawTitleBackgroundResolverModeInput("CreateScene", ref createSceneMode);
        changed |= DrawTitleBackgroundResolverModeInput("LobbyUpdate", ref lobbyUpdateMode);

        if (!changed)
        {
            return;
        }

        _configuration.TitleBackgroundCreateSceneResolverMode = createSceneMode;
        _configuration.TitleBackgroundLobbyUpdateResolverMode = lobbyUpdateMode;
        _configuration.Save();
    }

    private static bool DrawTitleBackgroundResolverModeInput(string label, ref TitleBackgroundResolverMode mode)
    {
        if (!ImGui.BeginCombo($"{label} resolver##TitleBackground{label}ResolverMode", GetTitleBackgroundResolverModeLabel(mode)))
        {
            return false;
        }

        var changed = false;
        foreach (TitleBackgroundResolverMode candidate in Enum.GetValues(typeof(TitleBackgroundResolverMode)))
        {
            if (ImGui.Selectable(GetTitleBackgroundResolverModeLabel(candidate), mode == candidate))
            {
                mode = candidate;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawTitleBackgroundSignatureInput(string label, ref string signature)
    {
        return ImGui.InputTextWithHint($"{label}##TitleBackground{label}Signature", "xx xx ?? ...", ref signature, 512);
    }

    private void NormalizeTitleBackgroundSignatures()
    {
        _configuration.TitleBackgroundCreateSceneSignature = (_configuration.TitleBackgroundCreateSceneSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundFixOnSignature = (_configuration.TitleBackgroundFixOnSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLobbyUpdateSignature = (_configuration.TitleBackgroundLobbyUpdateSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLoadLobbySceneSignature = (_configuration.TitleBackgroundLoadLobbySceneSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundLobbyCurrentMapSignature = (_configuration.TitleBackgroundLobbyCurrentMapSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = (_configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = (_configuration.TitleBackgroundSetCameraCurveMidPointSignature ?? string.Empty).Trim();
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = (_configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature ?? string.Empty).Trim();
    }

    private void ClearTitleBackgroundSelectedPreset()
    {
        if (string.IsNullOrEmpty(_configuration.TitleBackgroundSelectedPresetId)
            && string.IsNullOrEmpty(_titleBackgroundPendingPresetId))
        {
            return;
        }

        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _titleBackgroundPendingPresetId = string.Empty;
        _titleBackgroundPresetMessage = "手入力により preset 選択を解除しました。";
        _titleBackgroundPresetMessageColor = new Vector4(1f, 0.75f, 0.35f, 1f);
    }

    private static string GetTitleBackgroundRuntimeModeLabel(TitleBackgroundRuntimeMode mode)
    {
        return mode switch
        {
            TitleBackgroundRuntimeMode.ResolveOnly => "準備だけ（address解決）",
            TitleBackgroundRuntimeMode.Disabled => "無効",
            TitleBackgroundRuntimeMode.HookProbe => "診断だけ（見た目変更なし）",
            TitleBackgroundRuntimeMode.CharaSelectOnly => "キャラ選択だけ",
            TitleBackgroundRuntimeMode.TitleAndCharaSelect => "タイトル+キャラ選択",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCharacterSelectBackgroundModeLabel(TitleBackgroundCharacterSelectBackgroundMode mode)
    {
        return TitleBackgroundQuickCheckUiPresenter.GetBackgroundModeUiLabel(mode);
    }

    private static string GetTitleBackgroundCharacterSelectLightingModeLabel(TitleBackgroundCharacterSelectLightingMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharacterSelectLightingMode.Default => "既定",
            TitleBackgroundCharacterSelectLightingMode.DiagnosticsOnly => "診断のみ",
            TitleBackgroundCharacterSelectLightingMode.PreferBrightPreset => "明るい候補推奨",
            TitleBackgroundCharacterSelectLightingMode.PreferBrightLayer => "明るいレイヤー推奨",
            TitleBackgroundCharacterSelectLightingMode.EnvironmentOverrideExperimental => "環境 override（実験）",
            TitleBackgroundCharacterSelectLightingMode.DisableDarkeningExperimental => "暗転抑制（実験）",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundOverrideCandidateLabel(TitleBackgroundCharacterSelectOverrideCandidate candidate)
    {
        return TitleBackgroundQuickCheckUiPresenter.BuildCandidateLabel(candidate);
    }

    private static string GetTitleBackgroundSettingsDisplayModeLabel(TitleBackgroundSettingsDisplayMode mode)
    {
        return mode switch
        {
            TitleBackgroundSettingsDisplayMode.Simple => "Simple",
            TitleBackgroundSettingsDisplayMode.Advanced => "Advanced",
            TitleBackgroundSettingsDisplayMode.DeveloperDiagnostics => "Developer Diagnostics",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCameraFramingModeLabel(TitleBackgroundCharaSelectCameraFramingMode mode)
    {
        return mode switch
        {
            TitleBackgroundCharaSelectCameraFramingMode.Default => "Default",
            TitleBackgroundCharaSelectCameraFramingMode.LowerCamera => "Lower camera",
            TitleBackgroundCharaSelectCameraFramingMode.CenterCharacter => "Center character",
            TitleBackgroundCharaSelectCameraFramingMode.CloserCharacter => "Closer character",
            TitleBackgroundCharaSelectCameraFramingMode.CandidateRecommended => "n4f4 experimental",
            TitleBackgroundCharaSelectCameraFramingMode.CustomExperimental => "Custom experimental",
            _ => mode.ToString(),
        };
    }

    private static string GetTitleBackgroundCharacterVisualStatusLabel(TitleBackgroundCharacterVisualStatus status)
    {
        return status switch
        {
            TitleBackgroundCharacterVisualStatus.Unknown => "Unknown",
            TitleBackgroundCharacterVisualStatus.Visible => "Visible",
            TitleBackgroundCharacterVisualStatus.VisibleButTooSmall => "Too small",
            TitleBackgroundCharacterVisualStatus.VisibleTopDown => "Top-down",
            TitleBackgroundCharacterVisualStatus.NotVisible => "Not visible",
            TitleBackgroundCharacterVisualStatus.Offscreen => "Offscreen",
            _ => status.ToString(),
        };
    }

    private void DrawTitleBackgroundCameraFramingSelector()
    {
        var framingMode = _configuration.TitleBackgroundCharaSelectCameraFramingMode;
        if (ImGui.BeginCombo("Camera framing##TitleBackgroundCameraFraming", GetTitleBackgroundCameraFramingModeLabel(framingMode)))
        {
            foreach (TitleBackgroundCharaSelectCameraFramingMode mode in Enum.GetValues(typeof(TitleBackgroundCharaSelectCameraFramingMode)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCameraFramingModeLabel(mode), framingMode == mode))
                {
                    _configuration.TitleBackgroundCharaSelectCameraFramingMode = mode;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawTitleBackgroundCharacterVisualStatusSelector()
    {
        var visualStatus = _configuration.TitleBackgroundCharacterVisualStatus;
        if (ImGui.BeginCombo("Character visual status##TitleBackgroundCharacterVisualStatus", GetTitleBackgroundCharacterVisualStatusLabel(visualStatus)))
        {
            foreach (TitleBackgroundCharacterVisualStatus status in Enum.GetValues(typeof(TitleBackgroundCharacterVisualStatus)))
            {
                if (ImGui.Selectable(GetTitleBackgroundCharacterVisualStatusLabel(status), visualStatus == status))
                {
                    _configuration.TitleBackgroundCharacterVisualStatus = status;
                    _configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.TextDisabled("Manually record what you see in Character Select.");
    }

    private static string GetTitleBackgroundResolverModeLabel(TitleBackgroundResolverMode mode)
    {
        return mode switch
        {
            TitleBackgroundResolverMode.AutoDiagnosticOnly => "自動診断のみ",
            TitleBackgroundResolverMode.ManualDirectTextProbe => "手動DirectText probe",
            _ => mode.ToString(),
        };
    }

    private bool DrawTitleBackgroundVectorInput(string label, ref float x, ref float y, ref float z)
    {
        ImGui.Text(label);

        ImGui.SetNextItemWidth(90f);
        var changed = ImGui.InputFloat($"X##TitleBackground{label}X", ref x, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        changed |= ImGui.InputFloat($"Y##TitleBackground{label}Y", ref y, 1f, 10f, "%.2f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        changed |= ImGui.InputFloat($"Z##TitleBackground{label}Z", ref z, 1f, 10f, "%.2f");

        if (changed)
        {
            x = TitleBackgroundPreset.SanitizeCoordinate(x);
            y = TitleBackgroundPreset.SanitizeCoordinate(y);
            z = TitleBackgroundPreset.SanitizeCoordinate(z);
        }

        return changed;
    }

    private void ClearTitleBackgroundInputs()
    {
        _configuration.TitleBackgroundOverrideEnabled = false;
        _configuration.TitleBackgroundCameraOverrideEnabled = false;
        _configuration.TitleBackgroundSelectedPresetId = string.Empty;
        _configuration.TitleBackgroundCharacterSelectOverrideCandidateId = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1Enabled = false;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1DisplayName = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryPath = string.Empty;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1TerritoryId = 0;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1LayerFilterKey = 0;
        _configuration.TitleBackgroundCharacterSelectManualCandidate1ExpectedBrightness = TitleBackgroundCharacterSelectExpectedBrightness.Unknown;
        _titleBackgroundPendingPresetId = string.Empty;
        _configuration.TitleBackgroundRuntimeMode = TitleBackgroundRuntimeMode.ResolveOnly;
        _configuration.TitleBackgroundCharacterSelectBackgroundMode = TitleBackgroundCharacterSelectBackgroundMode.SceneOverrideOnly;
        _configuration.TitleBackgroundCharacterSelectLightingMode = TitleBackgroundCharacterSelectLightingMode.Default;
        _configuration.TitleBackgroundCreateSceneResolverMode = TitleBackgroundResolverMode.AutoDiagnosticOnly;
        _configuration.TitleBackgroundLobbyUpdateResolverMode = TitleBackgroundResolverMode.AutoDiagnosticOnly;
        _configuration.TitleBackgroundTerritoryPath = string.Empty;
        _configuration.TitleBackgroundTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutTerritoryTypeId = 0;
        _configuration.TitleBackgroundLayoutLayerFilterKey = 0;
        _configuration.TitleBackgroundCharacterPositionX = 0f;
        _configuration.TitleBackgroundCharacterPositionY = 0f;
        _configuration.TitleBackgroundCharacterPositionZ = 0f;
        _configuration.TitleBackgroundCharacterRotation = 0f;
        _configuration.TitleBackgroundCameraX = 0f;
        _configuration.TitleBackgroundCameraY = 0f;
        _configuration.TitleBackgroundCameraZ = 0f;
        _configuration.TitleBackgroundFocusX = 0f;
        _configuration.TitleBackgroundFocusY = 0f;
        _configuration.TitleBackgroundFocusZ = 0f;
        _configuration.TitleBackgroundFovY = TitleBackgroundPreset.DefaultFovY;
        _configuration.TitleBackgroundWeatherId = 0;
        _configuration.TitleBackgroundTimeOffset = 0;
        _configuration.TitleBackgroundBgmPath = string.Empty;
        _configuration.TitleBackgroundCreateSceneSignature = string.Empty;
        _configuration.TitleBackgroundFixOnSignature = string.Empty;
        _configuration.TitleBackgroundLobbyUpdateSignature = string.Empty;
        _configuration.TitleBackgroundLoadLobbySceneSignature = string.Empty;
        _configuration.TitleBackgroundLobbyCurrentMapSignature = string.Empty;
        _configuration.TitleBackgroundCalculateLobbyCameraLookAtYSignature = string.Empty;
        _configuration.TitleBackgroundSetCameraCurveMidPointSignature = string.Empty;
        _configuration.TitleBackgroundCalculateCameraCurveLowAndHighPointSignature = string.Empty;
        _configuration.Save();
        _titleScreenBackgroundService.ReloadNativeIntegration();
    }
}

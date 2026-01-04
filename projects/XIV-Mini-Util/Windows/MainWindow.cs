// Path: projects/XIV-Mini-Util/Windows/MainWindow.cs
// Description: メイン操作UIを提供しサービスの状態を制御する
// Reason: ユーザーがゲーム内で機能を操作できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTabItemFlags = Dalamud.Bindings.ImGui.ImGuiTabItemFlags;
using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Materia;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.Submarine;
using XivMiniUtil.Windows.Components;

namespace XivMiniUtil.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly DesynthService _desynthService;
    private readonly HomeTab _homeTab;
    private readonly SearchTab _searchTab;
    private readonly SubmarineTab _submarineTab;
    private readonly SettingsTab _settingsTab;

    private DesynthWarningInfo? _warningInfo;
    private bool _showWarningDialog;
    private bool _selectSettingsTab;

    public MainWindow(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        ShopDataCache shopDataCache,
        ShopSearchService shopSearchService,
        SubmarineDataStorage submarineDataStorage,
        DiscordService discordService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
        : base("XIV Mini Util")
    {
        _desynthService = desynthService;
        _homeTab = new HomeTab(configuration, materiaService, desynthService, materiaFeatureEnabled, desynthFeatureEnabled);
        _searchTab = new SearchTab(shopDataCache, shopSearchService);
        _submarineTab = new SubmarineTab(configuration, submarineDataStorage);
        _settingsTab = new SettingsTab(configuration, materiaService, desynthService, shopDataCache, discordService, materiaFeatureEnabled, desynthFeatureEnabled);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(900, 700),
        };

        _desynthService.OnWarningRequired += ShowWarningDialog;
    }

    public new void Toggle()
    {
        IsOpen = !IsOpen;
    }

    public void OpenSettingsTab()
    {
        _selectSettingsTab = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("MainTabs"))
        {
            if (ImGui.BeginTabItem("Home"))
            {
                _homeTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Search"))
            {
                _searchTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Submarines"))
            {
                _submarineTab.Draw();
                ImGui.EndTabItem();
            }

            var settingsFlags = _selectSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Settings", settingsFlags))
            {
                _selectSettingsTab = false;
                _settingsTab.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawWarningDialog();
        DrawResult();
    }

    public void Dispose()
    {
        _desynthService.OnWarningRequired -= ShowWarningDialog;
        _homeTab.Dispose();
        _searchTab.Dispose();
        _submarineTab.Dispose();
        _settingsTab.Dispose();
    }

    public void ShowWarningDialog(DesynthWarningInfo info)
    {
        _warningInfo = info;
        _showWarningDialog = true;
    }

    private void DrawWarningDialog()
    {
        if (_showWarningDialog)
        {
            ImGui.OpenPopup("分解警告");
            _showWarningDialog = false;
        }

        var dialogOpen = true;
        if (ImGui.BeginPopupModal("分解警告", ref dialogOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (_warningInfo != null)
            {
                ImGui.Text("高レベルアイテムの分解を検出しました。");
                ImGui.Text($"アイテム: {_warningInfo.ItemName}");
                ImGui.Text($"レベル: {_warningInfo.ItemLevel} / 最高: {_warningInfo.MaxItemLevel}");
            }

            ImGui.Separator();
            if (ImGui.Button("はい"))
            {
                _desynthService.ConfirmWarning(true);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("いいえ"))
            {
                _desynthService.ConfirmWarning(false);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawResult()
    {
        var resultMessage = _homeTab.LastResultMessage;
        if (!string.IsNullOrWhiteSpace(resultMessage))
        {
            ImGui.Separator();
            ImGui.TextWrapped(resultMessage);
        }
    }
}

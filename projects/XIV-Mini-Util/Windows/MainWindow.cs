// Path: projects/XIV-Mini-Util/Windows/MainWindow.cs
// Description: メイン操作UIを提供しサービスの状態を制御する
// Reason: ユーザーがゲーム内で機能を操作できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/MateriaExtractService.cs, projects/XIV-Mini-Util/Services/DesynthService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTabItemFlags = Dalamud.Bindings.ImGui.ImGuiTabItemFlags;
using XivMiniUtil.Services.Common;
using XivMiniUtil.Services.Checklist;
using XivMiniUtil.Services.Desynth;
using XivMiniUtil.Services.Materia;
using XivMiniUtil.Services.Notification;
using XivMiniUtil.Services.Shop;
using XivMiniUtil.Services.Submarine;
using XivMiniUtil.Windows.Components;

namespace XivMiniUtil.Windows;

public sealed class MainWindow : Window, IDisposable
{
    public static readonly string BuildInfo = GetBuildInfo();

    private readonly HomeTab _homeTab;
    private readonly SearchTab _searchTab;
    private readonly ChecklistTab _checklistTab;
    private readonly SubmarineTab _submarineTab;
    private readonly SettingsTab _settingsTab;

    private bool _selectSettingsTab;

    public MainWindow(
        Configuration configuration,
        MateriaExtractService materiaService,
        DesynthService desynthService,
        InventoryCacheService inventoryCacheService,
        ShopDataCache shopDataCache,
        ShopSearchService shopSearchService,
        ChecklistService checklistService,
        SubmarineDataStorage submarineDataStorage,
        DiscordService discordService,
        DutyReadyNotificationService dutyReadyNotificationService,
        bool materiaFeatureEnabled,
        bool desynthFeatureEnabled)
        : base($"XIV Mini Util [{BuildInfo}]")
    {
        _homeTab = new HomeTab(configuration, materiaService, desynthService, inventoryCacheService, materiaFeatureEnabled, desynthFeatureEnabled);
        _searchTab = new SearchTab(shopDataCache, shopSearchService);
        _checklistTab = new ChecklistTab(configuration, checklistService);
        _submarineTab = new SubmarineTab(configuration, submarineDataStorage);
        _settingsTab = new SettingsTab(configuration, materiaService, desynthService, shopDataCache, discordService, checklistService, dutyReadyNotificationService, materiaFeatureEnabled, desynthFeatureEnabled);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(900, 700),
        };

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
            var homeOpen = ImGui.BeginTabItem("Home");
            if (homeOpen)
            {
                _homeTab.SetVisible(true);
                _homeTab.Draw();
                ImGui.EndTabItem();
            }
            else
            {
                _homeTab.SetVisible(false);
            }

            if (ImGui.BeginTabItem("Search"))
            {
                _searchTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Checklist"))
            {
                _checklistTab.Draw();
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

        DrawResult();
    }

    public void Dispose()
    {
        _homeTab.Dispose();
        _searchTab.Dispose();
        _checklistTab.Dispose();
        _submarineTab.Dispose();
        _settingsTab.Dispose();
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

    private static string GetBuildInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            return version;
        }

        var buildDate = File.GetLastWriteTime(location);
        return $"{version} / {buildDate:MM-dd HH:mm}";
    }
}

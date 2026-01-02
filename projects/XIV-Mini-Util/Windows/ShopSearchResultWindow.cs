// Path: projects/XIV-Mini-Util/Windows/ShopSearchResultWindow.cs
// Description: 販売場所検索の詳細リストを表示する
// Reason: 複数件ある場合にユーザーが比較できるようにするため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Services/MapService.cs, projects/XIV-Mini-Util/Models/DomainModels.cs
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiSelectableFlags = Dalamud.Bindings.ImGui.ImGuiSelectableFlags;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using ImGuiTableColumnFlags = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using XivMiniUtil;
using XivMiniUtil.Services;

namespace XivMiniUtil.Windows;

public sealed class ShopSearchResultWindow : Window, IDisposable
{
    private readonly MapService _mapService;
    private readonly TeleportService _teleportService;
    private SearchResult? _result;
    private int _selectedIndex = -1;

    public ShopSearchResultWindow(MapService mapService, TeleportService teleportService)
        : base("販売場所検索")
    {
        _mapService = mapService;
        _teleportService = teleportService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 360),
            MaximumSize = new Vector2(900, 720),
        };
    }

    public void SetResult(SearchResult result)
    {
        _result = result;
        _selectedIndex = -1;
    }

    public override void Draw()
    {
        if (_result == null)
        {
            ImGui.Text("検索結果がありません。");
            return;
        }

        if (!_result.Success)
        {
            ImGui.Text(_result.ErrorMessage ?? "検索結果がありません。");
            return;
        }

        DrawItemInfo(_result);
        ImGui.Separator();
        DrawLocationTable(_result.Locations);
        DrawSelectedDetails(_result.Locations);
    }

    private void DrawItemInfo(SearchResult result)
    {
        ImGui.Text($"アイテム: {result.ItemName}");

        // 価格情報を集計
        var prices = result.Locations
            .Where(l => l.Price > 0)
            .Select(l => l.Price)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        ImGui.SameLine();
        if (prices.Count == 0)
        {
            ImGui.TextDisabled("(価格情報なし)");
        }
        else if (prices.Count == 1)
        {
            ImGui.Text($"  価格: {prices[0]:N0} ギル");
        }
        else
        {
            ImGui.Text($"  価格: {prices[0]:N0} ~ {prices[^1]:N0} ギル");
        }

        ImGui.Text($"販売店舗: {result.Locations.Count}件");
    }

    public void Dispose()
    {
        // WindowSystem破棄時に備えた明示的なフック
    }

    private void DrawLocationTable(IReadOnlyList<ShopLocationInfo> locations)
    {
        if (!ImGui.BeginTable("ShopSearchResults", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            return;
        }

        ImGui.TableSetupColumn("エリア", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("NPC", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("座標", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("テレポ", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        for (var i = 0; i < locations.Count; i++)
        {
            var location = locations[i];
            ImGui.TableNextRow();

            // エリア列
            ImGui.TableSetColumnIndex(0);
            var label = string.IsNullOrWhiteSpace(location.SubAreaName)
                ? location.AreaName
                : $"{location.AreaName} {location.SubAreaName}";

            if (ImGui.Selectable(label, _selectedIndex == i, ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedIndex = i;
                _mapService.SetMapMarker(location);
            }

            // NPC列
            ImGui.TableSetColumnIndex(1);
            ImGui.Text(location.NpcName);

            // 座標列
            ImGui.TableSetColumnIndex(2);
            ImGui.Text($"X:{location.MapX:0.0} Y:{location.MapY:0.0}");

            // テレポ列（上位3件のみ表示）
            ImGui.TableSetColumnIndex(3);
            if (i < 3)
            {
                DrawRowTeleportButton(location, i);
            }
            else
            {
                ImGui.TextDisabled("-");
            }
        }

        ImGui.EndTable();
    }

    private void DrawRowTeleportButton(ShopLocationInfo location, int rowIndex)
    {
        var aetheryteInfo = _teleportService.GetNearestAetheryteInfo(location);
        if (aetheryteInfo == null)
        {
            ImGui.TextDisabled("-");
            return;
        }

        var isUnlocked = _teleportService.IsAetheryteUnlocked(aetheryteInfo.AetheryteId);
        var buttonId = $"{aetheryteInfo.Name}##{rowIndex}";

        if (!isUnlocked)
        {
            ImGui.BeginDisabled();
            ImGui.SmallButton(buttonId);
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.SmallButton(buttonId))
            {
                _teleportService.TeleportToNearestAetheryte(location);
            }
        }
    }

    private void DrawSelectedDetails(IReadOnlyList<ShopLocationInfo> locations)
    {
        if (_selectedIndex < 0 || _selectedIndex >= locations.Count)
        {
            return;
        }

        var location = locations[_selectedIndex];
        ImGui.Separator();

        // ショップ詳細を横並びで表示
        ImGui.Text($"ショップ: {location.ShopName}");
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        if (!string.IsNullOrEmpty(location.ConditionNote) && location.ConditionNote != "なし")
        {
            ImGui.Text($"条件: {location.ConditionNote}");
        }
        else
        {
            ImGui.TextDisabled("条件なし");
        }
    }
}

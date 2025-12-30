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
using XivMiniUtil;
using XivMiniUtil.Services;

namespace XivMiniUtil.Windows;

public sealed class ShopSearchResultWindow : Window, IDisposable
{
    private readonly MapService _mapService;
    private SearchResult? _result;
    private int _selectedIndex = -1;

    public ShopSearchResultWindow(MapService mapService)
        : base("販売場所検索")
    {
        _mapService = mapService;

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

        ImGui.Text($"アイテム: {_result.ItemName}");
        ImGui.Text($"件数: {_result.Locations.Count}");
        ImGui.Separator();

        DrawLocationTable(_result.Locations);
        DrawSelectedDetails(_result.Locations);
    }

    public void Dispose()
    {
        // WindowSystem破棄時に備えた明示的なフック
    }

    private void DrawLocationTable(IReadOnlyList<ShopLocationInfo> locations)
    {
        if (!ImGui.BeginTable("ShopSearchResults", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            return;
        }

        ImGui.TableSetupColumn("エリア");
        ImGui.TableSetupColumn("NPC");
        ImGui.TableSetupColumn("座標");
        ImGui.TableHeadersRow();

        for (var i = 0; i < locations.Count; i++)
        {
            var location = locations[i];
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            var label = string.IsNullOrWhiteSpace(location.SubAreaName)
                ? location.AreaName
                : $"{location.AreaName} {location.SubAreaName}";

            if (ImGui.Selectable(label, _selectedIndex == i, ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedIndex = i;
                // 選択した販売場所に合わせてマップピンを更新する
                _mapService.SetMapMarker(location);
            }

            ImGui.TableSetColumnIndex(1);
            ImGui.Text(location.NpcName);

            ImGui.TableSetColumnIndex(2);
            ImGui.Text($"X:{location.MapX:0.0} Y:{location.MapY:0.0}");
        }

        ImGui.EndTable();
    }

    private void DrawSelectedDetails(IReadOnlyList<ShopLocationInfo> locations)
    {
        if (_selectedIndex < 0 || _selectedIndex >= locations.Count)
        {
            return;
        }

        var location = locations[_selectedIndex];
        ImGui.Separator();
        ImGui.Text("詳細情報");
        ImGui.Text($"ショップ名: {location.ShopName}");
        ImGui.Text($"NPC: {location.NpcName}");
        ImGui.Text($"エリア: {location.AreaName}");
        if (!string.IsNullOrWhiteSpace(location.SubAreaName))
        {
            ImGui.Text($"サブエリア: {location.SubAreaName}");
        }

        var priceLabel = location.Price > 0 ? $"{location.Price:N0} ギル" : "未取得";
        ImGui.Text($"価格: {priceLabel}");
        ImGui.Text($"必要条件: {location.ConditionNote}");
    }
}

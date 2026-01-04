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
    private const int MaxDisplayResults = 10;
    private const int MaxTeleportButtons = 10;

    private readonly MapService _mapService;
    private readonly TeleportService _teleportService;
    private readonly Configuration _configuration;
    private SearchResult? _result;
    private IReadOnlyList<ShopLocationInfo> _displayLocations = Array.Empty<ShopLocationInfo>();
    private readonly Dictionary<int, AetheryteInfo?> _aetheryteInfoCache = new();
    private readonly Dictionary<int, bool> _aetheryteUnlockCache = new();
    private int _selectedIndex = -1;
    private string _priceSummary = string.Empty;
    private bool _priceSummaryDisabled;
    private int _locationCount;

    public ShopSearchResultWindow(MapService mapService, TeleportService teleportService, Configuration configuration)
        : base("販売場所検索")
    {
        _mapService = mapService;
        _teleportService = teleportService;
        _configuration = configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 360),
            MaximumSize = new Vector2(900, 720),
        };
    }

    public void SetResult(SearchResult result)
    {
        _result = result;
        _displayLocations = result.Locations.Take(MaxDisplayResults).ToList();
        _locationCount = result.Locations.Count;
        _priceSummary = BuildPriceSummary(result.Locations, out _priceSummaryDisabled);
        _aetheryteInfoCache.Clear();
        _aetheryteUnlockCache.Clear();
        _selectedIndex = -1;

        // 描画中に重い処理をしないため、表示対象の情報を先にキャッシュ
        for (var i = 0; i < _displayLocations.Count; i++)
        {
            var location = _displayLocations[i];
            var aetheryteInfo = _teleportService.GetNearestAetheryteInfo(location);
            _aetheryteInfoCache[i] = aetheryteInfo;
            _aetheryteUnlockCache[i] = aetheryteInfo != null
                && _teleportService.IsAetheryteUnlocked(aetheryteInfo.AetheryteId);
        }
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
        DrawLocationTable(_displayLocations);
        DrawSelectedDetails(_displayLocations);
    }

    private void DrawItemInfo(SearchResult result)
    {
        ImGui.Text($"アイテム: {result.ItemName}");

        ImGui.SameLine();
        if (_priceSummaryDisabled)
        {
            ImGui.TextDisabled(_priceSummary);
        }
        else
        {
            ImGui.Text(_priceSummary);
        }

        ImGui.Text($"販売店舗: {_locationCount}件");
        if (_locationCount > MaxDisplayResults)
        {
            ImGui.TextDisabled($"表示: 上位{MaxDisplayResults}件のみ");
        }
    }

    private static string BuildPriceSummary(IReadOnlyList<ShopLocationInfo> locations, out bool isDisabled)
    {
        var prices = locations
            .Where(l => l.Price > 0)
            .Select(l => l.Price)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (prices.Count == 0)
        {
            isDisabled = true;
            return "(価格情報なし)";
        }

        if (prices.Count == 1)
        {
            isDisabled = false;
            return $"  価格: {prices[0]:N0} ギル";
        }

        isDisabled = false;
        return $"  価格: {prices[0]:N0} ~ {prices[^1]:N0} ギル";
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

            // 同名エリアが複数行に出るため、IDは行番号でユニーク化する
            var selectableId = $"{label}##{i}";
            if (ImGui.Selectable(selectableId, _selectedIndex == i, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap))
            {
                _selectedIndex = i;
                _mapService.SetMapMarker(location);
                if (_configuration.ShopSearchAutoTeleportEnabled)
                {
                    _teleportService.TeleportToNearestAetheryte(location);
                }
            }
            // 行全体のSelectableが他の操作を奪わないようにする
            ImGui.SetItemAllowOverlap();

            // NPC列
            ImGui.TableSetColumnIndex(1);
            ImGui.Text(location.NpcName);

            // 座標列
            ImGui.TableSetColumnIndex(2);
            ImGui.Text($"X:{location.MapX:0.0} Y:{location.MapY:0.0}");

            // テレポ列（上位3件のみ表示）
            ImGui.TableSetColumnIndex(3);
            ImGui.PushID(i);
            if (i < MaxTeleportButtons)
            {
                DrawRowTeleportButton(location, i);
            }
            else
            {
                ImGui.TextDisabled("-");
            }
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawRowTeleportButton(ShopLocationInfo location, int rowIndex)
    {
        if (!_aetheryteInfoCache.TryGetValue(rowIndex, out var aetheryteInfo))
        {
            aetheryteInfo = _teleportService.GetNearestAetheryteInfo(location);
            _aetheryteInfoCache[rowIndex] = aetheryteInfo;
        }
        if (aetheryteInfo == null)
        {
            ImGui.TextDisabled("-");
            return;
        }

        if (!_aetheryteUnlockCache.TryGetValue(rowIndex, out var isUnlocked))
        {
            isUnlocked = _teleportService.IsAetheryteUnlocked(aetheryteInfo.AetheryteId);
            _aetheryteUnlockCache[rowIndex] = isUnlocked;
        }
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

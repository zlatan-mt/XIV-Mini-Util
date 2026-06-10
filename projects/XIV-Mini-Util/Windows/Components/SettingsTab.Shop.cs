// Path: projects/XIV-Mini-Util/Windows/Components/SettingsTab.Shop.cs
// Description: ショップ検索関連の設定UI
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using ImGuiTableColumnFlags = Dalamud.Bindings.ImGui.ImGuiTableColumnFlags;
using ImGuiTableFlags = Dalamud.Bindings.ImGui.ImGuiTableFlags;
using XivMiniUtil.Services.Shop;

namespace XivMiniUtil.Windows.Components;

public sealed partial class SettingsTab
{
    private void DrawShopSearchSettings()
    {
        ImGui.Text("販売場所検索");
        ImGui.Separator();

        var cacheReady = _shopDataCache.IsInitialized;
        if (!cacheReady)
        {
            ImGui.Text("ショップデータを準備中です。");
        }

        DrawShopDataCacheStatus();

        var echoEnabled = _configuration.ShopSearchEchoEnabled;
        if (ImGui.Checkbox("チャットに検索結果を表示", ref echoEnabled))
        {
            _configuration.ShopSearchEchoEnabled = echoEnabled;
            _configuration.Save();
        }

        var windowEnabled = _configuration.ShopSearchWindowEnabled;
        if (ImGui.Checkbox("検索結果ウィンドウを表示（4件以上）", ref windowEnabled))
        {
            _configuration.ShopSearchWindowEnabled = windowEnabled;
            _configuration.Save();
        }

        var autoTeleportEnabled = _configuration.ShopSearchAutoTeleportEnabled;
        if (ImGui.Checkbox("検索時/マップピン時に自動テレポ", ref autoTeleportEnabled))
        {
            _configuration.ShopSearchAutoTeleportEnabled = autoTeleportEnabled;
            _configuration.Save();
        }

        var verboseLogging = _configuration.ShopDataVerboseLogging;
        if (ImGui.Checkbox("ショップデータ詳細ログを有効化", ref verboseLogging))
        {
            _configuration.ShopDataVerboseLogging = verboseLogging;
            _configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Text("Universalis検索");

        var showTopThree = _configuration.UniversalisShowTopThreeListings;
        if (ImGui.Checkbox("3位までの安値を表示", ref showTopThree))
        {
            _configuration.UniversalisShowTopThreeListings = showTopThree;
            _configuration.Save();
        }

        var searchRegionWide = _configuration.UniversalisSearchRegionWide;
        if (ImGui.Checkbox("データセンター外も検索", ref searchRegionWide))
        {
            _configuration.UniversalisSearchRegionWide = searchRegionWide;
            _configuration.Save();
        }

        ImGui.Separator();
        DrawShopSearchPriorityList();
        ImGui.Separator();
        if (!cacheReady)
        {
            ImGui.BeginDisabled();
        }

        DrawShopSearchAddArea();

        if (!cacheReady)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawShopDataCacheStatus()
    {
        var status = _shopDataCache.BuildStatus;
        if (status.State == ShopCacheBuildState.Running)
        {
            ImGui.Text($"構築中: {status.Phase}");
            if (!string.IsNullOrWhiteSpace(status.Message))
            {
                ImGui.TextDisabled(status.Message);
            }

            if (status.Processed > 0)
            {
                ImGui.TextDisabled($"処理件数: {status.Processed:N0}");
            }

            if (ImGui.Button("構築をキャンセル"))
            {
                _shopDataCache.CancelBuild();
            }
        }
        else
        {
            if (ImGui.Button("ショップデータを再構築"))
            {
                _ = _shopDataCache.RebuildAsync("手動再構築");
            }

            if (status.State == ShopCacheBuildState.Canceled)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("構築はキャンセルされました。");
            }
            else if (status.State == ShopCacheBuildState.Failed)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "構築に失敗しました。");
            }
        }

        ImGui.Separator();
    }

    private void DrawShopSearchPriorityList()
    {
        ImGui.Text("エリア優先度");

        var priorities = _configuration.ShopSearchAreaPriority;
        if (priorities.Count == 0)
        {
            ImGui.Text("優先度リストが空です。");
        }

        if (ImGui.BeginTable("ShopPriorityTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("エリア");
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 140f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < priorities.Count; i++)
            {
                var territoryId = priorities[i];
                var territoryName = _shopDataCache.GetTerritoryName(territoryId);
                var label = string.IsNullOrWhiteSpace(territoryName)
                    ? $"不明 (ID:{territoryId})"
                    : territoryName;

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(label);

                ImGui.TableSetColumnIndex(1);
                ImGui.PushID(i);

                if (ImGui.Button("↑") && i > 0)
                {
                    (priorities[i - 1], priorities[i]) = (priorities[i], priorities[i - 1]);
                    _configuration.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("↓") && i < priorities.Count - 1)
                {
                    (priorities[i + 1], priorities[i]) = (priorities[i], priorities[i + 1]);
                    _configuration.Save();
                }

                ImGui.SameLine();
                if (ImGui.Button("削除"))
                {
                    priorities.RemoveAt(i);
                    _configuration.Save();
                    ImGui.PopID();
                    break;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (ImGui.Button("優先度をリセット"))
        {
            _configuration.ResetShopSearchAreaPriority();
        }
    }

    private void DrawShopSearchAddArea()
    {
        ImGui.Text("エリア追加");
        ImGui.InputTextWithHint("##ShopAreaFilter", "エリア名で検索", ref _shopAreaFilter, 64);

        var priorities = _configuration.ShopSearchAreaPriority;
        UpdateFilteredTerritories(priorities);

        if (ImGui.BeginCombo("エリア追加", "追加するエリアを選択"))
        {
            if (_cachedFilteredTerritories.Count == 0)
            {
                ImGui.Text("候補がありません。");
            }

            foreach (var group in _cachedFilteredTerritories)
            {
                if (ImGui.Selectable(group.TerritoryName))
                {
                    // 同名エリアは代表IDのみ追加する
                    if (!priorities.Contains(group.RepresentativeTerritoryTypeId))
                    {
                        priorities.Add(group.RepresentativeTerritoryTypeId);
                        _configuration.Save();
                    }
                    _shopAreaFilter = string.Empty;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void UpdateFilteredTerritories(IReadOnlyList<uint> priorities)
    {
        var priorityHash = ComputePriorityHash(priorities);
        var cacheVersion = _shopDataCache.BuildVersion;

        if (_cachedShopAreaFilter == _shopAreaFilter
            && _cachedPriorityHash == priorityHash
            && _cachedTerritoryGroupsVersion == cacheVersion)
        {
            return;
        }

        var priorityNames = BuildPriorityTerritoryNames(priorities);
        var groups = _shopDataCache.GetShopTerritoryGroups();
        _cachedFilteredTerritories = groups
            .Where(group => !priorityNames.Contains(group.TerritoryName))
            .Where(group => string.IsNullOrWhiteSpace(_shopAreaFilter)
                || group.TerritoryName.Contains(_shopAreaFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cachedShopAreaFilter = _shopAreaFilter;
        _cachedPriorityHash = priorityHash;
        _cachedTerritoryGroupsVersion = cacheVersion;
    }

    private static int ComputePriorityHash(IReadOnlyList<uint> priorities)
    {
        var hash = new HashCode();
        foreach (var priority in priorities)
        {
            hash.Add(priority);
        }
        return hash.ToHashCode();
    }

    private HashSet<string> BuildPriorityTerritoryNames(IReadOnlyList<uint> priorities)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var territoryId in priorities)
        {
            var territoryName = _shopDataCache.GetTerritoryName(territoryId);
            if (!string.IsNullOrWhiteSpace(territoryName))
            {
                names.Add(territoryName);
            }
        }
        return names;
    }
}

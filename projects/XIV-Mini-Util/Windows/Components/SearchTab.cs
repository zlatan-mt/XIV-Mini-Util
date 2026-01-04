// Path: projects/XIV-Mini-Util/Windows/Components/SearchTab.cs
// Description: ショップ検索タブのUIと検索処理を担当する
// Reason: MainWindowから検索の状態管理を分離するため
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ImGui = Dalamud.Bindings.ImGui.ImGui;
using XivMiniUtil.Services.Shop;

namespace XivMiniUtil.Windows.Components;

public sealed class SearchTab : ITabComponent
{
    private readonly ShopDataCache _shopDataCache;
    private readonly ShopSearchService _shopSearchService;
    private string _searchQuery = string.Empty;
    private List<(uint Id, string Name)> _searchResults = new();
    private bool _isSearching;
    private string? _searchStatusMessage;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    public SearchTab(ShopDataCache shopDataCache, ShopSearchService shopSearchService)
    {
        _shopDataCache = shopDataCache;
        _shopSearchService = shopSearchService;
    }

    public void Draw()
    {
        ImGui.Text("アイテム名で販売場所を検索");
        ImGui.Separator();

        if (!_shopDataCache.IsInitialized)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "ショップデータを準備中です...");
            return;
        }

        var enterPressed = ImGui.InputTextWithHint("##ItemNameSearch", "アイテム名を入力...", ref _searchQuery, 100, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();

        if ((ImGui.Button("検索") || enterPressed) && !string.IsNullOrWhiteSpace(_searchQuery))
        {
            _ = ExecuteSearchAsync(_searchQuery);
        }

        if (_isSearching)
        {
            ImGui.Text("検索中...");
        }
        else if (_searchStatusMessage != null)
        {
            ImGui.TextWrapped(_searchStatusMessage);
        }

        ImGui.Separator();

        ImGui.BeginChild("SearchResults", new Vector2(0, -1), true);

        lock (_searchLock)
        {
            if (_searchResults.Count > 0)
            {
                ImGui.Text($"検索結果: {_searchResults.Count}件");
                foreach (var (id, name) in _searchResults)
                {
                    if (ImGui.Selectable($"{name}##{id}"))
                    {
                        _shopSearchService.Search(id);
                    }
                }
            }
            else if (!_isSearching && !string.IsNullOrEmpty(_searchStatusMessage) && _searchStatusMessage.Contains("0件"))
            {
                ImGui.TextDisabled("該当なし");
            }
        }

        ImGui.EndChild();
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }

    private async Task ExecuteSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _isSearching = true;
        _searchStatusMessage = null;

        try
        {
            // UIスレッドをブロックしないようにバックグラウンドで実行
            var trimmedQuery = query.Trim();
            var results = await Task.Run(
                () => _shopDataCache.SearchItemsByName(trimmedQuery, 50).ToList(),
                token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            lock (_searchLock)
            {
                _searchResults = results;
                _searchStatusMessage = results.Count == 0 ? "該当するアイテムがありません（0件）" : null;
            }
        }
        catch (Exception ex)
        {
            if (ex is not TaskCanceledException)
            {
                _searchStatusMessage = $"検索エラー: {ex.Message}";
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _isSearching = false;
            }
        }
    }
}

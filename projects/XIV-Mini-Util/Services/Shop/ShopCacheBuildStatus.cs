// Path: projects/XIV-Mini-Util/Services/ShopCacheBuildStatus.cs
// Description: ショップデータ構築の進捗をUIに伝えるための状態モデル
// Reason: ビルド進捗表示とキャンセル判定を簡潔に扱うため
// RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopDataCache.cs, projects/XIV-Mini-Util/Windows/MainWindow.cs
namespace XivMiniUtil.Services.Shop;

internal enum ShopCacheBuildState
{
    Idle,
    Running,
    Completed,
    Canceled,
    Failed,
}

internal sealed record ShopCacheBuildStatus(
    ShopCacheBuildState State,
    string Phase,
    string Message,
    int Processed,
    int Total)
{
    public static ShopCacheBuildStatus Idle =>
        new(ShopCacheBuildState.Idle, "Idle", string.Empty, 0, 0);
}

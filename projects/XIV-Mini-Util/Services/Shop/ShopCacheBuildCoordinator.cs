// Path: projects/XIV-Mini-Util/Services/Shop/ShopCacheBuildCoordinator.cs
// Description: Shop cache buildのgeneration・再利用・cancelを管理する
// Reason: sheet構築処理から並行実行のライフサイクルを分離するため

namespace XivMiniUtil.Services.Shop;

internal sealed class ShopCacheBuildCoordinator : IDisposable
{
    private readonly object syncRoot = new();
    private Task? currentTask;
    private CancellationTokenSource? cancellation;

    public int Generation { get; private set; }

    public Task Start(
        bool rebuild,
        Func<int, CancellationToken, Task> build)
    {
        lock (syncRoot)
        {
            if (!rebuild && currentTask != null)
            {
                return currentTask;
            }

            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();

            var generation = ++Generation;
            var token = cancellation.Token;
            currentTask = Task.Run(() => build(generation, token), token);
            return currentTask;
        }
    }

    public void Cancel()
    {
        lock (syncRoot)
        {
            cancellation?.Cancel();
        }
    }

    public bool IsCurrent(int generation) => generation == Generation;

    public void Dispose()
    {
        lock (syncRoot)
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }
    }
}

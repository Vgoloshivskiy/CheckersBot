using Checkers.Application;
using Checkers.Engine;
using Checkers.Engine.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Checkers.Tests;

public class EnginePoolTests
{
    private sealed class FakeAdapter : IEngineAdapter
    {
        private int _active;

        public FakeAdapter(int id, int databasePieces = 8)
        {
            Id = id;
            Info = new HostInfo("Kingsrow-English", "about", databasePieces, "layout");
        }

        public int Id { get; }
        public HostInfo? Info { get; }
        public bool Started { get; private set; }
        public int Searches { get; private set; }
        public int MaxConcurrent { get; private set; }
        public string? Position { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task SetPositionAsync(string positionPdn, CancellationToken cancellationToken)
        {
            Position = positionPdn;
            return Task.CompletedTask;
        }

        public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(20, cancellationToken);
                Searches++;
                return new SearchResponse($"worker{Id}", Array.Empty<string>(), 0, 1, 1, false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFactory : IEngineAdapterFactory
    {
        public List<FakeAdapter> Adapters { get; } = new();
        public int DatabasePieces { get; set; } = 8;

        public IEngineAdapter Create(int workerId)
        {
            var adapter = new FakeAdapter(workerId, DatabasePieces);
            Adapters.Add(adapter);
            return adapter;
        }
    }

    private static EnginePool CreatePool(FakeFactory factory, int workers) =>
        new(factory, Options.Create(new EngineOptions { Workers = workers }), NullLogger<EnginePool>.Instance);

    [Fact]
    public async Task Start_warms_up_every_worker_and_reports_the_engine()
    {
        var factory = new FakeFactory();
        await using var pool = CreatePool(factory, 2);
        Assert.False(pool.IsReady);

        await pool.StartAsync(CancellationToken.None);

        Assert.True(pool.IsReady);
        Assert.Equal(2, pool.WorkerCount);
        Assert.All(factory.Adapters, a => Assert.True(a.Started));
        Assert.Equal("Kingsrow-English", pool.Info.Name);
        Assert.Equal(8, pool.Info.DatabasePieces);
    }

    [Fact]
    public async Task Requests_are_refused_until_the_pool_is_ready()
    {
        await using var pool = CreatePool(new FakeFactory(), 2);

        await Assert.ThrowsAsync<EngineUnavailableException>(
            () => pool.SearchAsync("B:W21:B10", new SearchRequest(10), CancellationToken.None));
    }

    [Fact]
    public async Task Requests_are_spread_round_robin_over_the_workers()
    {
        var factory = new FakeFactory();
        await using var pool = CreatePool(factory, 2);
        await pool.StartAsync(CancellationToken.None);

        var answers = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            answers.Add((await pool.SearchAsync("B:W21:B10", new SearchRequest(10), CancellationToken.None)).BestMove);
        }

        Assert.Equal(new[] { "worker1", "worker2", "worker1", "worker2" }, answers);
        Assert.Equal("B:W21:B10", factory.Adapters[0].Position);
    }

    [Fact]
    public async Task A_worker_handles_one_request_at_a_time()
    {
        var factory = new FakeFactory();
        await using var pool = CreatePool(factory, 1);
        await pool.StartAsync(CancellationToken.None);

        var searches = Enumerable.Range(0, 6)
            .Select(_ => pool.SearchAsync("B:W21:B10", new SearchRequest(10), CancellationToken.None));
        await Task.WhenAll(searches);

        Assert.Equal(6, factory.Adapters[0].Searches);
        Assert.Equal(1, factory.Adapters[0].MaxConcurrent);
    }

    [Fact]
    public async Task The_smallest_database_any_worker_reports_is_used()
    {
        var factory = new FakeFactory { DatabasePieces = 6 };
        await using var pool = CreatePool(factory, 2);

        await pool.StartAsync(CancellationToken.None);

        Assert.Equal(6, pool.Info.DatabasePieces);
    }

    [Fact]
    public async Task A_request_waiting_for_a_busy_worker_can_be_cancelled()
    {
        var factory = new FakeFactory();
        await using var pool = CreatePool(factory, 1);
        await pool.StartAsync(CancellationToken.None);

        var busy = pool.SearchAsync("B:W21:B10", new SearchRequest(10), CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pool.SearchAsync("B:W21:B10", new SearchRequest(10), cts.Token));
        await busy;
    }
}

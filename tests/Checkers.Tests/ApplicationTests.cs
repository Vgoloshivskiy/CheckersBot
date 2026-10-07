using Checkers.Application;
using Checkers.Engine.Protocol;
using Xunit;

namespace Checkers.Tests;

public class ApplicationTests
{
    private const string Midgame = "W:W17,18,21,22,23,24,26,27,28,29,30,32:B1,2,3,4,6,7,8,9,10,11,12,16";
    private const string DatabasePosition = "B:WK7,K10,18:BK1,K3,K4";   // 6 pieces, no pending capture

    private sealed class FakeGateway : IEngineGateway
    {
        public bool IsReady { get; set; } = true;
        public int WorkerCount => 1;
        public EngineInfo Info { get; set; } = new("fake", 0, string.Empty);
        public List<(string Pdn, SearchRequest Request)> Calls { get; } = new();

        /// <summary>By default: play the first legal move, as a bare from-to pair, and report a normal search.</summary>
        public Func<string, SearchRequest, SearchResponse> Respond { get; set; } = (pdn, _) =>
        {
            Pdn.TryParse(pdn, out var board, out _);
            var move = MoveGenerator.Generate(board!)[0];
            return new SearchResponse(move.Notation, new[] { $"{move.From}-{move.To}" }, 25, 9, 1000, false);
        };

        public Task<SearchResponse> SearchAsync(string positionPdn, SearchRequest request, CancellationToken cancellationToken)
        {
            Calls.Add((positionPdn, request));
            return Task.FromResult(Respond(positionPdn, request));
        }
    }

    private static MoveSuggestService Create(FakeGateway gateway) =>
        new(
            gateway,
            new LevelPolicy(new LevelsOptions(), new LimitsOptions()),
            new LimitsOptions(),
            new LruTtlCache<string, SuggestResult>(100, TimeSpan.FromMinutes(15), TimeProvider.System));

    private static PreparedSuggest Prepare(MoveSuggestService service, string position, string? level = "medium") =>
        service.Prepare("checkers-8x8", "PDN", position, level, null);

    [Fact]
    public async Task A_normal_search_uses_the_level_limits_and_returns_a_legal_move()
    {
        var gateway = new FakeGateway();
        var service = Create(gateway);

        var result = await service.SuggestAsync(Prepare(service, Midgame, "medium"), CancellationToken.None);

        var call = Assert.Single(gateway.Calls);
        Assert.Equal(250, call.Request.MoveTimeMs);
        Assert.Equal(12, call.Request.MaxDepth);
        Assert.False(result.TablebaseHit);
        Assert.Equal(25, result.ScoreOrWdl);
        Assert.Equal(9, result.Depth);
        Assert.Equal("pdn:" + call.Pdn, result.PositionKey);

        Pdn.TryParse(Midgame, out var board, out _);
        Assert.Contains(MoveGenerator.Generate(board!), m => m.Notation == result.BestMove);
    }

    [Fact]
    public async Task The_same_request_is_answered_from_the_cache()
    {
        var gateway = new FakeGateway();
        var service = Create(gateway);
        var prepared = Prepare(service, Midgame);

        var first = await service.SuggestAsync(prepared, CancellationToken.None);
        var second = await service.SuggestAsync(prepared, CancellationToken.None);

        Assert.Single(gateway.Calls);
        Assert.False(first.Cached);
        Assert.True(second.Cached);
        Assert.Equal(first.BestMove, second.BestMove);
    }

    [Fact]
    public async Task A_different_level_is_not_served_from_the_cache()
    {
        var gateway = new FakeGateway();
        var service = Create(gateway);

        await service.SuggestAsync(Prepare(service, Midgame, "weak"), CancellationToken.None);
        await service.SuggestAsync(Prepare(service, Midgame, "strong"), CancellationToken.None);

        Assert.Equal(2, gateway.Calls.Count);
        Assert.Equal(8, gateway.Calls[0].Request.MaxDepth);
        Assert.Equal(18, gateway.Calls[1].Request.MaxDepth);
    }

    [Fact]
    public async Task A_database_answer_is_returned_immediately_as_win_draw_loss()
    {
        var gateway = new FakeGateway
        {
            Info = new EngineInfo("fake", 8, string.Empty),
            Respond = (pdn, _) =>
            {
                Pdn.TryParse(pdn, out var board, out _);
                var move = MoveGenerator.Generate(board!)[0];
                return new SearchResponse(move.Notation, Array.Empty<string>(), 20000, 0, 0, TablebaseHit: true);
            }
        };
        var service = Create(gateway);

        var result = await service.SuggestAsync(Prepare(service, DatabasePosition, "strong"), CancellationToken.None);

        var call = Assert.Single(gateway.Calls);
        Assert.Equal(new LimitsOptions().TablebaseProbeMs, call.Request.MoveTimeMs);   // the short probe, not the strong search
        Assert.Null(call.Request.MaxDepth);
        Assert.True(result.TablebaseHit);
        Assert.Equal(1, result.ScoreOrWdl);   // win for the side to move
    }

    [Fact]
    public async Task When_the_database_has_no_entry_the_normal_search_follows()
    {
        var gateway = new FakeGateway { Info = new EngineInfo("fake", 8, string.Empty) };   // default responder: not a database hit
        var service = Create(gateway);

        var result = await service.SuggestAsync(Prepare(service, DatabasePosition, "strong"), CancellationToken.None);

        Assert.Equal(2, gateway.Calls.Count);
        Assert.Equal(40, gateway.Calls[0].Request.MoveTimeMs);
        Assert.Equal(500, gateway.Calls[1].Request.MoveTimeMs);
        Assert.False(result.TablebaseHit);
    }

    [Fact]
    public async Task Positions_with_more_pieces_than_the_database_covers_skip_the_probe()
    {
        var gateway = new FakeGateway { Info = new EngineInfo("fake", 8, string.Empty) };
        var service = Create(gateway);

        await service.SuggestAsync(Prepare(service, Midgame), CancellationToken.None);

        Assert.Single(gateway.Calls);
    }

    [Fact]
    public async Task Without_a_database_there_is_no_probe()
    {
        var gateway = new FakeGateway();   // DatabasePieces = 0
        var service = Create(gateway);

        await service.SuggestAsync(Prepare(service, DatabasePosition), CancellationToken.None);

        Assert.Single(gateway.Calls);
    }

    [Fact]
    public async Task An_illegal_best_move_falls_back_to_a_legal_move_from_the_principal_variation()
    {
        var gateway = new FakeGateway
        {
            Respond = (pdn, _) =>
            {
                Pdn.TryParse(pdn, out var board, out _);
                var legal = MoveGenerator.Generate(board!)[0];
                return new SearchResponse("1-2", new[] { "1-2", $"{legal.From}-{legal.To}" }, 0, 5, 10, false);
            }
        };
        var service = Create(gateway);

        var result = await service.SuggestAsync(Prepare(service, Midgame), CancellationToken.None);

        Pdn.TryParse(Midgame, out var parsed, out _);
        Assert.Contains(MoveGenerator.Generate(parsed!), m => m.Notation == result.BestMove);
    }

    [Fact]
    public async Task No_legal_move_anywhere_is_an_engine_failure()
    {
        var gateway = new FakeGateway
        {
            Respond = (_, _) => new SearchResponse("1-2", new[] { "1-2" }, 0, 5, 10, false)
        };
        var service = Create(gateway);

        await Assert.ThrowsAsync<EngineFailureException>(
            () => service.SuggestAsync(Prepare(service, Midgame), CancellationToken.None));
    }

    [Fact]
    public async Task An_engine_that_is_not_ready_is_unavailable()
    {
        var gateway = new FakeGateway { IsReady = false };
        var service = Create(gateway);

        await Assert.ThrowsAsync<EngineUnavailableException>(
            () => service.SuggestAsync(Prepare(service, Midgame), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_passes_through_to_the_engine()
    {
        var gateway = new FakeGateway();
        gateway.Respond = (_, _) => throw new OperationCanceledException();
        var service = Create(gateway);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SuggestAsync(Prepare(service, Midgame), CancellationToken.None));
    }

    [Theory]
    [InlineData("checkers-10x10", "PDN", Midgame, "medium", "unsupported_game")]
    [InlineData("checkers-8x8", "FEN", Midgame, "medium", "unsupported_notation")]
    [InlineData("checkers-8x8", "PDN", "B:W18,19,22:B18,5", "medium", "invalid_position")]
    [InlineData("checkers-8x8", "PDN", null, "medium", "invalid_position")]
    [InlineData("checkers-8x8", "PDN", Midgame, "godlike", "invalid_level")]
    public void Bad_requests_are_validation_errors(string game, string notation, string? position, string level, string code)
    {
        var service = Create(new FakeGateway());

        var ex = Assert.Throws<RequestValidationException>(() => service.Prepare(game, notation, position, level, null));

        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void A_position_with_no_legal_moves_is_a_validation_error()
    {
        var service = Create(new FakeGateway());

        // Black's only piece, a king on 32, is boxed in by White men on 27, 28 and 23.
        var ex = Assert.Throws<RequestValidationException>(() => service.Prepare("checkers-8x8", "PDN", "B:W23,27,28:BK32", "medium", null));

        Assert.Equal("no_legal_moves", ex.Code);
    }

    [Fact]
    public void Request_limits_can_only_tighten_the_level()
    {
        var policy = new LevelPolicy(new LevelsOptions(), new LimitsOptions());

        var tightened = policy.Resolve("weak", new LimitsRequest(MaxDepth: 12, SoftTimeMs: 250, HardTimeMs: 5000));
        Assert.Equal(8, tightened.MaxDepth);                                  // weak is depth 8; asking for 12 does not raise it
        Assert.Equal(TimeSpan.FromMilliseconds(100), tightened.SoftTime);     // weak is 100 ms
        Assert.Equal(TimeSpan.FromMilliseconds(1200), tightened.HardTime);    // the configured ceiling

        var lowered = policy.Resolve("strong", new LimitsRequest(MaxDepth: 6, SoftTimeMs: 60, HardTimeMs: 100));
        Assert.Equal(6, lowered.MaxDepth);
        Assert.Equal(TimeSpan.FromMilliseconds(60), lowered.SoftTime);
        Assert.Equal(TimeSpan.FromMilliseconds(100), lowered.HardTime);
    }

    [Fact]
    public void The_soft_time_stays_under_the_hard_time()
    {
        var policy = new LevelPolicy(new LevelsOptions(), new LimitsOptions());

        var limits = policy.Resolve("strong", new LimitsRequest(null, null, 400));

        Assert.True(limits.SoftTime < limits.HardTime);
        Assert.Equal(TimeSpan.FromMilliseconds(320), limits.SoftTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public void Out_of_range_depth_is_rejected(int depth)
    {
        var policy = new LevelPolicy(new LevelsOptions(), new LimitsOptions());

        var ex = Assert.Throws<RequestValidationException>(() => policy.Resolve("weak", new LimitsRequest(depth, null, null)));

        Assert.Equal("invalid_limits", ex.Code);
    }

    [Fact]
    public void The_principal_variation_is_rewritten_in_the_notation_of_the_best_move()
    {
        Pdn.TryParse("B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16", out var board, out _);
        var best = MoveGenerator.Generate(board!).Single(m => m.Notation == "14x23");

        // KingsRow prints a jump as a bare from-to pair.
        var pv = PvNormalizer.Normalize(board!, best, new[] { "14-23", "27-18" });

        Assert.Equal(new[] { "14x23", "27x18" }, pv);
    }

    [Fact]
    public void A_principal_variation_that_does_not_start_with_the_best_move_is_dropped()
    {
        Pdn.TryParse("B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16", out var board, out _);
        var best = MoveGenerator.Generate(board!).Single(m => m.Notation == "14x23");

        var pv = PvNormalizer.Normalize(board!, best, new[] { "16-23", "27-18" });

        Assert.Equal(new[] { "14x23" }, pv);
    }

    [Fact]
    public void The_principal_variation_stops_at_a_move_it_cannot_read()
    {
        Pdn.TryParse("B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16", out var board, out _);
        var best = MoveGenerator.Generate(board!).Single(m => m.Notation == "14x23");

        var pv = PvNormalizer.Normalize(board!, best, new[] { "14-23", "xxxx", "5-9" });

        Assert.Equal(new[] { "14x23" }, pv);
    }

    [Fact]
    public void Validate_and_legal_moves_answer_from_the_rules()
    {
        var positions = new PositionService();
        const string start = "B:W21-32:B1-12";

        Assert.True(positions.Validate(start, "9-13").Legal);
        Assert.False(positions.Validate(start, "9-18").Legal);
        Assert.False(positions.Validate(start, "nonsense").Legal);
        Assert.Throws<RequestValidationException>(() => positions.Validate("garbage", "9-13"));

        var legal = positions.GetLegalMoves(start);
        Assert.Equal(7, legal.Moves.Count);
        Assert.False(legal.GameOver);
        Assert.Equal(Side.Black, legal.SideToMove);

        var over = positions.GetLegalMoves("B:W23,27,28:BK32");
        Assert.True(over.GameOver);
        Assert.Equal(Side.White, over.Winner);
    }

    [Fact]
    public void The_cache_expires_entries_and_evicts_the_least_recently_used()
    {
        var time = new ManualTime();
        var cache = new LruTtlCache<string, int>(2, TimeSpan.FromMinutes(15), time);

        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _));     // "a" is now the most recently used
        cache.Set("c", 3);                          // evicts "b"

        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));

        time.Now = time.Now.AddMinutes(16);
        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("c", out _));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

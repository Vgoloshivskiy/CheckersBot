using Checkers.Engine.KingsRowHost;
using Checkers.Engine.KingsRowHost.Native;
using Xunit;

namespace Checkers.Tests;

/// <summary>The parts of the host that need no DLL: board conversion and status parsing.</summary>
public class KingsRowHostTests
{
    private static Board Parse(string pdn)
    {
        Assert.True(Pdn.TryParse(pdn, out var board, out var error), error);
        return board!;
    }

    private static void WithLayout(BoardLayout layout, Action action)
    {
        var previous = KingsRowBoard.Layout;
        KingsRowBoard.Layout = layout;
        try
        {
            action();
        }
        finally
        {
            KingsRowBoard.Layout = previous;
        }
    }

    [Fact]
    public void Every_layout_places_the_32_squares_on_32_distinct_cells()
    {
        foreach (var layout in BoardLayout.Candidates)
        {
            var cells = new HashSet<int>();
            for (int square = 1; square <= 32; square++)
            {
                int cell = layout.CellOf(square);
                Assert.InRange(cell, 0, 63);
                Assert.True(cells.Add(cell), $"{layout.Name}: square {square} reuses cell {cell}");
                Assert.Equal(square, layout.SquareOf(cell));
            }
        }
    }

    [Fact]
    public void Pieces_are_encoded_like_CheckerBoard()
    {
        var cells = KingsRowBoard.ToCells(Parse("B:WK2,10:BK17,5"));

        Assert.Equal(1, cells.Count(c => c == (1 | 8)));   // White king
        Assert.Equal(1, cells.Count(c => c == (1 | 4)));   // White man
        Assert.Equal(1, cells.Count(c => c == (2 | 8)));   // Black king
        Assert.Equal(1, cells.Count(c => c == (2 | 4)));   // Black man
        Assert.Equal(4, cells.Count(c => c != 0));
        Assert.Equal(2, KingsRowBoard.Color(Side.Black));
        Assert.Equal(1, KingsRowBoard.Color(Side.White));
    }

    [Fact]
    public void The_expected_layout_puts_square_29_in_the_bottom_left_corner()
    {
        // board[x][y] with White at y = 0: square 29 is at x = 0, y = 0.
        Assert.Equal(0, BoardLayout.Candidates[0].CellOf(29));
        // Square 4 is at the top right of the board's dark squares: x = 7, y = 7.
        Assert.Equal(7 * 8 + 7, BoardLayout.Candidates[0].CellOf(4));
    }

    [Fact]
    public void FindMove_reads_every_legal_move_back_in_every_layout()
    {
        var positions = new[]
        {
            Board.Initial(),
            Parse("B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16"),   // captures
            Parse("W:W21-32:B1-11,13"),
            Parse("B:W6,15:B1"),                                       // double jump
            Parse("B:W26,27:B24"),                                     // crowning
        };

        foreach (var layout in BoardLayout.Candidates)
        {
            WithLayout(layout, () =>
            {
                foreach (var board in positions)
                {
                    var before = KingsRowBoard.ToCells(board);
                    foreach (var move in MoveGenerator.Generate(board))
                    {
                        var after = KingsRowBoard.ToCells(board.Apply(move));
                        Assert.Equal(move.Notation, KingsRowBoard.FindMove(before, after, board.ToMove));
                    }
                }
            });
        }
    }

    [Fact]
    public void FindMove_returns_null_when_the_board_is_not_the_result_of_a_legal_move()
    {
        var board = Board.Initial();
        var before = KingsRowBoard.ToCells(board);

        Assert.Null(KingsRowBoard.FindMove(before, (int[])before.Clone(), board.ToMove));   // nothing moved
    }

    [Fact]
    public void A_wrong_layout_is_rejected_by_the_move_check()
    {
        // Write the board with one layout and read the engine's "reply" with another, as happens when calibration
        // is trying a candidate that is not the engine's layout.
        var board = Board.Initial();
        int[] before = null!;
        int[] after = null!;
        var move = MoveGenerator.Generate(board)[0];

        WithLayout(BoardLayout.Candidates[0], () =>
        {
            before = KingsRowBoard.ToCells(board);
            after = KingsRowBoard.ToCells(board.Apply(move));
        });

        WithLayout(BoardLayout.Candidates[1], () =>
        {
            Assert.Null(KingsRowBoard.FindMove(before, after, board.ToMove));
        });
    }

    [Fact]
    public void Status_line_from_the_KingsRow_help_is_parsed()
    {
        Assert.True(SearchStatus.TryParse(
            "value=-6,  depth 17/19.4/33,  4.8s,  1033 kN/s,  pv 25-22 2-6 23-18 5-9 31-27", out var status));

        Assert.Equal(-6, status!.Value);
        Assert.Equal(17, status.Depth);
        Assert.Equal(1033, status.KiloNodesPerSecond);
        Assert.Equal(new[] { "25-22", "2-6", "23-18", "5-9", "31-27" }, status.Pv);
        Assert.False(status.IsDatabaseResult);
        Assert.Equal(6, status.ScoreFor(Side.White));      // negative is good for White
        Assert.Equal(-6, status.ScoreFor(Side.Black));
        Assert.InRange(status.NodesIn(TimeSpan.FromSeconds(4.8)), 4_900_000L, 5_000_000L);   // 1033 kN/s for 4.8 s
    }

    [Fact]
    public void Status_line_with_a_node_count_uses_it()
    {
        Assert.True(SearchStatus.TryParse(
            "best 11-15, value 12, depth 14/16.2/30, nodes 1234567, time 0.50, 2469 kN/s, db 0", out var status));

        Assert.Equal(12, status!.Value);
        Assert.Equal(14, status.Depth);
        Assert.Equal(1234567, status.NodesIn(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_database_draw_lists_moves_with_probabilities_and_has_no_search_depth()
    {
        Assert.True(SearchStatus.TryParse("11-7* (0.111), 11-6* (0.111), 14-19 (0.296)", out var status));

        Assert.True(status!.IsDatabaseResult);
        Assert.Equal(0, status.Value);
        Assert.Equal(0, status.Depth);
        Assert.True(status.IsTablebaseHit(withinDatabaseRange: true));
        Assert.False(status.IsTablebaseHit(withinDatabaseRange: false));
    }

    [Theory]
    [InlineData("value=2001,  11-15 (conv. in 8)*")]
    [InlineData("11-15 (black wins in 12)*")]
    public void Database_win_formats_count_as_tablebase_hits(string text)
    {
        Assert.True(SearchStatus.TryParse(text, out var status));

        Assert.True(status!.IsTablebaseHit(withinDatabaseRange: true));
        Assert.True(status.ScoreFor(Side.Black) > 2000);
        Assert.True(status.ScoreFor(Side.White) < -2000);
    }

    [Fact]
    public void A_database_win_for_white_is_a_loss_for_black()
    {
        Assert.True(SearchStatus.TryParse("14-18 (white wins in 9)*", out var status));

        Assert.True(status!.ScoreFor(Side.White) > 2000);
        Assert.True(status.ScoreFor(Side.Black) < -2000);
    }

    [Fact]
    public void Plus_or_minus_one_means_a_database_draw()
    {
        Assert.True(SearchStatus.TryParse("value=1,  depth 9/11.0/20,  0.1s,  800 kN/s,  pv 3-8 xxxx", out var status));

        Assert.Equal(0, status!.ScoreFor(Side.Black));
        Assert.Equal(0, status.ScoreFor(Side.White));
        Assert.True(status.IsTablebaseHit(withinDatabaseRange: true));
        Assert.Equal(new[] { "3-8", "xxxx" }, status.Pv);
    }

    [Fact]
    public void A_large_score_outside_the_database_range_is_not_a_database_hit()
    {
        Assert.True(SearchStatus.TryParse("value=2500,  depth 20/22.0/40,  0.5s,  900 kN/s,  pv 3-8", out var status));

        Assert.False(status!.IsTablebaseHit(withinDatabaseRange: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Thinking...")]
    [InlineData("Illegal position.")]
    public void Statuses_without_a_search_result_are_rejected(string text)
    {
        Assert.False(SearchStatus.TryParse(text, out _));
    }
}

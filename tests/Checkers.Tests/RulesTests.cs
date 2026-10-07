using Xunit;

namespace Checkers.Tests;

public class RulesTests
{
    private static Board Parse(string pdn)
    {
        Assert.True(Pdn.TryParse(pdn, out var board, out var error), error);
        return board!;
    }

    private static long Perft(Board board, int depth)
    {
        if (depth == 0) return 1;

        long total = 0;
        foreach (var move in MoveGenerator.Generate(board))
        {
            total += Perft(board.Apply(move), depth - 1);
        }
        return total;
    }

    // Published node counts for English checkers from the starting position.
    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 49)]
    [InlineData(3, 302)]
    [InlineData(4, 1469)]
    [InlineData(5, 7361)]
    [InlineData(6, 36768)]
    [InlineData(7, 179740)]
    [InlineData(8, 845931)]
    public void Perft_matches_published_counts(int depth, long expected)
    {
        Assert.Equal(expected, Perft(Board.Initial(), depth));
    }

    [Fact]
    public void Spec_example_position_has_two_mandatory_captures()
    {
        var board = Parse("B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16");

        var moves = MoveGenerator.Generate(board).Select(m => m.Notation).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "14x23", "16x23" }, moves);
    }

    [Fact]
    public void Canonical_pdn_sorts_squares_and_keeps_kings()
    {
        var board = Parse("B:WK15,22,K10:Bk3,20,K7");

        Assert.Equal("B:WK10,K15,22:BK3,K7,20", board.ToPdn());
    }

    [Fact]
    public void Pdn_accepts_ranges_and_either_piece_order()
    {
        var a = Parse("B:W21-32:B1-12");
        var b = Parse("B:B1-12:W21-32");

        Assert.Equal(Board.Initial().ToPdn(), a.ToPdn());
        Assert.Equal(a.ToPdn(), b.ToPdn());
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("B:W18,19,22:B18,5")]      // square 18 twice
    [InlineData("B:W33:B1")]               // square out of range
    [InlineData("X:W18:B1")]               // bad side to move
    [InlineData("B:W18:B30")]              // Black man on its far row without a K
    [InlineData("B:W:B1")]                 // White has no pieces
    public void Invalid_pdn_is_rejected(string pdn)
    {
        Assert.False(Pdn.TryParse(pdn, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Multi_jump_must_be_completed()
    {
        // Black man on 1 jumps 6 to land on 10, then jumps 15 to land on 19.
        var board = Parse("B:W6,15:B1");

        var move = Assert.Single(MoveGenerator.Generate(board));

        Assert.Equal("1x10x19", move.Notation);
        Assert.Equal(new[] { 6, 15 }, move.Captured);
    }

    [Fact]
    public void Man_that_reaches_the_far_row_is_crowned_and_stops_jumping()
    {
        // Black man on 24 jumps 27 and lands on 31 (far row). A king there could jump 26 next,
        // but a newly crowned piece's move ends, so the only move is 24x31.
        var board = Parse("B:W26,27:B24");

        var move = Assert.Single(MoveGenerator.Generate(board));
        Assert.Equal("24x31", move.Notation);

        var next = board.Apply(move);
        Assert.Equal(2, next[31]);   // Black king
        Assert.Equal(0, next[27]);   // captured piece removed
        Assert.Equal(0, next[24]);
    }

    [Fact]
    public void Notation_matches_exact_path_or_unambiguous_endpoints()
    {
        var board = Parse("B:W6,15:B1");
        var legal = MoveGenerator.Generate(board);

        Assert.True(MoveNotation.TryParse("1x10x19", out var full));
        Assert.NotNull(MoveNotation.Find(legal, full!));

        Assert.True(MoveNotation.TryParse("1-19", out var ends));
        Assert.NotNull(MoveNotation.Find(legal, ends!));

        Assert.True(MoveNotation.TryParse("1x10", out var partial));
        Assert.Null(MoveNotation.Find(legal, partial!));

        Assert.False(MoveNotation.TryParse("1-40", out _));
    }
}

namespace Checkers;

/// <summary>Black moves first and advances toward square 32. White advances toward square 1.</summary>
public enum Side
{
    Black = 1,
    White = -1
}

public static class SideExtensions
{
    public static Side Opposite(this Side side) => side == Side.Black ? Side.White : Side.Black;

    /// <summary>"B" or "W", as used in PDN.</summary>
    public static string ToPdnLetter(this Side side) => side == Side.Black ? "B" : "W";
}

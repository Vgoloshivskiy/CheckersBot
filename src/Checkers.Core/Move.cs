namespace Checkers;

/// <summary>
/// One legal move. <see cref="Path"/> lists every square the piece stands on, so a plain move is
/// [22, 18] and a double jump is [22, 15, 8]. <see cref="Captured"/> lists the jumped squares.
/// </summary>
public sealed class Move
{
    public Move(int[] path, int[] captured)
    {
        Path = path;
        Captured = captured;
    }

    public int[] Path { get; }
    public int[] Captured { get; }

    public int From => Path[0];
    public int To => Path[Path.Length - 1];
    public bool IsCapture => Captured.Length > 0;

    /// <summary>Standard notation: 22-18 for a step, 22x15x8 for a jump sequence.</summary>
    public string Notation => string.Join(IsCapture ? "x" : "-", Path);

    public override string ToString() => Notation;
}
